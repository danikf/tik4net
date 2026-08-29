---
name: mikrotik-cli-probe
description: >
  Probe what a MikroTik router actually returns over the Telnet/PTY command line (TCP 23), and apply
  the hard-won knowledge of RouterOS CLI quirks. Use when debugging tik4net CLI-based connections
  (Telnet, SSH-PTY, MAC-Telnet and the WinBox CLI transports), when terminal output comes back empty or
  garbled, when a `print as-value` / `:put` command behaves unexpectedly, when parsing as-value output,
  or whenever you need ground truth for "what does the router send back over telnet for command X"
  independently of the tik4net library. This is about the raw CLI/terminal layer — for normal
  structured queries via the binary API use the `mikrotik` skill (mikrotik_call MCP) instead.
---

# MikroTik CLI probe (Telnet / PTY)

The binary API (`mikrotik` skill / `mikrotik_call`) is the right tool for normal queries. Use THIS
skill when the question is specifically about the **terminal/CLI layer**: what RouterOS emits over a
PTY, why a CLI command returns nothing, how to shape a command so its output is parseable, etc.
This is the layer tik4net's CLI-based transports (`tik4net/Cli/`, `tik4net/Telnet/`) sit on.

> **Two ways to get terminal ground truth.** The probe script below is the router-only view (no tik4net
> at all). To see the same raw bytes **as tik4net itself sees them** — through the actual transport, with
> the mepty pull cadence, VT100 answers and prompt/settle decisions the library makes — use
> `mikrotik_call` with `traceLevel:'bytes'` (channels `telnet.sock`, `wbxcli.mepty`, `mactelnet.udp`) and
> optionally `includeRouterLog:true` for the device-side log. That is the in-library equivalent of this
> probe and the faster first stop when the question is "what did *our client* do", not "what can the
> router do at all". See the `mikrotik` skill's *Wire tracing & router-log debugging* section. Reach for
> the standalone probe script when you need a reference implementation independent of tik4net.
>
> **Check which server answered.** The MCP route runs a *staged copy* of the build output, so a trace can
> silently come from the server you had before your last build. Every answer names the assembly that
> produced it — `mikrotik_call` on a trailing `--- MCP SERVER --- … built <timestamp> …` line,
> `mikrotik_cli_complete` in its `serverBuild` property. If that timestamp predates your build, the trace is
> evidence about the OLD code; reconnect `tik4net-mcp` and take it again. The probe script has no such trap:
> it does not use the library at all, which is exactly why it stays the tie-breaker.

## The probe script

`Tools/probes/telnet-cli-probe.ps1` is a minimal, self-contained Telnet client that reproduces
exactly what a correct PTY transport must do — Telnet IAC negotiation, VT100 cursor-probe answers, and
login with change-password-nag dismissal — then prints the RAW bytes the router returns (ESC shown as
`\e`, CR/LF as `\r`/`\n`). It deliberately does NOT use the tik4net library, so it gives you ground
truth to compare the library against.

Run it (Windows PowerShell 5.1):

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\probes\telnet-cli-probe.ps1 `
  -RouterHost <host-from-App.config> -User <user-from-App.config> `
  -Command ':put [/interface print detail as-value]', ':put [/system resource print as-value]'
```

Take the router coordinates from `tik4net.integrationtests/App.config` (`host`, `user`, `pass`).
The script takes `-RouterHost` (mandatory), `-User` (default `admin`), `-Pass` (default empty),
`-Command` (string array of CLI lines to send after login), `-CommandFile` and `-LoginTries`.
**Omit `-Pass` for an empty password** — passing `-Pass ''` through `powershell -File` is unreliable
(it errors "Missing an argument for parameter 'Pass'"); the default is already empty.

**Use `-CommandFile <path>` whenever a command contains parentheses** — or anything else PowerShell
parses as syntax. `-Command ':put [... where comment="a (b)"]'` fails with *"A positional parameter
cannot be found"*; a file (one command per line, `#` comments allowed) bypasses argument parsing.

The login retries on a fresh TCP connection, `-LoginTries` times (default 10). The probe answers the
VT100 cursor probe with a canned report, which sometimes desyncs the router's credential read into a
spurious *"incorrect username or password"* — so a first-attempt failure is expected occasionally and
is not evidence about the router or about the library. Waiting for the `Login:` prompt instead of
using fixed delays was measured **worse**: the probe cannot tell a prompt from an echo of one.

Interpreting output: each command echoes back first (with stray ESC bytes), then the data, then the
prompt `[user@identity] >`. Strip the `\e…` escapes and the echo/prompt to see the payload. If you
see only `\r\r\r\r] >` with no data, VT100 negotiation failed (see §VT100 below).

## RouterOS CLI quirks — ground truth (verified live, ROS 7.x)

These are the things that make terminal CLI different from the binary API. Canonical reference with
full context: `Docs/findings-cli.md`.

- **`print as-value` prints NOTHING to an interactive terminal.** It only materialises in script
  context — wrap it: `:put [/path print as-value]`. The `:put` output is ONE line, records joined by
  `;`, a new record starting at each `.id=` (singletons have no `.id` → one record).
- **`detail` is required for the full field set.** Bare `print as-value` returns only summary columns
  (e.g. `/interface` omits `default-name`, `mtu`, `rx-byte`). Use `print detail as-value`.
- **`print stats` for live counters.** `print detail as-value` does NOT include runtime counters like
  firewall `bytes`/`packets` — those need `print stats` (a separate print mode). Known limitation:
  tik4net's CLI layer can't request it yet, so those properties come back empty over CLI transports.
- **Quote `where` values with special chars.** `where address=192.168.1.1/24` matches NOTHING — `/`
  and `:` are operators in the where-expression. Use `where address="192.168.1.1/24"`. Bare id values
  (`where .id=*1`) are fine unquoted.
- **VT100 cursor probes must be answered.** RouterOS sends `ESC[6n` (and moves the cursor) to detect
  terminal size; if you never reply with a cursor report it assumes a 1×1 terminal and emits no output.
  Advertise a WIDE terminal in a real client, or long as-value lines get wrapped and corrupted.
- **Change-password nag** is `new password>` (not "change password") — dismiss with Ctrl-C (`0x03`).
- **`.NET Framework NetworkStream.ReadAsync` ignores ReadTimeout and CancellationToken** once a read is
  pending with no data → it hangs forever. Read by polling `stream.DataAvailable` + `Task.Delay`.
- **Prompt detection:** the prompt is redrawn (even before output), so match `TrimEnd().EndsWith("] >")`
  and wait for the stream to fall silent after it ("prompt + settle"); drain residual output after login.
- **Errors → exceptions:** `bad command name` / `syntax error` ⇒ no-such-command; `expected item id`
  (e.g. `remove`/`set` with an id that `[find]` can't resolve) ⇒ no-such-item; `already have such` ⇒
  duplicate. Scalars must be read via `print` — `get value-name=.id` is invalid.
- **`/system/script/run`** yields no per-line `!re` over a terminal (fire-and-forget action) — a known
  CLI gap, unlike the binary API.

## Workflow

1. Reproduce the failing/uncertain command with the probe script to get raw ground truth.
2. Compare against what the tik4net CLI layer builds/parses (`tik4net/Cli/CliCommandBuilder.cs`,
   `CliOutputParser.cs`) or against the binary API result via the `mikrotik` skill.
3. Apply the quirks above to explain the difference and fix the builder/parser/transport.
4. Every CLI transport (Telnet, SSH-PTY, MAC-Telnet, WinBox CLI) shares that layer and the same quirks —
   `tik4net/Cli/` (`RouterOsCliLogin`, `Vt100State`); a new one reuses it rather than re-deriving it.
