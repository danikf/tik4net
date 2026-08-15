# findings-winbox.md — investigation history

Superseded diagnoses, incidents and pinned measurements for the WinBox transport and session layer.
The current behaviour is in [findings-winbox.md](findings-winbox.md); this file is history and describes
nothing about how the code works today.

Indexed from [HISTORY.md](HISTORY.md), which carries the transferable lessons.

---

## Diagnoses that were wrong

### WinBox mepty SESSION_ID — misdiagnosed as terminal drain timing (§1)

**Symptom:** the mepty terminal wouldn't open — `OpenTerminalSession` threw `InvalidOperationException:
No SESSION_ID in M2 response`.

**First diagnosis (wrong):** "drain timing between terminal sessions" — the proof-of-concept marked
both mepty tests `[Ignore]` on this assumption, and separately worried that opening a fresh mepty
session per command (rather than reusing one) might also be implicated.

**Actual cause:** RouterOS session ids are not always byte-sized. A live dump of the mepty-open response
showed a SESSION_ID of 265 encoded as u32 (type `0x08`), not u8 (type `0x09`). The PoC's
`M2Message.ParseSessionId` only recognised type `0x09`, found nothing, and `SessionIdField(int)` always
encoded u8 — so sending 265 back became `(byte)265 = 9`, addressing the wrong session and killing the
terminal. Nothing about timing was involved; the fix was purely `ParseSessionId`/`SessionIdField`
handling both wire types. The reuse-of-session question was a red herring too: opening the mepty session
once and reusing it for every command (as production does) works fine and was never the problem.

This had already been predicted, and then not acted on: an earlier round of PoC notes said "Correct
SessionIdField implementation — in reality it may be 2B", but the drain-timing hypothesis from the
ignore-comment was chased instead.

### MAC-WinBox — PoC assumed MAC-Telnet's own auth/framing, both wrong

**Symptom:** the PoC's `WinboxMacClient` was left `[Ignore]`d as "EXPERIMENTAL, M2 framing unverified".

Two mistaken hypotheses, disproven live against RouterOS 7.21.4:

1. **"Auth is MAC-Telnet control-packet auth."** The PoC called the base
   `MacLayerTransport.Authenticate` (`CTRL_BEGINAUTH`/`PASSSALT`) and got a timeout — the router never
   replies on that path over this service. The actual behaviour is the same WinBox EC-SRP5 handshake as
   over TCP, sent as length-prefixed `[len][0x06][payload]` frames as DATA payload — confirmed by
   capturing the challenge as `31-06-0E-22-…` = `[len=49][tag=0x06][32B xWB][1B parity][16B salt]`.
2. **"Encrypted M2 is a bare `Encrypt(m2)` inside DATA."** The PoC sent `Send(PKT_DATA, Encrypt(m2))`
   and decoded the raw payload, decrypting from the wrong offset and producing "Not a valid M2 response".
   The actual behaviour reuses the same chunk framing as over TCP (`[chunkLen 1B][tag][data]…`, `0xFF` =
   continuation) carried inside DATA packets, reassembled via a receive buffer before decryption.

Both wrong guesses came from assuming WinBox-over-MAC was a variant of MAC-Telnet, when it is in fact
the full WinBox protocol tunneled over the same carrier MAC-Telnet uses. That is also why the CLI engine
(`WinboxCliClient`) could later be shared unchanged between the TCP and MAC-layer transports — only a
channel abstraction was needed, once the framing assumption was corrected.

### `0xFE0015` — first read as a bare "more pages" flag from a truncated transcription

An earlier pass over the webfig JS (2026-08-13) recorded this control-flow fragment:

```js
else if ((rep.ufe0003 != null || rep.mfe0015) && !me.block) {
    if (rep.ufe0003 != null) req.ufe0003 = rep.ufe0003;
    post(req, onreply);
}
```

— but the transcription had silently dropped the `if (rep.mfe0015 != null) req.mfe0015 = rep.mfe0015;`
line. Without it, `mfe0015` looked like a bare boolean "more pages" signal rather than what it actually
is: a second continuation token, echoed back verbatim exactly like `ufe0003`, only carried as a
message-array (`m` prefix, ftype 21) instead of a u32. Re-reading the file verbatim at its actual byte
offset recovered the missing line.

**Consequence of the truncation, not just a documentation gap:** the client's own getall loop
(`WinboxNativeM2Operations`) only ever attached `ufe0003` to the next request, never `mfe0015` — so a
handler that pages exclusively via `mfe0015` would have silently returned only its first page. This sat
open as a known gap for some time until `WinboxM2Continuation` was added
to carry the raw TLV bytes of *both* continuation keys, whichever the reply set. A catalog sweep across
five archived `.jg` catalog versions found no textual trace of `fe0015` in any of them, but the same
sweep also failed to find `fe0003` — the continuation key already known to be in live use — which
established that catalog absence is not evidence either way for a protocol-level (non-displayed) field,
closing off catalog-searching as a way to settle the question. Only a live trace of an actually-paging
handler could have settled which handlers use `mfe0015`, and none was found before the client was fixed
to handle both keys regardless.

### `0xFE0019` — suspected as a "more frames follow" flag, and as continuation-adjacent

Before the current understanding (an informational object count, never read for flow control) was
confirmed against webfig's only two uses of the field, an earlier version of this material floated the
idea that `0xFE0019=u8:1` might signal that more frames follow a getall reply. Reading the field's only
two use sites in webfig (`ObjectMap.prototype.getall`/`listen`, both just `me.objCount = rep.ufe0019`)
settled it: the value is stored and never consulted for looping or termination.

### The singleton write path was entirely missing, undetected by the test suite (§14)

**Symptom:** every write to a `.jg` `type:'item'` singleton (`/system/identity`, `/ip/dns`,
`/ip/settings`, `/snmp`, `/system/note`, and roughly thirty others) failed with `no such item: could not
resolve record .id '' on '/system/identity/set'`.

**Cause:** `0xFE000E` (`setcmd(holder)`) had been documented in the protocol reference from the start,
but the WinboxNative transport never called it — writes only ever went through the generic
`0xFE0003`/`.id` path used for ordinary tables, and a singleton has no `.id` at all.

**Why it went unnoticed:** the integration suite only ever *read* singletons, never wrote to them, so a
code path that was completely absent produced no failing test until someone tried a write by hand.

### A stuck MAC-layer session — "the router no longer has that session" was an overstatement

An earlier version of the §16 material concluded that the router had dropped the session entirely.
That overstated what the evidence showed: the router's own log recorded nothing at all at the moment of
the wedge — no logout, no error — so the precise claim the data supported was narrower: the router's MAC
layer stops acknowledging the client's bytes, while the router's own accounting has no record of any
session ending. The distinction mattered for where to look next; the corrected framing is what led to
finding the real cause in §17/§18.

### The §17 MAC-session wedge — two more wrong turns before the real cause

While chasing the two wedges that a Safe Mode rollback didn't explain, the investigation stated (and
later had to retract) two successive conclusions:

1. **"It's a property of the MAC carrier, not the CLI engine."** Drawn from a single contrast with
   `WinboxCli` over TCP (unaffected). `MacTelnet` was only measured afterward — and it survives too.
2. **"So it's exclusively their combination — the router's `mac-winbox` service."** Ruling out two
   things (TCP, and MAC-Telnet's own protocol) does not put the remaining culprit on the router; it just
   means the difference lies somewhere else, and that somewhere else turned out to be **client-side**:
   `MacTelnetUdpClient` has had a receive pump since it was written, and WinBox-over-MAC did not, until
   §18 added `StartIdleServicing`. With the pump added, the reproduction rate for the sibling-teardown
   probe fell from 5/5 wedges to 0/2.

### The §17 idle-logout hypothesis — disproved by a measurement of the wrong thing, and it stuck for nine hours

An earlier pass ruled out "the router logs out an idle MAC console, like MAC-Telnet does" with the
claim: "per-session trace shows the session receiving packets right up to the start of the test — it
lives and dies only on the first command of that test." That measurement was wrong, not the hypothesis:
a trace timestamp records when the client's socket was *read*, not when a packet physically arrived, so
an unserviced session shows no gap in its own trace — its entire backlog dumps out at once on the next
read. The tell was there in the same trace all along: the same `counter=…` value arriving ten times in a
row at the moment the victim test starts, meaning the router had been retransmitting one unacknowledged
packet the whole time. This wrong conclusion was marked resolved in the notes for roughly nine hours
before the idle-gap correlation (two gaps ≥3 s in an entire 340-test run, two wedges) surfaced and
reopened it.

---

## Measurement traps

- **A trace timestamp records when the *client* read a packet, not when the router sent it** (§17→§18).
  Applies symmetrically to the SESSIONSTART-ACK latency trap already recorded for MAC-Telnet in
  `Docs/HISTORY.md`.
- **Absence of a field from every version of the `.jg` catalog proves nothing about whether the protocol
  uses it**, because the catalog only declares a window's *displayed* fields, and a pagination cursor is
  never displayed (§12.7.1, the `fe0015`/`fe0003` sweep above).
- **A single-exchange trace can make an unrelated field look like a correlation id.** `0xFF0003` happens
  to equal the request id in a trace of exactly one round trip (both `2`); only a multi-request trace
  (the `/ip/address/print` reference-resolution trace, §12.1) exposes that it stays constant while the
  request id increments.
- **`DataAvailable`-style polling can measure "did anything arrive" instead of "is my frame ready"**, and
  the cost of the gap is invisible until measured per-span: the WinboxCliMac latency breakdown (§15)
  showed the first byte arriving exactly as fast as over TCP, with the entire 5 s loss concentrated
  after the prompt — a fact a coarser "time the whole command" measurement would not have located.

---

## Measurements pinned to a moment

| Measured | What | Value |
|---|---|---|
| (WinboxCli, TCP) | `WinboxCliProtocolTest` | 2/2 (login+list interfaces, set+verify ether1 comment) |
| (WinboxCli, TCP) | `InterfaceTest` over `winboxcli.runsettings` | 9 pass / 6 skip / 0 fail (skips: CLI capability limits) |
| (WinboxCli, TCP) | typical timing | login ~0.6 s, set+verify ~2 s |
| (WinboxCliMac, MAC) | `WinboxCliMacProtocolTest` | 2/2 (login+list, set+verify ether1 comment) |
| (WinboxCliMac, MAC) | typical timing | login ~16 s, set+verify ~32 s (MNDP ~5 s + per-frame AES + UDP polling) |
| (WinboxCliMac, MAC) | full integration run before the `DataAvailable`/§15 fix | 313 pass / 9 fail, 1 h 22 m |
| (WinboxCliMac, MAC) | same 9-test subset, before vs. after a `Socket.Poll(20 ms)` experiment | 6 fail / 3 m 14 s → 5 fail / 2 m 45 s (~15% better, still red; reverted — the remaining ~85% traced to `DataAvailable` polling, fixed properly in §15) |
| (§16/§17 wedge) | dropped sessions per full run, before any fix | 3 |
| (§17 Safe Mode fix: `SafeModeTest.OnCleanup`) | dropped sessions per full run | 3 → 2 |
| (§18 idle-servicing fix) | dropped sessions per full run | 2 → 1 (the remaining one is the genuine ~30 s router idle-logout) |
| (§17) | Safe Mode rollback reproduction (`WinboxCliMac` held, sibling rolls back) | 5/5 wedges before the §18 fix, ~4.3 s recovery each |
| (§17) | same probe, `MacTelnet` held instead | 0/2, ~0.15 s |
| (§17) | same probe, `WinboxCli` (TCP) held instead | 0/2, ~0.37 s |
| (§19) | unacknowledged-queue depth against an idle session, 45 s idle probe | 23 packets deep before the send-window throttle; 2 after |

---

## Superseded artifacts

- **A private phase-plan document (`winbox-native-m2-plan.md`) was cited as the source for the full list
  of M2 protocol constants and their `0xFE00xx` collisions.** That document was never part of this
  repository (it lived in the maintainer's local working notes) and no longer exists to reference. The
  actual list, with each constant's meaning, wire encoding and documented collisions, lives in
  `tik4net/Winbox/WinboxM2Protocol.cs` itself — the constants were centralized there specifically so
  that no separate, easily-stale document would be needed to look them up.

---

## Open, unexplained (not settled — flagged for whoever picks this up next)

- Across the three traced full runs used to investigate the §16/§17 MAC-session wedge, 27 opened
  sessions correspond to 47 `via winbox` login lines in the router's own log — some paired within the
  same second, some standalone. The unevenness rules out simple systematic double-logging, but nothing
  further was found about what causes the pairing. Not linked to any of the wedges investigated in
  §16–§18.
- One `via api` login-failure log line, in the data used to establish that the API transport shows zero
  EC-SRP5 refusals (§13, Settled questions), could not be attributed to any test client. It was excluded
  from the refusal count rather than treated as a fifth API-side refusal, since 400 fresh API logins
  otherwise came back completely clean. Whether it is a fifth genuine refusal or an artifact of shared
  router state was not determined.
