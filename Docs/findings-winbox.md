# Findings — WinBox CLI connection (Chapter G)

**Date:** 2026-06-05
**Transport:** `TikConnectionType.WinboxCli` (TCP 8291, mepty terminal)
**Verified live:** RouterOS 7.21.4 CHR (coordinates in `tik4net.integrationtests/App.config`)

Shared protocol findings live in memory notes `project_winbox_m2_poc` and `ref_cli_telnet`. This
file covers only what came to light or got resolved during production integration (Chapter G).

---

## 1. ROOT CAUSE — SESSION_ID > 255 is a u32, not a u8 (resolved)

**Symptom:** the mepty terminal wouldn't open — `OpenTerminalSession` threw
`InvalidOperationException: No SESSION_ID in M2 response`. Because of this, the PoC had both mepty
tests marked `[Ignore]` with the assumption "drain timing between terminal sessions" — a **wrong
diagnosis**.

**Actual root cause (live dump of the mepty-open response):**

```
4D 32                                              "M2"
01 00 FF 88 02 00 00000000 00000008                SYS_TO  u32[] = [0, 8]
02 00 FF 88 01 00 0000004C                          SYS_FROM u32[] = [76]   (0x4C = mepty handler)
1C 00 FF A0 0001 0010 6D73...34                     0xFF001C str_array "msg-proxy-7.21.4"
01 00 FE 08 09 01 00 00                              SESSION_ID key=0xFE0001, TYPE=0x08 (u32) = 0x109 = 265
03 00 FF 09 02                                       0xFF0003 u8 = 2
06 00 FF 09 01                                       0xFF0006 u8 = 1
```

Session id = **265**, sent as **u32 (type 0x08)**, because it can't fit in a single byte.
The PoC's `M2Message.ParseSessionId` only looked for `type == 0x09` (u8) → found nothing. And
`SessionIdField(int)` always encoded a u8 → sending 265 back turned into `(byte)265 = 9` →
addressing the wrong session → dead terminal.

**Fix** (`tik4net/Winbox/M2Message.cs`):
- `ParseSessionId` reads key `0xFE0001` as both type 0x09 (u8) and 0x08 (u32).
- `SessionIdField(int id)` encodes u8 for `id ≤ 255`, u32 otherwise.

This was actually predicted in `project_winbox_m2_poc` §9: "Correct SessionIdField implementation —
in reality it may be 2B". The drain-timing hypothesis from the PoC's ignore comment was a dead end.

---

## 2. Login terminal-size hint: 80×25, NOT wide

`meptyLogin` (cmd `0x0A0065`) carries `U32User(3)=cols`, `U32User(4)=rows`. Feeding it a large value
(65535 was tried) makes RouterOS return an **error response with no SESSION_ID** (same symptom as
§1, different cause!). Stick with **80×25** as in the PoC.

The actual terminal width is determined later anyway, by the **VT100 cursor probe**
(`ESC[9999C ESC[6n`), which gets back `Vt100State(65535, 25)` — the reply caps at ~9999 columns,
which is enough to keep long `print as-value` lines from wrapping. (Same principle as MAC-Telnet,
see findings-mactelnet.)

---

## 3. Encrypted channel — DataAvailable gating is mandatory

Unlike MAC-Telnet (UDP datagrams), WinBox runs over **TCP + AES-128-CBC frames**. The trap
(confirmed from `project_winbox_m2_poc` §2): never call decrypt with a short timeout inside a retry
loop — when the timeout expires **mid-frame**, the TCP stream is left misaligned and every
subsequent read fails with an IOException.

The fix in `WinboxCliClient`: every read is gated by `while (!_session.DataAvailable)
Thread.Sleep(20)`, and only then does `_session.Receive(5000)` run with a generous per-frame
timeout (it only bounds a frame that is *already arriving*, and never expires mid-frame). This
works reliably even for multi-packet responses.

Warning: that generous timeout is safe **only as long as `DataAvailable` doesn't lie**. Over
MAC/UDP it did lie, and cost 5 s per command — see Chapter 15.

---

## 4. A persistent mepty session works

The PoC opened a **new** mepty session for every command (`RunTerminalCommand`). Production opens
mepty **once**, in `LoginAsync`, and every subsequent command reuses the same session (the counter
`U32User(3)` increments across commands). No problem there — a PTY is a terminal, it holds state.
This also makes the supposed "drain timing between terminal sessions" issue moot.

---

## 5. Architecture — shared vs. CLI-specific

- `tik4net/Winbox/` = **shared only**: `M2Message` (TLV), `WinboxTcpTransport` (chunked framing),
  `WinboxM2Session` (EC-SRP5/legacy MD5 auth + AES frame I/O + generic `Send`/`Receive`/`SendReceive`
  /`NextReqIdField`). **No mepty/VT100** here — so a future `WinboxNative*` can build on it.
- `tik4net/WinboxCli/` = the terminal mode: `WinboxCliClient` (mepty [76] + VT100),
  `WinboxCliConnection`.

The crypto (`EcSrp5`, `WinboxStreamCrypto`) already lives in `tik4net/Crypto/` from Chapter E
(shared with MAC-Telnet) → WinBox does NOT need a separate NuGet package, it lives in-core just
like Telnet/MAC-Telnet.

For Chapter H (`WinboxCliMac*`), `WinboxM2Session` will need to be generalized over the transport
(`WinboxTcpTransport` → `MacLayerTransport`); the generic Send/Receive methods are already set up
for that.

---

## 6. Legacy MD5 + terminal = unsupported

A mepty session only runs over the encrypted EC-SRP5 channel. If the server falls back to legacy
MD5 auth (pre-6.43 RouterOS), `WinboxCliClient.OpenTerminalSession` throws a
`NotSupportedException`. The auth fallback in `WinboxM2Session` stays in place (for future native
legacy operations), but the terminal path does not support it.

---

## 7. Results (WinboxCli / TCP)

- `WinboxCliProtocolTest` — **2/2** (login+list interfaces, set+verify ether1 comment)
- `InterfaceTest` over `winboxcli.runsettings` — **9 pass / 6 skip / 0 fail** (skips are CLI
  limitations: async/listen/monitor-traffic; exact parity with Telnet/MAC-Telnet)
- Login ~0.6 s, set+verify ~2 s.

---

# WinBox CLI over MAC (`WinboxCliMac`, Chapter H)

**Transport:** UDP 20561, `client_type=0x0f90`, mepty terminal. Shares the entire CLI engine with
Chapter G through the `IWinboxM2Channel` abstraction; only the channel differs
(`WinboxMacM2Session : MacLayerTransport`).

## 8. ROOT CAUSE — MAC-WinBox is the WinBox protocol tunneled over MAC (the PoC got both parts wrong)

The PoC's `WinboxMacClient` was `[Ignore]`d as "EXPERIMENTAL, M2 framing unverified". Two mistaken
hypotheses, disproven live (RouterOS 7.21.4):

1. **Auth is NOT MAC-Telnet control-packet auth.** The PoC called the base
   `MacLayerTransport.Authenticate` (CTRL_BEGINAUTH/PASSSALT) → **timeout** (the router never
   replies). The correct behavior: the **same WinBox EC-SRP5 handshake as over TCP** —
   length-prefixed `[len][0x06][payload]` frames sent as DATA payload. The challenge arrived as
   `31-06-0E-22-…` = `[len=49][tag=0x06][32B xWB][1B parity][16B salt]`.
   ⇒ In `WinboxMacM2Session.MacAuthEcSrp5`, after `BaseConnect(0x0f90)` the WinBox hello is sent and
   the same EC-SRP5 math runs as in `WinboxM2Session.EcSrp5Auth`.

2. **Encrypted M2 is NOT a bare `Encrypt(m2)` inside DATA.** The PoC sent
   `Send(PKT_DATA, Encrypt(m2))` and decoded the raw payload → decrypting from the wrong offset →
   "Not a valid M2 response". The correct behavior: **the same chunk framing as over TCP**
   (`[chunkLen 1B][tag][data]…`, 0xFF = continuation) inside the DATA packets. ⇒ `Send` chunk-wraps
   (`ChunkWrap`), `Receive` reassembles via the `_rxBuf` buffer (a chunk can cross a DATA packet
   boundary), and only then does `WinboxStreamCrypto.Decrypt` run.

**Conclusion:** MAC-WinBox is the entire WinBox protocol (EC-SRP5 handshake + chunked AES frames)
tunneled over the MAC reliable stream. The only difference from TCP is the transport (DATA/ACK
packets instead of a TCP stream). That's why the CLI engine (`WinboxCliClient`) could be shared
unchanged — a channel abstraction was all that was needed.

## 9. Further MAC findings

- **ACK = counter+payloadLen** — the production `MacLayerTransport.AckData` (from Chapter D/E), not
  the bare `SendAck(counter)` from the PoC. Retransmission dedup happens via `_inCounter`.
- **The mac-winbox server is separate from mac-telnet:** `/tool/mac-server/mac-winbox set
  allowed-interface-list=all` (the test sets this in `[ClassInitialize]`). Both were enabled live.
- **The SESSION_ID u8/u32 fix from Chapter G applies here too** (shared `M2Message`).
- **Speed:** login ~16 s, set+verify ~32 s (MNDP ~5 s + per-frame AES + UDP polling with sleeps).
  Much slower than TCP (~1–2 s). For production, set `RouterMac` to bypass MNDP.

## 10. Results (WinboxCliMac / MAC)

- `WinboxCliMacProtocolTest` — **2/2** (login+list, set+verify ether1 comment).
- Regression: WinboxCli 2/2 + MacTelnet 2/2 + WinboxCliMac 2/2 = **6/6** combined.

---

## 11. Protocol constants centralized (2026-06-11)

All the M2 numbers mentioned above inline (`0x0A0065` meptyLogin, `0x0A0067` meptyData, mepty
`U32User(3)=cols`/`U32User(4)=rows`, `0xFF0005/06/07`, mproxy cmd 7/3/4/5, SESSION_ID `0xFE0001`)
now live in **`tik4net/Winbox/WinboxM2Protocol.cs`** (`internal static`, shared by production code
and tests). Sections: `SysKey` / `RecordKey` / `Command` / `Error` / `Mproxy` / `SysInfo` /
`LegacyAuth` / `Mepty` / `Tlv`.
Note: mepty `Key.Cols` (3 on Login) and `Key.Counter` (3 on Data) are the same number with different
meanings (now documented).
See `winbox-native-m2-plan.md` §12 for the full list and collisions.

---

## 12. M2 request/response correlation — groundwork for multiplexing (2026-07-21)

Verified live (RouterOS 7.21.4, test CHR) while preparing `winbox-m2-multiplexing-design.md`. Up to
this point the M2 layer ran **lockstep** — `SendRecvRaw` reads "the next frame", not "my frame" — so
nobody needed correlation, and `M2Message` still has no parser for it.

### 12.1 `0xFF0006` (RequestId) comes back in the response ⇒ it's the correlation key

`/ip/address/print` over WinboxNative is three M2 exchanges within one session (reference
resolution — the address refers to `ether1`, so a getall for interface and VRF follows):

| exchange | handler (`0xFF0001` To) | request `0xFF0006` | response `0xFF0006` | response `0xFF0003` |
|---|---|---|---|---|
| getall address | `[20,1]` | 2 | **2** | 2 |
| getall vrf | `[20,101]` | 3 | **3** | 2 |
| getall interface | `[20,0]` | 4 | **4** | 2 |

`0xFF0006` tracks the request exactly. Multiplexing (multiple requests in flight, dispatching
responses by id) is therefore feasible.

### 12.2 `0xFF0003` is NOT a correlation field — a trap for single-exchange traces

`0xFF0003` isn't defined in `WinboxM2Protocol`, and it stays constant (2) across the session while
the req id keeps increasing. It looks like a session / reply-channel id.

**Trap:** in a trace of a single exchange (`/system/identity/print`, req id = 2), `0xFF0003`
*happens to have the same value* as the req id → on a single sample you'd pick the wrong field.
Only more round-trips tell them apart.

Independent confirmation is, in fact, **already in §1 of this document**: the mepty-open dump from
2026-06-05 has `0xFF0003 u8 = 2` and `0xFF0006 u8 = 1` — there the two fields differ. That evidence
sat in the repo for 6 weeks before anyone needed it.

### 12.3 The crypto is stateless per frame ⇒ multiplexing is cryptographically safe

The key finding, because this was the only real blocker. Despite the name `WinboxStreamCrypto`,
**it is not a running stream cipher**: `Encrypt` emits `[enc_len 2B BE][IV 16B][ciphertext]` with a
**fresh random IV for every frame**, and `Decrypt` needs only that frame plus the fixed keys from the
handshake. No cross-frame state, no counter, no replay window.

⇒ Frames can be decrypted independently and **completed out of order**. If this were a stateful
stream cipher, multiplexing would be impossible without redesigning the crypto layer.

The only remaining ordering constraint is **framing**: `RecvChunked` assembles a sequence of chunks,
so reads must be serialized (a single reader) and so must writes (a chunk sequence must not
interleave). That's exactly a reader-loop plus a write-lock, nothing more.

### 12.4 `0xFF0001`/`0xFF0002` (To/From) swap in the response

Request `To=[20,1] From=[0,8]` → response `To=[0,8] From=[20,1]`. The handler is therefore a
secondary signal, but **not unique** — two concurrent requests to the same handler can't be told
apart by it. Dispatch exclusively on `0xFF0006`.

### 12.5 Today there are no unsolicited incoming frames

Monitors are **polling loops**, not subscriptions: `MonitorLoop` does `StartMonitor` → repeated
`PollMonitor` → `CancelMonitor`, each step a normal request/response. That's exactly why lockstep
works at all. A multiplexed implementation, though, still needs to be able to discard an unmatched
frame (a late response after a timeout) — a robustness concern, not the common path.

### 12.6 The req id is one byte

`NextReqIdField()` = `U8Sys(RequestId, (byte)(++_reqId))` → **wraps at 256**, and `++_reqId` on a
plain `int` field stops being safe as soon as there are concurrent senders (needs `Interlocked` +
an 8-bit mask). Id `0` is never used today (the counter is pre-incremented) → keep it reserved as
"no id".

### 12.7 `0xFE0019` is objCount, not "more frames follow" (closed 2026-07-21)

The suspicion from an earlier version of this section (that `0xFE0019=u8:1` signals continuation)
did **not** hold up.

Source of truth — webfig `master-d53cd8ec58cb.js`, the only two uses of the field in the entire
file:

```js
// ObjectMap.prototype.getall  → onreply
if (rep.ufe0019 != null) me.objCount = rep.ufe0019;
// ObjectMap.prototype.listen  → notifyLstn
if (msg.ufe0019 != null) me.objCount = msg.ufe0019;
```

It's stored into `objCount` and **never read** anywhere in flow control — no loop condition, no
termination check, no registration. It's just an informational total object count (hence `1` for
exchanges with a single record, and its absence where the handler didn't send it). In fact
`WinboxM2Protocol.RecordKey.Count` was already documented that way — the constant sat in the repo
with a comment saying "total object count", and all it took was actually looking at it instead of
carrying it forward as an open question.

**Impact on multiplexing: none.** The completion rule stays "one request → exactly one response
frame"; a registration closes on the first frame with a matching `0xFF0006`.

#### 12.7.1 There is no multi-frame paging

Verified in the same source — a continuation is a **new request**, not another unsolicited frame:

```js
else if ((rep.ufe0003 != null || rep.mfe0015) && !me.block) {
    if (rep.ufe0003 != null) req.ufe0003 = rep.ufe0003;
    post(req, onreply);            // ← new request, new id
}
```

That's exactly what our client does too: the loop calls `NextReqIdField()` on every iteration
([WinboxNativeM2Operations.cs:129](tik4net/Winbox/WinboxNativeM2Operations.cs:129)) and attaches the
token as `RecordKey.Continuation` ([:134](tik4net/Winbox/WinboxNativeM2Operations.cs:134)). So the
registration model doesn't change at all: **each page is a separate registration with its own id.**

Side finding (out of scope for multiplexing): webfig also continues on `rep.mfe0015`, while our
client only watches `ufe0003` ([:151](tik4net/Winbox/WinboxNativeM2Operations.cs:151)). For a
handler that pages via `mfe0015`, we would silently return only the first page. We haven't hit this
live; it's worth verifying separately.

#### 12.7.2 Note on `post()` — webfig correlates over HTTP, not `0xFF0006`

`uff0006` **never appears at all** in the webfig JS: it's a jsproxy over HTTP, where the
request/response pairing is handled by HTTP itself. Webfig is therefore **not** a source of truth
for req-id semantics — that rests on the live trace in §12.1. For `0xFE0019` it *is* a source of
truth, because that field's meaning is transport-independent.

Webfig only learns about unsolicited messages through `subscribe` (cmd `0xFE0012`), and dispatches
them by `Uff0002` (`From`/path) on a separate long-poll (`post_notification_request`). Over native
TCP, such pushes would arrive in-band — we don't use them today (§12.5), but this is a second
reason the reader loop needs a branch for an unmatched frame (§4.4 of the design doc), and it hints
at what such dispatching would key on if subscribe were ever added.

### 12.8 Parallel connections from one machine are not marked in M2

The hypothesis that some field must identify the connection (because of multiple sessions from one
machine, typically with MAC variants) **doesn't hold at the M2 layer** — it's distinguished below
that layer:

| transport | what separates parallel sessions |
|---|---|
| WinBox TCP / TCP-MAC | TCP socket (4-tuple), each session has its own connection |
| WinBox over the MAC layer | a random `_sessionKey` in the packet header ([MacLayerTransport.cs:98](tik4net/MacTelnet/MacLayerTransport.cs:98)) |

The candidate for a "reply-channel id" from §12.2, `0xFF0003` (constant 2 across the session), never
appears in the webfig JS at all, so its meaning remains undetermined. It doesn't matter for
dispatch either way: **it's constant, so it wouldn't distinguish two concurrent requests anyway.**
Correlation stays exclusively on `0xFF0006`.

---

## 13. The router refuses a correct login roughly one time in a hundred (2026-07-30, P2.41)

**Verified live on RouterOS 7.23.2.** Roughly **0.5–1% of WinBox logins** end with the router
sending **33 bytes of ASCII** where a 32-byte confirmation digest belongs:

```
69 6E 76 61 6C 69 64 20 75 73 65 72 20 6E 61 6D 65 20 6F 72 20 70 61 73 73 77 6F 72 64 20 28 36 29
"invalid user name or password (6)"
```

The router's own log backs this up (`system,error,critical login failure for user admin … via
winbox`), so **our "wrong password" message wasn't a fabrication** — nobody just knew why. The
credentials are correct the whole time and work again 50 ms later.

### 13.1 It's not us — proof by replaying the same key

The decisive experiment (`WinboxHandshakeLoopProbeTest.Probe_WinboxHandshake_SameKeyRetry`): after
each refusal, the handshake is repeated with the **same** client key `privA`. Result: **9 of 9
replays accepted** — the exact same bytes the router just refused get accepted moments later. The
only thing that changes between attempts is the router's own ephemeral key `xWB`. This ruled out:

| suspicion | how it was ruled out |
|---|---|
| a bug in our EC-SRP5 arithmetic | 4000 client↔server round-trips offline, **0 divergences** (`EcSrp5RoundTripTests`) |
| a leading zero byte in `xWA` (1/256 ≈ observed frequency) | forced deliberately: **4 of 5 succeeded**; the first sample was just luck |
| rate-limiting / attempt frequency | 2/40 at 0 ms, 0/40 at 250 ms, 1/40 at 1000 ms — no trend |
| frame desync | the frame is a well-formed chunk with tag `0x06`; the length of 33 matches the text length exactly — nothing overflowed or was missing |
| a different transport / different auth | API: **0 of 400** refusals — the phenomenon is specific to the WinBox handshake |

The log also has one lone `via api` entry that couldn't be attributed to any of our clients; the
400 fresh API logins were all clean, so nothing is built on that entry.

### 13.2 What to do about it — bounded retry, since the content can't distinguish the two causes

**A genuinely wrong password looks exactly the same** (it's the router's normal path for a
refusal), so the response content can't tell them apart — only the fact that a transient refusal
disappears and a real one doesn't. Hence `WinboxLoginRetry`: 3 attempts, 100 ms apart, retrying
**exclusively** on `WinboxLoginRefusedException`. Each attempt builds a **new channel** — a refused
handshake leaves the old one unusable.

The cost is deliberate: a truly wrong password now fails ~200 ms later and leaves 3 `login failure`
lines in the router instead of one.

**Verified:** 600 production connection opens (WinboxCli / WinboxNative / WinboxNativeMac, 200
each), **0 failures and 6 absorbed refusals**, all resolved on the first retry. That the retry is
actually doing work (and the router wasn't simply silent) is visible from the trace note
`wbx.login` — without it, a green run is indistinguishable from sweeping the problem under the rug.

### 13.3 Side findings

- **The handshake never showed up in the wire trace at all.** `SendHandshake` writes directly to the
  `Stream` and reads via `ReadExact`, so it bypassed the emit points in `SendChunked`/`RecvChunked`
  — exactly the exchange that's hardest to debug was the one invisible piece. Fixed (`wbxtcp.frame`,
  note `ecsrp5 …`).
- **The MAC layer only traced sends.** `RecvUntil` emitted nothing, so the trace couldn't
  distinguish "no response arrived" from "we never even asked." Fixed.
- **The fallback to legacy MD5 was selected by matching message text**
  (`ex.Message.Contains("EC-SRP5")`), and it only waited 3 s for the challenge. A slow router would
  fall through to MD5 auth, which then failed on modern RouterOS, resulting in "wrong username or
  password". Replaced with a dedicated `WinboxEcSrp5UnsupportedException` type and a window of
  `max(3 s, ConnectTimeout)` = 15 s.
- **WinboxCliMac is slow enough that 9 tests time out** — a full run takes 1 h 22 m with 313/9,
  while the same CLI engine over TCP (`winboxcli`) does 322/322 in 8 minutes. Login ~11 s versus
  ~1.4 s. **Unrelated to P2.41**: those 9 tests behaved **identically (6 fail / 3 pass / 3 m 14 s)**
  on a build with P2.41 and on a stashed baseline.

  A trap worth avoiding: it's tempting to blame `RecvUntil`'s `Thread.Sleep(20)` instead of waiting
  on the socket (any frame arriving right after the `Available` check waits up to 20 ms). **Tried
  it** — `_udp.Client.Poll(20 ms, SelectRead)` moved the subset from 6 fail / 3 m 14 s to 5 fail /
  2 m 45 s, i.e. **~15%, and still red**. Reverted; the remaining ~85% is elsewhere, most likely in
  `WinboxCliClient` polling `DataAvailable` with its own sleeps (see §3 — that gating is
  intentional and must not be removed, only converted to event-driven). Written up as P2.43.

## 14. Singletons weren't being written at all (P2.44, 2026-07-30)

`0xFE000E` (`setcmd(holder)`) has been documented in `winbox-native-m2-protocol.md` from the start,
but the transport **never called it**. Writes only went through one path — `0xFE0003` (`set`) +
`ufe0001` = `.id` — and a singleton (`.jg` `type:'item'`) has no `.id` at all, so
`ResolveRecordId(required:true)` ended up with:

```
no such item: could not resolve record .id '' on '/system/identity/set'
```

This applies to **every** `IsSingleton` entity (`/system/identity`, `/ip/dns`, `/ip/settings`,
`/snmp`, `/system/note`, … ~35 classes). The test suite never caught this because it only ever
**read** singletons.

The shape of the request, per webfig's `ObjectHolder.setObject`:

```js
req.Uff0001 = this.attrs.path;
req.uff0007 = this.attrs.setcmd || 0xfe000e;
if ("ufe0001" in obj) req.ufe0001 = obj.ufe0001;   // .id only when the object itself carries it
```

So `.id` is sent **optionally** — the only known case is the hidden "Change Password" window
(`setcmd:3`), which targets a user record. `WinboxNativeConnection.WriteFields` therefore only
sends `.id` in its literal `*HEX` form; looking it up by name would require a `getall`, which a
singleton handler has nothing to answer.

### 14.1 `/system/identity` also returns a field under its GUI label

Handler `[24,1]`, `.jg`:

```js
{title:'Identity',type:'item',path:[ 24,1 ],autostart:1,
 c:[{name:'Identity',type:'string',id:'sc'},{name:'Version',type:'string',id:'sd',nonpublic:1}]}
```

So a read returned `{"version":"7.23.2","identity":"CHR"}`, whereas the API returns
`{"name":"CHR"}` — `LoadSingle<SystemIdentity>()` failed with `Missing field 'name'`. Fixed with a
shipped field alias `name ↔ identity` (the text is stable, the key still comes from `.jg`).

The `version` field is **not discarded**: `nonpublic:1` doesn't mean "not an API field" — plenty of
fields the API routinely returns carry it too (`MAC Address`, `Interface`, `L2 MTU`). Native records
are generally a superset of API fields, and the mapper simply ignores the extras.

### 14.2 `multilinestring` is a string, not a list

`EncodeField` rejected anything whose `.jg` UI type started with `multi…` as an unencodable list.
But webfig says:

```js
types.multilinestring = inherit(types.string);   // differs only in VIEW (textarea instead of input)
```

Of all the `multi*` types, this is the only scalar one — the others (`multinumber`,
`multinumberrange`, `multiipaddr`, `multistring`, …) inherit `types.multi`. Because of the shared
prefix, `note` on `/system/note` couldn't be written at all.

### 14.3 A list's element type is carried in `c`, not `values`

`ExtractRefHandler` only read `node["values"]`, so `RefHandler` ended up empty for a list of
references:

```js
{name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:'dynamic',path:[ 3,3 ]}}]}
```

so `topics` on `/log` decoded as the raw `"[9,3]"` instead of `"script,error"`.

---

## 15. `DataAvailable` over UDP lied and cost 5 s per command (P2.43, 2026-08-01)

`WinboxCliMac` was an order of magnitude slower than `WinboxCli` (a full run took 1 h 22 m vs.
8 min), and this had been written off as "MAC channel latency". **It isn't.** Measured on 7.23.2
with the `WinboxCliLatencyProbeTest` probe, which breaks a single command down by the
`wbxcli.mepty` wire-trace channel (shared by both transports, so they're directly comparable):

| span | WinboxCli | WinboxCliMac (before) | WinboxCliMac (after) |
|---|---|---|---|
| send → first byte | 25 ms | **25 ms** | 25 ms |
| first byte → prompt | 25 ms | 1 ms | 0 ms |
| prompt → return | 166 ms | **5012 ms** | 164 ms |
| total / command | 216 ms | **5039 ms** | 193 ms |
| open | 1142 ms | **6053 ms** | 1053 ms |

The first byte arrived **just as fast as over TCP**. The entire loss sat right after the prompt and
matched `WinboxCliClient.FrameTimeoutMs` = 5000 ms exactly.

**Cause.** Chapter 3 above says every terminal read is gated by `DataAvailable` and only then does
`Receive(5000)` run — that timeout is only allowed to bound a frame that's **already arriving**.
Over TCP that holds, because `NetworkStream.DataAvailable` means "there are bytes of a frame here".
Over UDP, `_udp.Available > 0` only means "some datagram arrived", and the vast majority of traffic
on that socket is ACKs, PINGs, and router retransmissions. Captured timeline of a single command:

```
34.8 ms  prompt seen (bytes=254)
34.8 ms  Recv 310B type=0x01 counter=3021   ← duplicate, AckData discards it
34.8 ms  Recv 310B type=0x01 counter=3021   ← second duplicate
…        RecvUntil rides out to the deadline
5033 ms  settled -> return @5024ms
```

`RecvUntil`'s contract is "wait up to the timeout, until the handler says enough" — correct for a
caller that's willing to block, but a catastrophe for a **poller**: every false-positive
`DataAvailable` cost the entire frame timeout. This hit once per command, and again once on
`DrainSync(250)` after login, which is exactly the "a skipped test costs 6 s" measurement from
P2.50.

**Fix.** `MacLayerTransport.RecvAvailable(handler)` is the polling counterpart to `RecvUntil`: it
processes everything already sitting on the socket and returns immediately (sharing the
`ReceiveOne` body, so ACK/PING/duplicates are handled identically on both paths).
`WinboxMacM2Session.DataAvailable` is built on it and now answers **"is a complete M2 frame
ready"**, not "did a datagram arrive"; a finished frame is held in `_pendingFrame` and the next
`RecvFrame` call hands it out. So the getter does perform I/O — deliberately: it's a poll operation
on the channel, and only the single-threaded terminal loop in `WinboxCliClient` reads it (the
native transport runs a reader loop, and `SupportsStaleDrain = false` forbids it from polling).

**Lesson:** a property that a caller treats as permission to block must actually be true.
MAC-Telnet doesn't have the same defect — it has a background pump with a blocking socket, not
`DataAvailable` gating.

## 16. The router silently drops a session and we don't find out for 30 s (P2.54, 2026-08-01)

After the P2.43 fix, `winboxclimac` still had three red tests: `SearchByName_Interface_WillWork`,
`Create_IpAddress_With_LowLevel_API`, `ListRadiusServersWillNotFail` — always
`nothing was received within 30000 ms`, always ~30.1 s, unchanged across three full runs before and
after the fix. It's the same trio P2.32 recorded as this transport's "wedge signature".

**What the trace showed.** The mechanism is identical across all three: the datagram carrying the
command goes out, the router **never acknowledges it**, and ignores eight byte-identical
retransmissions — `RETRANSMIT #8 end=15639 highestAck=15475`, where `highestAck` is exactly the
starting offset of that command. And critically: **the router sends nothing at all for the full
30 s** — no ACK, no PING, no retransmission. So this isn't "the router is rejecting our input", as
this family of symptoms had been read up to now.

> Warning, corrected in §17: this used to say "the router no longer has that session". That
> **overstated the case** — the router's own log records nothing at all at the moment of the
> wedge, no logout, no error. The precise claim the data supports is: *its MAC layer stops
> acknowledging our bytes*, while its own accounting has no record of any session ending. What
> P2.54 adds doesn't depend on that claim — recovery hinges on the missing acknowledgment, not on
> an explanation of why it's missing.

**What we did about it now.** Not the cause — we still don't know it (suspicion: in the seconds
before each wedge the router re-sends frames we already acknowledged, so our packets stopped
reaching it before the command was even sent; and all three are immediately preceded by a test
that opened and closed a second connection). We did what's doable without knowing the cause and
what's worth doing on its own merits:

* **`IWinboxM2Channel.SendAbandoned`** surfaces `MacLayerTransport.LastSendAbandoned` up into the
  CLI engine. Over TCP it's always `false` — TCP has nothing to leave unacknowledged; a dead
  connection there shows up as FIN/RST.
* **`WinboxCliClient.ReadCommandResponseSync`** consults it and, when "nothing arrived **and** the
  router never took our bytes", throws `TikConnectionSessionClosedException` instead of riding out
  the full 30 s.
  The `sb.Length == 0` condition matters: once the console has produced any output at all, the
  command **provably** arrived and could have run — claiming "it didn't run" at that point would
  be a lie the caller has no way to verify.
* **`WinboxCliMacConnection`** hangs reopen + retry off this, following `MacTelnetConnection`
  exactly (same carrier, same problem): a new connection plus a new EC-SRP5 login via
  `WinboxLoginRetry`, with two exceptions — not inside Safe Mode (dropping the session is exactly
  what Safe Mode protects against), and not once any line has already been delivered to the caller
  (restarting would deliver those same lines twice).

Telling "dead session" apart from "slow command" is the entire value of this signal — which is why
the fast path hinges on the missing acknowledgment, not on silence. If it hinged on silence, every
legitimately long-running command would fail.

**Lesson (same as P2.39, just one layer over):** the message "nothing was received within N ms"
describes our read, not what the other side did. When the carrier can say more, someone has to ask
it.

## 17. Why the router drops a MAC session — six hypotheses ruled out (P2.55, 2026-08-01)

The P2.54 wedge survives but goes unexplained. Three traced full runs bounded it sharply: **out of
27 opened sessions, exactly three are dropped, always in the same three tests**
(`Create_IpAddress_With_LowLevel_API`, `ListRadiusServersWillNotFail`,
`SearchByName_Interface_WillWork`). It's deterministic, not background noise that a retry merely
papers over.

What made this possible: `MacLayerTransport` now logs `SESSION OPEN key= local= srcMac=`, and
**every traced line carries `key=`**. The `wbxmac.udp` channel is shared across all MAC sessions, so
a trace captured while several are alive interleaves them — and the question "what did *this*
session do" simply couldn't be asked before. Without this, an earlier measurement of the gap since
the last received message was off by two orders of magnitude.

| hypothesis | verdict |
|---|---|
| collision of the 16-bit session key or the local port | **no** — across 27 sessions, no key or port ever repeated. The key is also drawn randomly on every open, so a collision would move around between runs; it doesn't. |
| our own flood | **an effect, not a cause** — before a wedge, ~24 packets / 2.4 kB pile up behind the unacknowledged head, because the pull loop fires 8×/s regardless of anything. But this only starts **after** the command that went unanswered. |
| closing a sibling session | **no** — `Probe_SiblingSessionTeardown`, 20 cycles (WinBox-MAC, MAC-Telnet, an API sibling), zero wedges. This was the prime suspect. |
| traffic volume / a boundary in the byte stream | **no** — `Probe_LongLivedSession` ran 400 commands and 101,099 outbound bytes on a single session without a hiccup, past two of the three offsets where the suite dies. |
| idle logout (like MAC-Telnet) | ~~**no** — per-session trace shows the session receiving packets right up to the start of the test. It lives and dies only on the **first command** of that test.~~ **This line was wrong, see §18.** The trace timestamp records when we *picked up* the socket, not when the packet arrived — an unserviced session therefore shows no gap at all, because its entire backlog dumps out at once on the next read. |
| the router's log echoing into the terminal (P2.47 family) | **no** — the entire run has exactly one such echo, which could at most cover one of the three. |
| that specific command | **no** — all three pass in isolation in 3 s with no drop. |
| the immediately preceding test | **no** — all three pairs (predecessor + victim) pass in 2–5 s with no drop |
| a session-count limit / eviction on the router | **no** — the wedge hits after 2, 14, and 22 opened sessions, and never more than 1–2 are alive at once |

**View from the router (newly added).** `/log` across all three windows: **at the moment of the
wedge the router logs nothing at all** — no `logged out`, no error. The nearest logout is 4 s
*after* one wedge and 4 s *before* another, both unrelated connections. Our session stays logged in
per the router's own accounting, while its MAC layer has stopped acknowledging our bytes. That's a
mismatch between two of the router's own layers, not a session termination — which is why the
phrasing "the router no longer has that session" in §16 was corrected.

Side observation from the same log, still unexplained: the 27 sessions our trace opened correspond
to 47 `via winbox` logins from our MAC — some paired within the same second, some standalone.
Uneven, so it isn't systematic double-logging; worth finding out what causes the pairs.

**Cause of one of the three: a Safe Mode rollback kills a concurrent WinBox-over-MAC session.**
Reproduced 5/5 via `Probe_SafeModeRollbackOnASibling`:

| held session | carrier | upper layer | response | |
|---|---|---|---|---|
| `WinboxCliMac` | MAC / UDP 20561 | WinBox M2 | ~4.3 s | **wedge 5/5** |
| `MacTelnet` | MAC / UDP 20561 | plain telnet | ~0.15 s | fine 0/2 |
| `WinboxCli` | TCP 8291 | WinBox M2 | ~0.37 s | fine 0/2 |

The rollback lands each time after ~2.15 s, independent of who holds the second session.

> Warning: this section originally said "it's a property of the MAC carrier, not the CLI engine"
> (drawn from a single TCP contrast; `MacTelnet` was only measured afterward — and survives), then
> "so it's exclusively their combination, i.e. the router's `mac-winbox` service". **Both wrong, and
> the second just as hastily as the first:** ruling out two layers doesn't mean the culprit is on the
> router. The difference between `MacTelnet` and `WinboxCliMac` wasn't in what the router does, but
> in what **we** do — `MacTelnetUdpClient` has had a receive pump from the start; WinBox-over-MAC
> didn't. See §18; with a pump the reproduction rate is 0/2. Lesson: "neither A nor B" is still only
> about A and B, and says nothing about C.

> Warning: **the rollback is asynchronous**, and that's the whole trick. RouterOS keeps the Safe Mode
> owner alive even after the connection dies, until the connection-tracking timeout, so the rollback
> lands **~2 s later** — which is exactly why `SafeModeTest` polls for it for up to 30 s. Query the
> held session right after closing the sibling, and you're asking at the wrong moment and get a
> healthy 223/224 ms. That's precisely how the claim "Safe Mode isn't the cause" made it into these
> notes twice. **It was.** And it also explains why the victim is always the first test of the class
> **following** the one that triggered it: the rollback lands only after that triggering class has
> already finished.

The path here ran through noticing that `ConcurrentCommandsTest` and `SafeModeTest` are the only two
classes with `ReuseConnectionAcrossTests => false`. They run on their own connection, so they never
show up in per-session analysis among the "users" of a shared session — even though they're the ones
that break it — which is why I initially ruled them out as a predecessor. All three wedges sit right
on a test-class boundary.

**What this means for library consumers:** whoever holds a WinBox-CLI-MAC connection while, on
another connection, running Safe Mode that ends in a rollback, loses that first connection. P2.54
recovers from it, but it costs ~4.5 s.

**Fix in the suite:** `SafeModeTest.OnCleanup() => DisposeSharedConnection()`. Waiting longer, or
sleeping, doesn't help — that test already polls until the rollback lands, so by the time it
finishes it's already too late; the shared session dies in the meantime and nothing touches it again
before the test ends. It has to be declared dead, not waited on. This isn't papering over a library
bug (the transport recovers on its own), just removing the suite's silent reliance on that recovery.
Measured impact: full run 3 → 2 dropped sessions, reproduction rate from 1 drop / 10 s to 0 / 7 s.

**Where this stands:** two wedges remain (`Create_IpAddress_With_LowLevel_API`,
`ListRadiusServersWillNotFail`), neither involving Safe Mode, and the router has no record of them.
But there's now a new class of mechanism worth testing: what else does RouterOS do asynchronously
that only reaches that session later. → **resolved in §18.**

**Side finding worth fixing regardless of the wedge:** we have no **send window** at all. When the
head of the queue is unacknowledged, the pull loop keeps piling 8 packets/s on top of it, pumping
2.4 kB into a hole in the stream that the router has no way to accept. Retransmission runs every
400 ms and correctly resends only the head — but nothing stops the rest of the queue from growing.

**Lesson:** a ruled-out hypothesis is a result too, as long as it's recorded along with what
disproved it. Five of these six sounded plausible, and four of them had already once appeared in
these notes as the likely cause.

## 18. Nobody serviced the socket between commands (P2.55 completion, 2026-08-02)

The two remaining wedges from §17 share one property that the three traced runs missed, because
nobody had asked about it: **they're each preceded by the single longest idle stretch on that
session in the whole run.**

| session | idle before the command | outcome |
|---|---|---|
| `1eba` | **19.7 s** | wedge (`Create_IpAddress_With_LowLevel_API`) |
| `ff1c` | **56.1 s** | wedge (`ListRadiusServersWillNotFail`) |
| `ef45` | 3.6 s | fine |

Those are every gap ≥ 3 s in the entire 340-test run. Two gaps, two wedges.

**Why §17 ruled this out.** Because of the claim "the session receives packets right up to the start
of the test, gap 0.0 s" — but the trace timestamp is the moment we *picked up* the socket, not when
the packet arrived. A session nobody reads for twenty seconds therefore shows no gap in the trace at
all: the entire backlog dumps out at once on the next read. This is visible literally in the trace —
at the moment the victim test starts, **the same `counter=37405` arrives ten times in a row**,
meaning the router had been retransmitting one unacknowledged packet the whole time.

**Cause.** The RouterOS terminal isn't request/reply: the router writes into it on its own (a log
event, a Safe Mode rollback), and at the MAC layer every such write must be acknowledged, or the
router keeps retransmitting it and eventually stops servicing the session. But `WinboxCliClient`
only touches the channel **while a command is running** — nobody picks up the socket between
commands.

This was actually already written down in the repo. `MacTelnetUdpClient` has carried this sentence
in its class comment since it was created ("drops the session when that output is left
unacknowledged"), and that's exactly why it has a **receive pump**. WinBox over MAC didn't. This
also explains the difference §17 attributed to the router: `MacTelnet` survives a Safe Mode
rollback because its pump acknowledges it; `WinboxCliMac` doesn't, because nothing is watching. The
difference was on our side. `WinboxNativeMac` doesn't suffer from this for the same reason — it runs
through the `ReceiveNextFrame` reader loop, which services the socket continuously.

**Fix:** `IWinboxM2Channel.StartIdleServicing()`, called once after login. Over TCP it's a no-op (the
kernel acknowledges the byte stream); over MAC it starts a thread that, every 200 ms, takes
`_rxGate` via `TryEnter(0)` and drains whatever is on the socket. A read holds the same lock for the
entire duration of assembling a frame, so the pump can never grab a packet mid-frame. The pump
**only acknowledges and answers PING, it never initiates anything on its own** — four ways of
concocting idle traffic have been measured against MAC-Telnet, and all four shortened a session's
life.

**Measured:**

| | before | after |
|---|---|---|
| Safe Mode rollback on a sibling (`WinboxCliMac`) | wedge 5/5, ~4.3 s | **0/2, ~0.34 s** |
| dropped sessions per full run | 3 → 2 (§17) | **1** |
| idle 20 s → command | ok | ok |
| idle 30 s / 45 s → command | wedge | **wedge** (see below) |

The remaining wedge in a full run is the one with 35.9 s idle, and it's a **different mechanism**:
the router logs out an idle MAC console after ~30 s, and it says so itself. The idle ladder confirms
this symmetrically — `MacTelnet`, which has had the pump from the start, wedges at 30 s in exactly
the same way. This can't be, and shouldn't be, papered over from the client side; the reopen+retry
from P2.54 handles it. Two different causes, two different fixes:

* **an unacknowledged write from the router** → our bug, fixed by the pump
* **~30 s idle logout** → the router's own contract, handled by reconnecting

**Lesson:** "measured, unconfirmed" and "unmeasured" look the same in a hypothesis table, but they're
not the same thing. That idle-logout line was disproved by a measurement of something other than
what I thought it measured — and it sat there marked resolved for nine hours as a result.

## 19. The MAC layer has no send window (P2.56, 2026-08-02)

A side finding from P2.55, fixed separately because it isn't related to the wedge's cause — it just
made it needlessly more expensive.

When the router doesn't acknowledge the head of the queue, `WinboxCliClient` keeps **pumping empty
pulls** into it regardless (8/s, `PullIntervalMs = 120`). ACK at the MAC layer is cumulative, so
**the router can't process anything behind the unacknowledged packet** — everything else is traffic
sent for nothing, and it buries the one packet that actually needs to get through.
`RetransmitIfUnacked` correctly resends only the head, and `MaxUnackedTracked = 256` bounds memory
use, but **nothing throttled the sender**.

Measured with the same probe (`Probe_IdleSession_HowLongBeforeItWedges`, 45 s idle, queue depth read
from `queued=` in the RETRANSMIT lines):

| | before | after |
|---|---|---|
| max. depth of the unacknowledged queue | **23 packets** (1→2→4→7→10→14→17→20→23) | **2** |
| recovery time | unchanged | unchanged |

**Fix:** `MacLayerTransport.LastSendStalled` = the head of the queue is unacknowledged and **has
already been retransmitted at least once** (i.e. the stream has been stuck for at least
`MinRetransmitIntervalMs`), surfaced through `IWinboxM2Channel.SendStalled` (`false` over TCP — there
the window is handled by the kernel). The terminal loop only gates **speculative pulls** on it; a
user's actual command still goes out, since dropping it would mean dropping the caller's own
request.

Two things this rests on:

* **one retransmission, not zero** — a packet in normal flight is acknowledged within a few ms, so
  ordinary traffic must never trip the signal (otherwise the throttling would leak into streaming of
  large outputs, which relies on pulling),
* **the throttling ends on its own** once the ACK arrives, and `lastPullMs` doesn't advance while
  it's active, so the very next pull goes out immediately. Otherwise a single lost datagram would
  silence the terminal for the rest of the session — a wedge introduced by the wedge fix.

The difference from `SendAbandoned` (§16): that one says "the session is gone" and sits at the very
end of the budget; this one says "the stream is stuck but may still recover" and governs what we're
allowed to **send**, not what we're allowed to report.
