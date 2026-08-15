# findings-cli.md — investigation history

Superseded diagnoses, incidents and pinned measurements for CLI / PTY transports.
The current behaviour is in [findings-cli.md](findings-cli.md); this file is history and describes
nothing about how the code works today.

Indexed from [HISTORY.md](HISTORY.md), which carries the transferable lessons.

---

## Diagnoses that were wrong

### SSH assumed to be "exec, no PTY" — the opposite of how it had to be built

The original research (from MikroTik's own docs) concluded SSH exec without a PTY would be the
cleanest CLI transport to parse: no banner, no ANSI, a real exit code. Live implementation found the
opposite — RouterOS's exec channel doesn't produce usable `as-value` output any better than Telnet
does, so the SSH transport had to open an interactive `ShellStream` PTY and drive the exact same
VT100/login/echo stack as Telnet. The "cleanest transport" assumption was never tested against a live
router before being written down.

### The Telnet login modifier was documented backwards from what the code needed

The initial research recommended `admin+ct80w` (no colour, fixed 80-column width, no wrap) as the
login-name suffix for PTY sessions, reasoning that a narrow, wrap-free terminal would simplify the
ANSI stripper. Live VT100 negotiation work (in the same document, a few sections later) established
the opposite: RouterOS wraps long `:put` as-value lines into the terminal width and inserts `\r\n`
into the data, so the width advertised must be *wide* (>= 10000), not 80. The shipped code uses only
`+c` (colour off) and never pins a width — it answers the cursor-probe negotiation with a wide value
instead. The two sections of the original document silently disagreed with each other for a long time
because nothing forced them to be reconciled.

### `;` was known to be a hazard, but the fix that got written down never shipped

An early note ("Impact on the plan") called for either `:serialize to=dsv delimiter="#"` or an
escape-aware parser to handle fields that use `;` internally (route counts, wireless ranges, BGP
stats) and fields containing arbitrary text (file bodies, script sources). What actually shipped, much
later, is different from both options that were on record: a `.cli-json` marker
(`TikSpecialProperties.CliJson`) that switches free-text fields to `:serialize to=json`, parsed by a
dedicated `CliJsonParser`, gated on RouterOS 7.13+ support detected empirically per connection. The
`dsv`/`delimiter="#"` idea was never implemented; nothing in the codebase references it.

### An "off by one" was diagnosed and fixed before the real cause was found

During the investigation of the WinBox `mepty` terminal wedging on large output (documented in full in
`findings-mepty-byte-ack.md`), an earlier pass diagnosed and "fixed" an off-by-one in the pull logic.
It did not fix the wedge. The actual cause — `mepty`'s `Data` command being a *pull* protocol where a
response larger than one batch is never delivered unless the client keeps asking — was only found by
raw-byte wire tracing. The off-by-one fix is not mentioned anywhere in the current code or tests; it
was a red herring kept around only in the old write-up's phrasing ("my earlier running diagnosis...
was ALSO wrong").

---

## Measurement traps

### A login refusal that timed out for 30 seconds looked like it was testing the right thing

The only bad-credentials integration test was hardcoded to the binary API, so it ran there eleven
times (once per transport's runsettings file) without ever exercising a CLI login refusal. When CLI
transports were finally checked directly, RouterOS 7.23.2's actual refusal text ("Login failed,
incorrect username or password") matched none of the five phrases the CLI login's phrase-matcher
carried, so a rejected login on Telnet took the full 30-second receive deadline before failing (binary
API: 127 ms). The fix moved the authoritative signal from wording to position — RouterOS restarting
the `Login:` dialogue after credentials have been sent means rejected, regardless of what it says —
and cut Telnet's refusal time to about 1.3 seconds. The phrase list is now a cosmetic fast path, kept
for a better exception message, not load-bearing (verified by emptying it and confirming the
transcript tests still pass).

### A bug that erases its own trace: the empty-string round trip

`/system note set note=` (bare, mid-line) is a RouterOS syntax error — the parser consumes nothing for
an empty `name=` and stumbles on whatever follows. This went unnoticed for a long time because the
only test that exercised it was a round-trip test that restores the original (empty) note value at the
end. The failed restore left the router's note non-empty; the *next* run "restored" an already
non-empty value and passed. A red run happened once, quietly fixed the evidence of the bug on the
router, and every subsequent green run was testing a different, accidental case.

### A settled prompt is not proof the router answered the command that was actually asked

Every PTY read here waits for a shell prompt that has then gone quiet for a settle window, but that
only proves *some* command finished — not the one just sent. A response could still be in flight when
that settle window closed (the read returns as soon as the prompt is quiet; the next command's
pre-send drain only checks the socket at one instant), so a tail belonging to the *previous* response
could land ahead of the *next* command's echo and be silently accepted as its answer. This was
invisible in every existing test because on a healthy connection the real echo is always present by
the time a read returns (measured: the "echo missing" trace note fired zero times across full traced
runs on all five transports) — the fix (require the command's own echo before the settled prompt
counts) changes nothing observable on a healthy run and only manifests where it matters, which is
exactly why it went unnoticed for as long as it did.

---

## Incidents

### A router log line landing mid-response corrupted reads and mimicked write rejections

RouterOS ships with a default logging rule that echoes `critical` topics into every open terminal
session, not just the local console. Measured live: a `login failure` log entry, timestamped ~19
seconds before it was actually delivered (the router buffers it), landed inside the response window of
an unrelated command during a green suite run. Because the line has no `=` in it, it doesn't look like
a data line, but it also isn't blank, isn't a prompt, and isn't a fragment of the sent command — so the
existing head-trim logic mistook it for the first line of real output. On a read this glued the log
line onto the front of the first record; on a silent-on-success write it produced non-empty "output"
that the positional error rule read as the router rejecting the command. The fix teaches the output
cleaner to recognise and skip a line that starts with a wall-clock timestamp, in both the head-trim
loop and the echo-alignment residue check.

### Monitor commands driven through a read method silently lost every parameter they were called with

`/ping`, `/tool/traceroute`, `/interface/monitor-traffic` and `/interface/ethernet/monitor` are called
through `ExecuteList`/`LoadList` because they return rows, but their parameters are the command's own
inputs (`address=`, `interface=`), not a print filter. The synchronous CLI read path built these
commands with the ordinary print-query builder, which only understands print modifiers and a `where`
clause — so every input parameter vanished without a trace. `/ping =address=127.0.0.1 =count=2`
reached the router as `:put [/ping as-value]` and failed with "resolve failed"; a monitor-traffic call
against a bad interface name reached the router as a command missing the interface entirely, got back
"input does not match any value of interface" — a phrase no error classifier recognised — and the
caller received a *successful empty result* instead of an error. None of the affected tests had
coverage: they were all gated on `TikConnectionCapability.Streaming`, a flag reported only by the
binary API, so they reported Inconclusive on ten of eleven transports and nobody had actually run this
code path end to end.

---

## Measurements pinned to a moment

These were true when measured (RouterOS 7.23.2 unless noted) and are not maintained; re-measure rather
than citing them as current numbers.

| Measured | What | Value |
|---|---|---|
| Telnet login refusal, before the positional-signal fix | Full receive deadline wasted per rejected login | 30 193 ms (binary API for comparison: 127 ms) |
| Telnet login refusal, after the fix | Same measurement | 1 258 ms |
| SSH suite, mepty/byte-ack-adjacent work | Failure count before → after | 172 failures / 1 pass → 0 failures / 77 pass; SafeMode 3/3 |
| `:put [/ping … as-value]` streaming shape | Time to first (and only) byte of data vs. the bare interactive form | wrapped form: all 5 rows at once at ~4019 ms; bare form: first row at ~58 ms, subsequent rows roughly 1 s apart |
| Echo-alignment gate (`echo-missing` trace note) | Across full traced suite runs on all five CLI transports | fired 0 times — the gate costs nothing on a healthy connection |
