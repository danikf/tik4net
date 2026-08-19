# WinBox M2 multiplexing — request/response correlation and channel model

How native WinBox M2 operations (`WinboxNativeConnection`) get several requests in flight on one
connection instead of serializing on a single lock, and why that is safe on a reverse-engineered
protocol with no formal spec. Complements the live protocol findings in
[findings-winbox.md §12](findings-winbox.md) (correlation, crypto, pagination) and the MAC-layer
half of this same work in [findings-mactelnet.md §9.2](findings-mactelnet.md) (the retransmit
queue that makes concurrent sends safe over UDP).

> **Principle:** every native M2 reply echoes the request's id (`0xFF0006`), and the frame crypto
> is stateless per frame, so replies may be completed **out of order** and matched to their own
> caller instead of "whoever asked next". A single reader loop owns the channel's read side and
> dispatches by that id. The mepty/CLI terminal path (`WinboxCliClient`) never gets a multiplexer:
> it streams unsolicited, id-less frames, and handing those to id dispatch would silently drop all
> terminal output.

> Superseded diagnoses, incidents and pinned measurements for this area are in
> [`winbox-m2-multiplexing-design-history.md`](winbox-m2-multiplexing-design-history.md); this document describes current behaviour only.

---

## Architecture

```
tik4net/Winbox/
├── IWinboxM2Channel.cs          channel abstraction shared by native M2 and the mepty terminal
├── WinboxM2Session.cs           TCP channel (port 8291): chunked frames, EC-SRP5/legacy-MD5, AES
├── WinboxMacM2Session.cs        MAC-layer channel (UDP 20561): built on MacLayerTransport
├── WinboxM2Multiplexer.cs       reader loop + id-keyed dispatch — native M2 only
├── WinboxNativeM2Operations.cs  getall/get-one/set/add/monitor, multiplexed or lockstep
└── M2Message.cs                 wire codec, incl. ParseSysReqId (0xFF0006)

tik4net/WinboxNative/WinboxNativeConnection.cs   owns Open → InitAfterAuth → StartMultiplexer
tik4net/WinboxCli/WinboxCliClient.cs             mepty terminal — reads the channel directly, no multiplexer
```

`WinboxM2Session` and `WinboxMacM2Session` both implement `IWinboxM2Channel` and are shared by two
consumers with incompatible read models: `WinboxNativeM2Operations` (every reply carries
`0xFF0006`) and `WinboxCliClient` (unsolicited terminal-output frames with no request id at all).
A connection is always exclusively one or the other — `WinboxNativeConnection` and the CLI
connections each construct their own channel and never share one — so the multiplexer's exclusive
ownership of the read side is uncontested. `IWinboxM2Channel.SupportsReaderLoop` is the guard: a
channel that streams unsolicited frames must never be given a multiplexer.

---

## 1. Correlation: dispatch by request id, nothing else

A live trace (RouterOS 7.21.4, `/ip/address/print` over native M2 — three exchanges in one session,
because the address record's interface and VRF references trigger follow-up `getall`s) shows the
request id echoed exactly and a second field that only looks like a correlation key:

| Exchange | handler (`0xFF0001` To) | request `0xFF0006` | response `0xFF0006` | response `0xFF0003` |
|---|---|---|---|---|
| address getall | `[20,1]` | 2 | **2** | 2 |
| vrf getall | `[20,101]` | 3 | **3** | 2 |
| interface getall | `[20,0]` | 4 | **4** | 2 |

`0xFF0006` (`WinboxM2Protocol.SysKey.RequestId`) tracks the request exactly and is the sole
correlation key. `0xFF0003` is undefined in `WinboxM2Protocol` and stays constant across the whole
session while the request id keeps advancing — it is not a correlation field, and a single-exchange
trace is a trap here: in a one-exchange sample it happens to equal the request id. See
[findings-winbox.md §12.1–12.2](findings-winbox.md) for the full trace and the trap.

`0xFF0001`/`0xFF0002` (To/From) swap between request and response, so the handler is a secondary
signal, but not a unique one: two concurrent calls to the same handler are indistinguishable by it.
Dispatch is by request id only.

## 2. The crypto is stateless per frame, so out-of-order completion is safe

Despite the name, [`WinboxStreamCrypto`](../tik4net/Crypto/WinboxStreamCrypto.cs) is not a running
stream cipher. `Encrypt` emits `[enc_len 2B BE][IV 16B][ciphertext]` with a fresh random IV per
frame, and `Decrypt` derives everything it needs from that one frame plus the fixed post-handshake
keys. There is no cross-frame cipher state, no counter, no replay window — frames may be decrypted
independently and completed in any order relative to when they were sent. The one remaining
ordering constraint is framing itself: a chunked frame is a sequence of chunks, so reads must stay
serialized (one reader) and writes must not interleave (one write lock covering the write only, not
the round trip). See [findings-winbox.md §12.3](findings-winbox.md).

## 3. One request → exactly one reply frame; pagination is a new request, not a continuation frame

`0xFE0019` (`WinboxM2Protocol.RecordKey.Count`) is a total object count, informational only — never
read for flow control on either side. A registration completes on the first frame carrying the
matching `0xFF0006`; there is no "more frames follow" signal to also wait for. A `getall` that pages
issues a fresh request with a new id on every page, carrying back whichever continuation token the
previous reply supplied (`RecordKey.Continuation`/`ufe0003`, or the message-array form
`RecordKey.ContinuationRaw`/`mfe0015`) — so the registration model is unaffected by pagination: each
page is its own request/reply pair. See [findings-winbox.md §12.7](findings-winbox.md).

## 4. The reader loop and the write lock

[`WinboxM2Multiplexer`](../tik4net/Winbox/WinboxM2Multiplexer.cs) wraps one `IWinboxM2Channel` and
owns its read side for the lifetime of the connection:

```
_readerThread = new Thread(ReaderLoop) { IsBackground = true }   // one per connection, started eagerly

ReaderLoop (blocking, dedicated thread — not a pool thread):
    while not disposed:
        m2 = channel.ReceiveNextFrame()          // blocks with no per-read deadline
        if m2 == null: break                     // channel closed
        id = M2Message.ParseSysReqId(m2)
        if id has a pending registration: complete it with m2
        else: OnUnmatchedFrame(m2)                // late reply or id-less frame — drop it
    on exit → fault every still-pending registration

SendReceiveAsync(request, timeoutMs, ct):
    id = ParseSysReqId(request)                   // caller built it via NextReqIdField()
    register id in _pending BEFORE sending          // §6
    lock (_writeLock) { channel.Send(request) }    // held only for the write, not the round trip
    race the registration's task against Task.Delay(timeoutMs, ct)
    on timeout/cancellation: drop the registration, the reply (if it ever comes) is unmatched
```

The reader loop is a dedicated background `Thread` performing blocking synchronous I/O — not a
`Task`-based loop over an async channel. `IWinboxM2Channel` has no `SendAsync`/`ReceiveAsync`;
`WinboxTcpTransport` has no async send/receive members either. `Send` is a single synchronous
socket write of a handful of bytes and neither channel offers an async form to await instead, so
`SendReceiveAsync`'s asynchrony comes entirely from awaiting the `TaskCompletionSource` the reader
loop completes, not from the I/O underneath it. `SendReceive` (the synchronous entry point used by
`WinboxNativeM2Operations`'s sync API) blocks on `SendReceiveAsync` rather than duplicating the
logic.

The connection-wide lock this replaces (`_cmdLock` in `WinboxNativeConnection`) disappears from the
request path on a multiplexed connection: `EnterCommand`/`EnterCommandAsync` become no-ops once
`_mux` is set, because the reader loop's id dispatch already makes concurrent operations safe, and
serializing them would give back exactly the throughput multiplexing exists to gain. `_cmdLock`
stays the real semaphore only for a channel that never gets a multiplexer (`SupportsReaderLoop ==
false`).

## 5. Request id: one byte, allocated by the multiplexer once it owns dispatch

The request id is one byte on the wire, so the counter wraps at 256.
`WinboxM2Multiplexer.NextReqId()` allocates it with `Interlocked.Increment` masked to 8 bits and
skips `0`, which stays reserved for "no id" — `M2Message.ParseSysReqId` reports absence as `null`,
so a frame can never be mistaken for a reply to request `0`. `SendReceiveAsync` refuses to reuse an
id that is still pending (`_pending.TryAdd` failing is treated as a bug and throws): with at most
256 ids in flight, a collision means a registration is leaking, not legitimate concurrency — in
practice the outstanding count stays in the single digits.

`WinboxM2Session` and `WinboxMacM2Session` each also carry their own `NextReqIdField()`, a plain
`Interlocked.Increment` with no zero-skip and no collision guard. That version is only ever used
during the lockstep window before a multiplexer exists (connect, authentication, the `.jg` catalog
fetch) — `WinboxNativeM2Operations.NextReqIdField()` switches to the multiplexer's allocator as soon
as `UseMultiplexer` installs one, and every native operation after that point gets its id from
there.

## 6. Register before writing, always

The registration must exist in `_pending` before the request bytes go out, or a fast reply can reach
the reader loop before the sender has registered — and be dropped as unmatched. This is why
`SendReceiveAsync` registers first and only then takes the write lock.

## 7. Auth and one-time init stay lockstep; the multiplexer starts last

`EcSrp5Auth`/`LegacyMd5Auth` are timing-sensitive reverse-engineered sequences with raw
`WinboxTcpTransport.ReadExact` reads outside normal message framing, plus (on the legacy MD5 path) a
`Thread.Sleep(200)` + drain step around the challenge setup. They run once, on the socket directly,
before any multiplexer exists — cost is irrelevant and this is the most fragile code in the file, so
it is left untouched.

`WinboxNativeConnection.InitAfterAuth` runs authentication, the router version probe and the `.jg`
catalog fetch on that same lockstep path, and only then calls `StartMultiplexer` — deliberately the
last step, because all of the init sequences read the channel directly and would race a live reader
loop. Immediately before constructing the multiplexer, `WinboxNativeM2Operations.DrainBufferedFrames`
discards anything still buffered from the lockstep phase: a leftover frame is not merely noise here,
because the multiplexer restarts request ids from 1, so a stale frame carrying (say) id 3 could be
delivered to a *new* request that later gets id 3. That converts a merely shifted reply into a silent
wrong-reply, which is the harder failure to notice.

## 8. Timeouts are per-request, not per-socket

`IWinboxM2Channel.ReceiveNextFrame()` carries no timeout: a per-read socket deadline could fire
between two chunks of one frame and desynchronize the stream permanently, so on TCP the reader loop
switches the socket to an infinite receive timeout the first time it runs, and on the MAC channel
each `ReceiveNextFrame` call polls in bounded slices purely so disposal and the retransmit timer
keep running — neither carries a deadline of its own. Deadlines belong entirely to the per-request
registration: `SendReceiveAsync` races the reply against `Task.Delay(timeoutMs, …)`.

`InitAfterAuth` passes `ReceiveTimeout` (not `ConnectTimeout`) into `WinboxNativeM2Operations`, so
every native command is now bounded by the value that actually governs a stalled-but-connected
router. A timeout here usually means the router went quiet, not that a reply was lost: sustained
load — from any connection, the ceiling is aggregate — clamps round trips from roughly 1 ms to
roughly 20 ms, arriving on TCP as a batch of replies after a stretch of silence (see
[findings-router-throughput-ceiling.md](findings-router-throughput-ceiling.md)).

## 8a. A dropped session is asked about, not waited out

RouterOS drops an idle MAC-layer session after about 30 s. It closes no socket and sends no error, so
the session goes **silent**: the next `getall` is simply never answered. Waiting the deadline out and
reporting "no reply" would name the symptom at the wrong layer — by then the carrier has known the
answer for tens of seconds, because `MacLayerTransport` retransmitted the request to exhaustion
(8 tries, ~3.2 s) and was never acknowledged.

So `SendReceiveAsync` runs its race in 250 ms slices rather than as one `Task.Delay`, and asks
`IWinboxM2Channel.SendAbandoned` between them. When it is set the waiter fails with
`TikConnectionSessionClosedException` — the same type MAC-Telnet raises for the same event — instead
of a `TimeoutException`. `WinboxM2Session` (TCP) answers a constant `false`: nothing below it
acknowledges individual messages, so this costs TCP one property read per slice and changes nothing
there.

`WinboxNativeConnection.RunPrintAsync` catches that exception and **reopens the channel and reissues
the read**, which is what `MacTelnetConnection` has always done for the CLI transports. Three limits
make the retry honest rather than a guess:

- **Reads only.** `RunAdd`/`RunNonQuery` do not retry. "The bytes were never acknowledged" is a
  statement about the carrier, and re-adding a row on the strength of it is a guess this transport
  must not make; a read is idempotent and re-running it is not a second execution.
- **Not while Safe Mode is held.** Dropping the session is precisely what rolls Safe Mode's changes
  back, so a silent reopen would hide the event the caller asked to be protected by — and the new
  session would not hold Safe Mode either.
- **Once per dead session, not once per caller.** A generation counter guarded by a semaphore means
  two commands failing on the same session produce one reopen; the second sees the generation has
  moved and retries on the session the first built.

A running streaming monitor does not survive a reopen and does not have to: its own poll traffic is
what keeps the session from ever going idle, so a monitor and a dropped-idle session are not a
combination the router produces.

Measured on the lab CHR (`WinboxNativeMac`, `/queue/type` — 10 rows, 60 s idle, read again):
`TimeoutException` after 30 006 ms before, 10 rows in 3 845 ms after.

## 9. Unmatched frames

A frame with no `0xFF0006`, or one whose id has no pending registration (a late reply after a
timeout or a cancellation): reported via `OnUnmatchedFrame` and dropped. Never thrown — a late reply
to a cancelled request is expected (§10) and must not take the connection down.

## 10. Cancellation

`WinboxNativeConnection` declares `TikConnectionCapability.CancelInFlight`, which means two
different things depending on what is being cancelled. A **streaming window** (torch, ping, scan,
traceroute, bandwidth-test, …) is closed with the window's own `cancelcmd` — the same command WinBox
itself sends when its window closes — which is a genuine router-side cancel; every streaming window
in the `.jg` catalog declares one. An **ordinary round trip** (`getall`/`set`/`add`) has no cancel
verb at all, so cancelling it only abandons the local registration and frees the caller while the
router finishes the work regardless. That is safe specifically because dispatch is by request id: the
late reply, when it arrives, is identified and discarded (§9) rather than handed to whichever caller
asks next — a guarantee id dispatch gives for free and lockstep read-the-next-frame could not.

## 11. The MAC transport: same channel interface, a different write-side constraint

`WinboxMacM2Session` reuses `MacLayerTransport` (framing, ACK/PING, EC-SRP5, AES/HMAC key
derivation) and carries each M2 message as one encrypted blob inside `PKT_DATA` payloads instead of
the TCP chunk wrapper. `SupportsReaderLoop` is `true` here too — every write already goes through
`MacLayerTransport.SendGate`, so a background reader adds no writer the gate does not already cover,
and the retransmit buffer is a queue rather than a single slot (oldest resent first, cumulative ACKs
retiring everything they cover), which is what makes more than one request in flight survivable when
the MAC counter is a cumulative byte offset. The full rationale — including why the obvious candidate
obstacle, "ACK/PING sends interleaving with a background reader", is not the real one — is in
[findings-mactelnet.md §9.2](findings-mactelnet.md#92-unacknowledged-packets-are-a-queue-not-a-single-slot).

What is specific to the M2 channel wrapper: `WinboxMacM2Session.ReceiveNextFrame` does a one-time
handover on its first call, clearing the chunk-reassembly buffer left over from the lockstep init
phase — the MAC-layer counterpart of the TCP channel's one-time switch to an infinite socket
timeout (§8). Only that thread has read anything by that point, so nothing live is discarded.
`SupportsStaleDrain` is `false` on this channel: on UDP, `DataAvailable` reflects ACK/PING/retransmit
control traffic far more often than a real frame, so a stale-frame drain would thrash on noise
instead of discarding one stale `DATA` frame — the connection's `DrainBufferedFrames` cannot do this
job either, since what needs clearing is the partial reassembly state underneath a frame, not a
whole buffered frame.

## 12. What multiplexing does not touch

Wire encodings, the `.jg` catalog/resolver, `WinboxRecordCodec`, `EcSrp5`, `WinboxStreamCrypto`.
This is message *routing* only — a diff that touches an encoder has left the intended scope.

## 13. Test coverage

There is no loopback fake for the live router beyond what the tests build themselves:
`tik4net.unittests/Winbox/FakeWinboxServer.cs` is a TCP double that speaks the WinBox EC-SRP5
handshake (and its legacy-MD5 fallback, triggered the same way a real old router triggers it —
answering the hello with a non-`0x06` frame tag) without RouterOS on the other end.

- `WinboxM2SessionProtocolTests` pins the lockstep session framing, both auth paths, and
  `M2Message.ParseSysReqId`.
- `WinboxM2MultiplexerTests` covers the multiplexer directly: concurrent requests answered out of
  order each reach their own caller; an id-less frame is dropped and reported rather than delivered
  as a reply; an unanswered request times out and releases its registration for reuse; closing the
  channel faults every pending registration instead of letting each time out independently; the
  one-byte id counter skips `0` across the 256-wraparound; and the awaitable surface — a
  pre-cancelled token writes nothing, cancelling an in-flight request frees the caller while its late
  reply is reported unmatched rather than delivered to the next caller, two awaited callers each get
  their own reply, and one caller's short deadline does not affect another's.
- `WinboxM2DeadSessionTests` covers §8a against a fake channel — the TCP double cannot produce
  `SendAbandoned`, since the signal only exists on a carrier that acknowledges what it sends: a
  waiter fails as a closed session promptly when the carrier gives up, and a merely slow channel
  still times out as a `TimeoutException`.
- `WinboxM2PaginationTests` covers `getall` continuation, including the message-array continuation
  form (`RecordKey.ContinuationRaw`/`mfe0015`) alongside the plain one (`RecordKey.Continuation`).
- `tik4net.unittests/MacTelnet/MacLayerRetransmitTests.cs` covers the MAC-layer retransmit queue
  over loopback UDP — see [findings-mactelnet.md §11](findings-mactelnet.md).

Live verification for a change here: a full pass of the `winboxnative` and `winboxnativemac`
`.runsettings` files, plus the smoke subset (`ConnectionTest`, `SystemClockTest`,
`InterfaceListTest`, `IpRouteTest`) across the other transports — see the `mikrotik-tests` skill.

---

## Settled questions — do not re-investigate

- **Does `0xFE0019` mean "more frames follow"?** No. It is the total object count (`objCount`),
  informational only on both the client and the router's own webfig client — never read for flow
  control. The completion rule is unconditional: one request → exactly one reply frame, matched by
  `0xFF0006`. See [findings-winbox.md §12.7](findings-winbox.md).
- **Is `0xFF0003` a correlation or session field?** No. It stays constant for the whole session while
  the request id advances, and it never appears in the router's own webfig client at all. Dispatch
  exclusively on `0xFF0006`. See [findings-winbox.md §12.2](findings-winbox.md).
- **Does the crypto need to change to support multiplexing?** No. `WinboxStreamCrypto` is stateless
  per frame (fresh IV, no cross-frame counter) — this was the one finding that could have made
  multiplexing impossible without a redesign, and it came back clean. See
  [findings-winbox.md §12.3](findings-winbox.md).
- **Does the reader loop belong inside `WinboxM2Session`/`WinboxMacM2Session`?** No. Those classes
  are shared with the mepty terminal (`WinboxCliClient`), which reads unsolicited, id-less frames
  directly off the channel; a reader loop installed at that layer would consume terminal output and
  hand it to the unmatched-frame path, silently dropping every CLI reply. The multiplexer sits one
  layer above, wrapping the channel, and only `WinboxNativeM2Operations` is ever given one.
- **Does the MAC transport need its own single-outstanding-packet redesign beyond a queue?** No. The
  write side needed no change (`MacLayerTransport.SendGate` already covered it); the fix was
  replacing the single-slot retransmit buffer with a queue that resends the oldest unacknowledged
  packet, because the MAC counter is a cumulative byte offset and a lost first-of-two packets is
  exactly what one slot cannot resend. See
  [findings-mactelnet.md §9.2](findings-mactelnet.md#92-unacknowledged-packets-are-a-queue-not-a-single-slot).
- **Should `CancelInFlight` ship separately from multiplexing?** No. It shipped with it: id dispatch
  is what makes abandoning a local registration safe (the late reply is identified and dropped, never
  misdelivered), so there was no reason to wait. See §10.
