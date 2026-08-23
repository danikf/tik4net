# Project history — the index

The entry point for **narrative about the past**: diagnoses that turned out wrong, incidents, and
measurements pinned to a moment in time.

Everywhere else — [AGENTS.md](../AGENTS.md), [ARCHITECTURE.md](../ARCHITECTURE.md), the skills, and the
protocol findings in this directory — describes **how things work now**, in the present tense. When a
document would otherwise say "this used to be broken because…", the fact moves here and the document
keeps only the current behaviour.

This file holds the **lesson**; the full account lives in the `-history.md` sibling next to the
document it came from. An entry being here does not make it current: it records what was observed at
that time, on that RouterOS version, against that code.

| Full account | Covers |
|---|---|
| [`findings-cli-history.md`](findings-cli-history.md) | CLI / PTY transports |
| [`findings-winbox-history.md`](findings-winbox-history.md) | WinBox transport and session layer |
| [`findings-rest-api-history.md`](findings-rest-api-history.md) | REST transport |
| [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md) | WinBox native (structured M2) protocol and field vocabulary |
| [`winbox-m2-multiplexing-design-history.md`](winbox-m2-multiplexing-design-history.md) | M2 channel and request/reply correlation |
| [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md) | WinBox CLI `mepty` byte-acknowledgement |

## Why this file exists

Reference documentation that carries its own changelog is expensive to read and easy to misread. A
reader cannot tell, at a glance, whether "✅ FIXED" means the current code is correct or that a fix is
pending, and every superseded diagnosis left in place is a plausible-looking wrong answer sitting next
to the right one. Keeping history in its own files makes the reference documents short and
unambiguous, and makes the history itself easier to search.

---

# The lessons

These are the transferable ones — the reasoning failures worth recognising again, rather than the
individual bugs.

## Ask what result would have disproved the hypothesis

The throughput-ceiling finding first concluded the slowdown was *per-session state on the router*. It
is aggregate and router-wide. The conclusion rested on one comparison: a fresh connection was fast
while an aged one was slow — but the fresh connection made 24 requests against a knee that needs ~200,
and made them while the aged connection was paused. **The experiment could not have come out any other
way.**

Before trusting a confirming measurement, ask what result would have disproved the hypothesis. If the
design admits only one outcome, it has measured nothing.
→ [`findings-router-throughput-ceiling.md`](findings-router-throughput-ceiling.md)

## Test a hypothesis in the regime where the effect actually occurs

The `mepty` counter is a cumulative **byte** acknowledgement. That idea was raised early and dismissed
on *correct evidence reasoned backwards*: a 35-byte keystroke frame moved the counter by only +1. But
the counter acknowledges bytes **received**, not **sent**, so a keystroke frame should not move it by
35. Every trace informing that dismissal was of small output, where the window never closes — they
could not have separated the hypotheses whatever they showed.

The wrong theory ("the terminal degrades after ~10 commands") was a symptom measured in the wrong
unit: ~800 B per command against an 8192 B budget.
→ [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md)

## A trace timestamp records when *you* read, not when it arrived

SESSIONSTART acknowledgement latency measured as "81–108 ms" in every MAC session — because the client
slept 80 ms before reading, not because the router was slow. The same class of error left a wrong
measurement standing for nine hours during a WinBox session-wedge investigation.
→ [`findings-mactelnet.md`](findings-mactelnet.md), [`findings-winbox-history.md`](findings-winbox-history.md)

## Untagged diagnostics produce confident nonsense

One of two MAC RECV parsers omitted the session tag. Replaying a **green** run through a per-session
reconstruction attributed those lines to whichever session opened last and reported **311 stream holes
that never happened**.
→ [`findings-mactelnet.md`](findings-mactelnet.md)

## A gap in one transport is our bug until the router refuses

`/tool/wol` was recorded as a probable REST gap. The builder was posting `/tool/wol/print` and had
never asked for `/tool/wol` at all. This is the origin of the feature-parity rule in
[AGENTS.md](../AGENTS.md).

Related: "list/array field writes are not encodable over M2" was filed as a protocol limit. The wire
format supports array writes — it was an unimplemented encoder on our side.
→ [`findings-rest-api-history.md`](findings-rest-api-history.md)

Related, and the same shape a third time: `add` of an interface subtype over WinBox native was recorded
as refused by the router itself (`unsupported device type`), and eleven integration tests skipped on it.
The refusal was correct — the request never said what to create. The generic `[20,0]` handler's type
discriminator is the same field on a read and on an add, and native was only sending it on reads. The
eleven skips had been hiding three further defects underneath.
→ [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md)

## A skip is not a pass — a capability gate can hide a whole path

Gating synchronous monitor tests on `Streaming` made them Inconclusive on 10 of 11 transports, hiding
three real defects. A capability gate must be tied to an actual refusal by the router, not to a
plausible-sounding flag.

The same shape recurs in test guards: a known-gap entry makes an audit *skip* a path, so a fix that
lands afterwards is never verified.

## Read the request, not the reply

"No address was specified" is usually a correct complaint about a malformed request. The codec was
sending `addr` fields as bare strings and only understood IPv4.
→ [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md)

## A PoC that exercises the easy path proves less than it appears to

MAC-Telnet's ACK rule was wrong from the proof of concept onward, but `/interface print` — short
tabular lines — tolerates retransmission. Only the longer, time-sensitive terminal negotiation exposed
it. A latent defect inherited from a PoC, not a porting regression.
→ [`findings-mactelnet.md`](findings-mactelnet.md)

## Silent success is the worst failure mode

`/file/print` over CLI has worn three faces: an empty read (the test passes vacuously), a thrown
`Missing field 'name'`, and its current one — the call succeeds and returns 1 of 27 rows. Only the
middle is detectable without checking a row count.

Likewise `generate-key name=X key-size=2048` over WinBox native produced an unnamed 1024-bit key and
reported success, because the action dispatcher dropped every caller-supplied argument.

---

# Incidents

## The lab router's admin password was changed

A recycle cascade fed a desynchronised terminal into RouterOS's `new password>` nag; VT100 cursor
reports were typed into the prompt and two matching entries set a new password. There was no second
account, so recovery needed an out-of-band restore.

**Consequence:** no byte except Ctrl-C is ever sent while a password prompt is on screen, and the test
router is provisioned with a second full-privilege recovery account.
→ [`findings-mepty-byte-ack-history.md`](findings-mepty-byte-ack-history.md)

## MAC transports dead while IP transports worked

MAC-Telnet and WinBox-MAC stopped reaching the router while every IP transport, and MNDP discovery,
kept working. The cause was host-side: broadcast leaving a stale deprecated NIC. MNDP continuing to
work is what made it misleading — it is also broadcast, but was answered over a different path.

The proof-of-concept-era hypotheses (a CHR limitation, a source-port rule, Windows Firewall) were all
wrong.
→ [`findings-winbox-terminal.md`](findings-winbox-terminal.md) §7

---

# Measurements pinned to a moment

True when measured, not maintained. Re-measure rather than citing them.

| Measured | What | Value |
|---|---|---|
| 2026-07-26 | Full integration run, RouterOS 7.23.2, 390 tests | Api/ApiSsl ~5 min; Rest/RestSsl ~3 min; Telnet/Ssh ~7 min; MacTelnet ~13 min; WinboxNative ~5–8 min; WinboxCli ~7 min; WinboxCliMac ~1 h 20 min |
| 2026-07-26 | Same-version reinstall of the MCP global tool | Verified to deliver new code: a marker in `Program.cs` changed the installed assembly hash |

MAC transports pay roughly 5 s per command against ~200 ms over TCP, which is why `WinboxCliMac`
dominates a full run. That is a property of the MAC layer, not of the WinBox terminal.

---

# Phasing

`AsyncCommands` and `CancelInFlight` were not added to every transport at once: REST first, then the
whole CLI family, then the binary API, then WinBox native, each over its own awaited I/O. All in-tree
transports now declare both. The binary API's per-connection reader — dispatching each sentence to the
tag that asked for it, so an async command holds a registration rather than a thread — arrived in the
same phase, after `AsyncCommands` had already shipped for REST and the CLI family.

---

# Superseded artifacts

- **`tik4net.console`, `tik4net.torch`, `tik4net.coreconsole`** — three separate demo projects, replaced
  in 4.0 by the single [`samples/tik4net.samples`](../samples/tik4net.samples/README.md) app and its
  `console` / `torch` / `crud` subcommands. `tik4net.coreconsole` existed only to demonstrate that the
  library works on .NET Core, a question the `netstandard2.0` target settles on its own. Each shipped its
  own `App.config` carrying a lab router address; the sample takes coordinates as arguments instead.

- **`TestResults/test-failures-report.md`** (2026-06-20) — a baseline failure catalog with categories
  A–K. The directory is git-ignored and the file no longer exists; most of its entries did not
  reproduce when re-verified, having been orphan contamination or flaky timing rather than defects.
  Named here only so references to it in older material resolve. Do not reconstruct it: regenerate
  counts from TRX files after a clean run (`Tools/probes/parse-trx.ps1`).
