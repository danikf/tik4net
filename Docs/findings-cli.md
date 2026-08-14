# Findings — MikroTik RouterOS CLI (Command Line Interface)

**Source:** https://help.mikrotik.com/docs/spaces/ROS/pages/328134/Command+Line+Interface
**Date:** 2026-05-31
**Retrieval status:** Official documentation processed by a research agent (Sonnet). 📄 marks material from the documentation.
**✅ LIVE-VERIFIED 2026-05-31** during the implementation of Chapter C (Telnet) against the test CHR router (ROS 7.x).
Some of the original assumptions turned out to be INACCURATE — see **section 10 (live-verified findings)**,
which takes precedence over the earlier 📄 claims. Probe tool: [`telnet-cli-probe.ps1`](telnet-cli-probe.ps1).

> Purpose: groundwork for the CLI-based transports (Telnet/SSH/MACTelnet) — translating `ITikCommand`
> into a CLI string and parsing the output. This **complements** the existing design document
> [terminal-cli-parsing.md](../terminal-cli-parsing.md) (which holds the full implementation architecture
> for `CliConnectionBase`/`CliOutputParser`/`VtStripper`); this file only adds **new/confirming findings
> from the official docs**.

---

## 1. Best format for parsing: `print as-value`

📄 `print as-value` is **machine-readable** output, one line per record, fields as `key=value`
separated by `;` **with no spaces**. Both field names and boolean values (`yes`/`no`) are
**byte-for-byte identical to the API protocol** → the `tik4net.entities` mapper works unchanged
(see [terminal-cli-parsing.md](../terminal-cli-parsing.md)).

```
/ip/address/print as-value
→ .id=*1;address=192.168.1.1/24;interface=ether1;comment=;dynamic=no;disabled=no
```

Additional `print` modifiers:
| Modifier | Purpose |
|---|---|
| `as-value` | machine `key=value;…` output (**primary format for parsing**) |
| `without-paging` | disables paging (`-- [Q quit...]`) — **required on PTY transports** (Telnet/MACTelnet) |
| `detail` | human-readable detail; comment shown as a `;;;` prefix (NOT for parsing) |
| `terse` | a more machine-friendly line-based output (alternative) |
| `count-only` | record count only |
| `where <cond>` | filter (equivalent of the API `?name=value`) |

---

## 2. ⚠️ Critical caveat — semicolon `;` inside list-type fields

📄 Some fields use `;` as an **internal** list separator → in `print as-value` they look like
additional fields, and a **naive split-on-`;` parser will break on them**. Specifically reported for:
- `route-count` (and similar statistical aggregates),
- wireless `ranges=` (a list of frequency ranges),
- BGP statistics.

**Workaround (RouterOS 7.x):** `:serialize to=dsv delimiter="#"` — re-serializes with a different
separator:
```
:put [:serialize to=dsv delimiter="#" [/ip route print as-value]]
```
→ records/fields separated by `#` instead of `;`, so embedded `;` no longer matters.

**Impact on the plan:** `CliOutputParser` (Chapter B) should optionally support `:serialize`/`delimiter`
for entities with risky fields, or an escape-aware parser. For ordinary entities (interface, address,
firewall) a naive split-on-`;` is sufficient. → Update item 1 of "Open questions" in
[terminal-cli-parsing.md](../terminal-cli-parsing.md).

---

## 3. Transport: SSH exec vs PTY (Telnet/MACTelnet)

📄 **SSH exec (no PTY) is the cleanest transport for parsing:**
- no banner, no prompt, no ANSI escape codes,
- separate stderr and an available **exit code** → robust error detection,
- **BUT: the MikroTik SSH server does not support PTY for the exec channel** → every `RunCommand()`
  must be **one complete command** (no interactive session, no multi-line stateful sequences).

📄 **PTY transports (Telnet, MACTelnet):**
- output contains ANSI escape sequences → requires a `VtStripper` (already designed in
  terminal-cli-parsing.md),
- `print without-paging` is required on **every** `print` (otherwise paging blocks),
- output = command echo + data + new prompt → echo and prompt must be stripped (handled by
  `VtStripper.RemovePromptAndEcho`).

---

## 4. Telnet/PTY: login and prompt sequence

📄 Expect patterns for Telnet auth (watch case and the trailing space):
- `"Login: "` (capital L) → send username
- `"Password: "` (capital P) → send password
- `"] > "` → shell prompt (end of prompt; detect via **`EndsWith("] > ")`**, not the whole prompt —
  the identity can contain arbitrary characters)

📄 **Login modifier `admin+ct80w`** (appended to the username): disables ANSI colors (`c`), sets a
fixed width of `80` (`t80`), `w` = no wrap → **considerably simplifies `VtStripper`** (fewer escape
sequences, stable width). General form: `<user>+<flags>`. Recommended for Telnet/MACTelnet PTY
sessions.

📄 RouterOS may show a "change password" nag after login → send Ctrl-C (`0x03`) to skip it (matches
the WinBox terminal findings).

---

## 5. Comments and other parsing details

📄 Comment:
- `print as-value` → inline `comment=<text>` (parses like any other field),
- `print detail` (human) → `;;;` prefix on its own line (NOT for parsing — that's the WinBox
  terminal trap).

📄 Quoting/escaping: values with spaces/special characters are enclosed in `"..."`;
`;` separates commands on a line; `#` introduces a comment; `\` is a line continuation;
`[ ... ]` is command substitution.

---

## 6. Add / scalar via CLI (confirms terminal-cli-parsing.md)

📄 `:put [/ip/address/add address=10.0.0.1/24 interface=ether1]` → returns the **`.id`** of the new
record (e.g. `*3`), equivalent to the API's `=ret=*3`. Without the `:put [...]` wrapper, `add`
returns nothing/an index, not `*N`.
→ The mapper's `Save` (reading the new `.id`) must use the `:put [...]` form.

📄 Scalar: `:put [/system/identity/get name]` or `/path get .id=*N value-name=x` → a single value.

---

## 7. Monitor / streaming via CLI

📄 Continuous commands (`/interface/monitor`, `/tool/torch`, `/tool/ping`) produce ongoing output in
a PTY → use `once` for a one-shot read (`/interface ethernet monitor ether1 once`).
There is **no** reliable Streaming/`/listen` equivalent over CLI (see the capability gaps in
terminal-cli-parsing.md).

---

## 8. Open questions / to verify during implementation

1. `:serialize to=dsv delimiter="#"` — exact behavior and availability across ROS 7.x; how to parse
   the output (record vs. field separator). Verify against entities with `;`-bearing fields (route,
   wireless, BGP).
2. `admin+ct80w` — verify the modifiers work on both Telnet and MACTelnet and actually suppress
   colors/wrap.
3. SSH no-PTY: verify that `print as-value` via `RunCommand` returns clean output without needing
   `without-paging`.
4. Exact CLI error texts for mapping onto `Tik*Exception` (partially covered in
   terminal-cli-parsing.md §"Error detection").
5. Exact `once` form for the various monitor commands.

---

## 9. Impact on the plan (Chapters B/C/F)

- Confirms the choice of `print as-value` as the parsing format and `CliConnectionBase` with
  `SemaphoreSlim` (terminal-cli-parsing.md).
- **New:** add handling in `CliOutputParser` for `;`-in-values via `:serialize`/escaping (risky
  entities).
- **New:** Telnet/MACTelnet PTY sessions should use `<user>+ct80w` + `print without-paging` +
  Ctrl-C on the password nag.
- **New:** SSH `RunCommand` = always one complete command (no-PTY) — fits the
  `ExecuteCliCommandCoreAsync` model.
- Carry these findings into Chapters B (CLI layer), C (Telnet), F (SSH) once they're up.

---

## 10. ✅ Live-verified findings (Chapter C, Telnet, 2026-05-31)

The first live deployment of the CLI layer over Telnet revealed a number of things the documentation
either omitted or stated inaccurately. **This section takes precedence** over the earlier 📄 claims.
Detailed context: [`C-telnet-implementation-plan.md`](C-telnet-implementation-plan.md)
(section "Implementation results").

### 10.1 `print as-value` in an interactive terminal prints NOTHING ⚠️ CRITICAL
A bare `/interface print as-value` typed into a PTY (telnet) returns **nothing** (just echo + prompt).
`as-value` only materializes in a **script context**. It must be wrapped in `:put [ … ]`:
```
:put [/interface print detail as-value where name=ether1]
```
The output of `:put` is **a single line**, records chained together with `;`, where the **boundary of
a new record is `.id=`** (a singleton with no `.id` is a single record). In other words, this is NOT
one-line-per-record, as section 1 stated!
→ `CliOutputParser` therefore splits records on `.id`.

### 10.2 `detail` is required for the full field set
`:put [/path print as-value]` only returns **summary columns** (e.g. `/interface` omits
`default-name`, `mtu`, `rx-byte`…). Getting the full set (parity with the API) requires
`print detail as-value`. The O/R mapper controls this via `IncludeDetails` → the builder translates
it to `print detail`.

### 10.3 `print stats` — live counters (✅ RESOLVED via `IncludeCliStats`, 2026-06-01)
`:put [/path print detail as-value]` does **NOT** include runtime counters (`bytes`/`packets`,
`rx-byte`…). Counters only appear in `print stats as-value`, which is a **different column mode** —
it returns counters + `.id` + a handful of identity fields, but **not** the config fields.
`detail stats` together behaves roughly like `stats` alone (config-only fields disappear). **No
single modifier gives both config and counters** → this requires **two queries plus a merge by
`.id`**.

**Solution (commit `a72431c`):** a CLI-only metadata flag **`IncludeCliStats`** (on
`FirewallFilter/Mangle/Nat`, `Interface`, `QueueSimple/Tree`). The mapper adds a marker
`.cli-stats` (`TikSpecialProperties.CliStats`); when `CliConnectionBase.RunPrint` sees the marker it
issues `print detail` (config) plus `print stats` (counters) and merges records by `.id`. API/REST
**ignore** the marker (they get counters from `detail` already; the marker never reaches the wire) —
see `IsSpecialParam` in ApiCommand/RestRequestBuilder. Details:
[`cli-print-stats-design.md`](cli-print-stats-design.md).

### 10.4 `where` values with special characters MUST be quoted
`where address=192.168.1.1/24` (unquoted) **matches nothing** — in a `where` expression context,
`/` (and `:`) are interpreted as operators. It must be `where address="192.168.1.1/24"`. The safe
unquoted character set is `[A-Za-z0-9._-]`. `*N` (id) values must NOT be quoted (`where .id=*1`
works). → `QuoteForWhere`.
Note: `name=value` for `add`/`set` does NOT need `/` quoting (it isn't an expression context).

### 10.4b Inside quotes, `$` and `\` are special — a written value gets silently rewritten (P2.38)
The RouterOS console has no single-quote form at all (`:put 'a$b'` → `syntax error` at the `'`), so
double quotes are the only form — and inside them, **variable substitution** and **escape sequences**
both apply. Measured on 7.23.2 (probe `Tools/probes/telnet-cli-probe.ps1`), writing to
`/system script`:

| Sent | Router stores | Note |
|---|---|---|
| `source="x\$y z"` | `x$y z` | `\$` = literal `$` |
| `source="x$y z"` | `x z` | **silent substitution** — `$y` undefined → empty |
| `source="x\\y z"` | `x\y z` | `\\` = literal `\` |
| `source="x\"b"` | `x"b` | |
| `source="C:\temp\new w"` | `C:<TAB>emp<LF>ew w` | **silent** — `\t`, `\n` are known escapes |
| `source="x\y z"` | `syntax error` | unknown escape |
| `source=x$y` | `syntax error` | `$` outside quotes is never legal |
| `source=x\y` | `syntax error` | neither is `\` outside quotes |

Full escape set (MikroTik docs, verified for `\$ \\ \" \t \n \_ \41`): `\"` `\\` `\n` `\r` `\t` `\$`
`\_` `\a` `\b` `\f` `\v` `\<hex>`.

Consequences for `QuoteIfNeeded`: a value containing `$` or `\` **must** be quoted (leaving it
unquoted is a hard error), and inside the quotes **both** must be escaped (an unescaped one causes
silent corruption). Escape in the order `\` → `"` → `$`, otherwise the backslash pass would double
what the dollar pass added. An actual newline is NOT rewritten to `\n` (the router accepts a literal
line break inside open quotes; `\n` would also be indistinguishable from a value that genuinely
contains `\`+`n`) — CR/LF are only in the trigger set because unquoted they would terminate the
command line.

This is the **write**-side mirror of P2.17: there, a value got corrupted on read; here the router
rewrites it before storing it — the add succeeds, the `.id` comes back, nothing complains, and it's
the router's own stored copy that's damaged, so every other transport agrees on the wrong value too.

### 10.5 VT100 cursor-probe negotiation is MANDATORY
Without responses to RouterOS's cursor probe (`ESC[6n` → cursor report `ESC[row;colR`), the router
treats the terminal as 1×1 and **does not render the command's output** (typically just
`…\r\n\r\r\r\r] > ` with no data). The cursor position must be tracked and answered (`Vt100State` in
`tik4net/Cli`). A **large width** must be advertised (not 80), otherwise RouterOS wraps long
as-value lines and inserts `\r\n` into the data → breaking parsing.
Note: RouterOS measures the width with the probe `ESC[9999C ESC[6n`, so the reported column is
`min(Vt100State.Width, ~10000)` — `Width` must be **≥ 10000** (otherwise the client truncates its own
answer). For MAC-Telnet see [findings-mactelnet.md](findings-mactelnet.md) §1–2 (also critical: the
**ACK = counter + payloadLen** semantics).

### 10.6 Change-password nag = `new password>` (NOT "change password")
A router with a default/empty password shows `new password>` after login (and
`repeat new password>`). Decline with **Ctrl-C (0x03)**. Detected via the substring `password>`.
(`RouterOsCliLogin`.)

### 10.7 .NET Framework's `NetworkStream.ReadAsync` respects neither timeout nor CancellationToken
A pending read with no data blocks **forever** (`ReadTimeout` only applies to synchronous `Read`;
the CancellationToken is only checked before the read starts). Reading must instead poll
`stream.DataAvailable` with `Task.Delay` and track the deadline manually.

### 10.8 Prompt detection: redraw and "settle"
The RouterOS prompt redraws itself (`\r\r\r\r] > `) even BEFORE a command's output → a naive
"ends with `] >`" check matches prematurely. Solution: after login, **drain** the leftover redraw;
read a command's output until "prompt, then settled" (prompt at the end followed by ~120 ms of
silence). Compare the prompt suffix as `TrimEnd().EndsWith("] >")` (without the trailing space).

### 10.9 CLI error text → exception mapping (verified)
| CLI text | Mapping |
|---|---|
| `no such item`, `expected item id (line N column M)` | `TikNoSuchItemException` |
| `no such command`, `bad command name …`, `expected end of command`, `syntax error (line …)` | `TikNoSuchCommandException` |
| `already have such …`, `item with such name already …` | `TikAlreadyHaveSuchItemException` |
| `failure:` / `error:` / other error stream | `TikCommandTrapException` |

Note: `remove`/`set` with a nonexistent/invalid `.id` (`[find .id=…]` empty) → `expected item id`.

### 10.10 Scalar: `get value-name=.id` is invalid
`:put [/path get .id=*N value-name=.id]` → `get .id=` is a syntax error, and `value-name=.id` →
"input does not match any value of value-name". **Scalars must be read via `print`**, selecting the
value from the row (this also works for `.id`). Do not use `get value-name=…` for `.id`.

### 10.11 Action commands with no per-row output (`script run`) — ✅ supported
`/system/script/run` via the terminal **does run the script**
(`/system script run [find .id=*N]`), but it does not return per-row `!re` output the way the binary
API does (it's a fire-and-forget action). `CliConnectionBase.RunPrint` therefore routes the `run`
verb as an action and returns an **empty** result set. The `RunScript_Issue53` test is
transport-aware (`TestBase.IsCliTransport()`): on CLI it only checks that the run did not fail, on
API/REST it checks the `!re` row count. (commit `eb5e687`)

### 10.12 Monitor commands: `numbers=` + `once`
`/interface/ethernet/monitor` requires `numbers=<iface> once` before `as-value`:
`:put [/interface ethernet monitor numbers=ether1 once as-value]`. The builder passes the `numbers`
NameValue param and the `once` flag (a continuous monitor/torch would otherwise block in a PTY —
see section 7).

---

## 11. ✅ Live-verified findings (Chapter F, SSH, 2026-06-15)

SSH is **NOT** "exec without PTY" (as section 3 implied) but an **interactive PTY ShellStream** —
over the exec channel, RouterOS doesn't print `as-value` output any better than Telnet does, so it
uses the same PTY/CLI stack as Telnet. The shared `RouterOsCliLogin`/`Vt100State`/`CliOutputHelper`
work unchanged; SSH only adds ~280 LOC of transport (`tik4net.ssh`, a separate package because of the
`Renci.SshNet` dependency).

### 11.1 Auth is handled by SSH.NET — no Login:/Password: prompts
After `SshClient.Connect()`, the prompt is simply pulled in via
**`RouterOsCliLogin.ResolveToPromptAsync`** (nag→prompt, extracted from `LoginAsync`) plus a drain of
the post-login redraw. The username flag `+c` is accepted over SSH; it falls back to the plain
username on `SshAuthenticationException`.

### 11.2 Raw PTY modes
`CreateShellStream(..., terminalModes)` with
`ECHO/ICANON/ISIG/IEXTEN/IXON/IXOFF/ICRNL/INLCR/OPOST = 0` — RouterOS wants raw keystrokes (it has
its own VT100 editor). The RouterOS SSH server largely ignores these modes anyway.

### 11.3 ⚠️ Ctrl+D = SSH EOF → closes the channel (Safe Mode unroll)
The discard key **Ctrl+D (0x04)** is conventionally SSH EOF; the RouterOS SSH server **closes the
channel** on it (`ShellStream` disposed) regardless of the raw modes. Telnet doesn't have this issue
(a bare byte → console → undo). **Solution:** SSH unroll goes through the **scriptable
`/safe-mode/unroll`** (RouterOS 7.18+) — a normal command, the channel stays alive, in place.
Fallback (older versions, `TikNoSuchCommandException`): Ctrl+D as rollback-by-disconnect (+ `Close`).
Take/Release (**Ctrl+X**) work fine in place over SSH.

### 11.4 `ShellStream.DataAvailable` polling
Same pattern as Telnet's `NetworkStream` (poll + `Task.Delay`, deadline from `ReceiveTimeout`) — no
hang, echo/prompt trimming (`CliOutputHelper.CleanOutput`) holds up even under the raw modes.
**Result: SSH suite 172/1→0/77, SafeMode 3/3.**

## 12. ✅ WinboxCli/MacCli: mepty is a PULL protocol — large output stalls (P2.13)

**Status: FIXED 2026-07-23** (`WinboxCliClient.SendPull`). A high-risk RE area; verified live with
raw-byte instrumentation (temporary, removed — see P2.15 for promotion into the MCP).

### Symptom
A full solo `winboxcli` run: **35 deterministic failures** (not latency, not contamination from
parallel runs, not a counter-semantics issue — all disproven live). The failures were timeouts
(multiples of 30s), not slow responses. In isolation they pass; failure only starts appearing after
enough commands on a **shared** connection.

### Root cause (raw-byte trace)
The mepty `Data` command (`0x0A0067`) does TWO things: it **sends keystrokes AND pulls output**.
RouterOS replies to a single `Data` with **one batch** of whatever output is pending. A response
larger than a batch (on the order of a few hundred bytes — e.g. `print detail as-value` across
several records) only gets delivered if the client **keeps pulling**. Our client sent one `Data` per
command and then just passively read → large output never arrived.

Key detail: after a large response, RouterOS sends the **echo** of the next command, but no output —
and from the following command onward it **stops echoing entirely**
(`DataAvailable=False` for 30 s, `bufLen=0`) — the terminal is stuck for the rest of the session.
That downstream emptiness is exactly what the skill logged as "gotcha A" (add returns empty) and
"gotcha B" (second print empty). **One bug, not two.** (My earlier running diagnosis of an "off by
one" was ALSO wrong — only fixed by the raw-byte trace.)

```
SEND :put [… datapath print as-value]     → RETURN len=526 ✓ (large response)
SEND :put [… datapath print detail …]     → echo (228 B) → DataAvailable=False 30 s → timeout
SEND :put [… datapath print as-value]     → bufLen=0 (not even echo) → timeout
…                                          every further command comes back empty
```

### Fix
`WinboxCliClient.SendPull()` — an empty `Data` frame (no `Input` key, monotonic counter; same shape
as `SendTerminalReady`). `ReadCommandResponseSync` now pulls whenever the buffer is empty AND the
completion prompt hasn't arrived yet (`prompted==false`). Once the prompt arrives the output is
complete → just settle, no further pulling (otherwise it would churn needlessly). Verified: `print
detail` went from a 30s stall to a full 620 B result. `WinboxCliClient` is shared, so this also fixes
`winboxclimac`.

### Open / to refine
- `ReadUntilQuietSync` (Tab completion) does NOT pull — completion output is small, but if it ever
  exceeds a batch it would stall the same way. Candidate for the same pull, if/when it shows up.
- Cadence: pulls every ~`PollSleepMs` (20 ms) while waiting. Works; further optimization (pull only
  after N ms of silence) is cosmetic, not a correctness issue.
- The exact "batch" size was never measured (not needed for the fix). If it's a record/byte count,
  it could be tracked down in the webfig mepty JS — but pull-until-prompt is robust without knowing
  the threshold.

### Impact on already-shipped work
Unblocks `TestBase.SaveTracked`'s orphan sweep on this transport (it previously read over the same
broken connection). After the fix, reads work → the sweep also finds id-less orphans.

## 13. ✅ An empty value is not an empty token (P2.44, 2026-07-30)

Live-verified on RouterOS 7.23.2 over telnet. Applies to **all CLI transports** (shared
`CliCommandBuilder`).

### 13.1 `name=` mid-line is a syntax error

```
/system note set note= show-at-login=yes
  → expected end of command (line 1 column 37)     ← column 37 = start of `show-at-login`
/system script add name=X source=":put 1" comment=
  → *1                                             ← passes at END of line
```

So a bare `name=` doesn't pass an empty string — the parser consumes nothing for it and stumbles on
the next token. The correct form is the two-character literal `name=""`, which works in both
positions.

Why this went unnoticed for so long: the suite never stored an empty string. It only surfaced on the
`/system/note` round-trip test, which restores the original value at the end — and that value was
empty. The failure also left residue on the router (the restore didn't happen), so **subsequent runs
passed** — they were restoring an already non-empty text. Exactly the kind of bug that erases its own
trace.

### 13.2 `where name` and `where name=""` are opposite queries

Two `/system/script` entries, one with the comment `hello`, the other without:

```
:put [/system script print as-value where comment]      → the row WITH a comment
:put [/system script print as-value where comment=""]   → nothing
API  ?comment=                                          → nothing
```

`where <field>` is a test for "is set" (truthiness), while the API's `?field=` means "equals empty".
The builder used to send a bare `name` for both, so filtering on an empty value returned exactly the
complementary set. It's now distinguished by whether the parameter's value is `null` (→ bare `name`,
API `?name`) or an empty string (→ `name=""`, API `?name=`).

## 14. ✅ The router writes into a live terminal on its own (P2.47, 2026-07-31)

RouterOS ships by default with a `/system/logging` rule `topics=critical action=echo`, and `echo`
does not mean "to the local console" — it means **into every open terminal session**. A line nobody
asked for can therefore land in a session at any time:

```
21:18:05.412 telnet.sock RECV | <CR>23:17:46 echo: system,error,critical login failure for user
                                admin from 192.168.4.31 via api<ESC>[K<CR><LF><CR><ESC>[9999B[admin@CHR] >
```

Measured on a wire trace of an entire (green) telnet suite run. Properties worth knowing:

- **It is not a login banner.** It arrived on a long-established session, between two tests, with no
  IAC negotiation nearby. (The banner also prints recent log lines, so it's easy to confuse the two
  when scanning a trace — filter by whether `<FF><FD>` negotiation is nearby.)
- **The router buffers it.** The timestamp in the line was ~19 s older than the moment of delivery,
  so cause and effect are not adjacent in time.
- **It only arrives on a session that is currently idle.** An attempt to force it during a 20s read
  (`/system script run` with `:delay 20s`) delivered nothing; in the actual incident the session was
  idle between commands.
- **A redrawn prompt follows the injected line** (`<ESC>[9999B[admin@CHR] > `), so a read that is in
  progress will still see the prompt at the end.
- **It can be triggered** by a failed login from a second session — `login failure` is `critical`.
  The log entry appears for both telnet and API (`/log/print ?message=login failure...` confirms
  it); delivery into a different session, though, depends on that session being idle, so it isn't a
  reliable injector.

If the line arrives **between commands**, `DrainAsync` swallows it and nothing happens — which is
also what occurred above. It becomes dangerous when it arrives **after** the drain, i.e. right at the
start of the response to the next command: in `CliOutputHelper.CleanOutput`, the header skip-loop
then stops (the log line isn't blank, isn't the prompt, and isn't a command fragment), and **the echo
of the command after it leaks into the data**. On a read, the echo attaches itself before the first
record; on a silently-successful write, it produces non-empty "output" that the positional rule from
P2.12 reads as a router rejection. Fixed by having the header also skip the log line — the
line-joining loop was already discarding it later, so nothing new is lost.

## 15. ✅ `:put [… as-value]` prints nothing until the command finishes (P2.50, 2026-07-31)

Streaming over CLI **is a property of the command's shape, not of the read loop**. `:put` receives an
already-complete array, so the router doesn't send a single byte until the command finishes.
Measured on 7.23.2 (telnet, timestamps are from when the command was sent):

```
:put [/ping address=127.0.0.1 count=5 as-value]
  +     5 ms     49 B  command echo
  +  4019 ms    322 B  .id=*0;host=…;seq=0;…  ← ALL five records at once, at the end
  +  4066 ms     24 B  prompt
```

The bare interactive form of the same command streams:

```
/ping address=127.0.0.1 count=5
  +     4 ms     33 B  command echo
  +    58 ms    150 B  header + seq=0 row
  +  1008 ms     68 B  seq=1
  +  2025 ms     68 B  seq=2
  +  3021 ms     68 B  seq=3
  +  4003 ms    148 B  seq=4 + summary sent=/received=/min-rtt=…
```

This holds across the whole CLI family (telnet, ssh, winboxcli, winboxclimac, mactelnet) — it's
router behavior, not transport behavior. `LoadAsync<ToolPing>(count=20)` over CLI therefore used to
return **0 rows for 20 s and then all 20 at once**; both the API and the two native transports stream
the same command row by row.

**What was done about it.** Self-terminating monitors (`ping`, `traceroute` —
`CliMonitorVerbs.Kind.Once`) are now sent in bare form (`CliCommandBuilder.BuildInteractiveMonitor`)
and read line by line: every CLI read loop calls `CliLineStreamer.Feed(stripped)` at the point where
`stripped` is already computed for prompt detection anyway. The prompt remains the sole terminator of
the read — streaming doesn't change *when* the read finishes, only when the caller learns about
individual rows. Continuous monitors (`monitor-traffic`, `profile`, …) are unchanged: they're polled
with a `once` snapshot every 500 ms, so rows already flow that way.

### Columns are read by offset, not by whitespace splitting

An empty column stays empty and the ones after it print in their own positions. A timeout row only
carries SEQ, HOST and STATUS:

```
  SEQ HOST                                     SIZE TTL TIME       STATUS
    0 127.0.0.1                                  56  64 107us
    1 192.168.4.99                                                 timeout
```

Splitting on whitespace would turn the second row into `[1, 192.168.4.99, timeout]`, landing
`timeout` in `size`. Measured offsets: header `SEQ[3..5] HOST[7..10] SIZE[48..51] TTL[53..55]
TIME[57..60] STATUS[68..73]`, values `0[4] 127.0.0.1[6..14] 56[49..50] 64[53..54] 107us[56..60]
timeout[67..73]`. Note that a value can start **one character to the left** of its header
(`107us` at 56 under `TIME` at 57) — the router reserves one padding character before the column —
and a left-aligned value can overflow far past its own header (`192.168.4.99` under `HOST`). A
column therefore spans from one character before its own header to one character before the next
header; that's the one rule that places both row shapes correctly (`CliTableParser`).

**Field names = the header, lowercased**, which happen to match the binary API's field names
exactly (`seq`, `host`, `size`, `ttl`, `time`, `status`). This form is actually more faithful than
as-value: the API reports `time=60us` the same way the table does, whereas `as-value` prints the
same field as `00:00:00.000060`. The summary row (`sent=…/received=…/min-rtt=…`) is discarded — it's
a summary of the table, not a row of it, and it arrives after the last record, so it can't be
attached to rows the way the API does it.

### Why offsets can't be tracked by position in the buffer

`VtStripper.StripAnsi` runs over the **entire** accumulated text on every pass. When a chunk boundary
falls in the middle of an escape sequence, one pass leaves it alone (it's incomplete) and the next
pass strips it — shortening the text *before* the current position. A pointer into the string would
therefore drift and deliver shifted data. `CliLineStreamer` therefore counts **delivered lines**, not
characters.

---

## 16. ✅ Monitor via the READ method lost all its parameters (P2.51, 2026-08-01)

**Context.** `/ping`, `/tool/traceroute`, `/interface/monitor-traffic` and
`/interface/ethernet/monitor` are invoked via a **read** method (`ExecuteList` / `LoadList`) — they
return rows, so consumers use them correctly. But `CliConnectionBase.RunPrint` sent them into
`CliCommandBuilder.BuildPrint`, which only knows about print modifiers and the `where` clause. The
command's own inputs were **vanishing without a trace**.

Measured on 7.23.2 over Telnet:

```
/ping =address=127.0.0.1 =count=2
  >> :put [/ping as-value]
  << failure: resolve failed

/interface/monitor-traffic =interface=ether1
  >> :put [/interface monitor-traffic once as-value]
  << input does not match any value of interface
  → caller received "OK (no data returned)"          ← silent failure (P2.12 class)
```

The async path never had this bug — it builds via `BuildMonitorSnapshot`, which does emit the
inputs.

**Fix.** `RunPrint` now routes monitor verbs (`CliMonitorVerbs.IsSyncMonitorVerb`) to
`BuildMonitorSnapshot(..., includeFilters: true)`. `includeFilters` is needed because
`TikGenericCommand.ResolveParamsForRead` rewrites the caller's parameters into Filter form before the
transport ever sees them; a monitor has no query semantics of its own, so Filter can only mean this
rewrite here. Same reason and same switch as `/tool/wol` (`BuildNonQuery`).

### `traceroute` rejects `once`

The default snapshot modifier is `once`, and traceroute **won't accept it**:

```
:put [/tool traceroute address=127.0.0.1 count=1 once as-value]
  << bad parameter once (line 1 column 54)
:put [/tool traceroute address=127.0.0.1 count=1 as-value]
  << .id=*1;address=127.0.0.1;avg=0;best=0;last=0;loss=0;sent=1;status=;std-dev=0;worst=0
```

`CliMonitorVerbs.Modifiers` therefore maps `traceroute` → `count=1` (same as ping). Before P2.51,
every CLI traceroute that reached the modifier got rejected by the router. The same applies to
`torch` — `bad parameter once (line 1 column 40)`, and its `as-value` form additionally prints
nothing at all — which is why it is **not** in the list of synchronous monitor verbs.

### A silent failure is recognized by position, not by phrase

The phrase `input does not match any value of interface` isn't caught by any classifier in
`CliErrorParser`. But a monitor exists to print something: a successful snapshot always emits at
least one `.id=…` record, a failed one only prints its complaint. `ParseMonitorSnapshot` therefore
treats **output with no record** as a rejection — no phrase list, no verb whitelist needed. Empty
output isn't itself an error (torch legitimately prints nothing). For the streamed form (P2.50), the
**table header** plays the same role: the router prints it before the first measurement, so text that
never produced a header is a rejection.

### `#` in the table is not a field

The interactive traceroute table starts with a `#` column — the terminal's row sequence number. The
binary API returns `.id` in its place and has no `#` field at all, so `CliTableParser` keeps that
column for offset purposes but never emits it as a value.

### It had no coverage anywhere

All the synchronous monitor tests had `EnsureCapability(TikConnectionCapability.Streaming)`, which is
reported **only by the binary API** — so on ten of the eleven transports they were Inconclusive, and
the entire synchronous monitor path had no coverage at all. Yet `ToolPing.Execute` is a plain
`LoadList` and doesn't need `Streaming`. See `[[feedback_silent_failures_are_invisible]]`.

## 17. ✅ A prompt is not proof the router is answering *you* (P2.47, 2026-08-02)

Every PTY read here ends the same way: wait until the accumulated text ends at a shell prompt, then
wait `SettleMs` (120 ms) more to be sure nothing follows. That test asks whether **a** command has
finished, never whether **this** one has — and the two come apart, because a response can arrive
after the read that asked for it has already returned:

- the read returns as soon as the prompt has been quiet for 120 ms;
- the next command's pre-send drain is gated on `DataAvailable` **at one instant**;
- so a tail that arrives after both is still in the socket when the next command goes out.

That tail then lands ahead of the next command's echo, and both layers accept it:

| layer | what it saw | what it did |
|---|---|---|
| read loop | the leftover **prompt** settles for 120 ms | returns before the router has said a word |
| `CleanOutput` | leftover **output** is not blank, not a prompt, not a command fragment | stops the head-trim there and calls it the first data line |

Neither is detectable from the result. A read gets the wrong row; a silent-on-success write gets
non-empty "output" that P2.12's positional rule reads as a rejection; and an `add` gets back an
`.id` that was never created here — which fails one call later, in the read-back, with `no such
item`, pointing at everything except the cause.

### The echo is the anchor

The router echoes the command before it answers it, so the response carries its own identity. Both
layers now use it (`CliOutputHelper.ContainsEcho` / `SkipForeignResidue`):

- a settled prompt terminates a read **only once this command's echo is on screen**;
- content in front of the echo that is genuinely foreign — not blank, not a repainted prompt, not an
  asynchronous log line (§14), not a partial echo — is dropped, and noted to the wire trace on
  channel `cli.align`.

Matching is on whitespace-squashed text (the line editor repaints the echo, it does not replay the
bytes) and only on a leading 40-character slice, since the tail of a long `add` may be wrapped or
repainted separately. The **first** match wins, which is what makes a false match harmless: a record
that quotes the command back — a stored script `source`, say — can only appear after the echo that
introduced it.

### Why the gate is free

Measured over full traced suite runs on all five CLI transports, `echo-missing` fired **0 times**:
at the moment each read returned, its own echo was already present. So requiring it changes nothing
on a healthy session — and where the echo is merely late, waiting for it turns a wrong answer into a
correct one rather than into a failure. Only a response that never arrives now reaches the receive
deadline, where the read already throws (`CliReadTimeout`).

### What it does not fix

Two identical commands in a row — the poll-and-diff Listen emulation issues those — cannot be told
apart this way: the previous response's echo satisfies the gate just as well as our own. The splice
is then invisible to both layers, and shows up (if at all) as a duplicated row rather than a wrong
one.

---

## 18. ✅ What the CLI login assumes about a RouterOS version, and how much of it is checked (P2.24)

The session bring-up shared by all five CLI transports (`RouterOsCliLogin`) reads a screen and decides
what state the router is in. Every decision is a guess about **wording and layout**, both of which
differ by version *and* by router state — and a guess that is wrong here does **not** fail. There is no
error channel on a terminal: an unrecognised screen simply never satisfies the predicate, so the read
runs to the receive deadline and the caller gets something plausible, late. That is the same shape as
the safe-mode prompt (P2.31, 30 s per command with the tests green) and the refusal wording below.

### 18.1 The assumptions, and what each one costs when it is wrong

| Assumption | Where | Evidence | Cost if wrong |
|---|---|---|---|
| Login prompt contains `ogin:` | `IsLoginPrompt` | 7.23.2 | Credentials typed into a screen that is not asking for them |
| Password prompt contains `assword:` | `IsPasswordPrompt` | 7.23.2 | Same |
| Nag contains `password>` | `IsChangePasswordNag` | 7.23.2 | Ctrl-C never sent → login ends at the deadline; worse, **bytes meant for the shell land in the new-password field** (P2.13c) |
| Prompt ends `] >` / `] <SAFE>` | `EndsWithPromptSuffix` | 7.23.2 (+ one historical form) | 30 s per command, results still "correct" (P2.31) |
| Refusal wording | `IsLoginFailure` | see 18.2 | Full receive deadline per rejected login |
| `+c` login flag accepted | `TerminalLoginFlags` | 7.23.2, all CLI transports | SSH falls back to the bare name; Telnet would fail the login |

### 18.2 The refusal wording did not match, and could not have told us

RouterOS 7.23.2 answers a wrong password with:

```
\r\nLogin failed, incorrect username or password\r\n\r\nLogin:
```

None of the five phrases `IsLoginFailure` carried matched it. Measured 2026-08-14 with the same
credentials on the same router: **binary API 127 ms, Telnet 30 193 ms** — the CLI login waited out the
whole receive deadline and then threw a login exception quoting the very text it had failed to
recognise. The suite never noticed because the only bad-credentials test
(`ConnectionTest.OpenConnectionWithInvalidCredential_WillFailWithProperException`) is hardcoded to the
binary API, so it runs eleven times against one transport.

**Fix — the signal is positional, not lexical.** After a refusal RouterOS *restarts the login
dialogue*, so a `Login:` prompt arriving **after credentials have been sent** means rejected, in any
language and on any version. `ResolveToPromptAsync` takes `loginPromptMeansFailure`, set only by the
interactive login (a transport that authenticated below the terminal never sends credentials, so a
`Login:` string reaching it is not evidence of anything). Telnet now reports in **1 258 ms**. The
phrase list is kept as a fast path and a better message, and is *not* load-bearing: with it emptied,
the transcript tests still pass.

### 18.3 SSH accepts a wrong password for a password-less account — and that is the router

`admin` with an **empty** password authenticates over SSH with method `none`:

```
ssh -o PreferredAuthentications=none admin@<host> "/system/identity/print"   →   name: CHR
```

The server grants the shell without ever checking a password, so a wrong one is accepted. Telnet and
WinBox-CLI, which have no such method, reject the same credentials.

**Measured per account** (`SshAuthProbeTest`, 7.23.2, 2026-08-14) — and what is granted is a full
session, not a bare shell: each OPENED row below ran `/system/identity/print` **and** `/user/print`
and got real data back.

| account | password given | result |
|---|---|---|
| `admin` (no password) | correct (empty) | OPENED — identity `CHR`, `/user/print` 2 rows |
| `admin` (no password) | **wrong** | **OPENED — identity `CHR`, `/user/print` 2 rows** |
| `test` (has a password) | correct | OPENED — identity `CHR`, `/user/print` 2 rows |
| `test` (has a password) | **wrong** | **REFUSED — `Permission denied (password)`** |

So SSH *is* an access control — for an account that has a password. An account without one has no
check at all, and the password a client sends is never examined. This is RouterOS policy, not
something a client can detect or refuse: `LoginFailureTest` reports Inconclusive for SSH while
`App.config` points at a password-less user, rather than pretending to cover it. Point it at a
password-protected account to cover SSH properly.

### 18.4 The transcripts are now data

`tik4net.unittests/Cli/RouterOsTranscripts.cs` holds the 7.23.2 byte streams captured off the wire
(banner, nag — which the router **repaints**, so one read carries `new password>` twice — prompt,
refusal), and `FakeRouterTerminal` replays them into the real state machine. A second RouterOS version
is a new block in that file, not another live campaign. Two of the tests exist specifically because
the failure mode is silence: one asserts nothing but Ctrl-C is ever sent while the nag is on screen,
and one refuses a login whose wording nobody here has seen (`Authentisierung fehlgeschlagen`) to prove
the positional signal carries it alone.
