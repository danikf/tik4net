# WinBox M2 multiplexing — design

*Follows `P2.1-async-contract-design.md` (D5a). Written 2026-07-21 against `master` @ `5eeb42c`.
Status: **proposal**. Prerequisite: the WinBoxNative async query-path conversion (D5a) must land first —
there is nothing to multiplex while every call is a blocking lockstep exchange.*

---

## 1. Verified ground truth

Three facts decide whether this is possible at all. All three were checked, not assumed.
Full traces and the disambiguation trap are recorded in
[`findings-winbox.md` §12](../connections/findings-winbox.md).

### 1.1 The router echoes the request id ✅

Live trace against RouterOS 7.21.4 (`/ip/address/print` over WinboxNative — three M2 exchanges in one
session, because interface/VRF references are resolved with follow-up `getall`s):

| Exchange | handler (`0xFF0001` To) | request `0xFF0006` | response `0xFF0006` | response `0xFF0003` |
|---|---|---|---|---|
| address getall | `[20,1]` | 2 | **2** | 2 |
| vrf getall | `[20,101]` | 3 | **3** | 2 |
| interface getall | `[20,0]` | 4 | **4** | 2 |

`0xFF0006` (`WinboxM2Protocol.SysKey.RequestId`) tracks the request exactly → **it is the correlation key.**

`0xFF0003` is undefined in `WinboxM2Protocol` and stays constant while the request id varies, so it is *not*
a correlation field (it looks like a session / reply-channel id). Do not dispatch on it — a single-sample
observation would have suggested otherwise, since in a one-exchange trace it happens to equal the request id.

`0xFF0001`/`0xFF0002` (To/From) swap between request and response, so the handler is a *secondary* signal,
but it is not unique — two concurrent calls to the same handler are indistinguishable by it. Request id only.

### 1.2 The crypto is per-frame stateless ✅

Despite the name, [`WinboxStreamCrypto`](tik4net/Crypto/WinboxStreamCrypto.cs) is **not** a running stream
cipher. `Encrypt` emits `[enc_len 2B BE][IV 16B][ciphertext]` with a **fresh random IV per frame**, and
`Decrypt` derives everything it needs from that one frame plus the fixed post-handshake keys. There is no
cross-frame cipher state, no counter, no replay window.

**Consequence: frames may be decrypted independently and completed out of order.** Had this been a stateful
stream cipher, multiplexing would have been impossible without a redesign — this was the real risk and it
came back clean.

### 1.3 There are no unsolicited inbound frames today ✅

Monitors are **polling loops**, not subscriptions: `MonitorLoop`
([WinboxNativeConnection.cs:654](tik4net/WinboxNative/WinboxNativeConnection.cs:654)) does
`StartMonitor` → repeat{`PollMonitor`, sleep autorefresh} → `CancelMonitor`, each step a normal
request/response under `_cmdLock`. So every inbound frame today is a reply to a request we sent.

The design must still handle an unmatched frame (§4.4) — but it is a robustness path, not the normal case.

---

## 2. What multiplexing actually buys — and what it does not

Worth stating honestly, because the headline benefit is *not* the obvious one.

**It does not speed up a single logical operation.** The three exchanges in §1.1 are sequential and
*dependent*: the interface `getall` happens because the address record referenced `ether1`. No amount of
multiplexing parallelizes a chain where each request's existence depends on the previous reply.

**What it does buy:**

1. **Monitors stop blocking CRUD and each other.** Today every poll takes `_cmdLock`
   ([:679](tik4net/WinboxNative/WinboxNativeConnection.cs:679)), as does every CRUD call
   ([:238](tik4net/WinboxNative/WinboxNativeConnection.cs:238), [:433](tik4net/WinboxNative/WinboxNativeConnection.cs:433), …).
   With two monitors plus interactive CRUD, everything queues behind one semaphore, and a slow CRUD call
   delays monitor rows past their autorefresh interval.
2. **It removes a correctness hack.** `SupportsStaleDrain` / `DrainSocket`
   ([WinboxM2Session.cs:260](tik4net/Winbox/WinboxM2Session.cs:260)) exists *only* because lockstep
   `SendRecvRaw` reads "the next frame" rather than "my frame": after a timed-out request, the late reply
   would be mis-delivered to the following caller. With id dispatch, a late reply is matched to its (dead)
   registration and dropped — deterministically, instead of being drained on a best-effort timer.
3. **It unlocks `CancelInFlight`** for WinBoxNative (P2.1 §3), moving it out of the CLI family's permanent
   ❌ tier.

Benefit 2 is the one that justifies the risk: it converts a papered-over desync into a structural guarantee.

---

## 3. Design

### 3.0 Correction: the reader loop does NOT go into `WinboxM2Session`

The table at the end of this section originally said "`WinboxM2Session` → reader loop + `_pending` registry".
**That is wrong and would break the WinBox-CLI transports.**

`WinboxM2Session` (and `WinboxMacM2Session`) is shared by *two* consumers with incompatible read models:

| consumer | read model |
|---|---|
| `WinboxNativeM2Operations` | request/response, every reply carries `0xFF0006` |
| `WinboxCliClient` (mepty terminal) | **streaming**: polls `DataAvailable` and calls `Receive()` for *unsolicited* terminal-output frames that carry no request id at all ([WinboxCliClient.cs:178](tik4net/WinboxCli/WinboxCliClient.cs:178), :199, :243, :293, :328) |

A reader loop installed in the session would consume the terminal frames and hand them to
`OnUnmatchedFrame` — i.e. silently swallow all CLI output. §12.5 of findings-winbox.md ("no unsolicited
frames") is true **of the native M2 path only**; the terminal is exactly the exception, and reading that
statement as a property of the session class is what produced the wrong placement.

**Multiplexing is a native-M2 concern, so it belongs in a layer above the channel:** a new internal
`WinboxM2Multiplexer` wrapping an `IWinboxM2Channel`, owning the read side, used only by
`WinboxNativeM2Operations`. `WinboxCliClient` keeps using the channel directly, exactly as today —
**zero change to the CLI transports**, which is also a meaningful risk reduction on reverse-engineered code.

This works because a given connection is either CLI or native, never both: `WinboxNativeConnection` and the
CLI connections construct their own channel and never share one, so the multiplexer's exclusive ownership of
the read side is uncontested.

### 3.1 Reader loop

Structurally identical to the API reader loop (P2.1 §4) over different framing — one reader, many waiters:

```
_readerTask = Task.Run(ReaderLoopAsync)      // started after Authenticate(), stopped on Close/EOF

ReaderLoopAsync:
    while (open):
        byte[] m2 = await _channel.ReceiveAsync()      // one decoded M2 message
        int? reqId = M2Message.ParseSysReqId(m2)       // NEW parser, key 0xFF0006
        if (reqId is int id && _pending.TryRemove(id, out var reg))
            reg.Complete(m2)
        else
            OnUnmatchedFrame(m2)                       // §4.4
    on EOF/exception → fault ALL pending registrations

SendReceiveAsync(build, ct):
    id  = NextReqId()                                  // Interlocked, §4.1
    reg = _pending.Register(id)                        // BEFORE the write — §4.3
    await _writeLock.WaitAsync(ct); try { await _channel.SendAsync(build(id)); } finally { release }
    return await reg.Task.WithCancellation(ct)
```

`_cmdLock` disappears from the request path. It is replaced by `_writeLock` — which is held only for the
duration of the *write*, not for the round-trip. That is the entire performance story.

### New/changed pieces

| Piece | Change |
|---|---|
| `M2Message` | add `ParseSysReqId(byte[])` — reads `0xFF0006`; returns `null` when absent |
| `IWinboxM2Channel` | add `SendAsync`/`ReceiveAsync`; **keep** `Send`/`Receive`/`SendReceive` for the auth path (§4.2) and for `WinboxCliClient`, which is not multiplexed (§3.0) |
| `WinboxM2Multiplexer` | **NEW** (§3.0) — reader loop + `_pending` registry over an `IWinboxM2Channel` |
| `WinboxM2Session` | async frame primitives only; **no** reader loop, no registry |
| `WinboxMacM2Session` | same, with the MAC caveats in §4.5 |
| `WinboxNativeConnection` | drop `_cmdLock` from CRUD + monitor paths |
| `WinboxTcpTransport` | async `ReadExact`/`Send*` (already in flight as the D5a conversion) |

---

## 4. Constraints and edge cases

### 4.1 The request id is one byte

`NextReqIdField()` is `U8Sys(RequestId, (byte)(++_reqId))`
([WinboxM2Session.cs:102](tik4net/Winbox/WinboxM2Session.cs:102)) — it **wraps at 256**, and `++_reqId` on a
plain `int` field is not thread-safe once concurrent senders exist.

- Use `Interlocked.Increment` and mask to 8 bits.
- **Refuse to reuse an id that is still pending.** With ≤256 outstanding requests a collision means a
  genuine leak, so: if the next id is already in `_pending`, that is a bug — throw rather than silently
  overwrite a registration and mis-deliver a reply. In practice outstanding count is <10.
- Id `0` is currently never used (counter is pre-incremented). Keep it reserved so "no id" stays
  unambiguous.

### 4.2 The auth handshake stays lockstep

`EcSrp5Auth`/`LegacyMd5Auth` are timing-sensitive reverse-engineered sequences with raw
`_transport.ReadExact(2)` reads outside the M2 message framing
([WinboxM2Session.cs:195-199](tik4net/Winbox/WinboxM2Session.cs:195)), plus a `Thread.Sleep(200)` +
`DrainSocket` step ([:229](tik4net/Winbox/WinboxM2Session.cs:229)).

**The reader loop starts only after authentication completes.** Auth keeps using the existing synchronous
lockstep path, untouched. This is the same call made in P2.1 D5a and for the same reason: it runs once, its
cost is irrelevant, and it is the most fragile code in the file.

### 4.2a Per-operation timeouts move off the socket — and this subsumes a known smell

Today every read sets the socket receive timeout, reads, and restores it
(`WinboxM2Session.Receive`/`RecvAndDecrypt`/`SendRecvRaw`, [:91](tik4net/Winbox/WinboxM2Session.cs:91),
[:114](tik4net/Winbox/WinboxM2Session.cs:114), [:127](tik4net/Winbox/WinboxM2Session.cs:127)). **A single
reader loop cannot do that** — there is no longer a per-call read to wrap. Timeouts move to the
registration: each pending request gets its own deadline (`CancellationTokenSource`), and the socket keeps
one connection-level receive timeout.

This structurally resolves the item P1.8 deliberately left alone: `InitAfterAuth` passes `ConnectTimeout` as
the per-operation timeout for `WinboxNativeM2Operations`
([WinboxNativeConnection.cs:201](tik4net/WinboxNative/WinboxNativeConnection.cs:201)), so every native
command is bounded by the *connect* timeout rather than `ReceiveTimeout`. Once timeouts are per-registration,
that plumbing is rewritten anyway and the right value (`ReceiveTimeout`) falls out naturally.

**Do not fix it separately first** — it would be churn on code this change rewrites.

### 4.3 Register before writing, always

The registration must exist before the bytes go out, or a fast reply can arrive at the reader loop before
the sender has registered — and be dropped as unmatched. This is the classic race in this pattern and the
reason the sketch in §3 registers outside the write lock.

### 4.4 Unmatched frames

A frame with no `0xFF0006`, or one whose id has no pending registration (late reply after a timeout/cancel).
Policy: **log via `TransportDiagnostic` and drop.** Do not throw — a late reply to a cancelled request is
expected once `CancelInFlight` exists, and it must not take the connection down.

### 4.5 The MAC transport is not the same problem

`WinboxMacM2Session` runs M2 over UDP 20561 with its own sequencing/ACK layer, and already declares
`SupportsStaleDrain = false` because `_udp.Available` reflects ACK/PING/retransmit noise rather than real
frames ([IWinboxM2Channel.cs:24-30](tik4net/Winbox/IWinboxM2Channel.cs:24)). Per the MAC-Telnet memory the
ACK accounting is `counter + payloadLen` and is easy to get subtly wrong.

**Recommendation: multiplex the TCP session first and ship it; treat the MAC variant as a separate follow-up**
with its own live verification. Same interface, materially different failure modes.

### 4.6 What must not change

Wire encodings, `.jg` catalog/resolver, `WinboxRecordCodec`, `EcSrp5`, `WinboxStreamCrypto`. This change is
message *routing* only. If a diff touches an encoder, it has left the intended scope.

---

## 5. Testing

The honest problem: **there is no loopback fake for WinBox**, unlike the API's `FakeRouterServer` (P1.7).
Multiplexing is exactly the kind of change that deterministic tests catch and live-router smoke tests do not.

Recommended, in order:

1. ~~**Build a `FakeWinboxServer`**~~ **✅ DONE 2026-07-21** — `tik4net.unittests/Winbox/FakeWinboxServer.cs`
   + 7 `WinboxM2SessionProtocolTests`. Cheaper than the "fixed post-handshake key pair" this section
   originally proposed: answering the EC-SRP5 hello with a non-`0x06` frame tag triggers the client's own
   fallback to **legacy MD5**, which is scriptable with no crypto and no production test seam — and covers
   the auth fallback path, which had zero tests. Also added `M2Message.ParseSysReqId` (§3's new parser).
   *Gap:* the legacy path is unencrypted, so the AES frame path is still uncovered; add an injected-key
   mode when a test needs it.
2. Deterministic tests over it:
   - two concurrent requests, replies returned **out of order** → each caller gets its own reply
   - reply with an unknown id → dropped, connection survives
   - reply missing `0xFF0006` → dropped, connection survives
   - EOF mid-flight → all pending registrations fault, no hang
   - id wraparound across 256 requests
   - concurrent monitor poll + CRUD → no interleaved chunk sequences on the wire (assert server-side)
3. **Live verification** per CLAUDE.md: full `winboxnative.runsettings` pass, plus the smoke subset
   (`ConnectionTest`, `SystemClockTest`, `InterfaceListTest`, `IpRouteTest`).

Step 1 is a real cost and it may be worth splitting into its own change that lands *before* the multiplexing
work — reviewing a protocol refactor and a new test harness in one diff serves neither.

---

## 6. Open questions

1. **`FakeWinboxServer` first, or live-only?** Leaning **first**, per §5 — CLAUDE.md marks `WinboxNative*/`
   as change-only-with-live-verification precisely because it has no deterministic coverage, and this change
   makes that gap more expensive, not less.
2. ~~**Does `0xFE0019` mean "more frames follow"?**~~ **RESOLVED (2026-07-21): no.** It is the total
   object count (`objCount`), informational only — see findings-winbox.md §12.7. The completion rule in
   §3 stands unchanged: **one request → exactly one reply frame**, registration completes on the first
   frame carrying the matching `0xFF0006`. Pagination is *not* multi-frame — a continuation is a new
   request with a new id (§3.1a).
3. **Should `CancelInFlight` ship with this, or after?** M2 has no observed cancel verb for an arbitrary
   in-flight request (monitors have `CancelMonitor`, which is a different thing). Without one, "cancel"
   can only mean abandoning the registration locally — which is safe here (unlike a PTY) precisely because
   id dispatch means the late reply is identifiable and droppable. Leaning: **ship multiplexing, then
   evaluate cancel separately.**
