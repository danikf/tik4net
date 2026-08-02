# findings-mactelnet.md — MAC-Telnet (UDP 20561) production implementation

**Chapter E** in [implementation-plan.md](implementation-plan.md). Complements the protocol
reference [mactelnet-protocol.md](../mactelnet-protocol.md) and the shared CLI findings
[findings-cli.md](findings-cli.md) with things verified directly during the production integration
into `tik4net` (2026-06-04, RouterOS 7.21.4, Hyper-V CHR VM).

> **Principle:** MAC-Telnet is a **CLI transport** (like Telnet/SSH), **not** an API protocol. After
> EC-SRP5 authentication it streams a **raw VT100 terminal** over UDP (unencrypted). CRUD goes through
> `CliConnectionBase` → `:put [/path print … as-value]` and text parsing, **identically to Telnet**.
> No binary `!re` sentences; `.id` is obtained from `as-value` output (CLI), not from the API.

---

## Architecture (where things live)

```
tik4net/MacTelnet/
├── MacLayerTransport.cs    public abstract — UDP 22-byte framing, EC-SRP5 auth, ACK/dedup, MNDP
├── MacTelnetUdpClient.cs   internal sealed — VT100 terminal: login + commands (fully synchronous)
└── MacTelnetConnection.cs  public sealed  — CliConnectionBase wrapper (ITikConnection)
```

- `MacTelnetConnection : CliConnectionBase` — `ExecuteCliCommandCoreAsync` delegates to
  `MacTelnetUdpClient.SendCommandAndReadAsync`. The `RouterMac` property bypasses MNDP (otherwise
  ~5 s of discovery).
- Crypto lives in `tik4net/Crypto/` (`EcSrp5`, `WinboxStreamCrypto`); VT100/stripping lives in
  `tik4net/Cli/` (`Vt100State`, `VtStripper`, `RouterOsCliLogin`, `CliOutputHelper`,
  `CliOutputParser`).
- `MacLayerTransport` is **public** on purpose — `WinboxMacClient` (chapter H PoC) in another
  assembly also derives from it.

---

## 1. ⚠️ ACK counter semantics = CRITICAL root cause (2026-06-04)

**Symptom:** after successful authentication (`END_AUTH`, router logs *"logged in"*), the client
waits 30 s for the prompt and throws `TimeoutException: timed out waiting for shell prompt`. In
diagnostics, the same DATA packet (e.g. `ctr=859`, 100 B) repeats **thousands of times** and the
terminal output is chopped up — the router inserts `\r\n` roughly every **3 characters**
(`you\r\nr p\r\nass\r\nwor\r\nd …`), so `IsChangePasswordNag`/`IsShellPrompt` never match.

**Cause:** the MAC-Telnet counter is a **cumulative byte offset into the stream**. After receiving a
DATA packet, you must ACK the offset **past** the packet, i.e. `ACK.counter = pkt.counter +
payload.Length` — **not** `pkt.counter`. The original port ACKed the bare `counter`, so RouterOS
considered the packet undelivered and kept retransmitting it forever, **while also coalescing**
subsequent terminal updates into a single packet. This threw off the **cursor-probe width
negotiation** (see §2) → the router measured a width of ~3 → wrapped output → broken detection.

**Proof of the counter behavior** (live sniff): `ctr=0(len58) → 58 → 58(len9) → 67 → 67(len24) → 91 →
91(len39) → 130 → …` Each subsequent counter equals the previous counter plus the previous len. The
ACK must therefore confirm `counter + len`.

**Fix** (`MacLayerTransport.AckData`):
```csharp
protected bool AckData(uint counter, int payloadLen)
{
    SendAck(counter + (uint)payloadLen);        // confirm the offset PAST the packet
    if (counter < _inCounter) return false;     // retransmission → do NOT reprocess
    _inCounter = counter + (uint)payloadLen;
    return true;
}
```
- `_inCounter` (cumulative bytes received) is reset in `BaseConnect`.
- **Deduplication is mandatory**: without it, a retransmitted packet gets fed into `Vt100State` again
  (throwing off the cursor) and gets appended to the output buffer again (duplicate/corrupted
  records). This matches the protocol: *"the client ignores packets with counter ≤ incounter"*
  ([mactelnet-protocol.md](../mactelnet-protocol.md)).
- `AckData` is used in **all** receive loops: `Authenticate`/`FinishAuth` (auth handlers),
  `WaitForPromptSync`, `ReadCommandResponseSync`, `DrainSync`.

**Verified:** with the fix, login reliably succeeds (3/3 runs), the router completes the full
cursor-probe negotiation and sends the nag/prompt **cleanly** (`Change your password (Ctrl-C to
skip) … new password>`).

> Note: the chapter D PoC "worked" even with a bare `SendAck(counter)`, because `/interface print`
> (tabular, short lines) survived even with retransmissions — but the longer, time-sensitive
> terminal negotiation did not. This was a latent bug inherited from the PoC, not a porting
> regression.

---

## 2. VT100 cursor-probe width negotiation — MANDATORY + "very large width"

After auth, RouterOS **measures the terminal** using a sequence of cursor moves plus `ESC[6n` (DSR).
The client must respond with the actual cursor position `ESC[row;colR` (handled by `Vt100State`).
Without responses, the router assumes 1×1 and doesn't render output. Observed probes (live):

| Sequence | Meaning | Our response |
|---|---|---|
| `ESC Z` | DECID | `ESC[?1;0c` |
| `ESC[6n` | DSR (position query) | `ESC[{Row};{Col}R` |
| `ESC[H` … `ESC[9999C` … `ESC[6n` | **width measurement** (go as far right as possible, where are you?) | `ESC[1;{min(Width,10000)}R` |
| `ESC[9999B`/`ESC[9999A`/`ESC D`/`ESC[r` | height / scroll-region measurement | tracking Row |
| `ESC[H ě H ESC[6n` | UTF-8 test (multi-byte character = 1 column) | `ESC[1;3R` |

**Key point:** the measuring probe is `ESC[9999C` → the reported column = `min(Vt100State.Width,
~1+9999)`. RouterOS probes up to ~9999, so `Width` **must be ≥ 10000**, otherwise `Vt100State` itself
truncates the response and the router measures a narrow terminal → long `as-value` lines get wrapped
and `\r\n` gets inserted into the data → breaks parsing. Production uses `Vt100State(65535, 25)`.
(Telnet uses 4096, which is only enough for shorter lines; for MAC-Telnet, and generally, ≥ 10000 is
safer.) See [findings-cli.md](findings-cli.md) §10.5.

**`CTRL_TERM_WIDTH` in auth** (currently `(ushort)80`, little-endian) is **ignored** by RouterOS after
the cursor-probe — it goes by the measured width instead. So this value doesn't block login (verified:
even with 80, the wide banner renders correctly after a proper ACK). Left at 80.

---

## 3. Receive-side timing — don't put expensive work in the loop

Cursor-probe responses are **time-sensitive**. Per-packet logging (`Console.Write` via the
`TransportDiagnostic` hook, with hex + `StripAnsi` of a growing buffer + substring) in
`WaitForPromptSync` slowed down responses and was initially suspected as the cause. **The real cause
was the ACK issue (§1)**, but the principle still holds: the receive loop must stay free of expensive
work. Verbose diagnostics were removed from the hot loop.

**Debugging without touching production code:** instead of `Console.Write` in production code, use a
non-invasive **session hook** — a debug subclass of the public `MacLayerTransport` in the test project
(`tik4net.tests/Protocols/Tests/MacTelnetDebugTest.cs`) with an in-memory hex dump. The test assembly
can see `internal` types via `InternalsVisibleTo("tik4net.tests")`.

---

## 4. Login sequence (`MacTelnetUdpClient.LoginAsync`, all synchronous inside `Task.Run`)

```
BaseConnect(host, CLIENT_TYPE=0x0015)   // NIC selection, MNDP/MAC override, socket bind
Authenticate(user, pass)                // StartSession() then EC-SRP5 (sync), ends with END_AUTH
WaitForPromptSync()                      // responds to cursor-probe; Ctrl-C on nag; waits for "] >"
DrainSync(250)                           // drains leftover redraw so it doesn't leak into the 1st command
```

- **Fully synchronous** (`UdpClient.ReceiveTimeout` + blocking `Receive`, 500 ms poll). Mixing
  sync/async `Receive` on .NET Framework 4.8 broke `SO_RCVTIMEO` → the async variants
  (`AuthenticateAsync`, `RecvUntilAsync`, `TryReceivePacketAsync`) are **unused dead code** and can be
  removed.
- **Change-password nag**: a router with an empty/default password shows `new password>` — respond
  with **Ctrl-C (0x03)**, `sb.Clear()`, then continue. Detected via
  `RouterOsCliLogin.IsChangePasswordNag` (substring `password>`). See
  [findings-cli.md](findings-cli.md) §10.6.
- **Prompt**: `RouterOsCliLogin.IsShellPrompt` = `TrimEnd().EndsWith("] >")`.

## 5. Executing a command (`SendCommandAndReadAsync` → `ReadCommandResponseSync`)

```
cmd = CliOutputHelper.InjectWithoutPaging(command)   // "without-paging" after "print"
Send(PKT_DATA, cmd + "\r")
raw = ReadCommandResponseSync()                       // prompt + 150 ms silence (settle), same as Telnet
return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd)  // strip echo + trailing prompt
```
The settle logic matches `TelnetClient.ReadCommandResponseAsync` (prompt at the end + `SettleMs` of
silence; a prompt redraw before the output resets the settle window). See
[findings-cli.md](findings-cli.md) §10.8.

---

## 6. ⚠️ Double command echo → `CleanOutput` (root cause of "Missing '.id'")

**Symptom:** after the login fix, `LoadAll<Interface>()` throws `TikSentenceException: Missing field
'.id'` in `CliReSentence`/the mapper. **Note — this arises in the shared CLI output layer, not in the
transport or the mapper** (the mapper and `.id` are legitimately CLI-based and shared with the
working Telnet transport).

**The raw output is clean and DOES contain `.id`** (verified via dump, width 65535 works):
```
:put [/interface print … as-value]\r[admin@MikroTik] > :put [/interface print … as-value]\r\n
.id=*2;…;name=ether1;…;.id=*1;…;name=lo;…\r\n
\r\r\r[admin@MikroTik] >
```
MAC-Telnet (raw VT100, lines terminated with just `\r`) echoes the command **twice**: (1) character
echo of the typed command on its own line, (2) a line-editor redraw as `<prompt> <command>`. Telnet
(`\r\n`) produces only one echo. The original `CleanOutput` removed **only the first** echo line, so
the leftover `[admin@…] > :put […]` merged into the first `.id=*2` record inside `ParseAsValue`
(which normalizes `\r\n` → `;`) — producing one merged "key" — so the first record was missing its
`.id` field → exception.

**Fix** (`CliOutputHelper.CleanOutput`): loop and remove **all** leading blank/echo lines. A line
counts as echo/noise if (a) it contains the prompt `] >` (prompt-prefixed redraw / leftover prompt),
or (b) it is a fragment of the sent command (`cmdCore.Contains(line)` / `cmdCore.StartsWith(line)`).
Point (b) also handles **multi-line commands** — `/system/script/add` with a `source` containing `\n`
gets echoed across multiple lines; without this fix, the leftover fragment merged into the first
record / was mistakenly returned as the id of the added record (`RunAdd`). A data line (`.id=…`, a
bare `*N`, or an error) meets none of these criteria → the loop stops there. Safe across transports
(no change for Telnet).

## 7. Status (2026-06-04) — ✅ DONE

| Item | Status |
|---|---|
| Transport / framing (22 B, big-endian session_key/client_type) | ✅ (chapter D) |
| EC-SRP5 auth (`END_AUTH`) | ✅ |
| **ACK counter + dedup (§1)** | ✅ fixed + verified |
| Cursor-probe width ≥ 10000 (§2) | ✅ `Vt100State(65535,25)` |
| Nag/prompt detection, Ctrl-C | ✅ |
| **Double echo → `CleanOutput` (§6)** | ✅ fixed (shared with Telnet, safe) |
| `MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne` | ✅ **PASS** |
| `MacTelnet_SetAndVerify_InterfaceEther1Comment` | ✅ **PASS** |

**Cleanup:** removed the verbose `TransportDiagnostic` from the hot loop, the unused `_diagnostic`
field, and the unused async variants (`AuthenticateAsync`, `RecvUntilAsync`,
`TryReceivePacketAsync`) — `MacTelnetUdpClient` is now fully synchronous. Debugging was done
non-invasively via a temporary debug subclass of `MacLayerTransport` in the test project (deleted
after completion).

---

## 8. Login timeout (`ConnectTimeout`) + behavior under load

Login (`WaitForPromptSync`) has its own timeout, `MacTelnetConnection.ConnectTimeout` (default
**15,000 ms**), **separate** from the per-command `ReceiveTimeout` (30,000 ms). Reason: under load
(hundreds of MAC-Telnet sessions in quick succession during the full test suite), occasionally one
login fails to reach the prompt. If login could block for the full 30 s (`ReceiveTimeout`), the
caller's connect-retry loop (TestBase: 1 s × 20 s window) wouldn't have time for a second attempt. A
shorter login timeout (15 s) means the retry actually happens and a flaky session recovers.

`TikConnectionSetup.ConnectTimeout` (TimeSpan, default 15 s) propagates to
`MacTelnetConnection.ConnectTimeout` in `CreateMacTelnetConnection[Async]`. It can also be set
directly: `new MacTelnetConnection { ConnectTimeout = 10000 }`.

## 9. Router-side prerequisites

- `/tool mac-server set allowed-interface-list=all` (or an allowed interface) — otherwise MAC-Telnet
  won't respond.
- MNDP (UDP 5678) enabled, unless the `RouterMac` override is used.
- Hyper-V: SESSIONSTART goes to the **subnet broadcast** (`192.168.x.255`), not
  `255.255.255.255`; DATA/ACK go to the router's **unicast** IP; prefer a NIC on the same subnet.
  (chapter D)

## 10. Retransmission: one slot isn't enough once multiple requests are in flight (2026-07-30, P2.42)

**Context:** P2.19 added send-side reliability — we hold the last sent DATA packet and resend it
byte-identically if the router doesn't ACK it. That was correct as long as every caller was
lockstep: at most one packet was ever in flight, so a single slot (`_lastDataPacket`) covered
everything.

**What breaks under multiplexing** (`WinboxNativeMac`, see
[winbox-m2-multiplexing-design.md](winbox-m2-multiplexing-design.md) §4.5): the counter is a
**cumulative byte offset** (§1), so acknowledgment is cumulative too. We send A (offset 0–99) and B
(100–199); A gets lost:

- the router receives B, but can't ACK **anything** — there's a hole in the stream, so its ACK stays
  at 0,
- the packet that needs to be resent is **A**,
- but the single slot has already been overwritten by B.

The result isn't a slow round trip but a **permanently stuck session**: we wait for a response, the
router waits for bytes that will never arrive, and the retransmit keeps resending B, which it
already has.

**Fix:** a queue of unacknowledged packets instead of a single slot.

- `SendCore` **appends** to the end (never overwrites another unacknowledged packet),
- `NoteAck(counter)` discards everything with `End <= counter` (one ACK can retire multiple packets)
  and resets the retransmit budget — that budget belongs to the packet at the head of the queue, and
  the head has just changed,
- `RetransmitIfUnacked` resends the **oldest** unacknowledged packet, because with cumulative ACKs the
  hole is always at the front of the queue; with a single request in flight this is the same packet as
  before, so behavior is unchanged for that case,
- `NoteAck` runs under `SendGate` — it's called from the receive side, which for a multiplexed channel
  is a different thread than the sending side. Previously these couldn't overlap, so no lock was
  needed there.
- The queue is bounded (`MaxUnackedTracked = 256`), so a caller writing into a dead session doesn't
  grow it without limit.

**Note on the write-side lock:** the §4.5 design expected the main obstacle to multiplexing to be
sending ACK/PONG from the receive path. It wasn't — all writes (`Send`, `SendAck`, `SendPong`,
`RetransmitIfUnacked`) already went through `SendGate` because of the MAC-Telnet pump. The real
obstacle was one layer down, in the retransmission queue itself.

**Tests:** `tik4net.unittests/MacTelnet/MacLayerRetransmitTests.cs` — loopback UDP, no router
required. These scenarios can't be reproduced live (the lab router doesn't drop packets on demand),
so the stream hole, partial ACK, exhausted budget, and concurrent send/ACK cases are covered only
here.

## 11. The login that "timed out" was refused (2026-08-02, P2.49)

**`MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne` failed about once per full suite run, always at
login, always after ~15.7 s, and never in isolation.** Nine reproduction runs found nothing, because
the message it produced — `timed out waiting for shell prompt` — described our own deadline and not
what the router had said. The first run after that message was given the tail of the screen answered
it outright:

```
tik4net.TikConnectionLoginException: Cannot log in. MAC-Telnet: timed out waiting for shell prompt
after 15004 ms; 1 terminal packet(s) received, 46 char(s),
screen tail: Login failed, incorrect username or password
```

The wire trace of that session (`key=bb39`, now readable because §11.4 tagged it) shows the whole
shape:

| t | packet | |
|---|---|---|
| +0.0 s | `RECV 0x01 counter=58` × 3 | `CTRL_END_AUTH` — **authentication reported success** |
| +1.1 s | `RECV 0x01 counter=67 len=46` | `Login failed, incorrect username or password` |
| +1.1 s | `RECV 0xff` × n | `PKT_END` — the router tears the session down |
| … | *(nothing)* | we wait out the remaining ~13.9 s for a prompt |

Three things were wrong, and all three had to be for the failure to look like a timeout:

1. **The refusal arrives after `CTRL_END_AUTH`.** `Authenticate` ends there, correctly — so no part of
   the EC-SRP5 exchange can detect this. It has to be recognised by the code waiting for the prompt,
   which had no idea the text existed.
2. **`PKT_END` was ignored by every loop** (`if (type != PKT_DATA) continue;`). The router says the
   session is over and we spend the rest of the timeout waiting for it to speak. Now handled **in the
   login wait only** — that is the one place it has been observed. Across two full traced runs
   `PKT_END` appears exactly six times, all six in the refused session; RouterOS does **not** send it
   when it logs an idle console out, so teaching the pump about it would be inventing a contract.
3. **The refusal is not evidence about the credentials.** It is the MAC-Telnet face of the transient
   refusal already measured on the WinBox handshake (P2.41, [findings-winbox.md](findings-winbox.md)
   §13): the same EC-SRP5 login, retried immediately, is accepted. The router even logs it as
   `login failure for user admin from 48:EA:62:D0:AD:17 via mac-telnet` — visible in the same trace on
   a *different* session's console, courtesy of the shipped logging rules.

**Fix:** `TikConnectionLoginRefusedException` thrown as soon as the line appears or the router hangs
up, and `MacTelnetConnection.Open`/`OpenAsync` wrapped in `Winbox.RouterLoginRetry` exactly as the three
WinBox connections already are. A refused login now fails in ~1 s instead of 15, and is retried up to
three times before it is reported.

**One exception type, not one per transport.** The refusal started life as a MAC-Telnet-specific class
alongside the WinBox one, reunited by a marker interface — an interface invented to undo a split that
should not have happened: nothing anywhere catches the two cases separately, and they carry identical
information. It is now the single **public** `TikConnectionLoginRefusedException : TikConnectionLoginException`,
with the router’s verbatim text on `RouterMessage` and the handshake on `Transport`. Public because it
already reached callers as an `InnerException` — visible in the message, impossible to catch by type —
and under `TikConnectionLoginException` so existing catch blocks are unaffected. The retry is
`RouterLoginRetry` (renamed from `WinboxLoginRetry`) and its trace channel is `login`, not `wbx.login`.

> **It is deliberately not called “transient”.** The retry is internal and bounded, so by the time this
> exception reaches a caller it is precisely the refusal that did **not** clear — telling them it is
> temporary would invite a retry loop exactly where retrying is already known not to help.

> **The narrow match is deliberate.** `RouterOsCliLogin.IsLoginFailure` is *not* used here: it matches
> `login failure`, which is the wording of the router's own log lines — echoed onto any console at any
> moment. Matching it during login would turn an unrelated background event into a refused login.

### The rest: the session opens on trust, and one packet has no way back

Three further defects found in the same few lines while chasing the above, all about the moment a
session is created — before there is any acknowledged traffic to reason from.

### 11.1 SESSIONSTART is the one packet that cannot be resent

`Authenticate` (and `WinboxMacM2Session.MacAuthEcSrp5`) sent SESSIONSTART, slept a blind **80 ms**,
and then sent the authentication request into a session the router might never have created. If that
first datagram is lost, nothing recovers it:

- it is **not** DATA, so it never enters the unacknowledged queue that §10 built, and
- that queue is inert until the first ACK arrives (`_haveAck`) — which is precisely the ACK
  SESSIONSTART was waiting for.

It is also the **only packet sent to subnet broadcast**, i.e. the one most likely to be dropped on the
way out — see the NIC-selection comment in `BaseConnect` for a measured case where every SESSIONSTART
left via a disconnected adapter and vanished. The cost of losing it was the full authentication
timeout with no retry at all.

Replaced by `MacLayerTransport.StartSession()`: send, wait for the router's ACK, resend every 300 ms,
give up after 2 s and let authentication fail on its own deadline (a second error here would only
hide the first). **Measured on 7.23.2 across five traced suite runs: 76 of 76 SESSIONSTARTs were
acknowledged, every one of them before the old blind sleep was over** — so on a healthy link the new
code waits *less*, and the retry only ever costs something in the case that used to be fatal.

> Beware of reading the old traces for that latency: the ACK is timestamped when *we read it*, and we
> were not reading during the 80 ms sleep. Every measurement said "81–108 ms" because the sleep was
> 80 ms, not because the router took that long (the P2.55 lesson, again).

### 11.2 Every session spent a retransmission on a packet that was in flight

`RetransmitIfUnacked` rate-limited itself with `_lastRetransmitUtc`, which starts at
`DateTime.MinValue` — so the **first** call after any send always passed the limit. The first ACK of a
session arrives while the packet behind it is still in flight, the read loop calls the retransmit
check on the very next poll, and out goes a duplicate of a packet a few milliseconds old.

This is visible in every trace as `RETRANSMIT #1` immediately after the first `RECV type=0x02`:
in five full runs, **every single MAC-layer session did it**, spending one of its eight
retransmissions before the exchange had properly begun. The rule was missing the obvious half: the
head packet must itself have been **unanswered for `MinRetransmitIntervalMs`**, not merely followed by
that much idle time. An ACK that covers less than the head says "not yet", not "lost".

### 11.3 The two states you cannot recover from were both silent

A MAC session has exactly two dead ends, and neither left a trace:

| state | what it means | now traced as |
|---|---|---|
| inbound hole (`counter > _inCounter`) | we dropped the packet and re-ACKed the low-water mark; recovery is entirely up to the **router** resending | `HOLE counter=… expected=… missing=…` |
| retransmit budget spent | the router never took our bytes; the stream is blocked for good | `RETRANSMIT BUDGET SPENT end=… highestAck=…` |

From either one, every subsequent read fails on its own deadline, far from the cause — which is the
exact shape P2.49 was filed as. And `MacTelnetUdpClient.WaitForPromptSync` reported that as a bare
`timed out waiting for shell prompt`, which cannot distinguish "the router said nothing at all" from
"we are mid-negotiation and one packet went missing". It now carries the elapsed time, the packet and
character counts, whether the nag was dismissed, and the tail of the screen.

### 11.4 Untagged trace lines produce confident nonsense

`MacTelnetUdpClient.TryParsePacket` — the parser the login loops and the pump use — emitted its RECV
line **without the session tag** that §10's sibling path (`ReceiveOne`) adds. With several MAC
sessions alive at once, a per-session reconstruction has no way to attribute those lines. This is not
a cosmetic gap: replaying a **green** suite run through such a reconstruction attributed the untagged
inbound lines to whichever session opened last and reported **311 stream holes that never happened**.
Now tagged, so a trace can be split by `key=` and read one session at a time.

### A hypothesis worth retiring

P2.49 was filed suspecting that *"a prior test's MAC session may still be occupying the local UDP
endpoint (20561)"*. It cannot: we bind to an **ephemeral** local port
(`Bind(new IPEndPoint(nic.LocalIp, 0))`, plus `ReuseAddress`) — 20561 is the *router's* port, never
ours. The traces say so directly, consecutive sessions showing `local=…:49931`, `local=…:49932`. Two
MAC sessions never contend for a local endpoint.
