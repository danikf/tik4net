# Terminal output → tik4net entities: design

> Design behind the CLI transports' output parsing. Companion to the live-verified behaviour in
> [`findings-cli.md`](findings-cli.md).

---

## Core idea

The RouterOS CLI offers the `print as-value` format, which is **machine-readable** and **maps
directly** onto the `ITikReSentence` structure. This makes it possible to implement
`ITikConnection` over SSH or MACTelnet without full VT100 emulation — plain text processing is
enough.

```
API protokol:         CLI (print as-value):
─────────────         ─────────────────────
!re                   .id=*1;address=192.168.1.1/24;interface=ether1;dynamic=no
=.id=*1
=address=192.168.1.1/24
=interface=ether1             ←── direct 1:1 match of keys and values
=dynamic=no
!re
=.id=*2;...
!done
```

The values are identical to the API's (`yes`/`no` for bools, the same address formats, etc.) —
the `tik4net.entities` entity mapper **doesn't need to know** it is talking over CLI instead of
the API.

---

## The `print as-value` format — documentation

### List entities

```
/ip address print as-value
```
Output (one line per entity, `;` separates the keys):
```
.id=*1;address=192.168.1.1/24;network=192.168.1.0;interface=ether1;comment=;dynamic=no;disabled=no;invalid=no;actual-interface=ether1
.id=*2;address=10.0.0.1/8;network=10.0.0.0;interface=bridge;comment=;dynamic=no;disabled=no;invalid=no;actual-interface=bridge
```

### Singleton entity

```
/system resource print as-value
```
Output (a single line, no `.id`):
```
uptime=1h30m;version=7.16;build-time=2024-01-01 00:00:00;free-memory=128.0MiB;total-memory=256.0MiB;cpu=ARM;cpu-count=4;cpu-load=3;free-hdd-space=10.0MiB
```

### Filtered print

```
/ip address print as-value where interface=ether1
```
Returns only the matching records — the same behavior as the API's `?interface=ether1`.

### Add — obtaining the .id via `:put`

```
:put [/ip address add address=10.0.0.1/24 interface=bridge]
```
Output: `*3` (returns the .id of the new entity — the equivalent of the API's `=ret=*3`).

Without the `:put [...]` wrapper, `add` returns an empty output or an index (a plain number), not
the `*N` format.

### Set

```
/ip address set [find .id=*1] comment=updated-comment
```
Output: empty (success).

### Remove

```
/ip address remove [find .id=*1]
```
Output: empty (success).

### Enable / Disable

```
/ip firewall filter enable [find .id=*1]
/ip firewall filter disable [find .id=*1]
```

### Error output (examples)

```
no such item
expected end of command (line 1 column 15)
failure: already have such entry
```

---

## Translation logic: API command → CLI string

### 1. Path translation (`CommandText`)

API format: `/ip/address/print`
CLI format: `/ip address print`

Algorithm: split on `/`, join the parts with spaces, keep the leading `/`.

```csharp
// /ip/address/print  →  /ip address print
string ApiPathToCli(string apiPath)
{
    var parts = apiPath.TrimStart('/').Split('/');
    return "/" + string.Join(" ", parts);
}
```

Examples:
| API CommandText | CLI string (base) |
|---|---|
| `/ip/address/print` | `/ip address print` |
| `/ip/address/add` | `/ip address add` |
| `/ip/address/set` | `/ip address set` |
| `/ip/address/remove` | `/ip address remove` |
| `/ip/firewall/filter/print` | `/ip firewall filter print` |
| `/system/identity/get` | `/system identity get` |
| `/system/reboot` | `/system reboot` |

### 2. Parameter translation

`ITikCommandParameter` has two kinds (`ParameterFormat`):

**Filter parameters** (`Format = Filter`, API: `?name=value`):
- Appended as `where name=value` after `print`
- Negation: `?name=!value` → `where name!=value`
- Comparison: `?>count=5` → `where count>5`
- Regex: `?~comment=eth` → `where comment~eth`
- Multiple filters: `where a=x && b=y`

**NameValue parameters** (`Format = NameValue`, API: `=name=value`):
- For `add`: `name=value name2=value2 ...`
- For `set`: extract `.id` → `[find .id=*N]`, the rest as `name=value`
- For everything else (nonquery): `name=value ...`
- Special case: `.proplist` is ignored (`as-value` always returns every field)

### 3. Building the CLI command per operation

Operation is detected from the last segment of the path:

```
print   → /path print as-value [where filter1=val && filter2=val]
add     → :put [/path add name=val name2=val2]
set     → /path set [find .id=*N] name=val name2=val2
remove  → /path remove [find .id=*N]
enable  → /path enable [find .id=*N]
disable → /path disable [find .id=*N]
move    → /path move [find .id=*N] destination=*M
get     → /path get .id=*N value-name=name  (alias for scalar)
```

**Build examples:**

```
API: /ip/address/print + ?interface=ether1
CLI: /ip address print as-value where interface=ether1

API: /ip/address/add + =address=10.0.0.1/24 + =interface=ether1
CLI: :put [/ip address add address=10.0.0.1/24 interface=ether1]

API: /ip/address/set + =.id=*1 + =comment=test
CLI: /ip address set [find .id=*1] comment=test

API: /ip/address/remove + =.id=*1
CLI: /ip address remove [find .id=*1]

API: /system/reboot (nonquery)
CLI: /system reboot
```

---

## Parsing `print as-value` output

### Algorithm

```csharp
IEnumerable<ITikReSentence> ParseAsValue(string output)
{
    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) continue;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        int pos = 0;
        while (pos < trimmed.Length)
        {
            // find '=' marking the end of the key
            int eq = trimmed.IndexOf('=', pos);
            if (eq < 0) break;
            string key = trimmed.Substring(pos, eq - pos);

            // find ';' marking the end of the value (or end of line)
            int semi = trimmed.IndexOf(';', eq + 1);
            string value = semi < 0
                ? trimmed.Substring(eq + 1)
                : trimmed.Substring(eq + 1, semi - eq - 1);

            fields[key] = value;
            pos = semi < 0 ? trimmed.Length : semi + 1;
        }

        if (fields.Count > 0)
            yield return new CliReSentence(fields);
    }
}
```

### Mock implementation of `ITikReSentence`

```csharp
internal sealed class CliReSentence : ITikReSentence
{
    private readonly IReadOnlyDictionary<string, string> _fields;

    public CliReSentence(Dictionary<string, string> fields)
        => _fields = fields;

    public string GetId()
        => GetResponseField(".id");

    public string GetResponseField(string fieldName)
        => _fields.TryGetValue(fieldName, out var v) ? v
           : throw new TikSentenceException($"Field '{fieldName}' not found in CLI response");

    public bool TryGetResponseField(string fieldName, out string fieldValue)
        => _fields.TryGetValue(fieldName, out fieldValue);

    public string GetResponseFieldOrDefault(string fieldName, string defaultValue)
        => _fields.TryGetValue(fieldName, out var v) ? v : defaultValue;
}
```

### Error detection and translation

Error output has no structure — detection is based on prefixes:

```csharp
void ThrowIfError(string output, int? exitCode = null)
{
    if (exitCode.HasValue && exitCode != 0) { /* SSH: parse output as an error */ }

    var line = output.Trim();
    if (line.StartsWith("no such item")) throw new TikNoSuchItemException(line);
    if (line.StartsWith("already have such")) throw new TikAlreadyHaveSuchItemException(line);
    if (line.StartsWith("no such command") || line.StartsWith("expected end"))
        throw new TikNoSuchCommandException(line);
    if (line.StartsWith("failure:") || line.StartsWith("error:"))
        throw new TikCommandTrapException(line);
    // Any other non-empty output on the error stream → TikCommandTrapException
}
```

**SSH advantage:** `SshClient.RunCommand()` returns stdout/stderr separately along with an exit
code — error detection is more robust.

**MACTelnet disadvantage:** everything is a single stream, so heuristic parsing is required.

---

## Architecture — the shared `tik4net.cli` layer

Because the translation and parsing logic is identical for SSH, MACTelnet, and Telnet, and has no
external dependencies, it **lives directly in the `tik4net` core** (namespace `Tik4Net.Cli`). A
separate `tik4net.cli` NuGet package doesn't exist — there's no reason for one.

```
tik4net/Cli/
├── CliCommandBuilder.cs                ~ 120 LOC  (ITikCommand → CLI string)
├── CliOutputParser.cs                  ~ 80 LOC   (as-value text → IEnumerable<ITikReSentence>)
├── CliErrorParser.cs                   ~ 40 LOC   (error text → tik4net exceptions)
├── CliReSentence.cs                    ~ 40 LOC   (mock ITikReSentence)
├── VtStripper.cs                       ~ 50 LOC   (ANSI escape remover for MACTelnet + Telnet)
└── CliConnectionBase.cs                ~ 220 LOC  (shared logic, abstract; SemaphoreSlim for async)
```

The transport packages (`tik4net.ssh`, `tik4net.mactelnet`, `tik4net.telnet`) depend only on
`tik4net` — the CLI layer comes for free.

The abstract base class (including the async strategy):

```csharp
// tik4net.cli
public abstract class CliConnectionBase : ITikConnection
{
    // A terminal is inherently sequential — one command at a time.
    // SemaphoreSlim serializes operations; callers still see plain async/await.
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    // Subclasses implement the async transport (TCP, UDP PTY…):
    protected abstract Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct);

    // Entry point for all operations — serialized through the semaphore:
    protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
    {
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try { return await ExecuteCliCommandCoreAsync(cliText, ct).ConfigureAwait(false); }
        finally { _cmdLock.Release(); }
    }

    // Sync wrapper for backward compatibility with ITikCommand.Execute*:
    protected string ExecuteCliCommand(string cliText)
        => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

    public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
        => new CliCommand(this, commandText, parameters);

    internal IEnumerable<ITikReSentence> RunPrint(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildPrint(cmd);
        var output = ExecuteCliCommand(cli);
        CliErrorParser.ThrowIfError(output);
        return CliOutputParser.ParseAsValue(output);
    }

    internal string RunAdd(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildAdd(cmd);    // :put [/path add ...]
        var output = ExecuteCliCommand(cli);
        CliErrorParser.ThrowIfError(output);
        return output.Trim();                         // returns *N (the new .id)
    }

    internal void RunNonQuery(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildNonQuery(cmd);
        ExecuteCliCommand(cli);
    }
}
```

**Async strategy for terminal connections:** MACTelnet, Telnet, and SSH-terminal are all
sequential protocols — a second command cannot be sent before the reply to the first has arrived
(there's no tag-based correlation like in the API protocol). The right solution is a
`SemaphoreSlim(1,1)` in `CliConnectionBase` — not a connection pool (limits sessions on the
router, adds overhead) and not a background task with correlation (complex, and output could
interleave). Operations are serialized internally, but from the caller's point of view they are
still `await`able.

`tik4net.ssh`, `tik4net.mactelnet`, and `tik4net.telnet` only implement the transport layer:

```csharp
// tik4net.ssh
public class SshConnection : CliConnectionBase
{
    private SshClient _ssh;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        // SSH.NET's RunCommand is synchronous — offload it to the thread pool
        var cmd = await Task.Run(() => _ssh.RunCommand(cliText), ct);
        if (!string.IsNullOrWhiteSpace(cmd.Error))
            CliErrorParser.ThrowIfError(cmd.Error, cmd.ExitStatus);
        return cmd.Result;
    }
}

// tik4net.mactelnet
public class MacTelnetConnection : CliConnectionBase
{
    private MacTelnetSession _session;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        var raw = await _session.RunCommandAndWaitAsync(cliText, ct);
        return VtStripper.Strip(raw);     // strip ANSI escape codes + echo + prompt
    }
}

// tik4net.telnet — identical structure to MacTelnetConnection, only the transport differs
public class TelnetConnection : CliConnectionBase
{
    private TelnetSession _session;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        var raw = await _session.RunCommandAndWaitAsync(cliText, ct);
        return VtStripper.Strip(raw);     // same VT100 output as MACTelnet
    }
}
```

---

## VT stripping (for MACTelnet and Telnet)

SSH's `RunCommand` returns plain stdout — **no VT stripping is needed**.

MACTelnet and Telnet both use a PTY → the output contains ANSI escape sequences. `VtStripper`
lives in the shared `tik4net.cli` layer and is reused by both transports. A simple regex-based
stripper:

```csharp
public static class VtStripper
{
    // Covers: CSI sequences, OSC sequences, other ESC sequences
    private static readonly Regex AnsiRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07]*\x07)",
        RegexOptions.Compiled);

    public static string Strip(string input)
    {
        var stripped = AnsiRegex.Replace(input, "");
        // Remove the echoed prompt and the echoed input command (MACTelnet echo):
        return RemovePromptAndEcho(stripped);
    }

    private static string RemovePromptAndEcho(string text)
    {
        // RouterOS prompt: "[user@identity] > "
        // Output = echo_of_command + newline + actual_output + newline + prompt
        var lines = text.Split('\n');
        // The first line is the command echo, the last is the new prompt → trim both
        return string.Join("\n", lines.Skip(1).SkipLast(1));
    }
}
```

---

## Mapping matrix: `ITikCommand` methods

| Method | Implementation over CLI | Note |
|---|---|---|
| `ExecuteNonQuery()` | `/path verb [find .id=*N] params` | empty output = success |
| `ExecuteList()` | `/path print as-value [where ...]` | parses the lines |
| `ExecuteSingleRow()` | `/path print as-value [where ...]` | asserts exactly 1 line |
| `ExecuteScalar()` | `:put [/path get .id=*N value-name=x]` | scalar output |
| `ExecuteScalar(target)` | `:put [/path get .id=*N value-name=target]` | |
| `ExecuteAsync(cb,...)` | ⚠️ emulated via thread + sync exec | no true push |
| `ExecuteListWithDuration` | ❌ not possible | needs streaming |
| `ExecuteListUntilDone` | ⚠️ only for self-terminating commands | `/ping count=N` |
| `CallCommandSync` | ❌ low-level, can't be mapped | raw API sentences |
| `CallCommandAsync` | ❌ not possible | |
| `Cancel()` | ❌ not possible | no in-flight command |

**Capabilities for a CLI-based connection:**
- `Read = ✅`, `Write = ✅`, `Listen = ❌`, `Streaming = ❌`, `Async = ⚠️`

---

## What `tik4net.entities` needs and gets

The entity mapper (`LoadAll<T>()`, `Save<T>()`, `Delete<T>()`, …) calls:

| Entities operation | Calls on ITikConnection/Command | CLI result |
|---|---|---|
| `LoadAll<IpAddress>()` | `ExecuteList()` on `/ip/address/print` | `as-value` parsing → `CliReSentence` list |
| `LoadById<IpAddress>("*1")` | `ExecuteSingleRow()` on `/ip/address/print ?=.id=*1` | 1 `as-value` line |
| `Save<IpAddress>(newEntity)` | `ExecuteNonQuery()` on `/ip/address/add` with params | `:put [add ...]` → new `.id` |
| `Save<IpAddress>(existing)` | `ExecuteNonQuery()` on `/ip/address/set` with `.id` + changes | `set [find .id=*N] ...` |
| `Delete<IpAddress>(entity)` | `ExecuteNonQuery()` on `/ip/address/remove` with `.id` | `remove [find .id=*N]` |
| `LoadSingle<SystemResource>()` | `ExecuteSingleRow()` on `/system/resource/print` | 1 `as-value` line (no `.id`) |

**The entities mapper has no knowledge** of CLI — it only sees `ITikReSentence` objects, populated
from `CliReSentence`.

---

## Boundaries and limitations

### Reliable (production-ready)

- Read operations via `LoadAll`, `LoadList`, `LoadById`, `LoadSingle`
- Write operations: `Save` (both add and set), `Delete`, `Enable`, `Disable`, `Move`
- Filters via `?name=value` parameters → `where name=value`
- Singleton entities (no `.id`)
- Boolean values: both the RouterOS CLI and the API use `yes`/`no`

### Conditionally reliable (with caveats)

- **Values containing a semicolon (`;`):** comments or values containing `;` will break parsing.
  Workaround: restrict to entities where this can't happen, or implement an escape-aware parser.
- **SSH vs. MACTelnet:** SSH is more robust (exit code, separate stderr). MACTelnet is fragile
  (heuristics).
- **RouterOS version:** the `as-value` format has been consistent since ROS 6.x. It may differ on
  ROS < 6.

### Not possible, or only with major effort

- **Listen (`/listen`)** — real-time push notifications are not possible over a terminal.
- **Streaming commands** — incremental results for `/tool/torch`, `/tool/ping`: SSH could handle
  this via a streaming `RunCommand`, but integrating it with
  `ITikCommand.ExecuteListWithDuration` is complex.
- **Async cancel** — `Cancel()` has no equivalent for a synchronous SSH `RunCommand`.
- **Batch commands** — no pipelining.
- **Proplist optimization** — `print as-value` always returns every field (can't be restricted the
  way the API does via `.proplist`).

---

## Implementation approach — recommended order

1. **CLI layer in `tik4net`** — implement `CliCommandBuilder` + `CliOutputParser` +
   `CliReSentence` + `CliConnectionBase` with the semaphore (these are independent of each other
   and testable without a network). Part of the same milestone: `TikConnectionSetup` with
   `CreateApiConnection()` / `CreateApiSslConnectionAsync()` — replacing `ConnectionFactory`.
2. **`tik4net.ssh` — `SshConnection`** — the simplest CLI transport (clean stdout, exit code);
   verify the entity mapper works against it. Extension method
   `TikConnectionSetup.CreateSshConnection(privateKeyPath?)`.
3. **Parser unit tests** — test `CliOutputParser` against recorded RouterOS output (no live
   router needed).
4. **`tik4net.mactelnet` — `MacTelnetConnection`** — add `VtStripper` and prompt detection;
   fragile, needs integration tests against a live router. Extension method
   `TikConnectionSetup.CreateMacTelnetConnection()`.
5. **`tik4net.telnet` — `TelnetConnection`** — add a `TelnetNegotiator` (~30 LOC), login/password
   prompt detection; reuse `VtStripper` unchanged. Implementation effort roughly 20% of
   MACTelnet's. Extension method `TikConnectionSetup.CreateTelnetConnection()`.

---

## Open questions

1. **Escape-aware `;` parser**: Is this needed for robust production use? Comments containing
   semicolons are a real occurrence.
   Option: parse left to right, track `=`, and skip over quoted values (if RouterOS quotes them).

2. **`add` and the new `.id`**: `:put [/path add ...]` only returns a `.id` if `add` returns a
   handle.
   Not every RouterOS entity returns a `.id` from `add` — this needs testing across different
   entities.

3. **MACTelnet prompt-detection reliability**: the RouterOS prompt can contain a custom identity
   with arbitrary characters.
   A more robust pattern: detect `] > ` (the end of the prompt) rather than the whole prompt.

4. **Sharing `tik4net.cli`**: as a separate NuGet package, or an internal dependency
   (InternalsVisibleTo)?
   Recommendation: a separate NuGet package from the start — lets other projects reuse the
   parsing.

5. **`ExecuteAsync` emulation**: `CliConnectionBase.ExecuteCliCommandAsync` plus the semaphore is
   the right foundation. `ITikCommand.ExecuteAsync(callback, done, trap)` can be emulated as
   `Task.Run(() => { /* sync exec */; callback(each_re); done(); })` — it returns immediately, and
   results arrive via the callback. Cancel is not possible (no in-flight command). Recommendation:
   implement it, but document the limitation.

6. **Telnet vs. MACTelnet prompt detection**: the RouterOS Telnet prompt is identical to the
   MACTelnet prompt (`[user@identity] > `). `VtStripper.RemovePromptAndEcho` can be reused
   unchanged — verify against a live router.
