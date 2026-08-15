# WinBox (TCP 8291 / UDP 20561) — session, mepty terminal and native M2

> **Principle:** WinBox is one session/crypto layer — EC-SRP5 handshake, AES-128-CBC chunked frames,
> M2 TLV messages — shared by two carriers (TCP port 8291, and the MAC-layer transport on UDP 20561
> that MAC-Telnet also uses) and two client engines built on top of it: the **mepty terminal**
> (`WinboxCli`/`WinboxCliMac`, streaming a RouterOS CLI like Telnet/MAC-Telnet) and the **structured
> M2** CRUD/monitor protocol (`WinboxNative`/`WinboxNativeMac`). Complements
> [findings-mactelnet.md](findings-mactelnet.md) (the shared MAC layer),
> [findings-mepty-byte-ack.md](findings-mepty-byte-ack.md) (the mepty byte-ACK counter),
> [winbox-native-m2-protocol.md](winbox-native-m2-protocol.md) (the native CRUD/monitor protocol) and
> [winbox-m2-multiplexing-design.md](winbox-m2-multiplexing-design.md) (the multiplexer built on §12
> below).

The C# implementation (`tik4net/Winbox/`, `tik4net/WinboxCli*/`, `tik4net/WinboxNative*/`) cites these
sections by number, so the section numbers below are stable — do not renumber a heading without
checking who cites it.

> Superseded diagnoses, incidents and pinned measurements for this area are in
> [`findings-winbox-history.md`](findings-winbox-history.md); this document describes current behaviour only.

---

## Architecture

```
tik4net/Winbox/                shared session/crypto layer + native M2 protocol constants and codec
├── M2Message.cs                   M2 TLV parse/build
├── WinboxM2Protocol.cs            protocol constants: SysKey/RecordKey/Command/Error/Mproxy/SysInfo/LegacyAuth/Mepty/Tlv
├── WinboxTcpTransport.cs          chunked AES framing over TCP
├── WinboxM2Session.cs             EC-SRP5 / legacy MD5 auth + frame I/O + generic Send/Receive/SendReceive
├── WinboxMacM2Session.cs          the same M2 session carried over MacLayerTransport
├── IWinboxM2Channel.cs            channel abstraction shared by the mepty and native transports
├── WinboxM2Multiplexer.cs         request/reply correlation for concurrent M2 operations (§12)
├── WinboxNativeM2Operations.cs    CRUD/monitor operations: getall/get-one/set/add/remove/move/singleton
├── WinboxM2Continuation.cs        getall/monitor pagination cursor (§12.7.1)
├── WinboxJgCatalog.cs / WinboxJgField.cs   the `.jg` catalog model
├── WinboxFieldResolver.cs         field name/type resolution between the API and `.jg` labels
├── WinboxRecordCodec.cs           record encode/decode
└── RouterLoginRetry.cs            bounded retry over a transient login refusal (§13)

tik4net/WinboxCli/       mepty terminal over TCP — WinboxCliClient, WinboxCliConnection
tik4net/WinboxCliMac/    mepty terminal over the MAC layer — WinboxCliMacConnection
tik4net/WinboxNative/    structured M2 CRUD/monitor over TCP — WinboxNativeConnection
tik4net/WinboxNativeMac/ structured M2 CRUD/monitor over the MAC layer — WinboxNativeMacConnection
tik4net/Crypto/          EcSrp5, WinboxStreamCrypto — shared with MAC-Telnet
```

- `WinboxM2Session`/`WinboxMacM2Session` carry no mepty/VT100 knowledge, so both native transports
  build on the same session type without dragging terminal concerns along.
- The crypto lives in `tik4net/Crypto/` and is shared with MAC-Telnet, so WinBox does not need a
  separate NuGet package — it ships in-core alongside Telnet/MAC-Telnet.
- All four client engines (`WinboxCli`, `WinboxCliMac`, `WinboxNative`, `WinboxNativeMac`) share
  `IWinboxM2Channel`; only the underlying channel implementation differs between the TCP and MAC-layer
  pairs.

---

## 1. A session id above 255 must round-trip as u32

Session ids are not always byte-sized. `M2Message.ParseSessionId` reads key `0xFE0001` as **either**
type `0x09` (u8) or type `0x08` (u32), and `M2Message.SessionIdField(int id)` encodes u8 for
`id ≤ 255` and u32 otherwise. A live mepty-open response carrying a u32 session id:

```
4D 32                                              "M2"
01 00 FF 88 02 00 00000000 00000008                SYS_TO  u32[] = [0, 8]
02 00 FF 88 01 00 0000004C                          SYS_FROM u32[] = [76]   (0x4C = mepty handler)
1C 00 FF A0 0001 0010 6D73...34                     0xFF001C str_array "msg-proxy-7.21.4"
01 00 FE 08 09 01 00 00                              SESSION_ID key=0xFE0001, TYPE=0x08 (u32) = 0x109 = 265
03 00 FF 09 02                                       0xFF0003 u8 = 2
06 00 FF 09 01                                       0xFF0006 u8 = 1
```

Session id 265 arrives as type `0x08` because it does not fit in a byte.

**Failure mode:** a u8-only implementation of `ParseSessionId`/`SessionIdField` truncates a session id
above 255 (`(byte)265 == 9`), silently addresses the wrong session, and the terminal never opens
(`InvalidOperationException: No SESSION_ID in M2 response`).

---

## 2. Login terminal-size hint: 80×25

`meptyLogin` (cmd `0x0A0065`) carries `U32User(3)=cols`, `U32User(4)=rows`; production sends **80×25**.
A large hint (e.g. 65535) makes RouterOS return an error response with no SESSION_ID — the same
symptom as §1, a different cause.

The actual terminal width is negotiated afterwards by the **VT100 cursor probe**
(`ESC[9999C ESC[6n`), answered with `Vt100State(65535, 25)`; the reply caps at ~9999 columns, which is
enough to keep long `print as-value` lines from wrapping (same principle as MAC-Telnet — see
[findings-mactelnet.md](findings-mactelnet.md) §2).

---

## 3. The encrypted channel requires `DataAvailable` gating before every read

WinBox runs over **TCP + AES-128-CBC frames** (unlike MAC-Telnet's UDP datagrams). Never decrypt with a
short timeout inside a retry loop: if the timeout expires **mid-frame**, the TCP stream is left
misaligned and every subsequent read fails with an `IOException`.

`WinboxCliClient` gates every read with `while (!_session.DataAvailable) Thread.Sleep(20)`, and only
then calls `_session.Receive(5000)` with a generous per-frame timeout — safe because it only bounds a
frame that is *already arriving*, never one that hasn't started. This works reliably for multi-packet
responses too.

That generous timeout is only safe as long as `DataAvailable` reports truthfully. Over the MAC layer it
does not — see §15.

---

## 4. A mepty session is opened once and reused for every command

`WinboxCliClient` opens a mepty session once, in `LoginAsync`, and every subsequent command reuses it
— the data counter (`Mepty.Key.Counter`, wire key 3 on `Data`) increments across commands within that
one session. A PTY is a terminal: it holds state, and nothing requires a fresh session per command.

---

## 5. The shared session layer carries no terminal state

`WinboxM2Session`/`WinboxMacM2Session` know nothing about mepty or VT100 — that lives entirely in
`WinboxCliClient` — so the native transports (`WinboxNative`, `WinboxNativeMac`) build on exactly the
same session and channel types without inheriting any terminal concerns. `WinboxMacM2Session`
implements `IWinboxM2Channel` over `MacLayerTransport` the same way `WinboxM2Session` implements it over
`WinboxTcpTransport`, which is what lets the mepty engine (`WinboxCliClient`) and the native engine be
shared unchanged across both carriers — only the channel implementation differs.

---

## 6. Legacy MD5 auth has no terminal support, and the fallback decision has a timeout floor

A mepty session only runs over the encrypted EC-SRP5 channel. `WinboxM2Session` keeps a legacy MD5 auth
fallback for pre-6.43 RouterOS (used by native operations), but `WinboxCliClient.OpenTerminalSession`
throws `NotSupportedException` when a session authenticated that way — the terminal path does not
support it.

Whether to fall back to legacy MD5 **at all** is decided exclusively by catching
`WinboxEcSrp5UnsupportedException` — no other exception triggers it — and that type is thrown only when
the router stays silent for the EC-SRP5 probe window, or answers with the wrong tag. The probe window is
`max(MinEcSrp5ProbeMs = 3000, connectTimeoutMs)`: silence is the *only* evidence a router gives for
"too old to speak EC-SRP5", so a window too short for a busy modern router to answer in time would
misroute it into the legacy path, where it then fails with a misleading "wrong username or password"
instead of the EC-SRP5 error that actually applies.

---

## 7. Test coverage — WinboxCli (TCP)

`WinboxCliProtocolTest` (login, list interfaces, set+verify a comment) exercises the mepty engine end
to end. `InterfaceTest` run over `winboxcli.runsettings` exercises it against the general connection
contract; skips there are CLI capability limits shared with Telnet/MAC-Telnet (async, `listen`,
monitor-traffic), not WinBox-specific gaps.

---

## 8. WinBox-over-MAC is the WinBox protocol tunneled over the MAC layer, not a MAC-Telnet variant

`WinboxCliMac`/`WinboxNativeMac` (UDP 20561, `client_type=0x0f90`) carry the **entire** WinBox protocol
— EC-SRP5 handshake and chunked AES frames — over the same MAC-layer reliable stream that MAC-Telnet
uses, rather than reusing MAC-Telnet's own auth or framing:

1. **Auth is the WinBox EC-SRP5 handshake**, not MAC-Telnet's control-packet auth
   (`CTRL_BEGINAUTH`/`PASSSALT`, which the router never answers on this port). After
   `BaseConnect(0x0f90)`, `WinboxMacM2Session.MacAuthEcSrp5` sends the same length-prefixed
   `[len][0x06][payload]` WinBox hello as DATA payload and runs the same EC-SRP5 math as
   `WinboxM2Session.EcSrp5Auth`. The challenge arrives as `[len=49][tag=0x06][32B xWB][1B parity][16B
   salt]`.
2. **Encrypted M2 uses the same chunk framing as TCP** — `[chunkLen 1B][tag][data]…` (`0xFF` =
   continuation) — carried inside DATA packets, not a bare `Encrypt(m2)` payload. `Send` chunk-wraps
   (`ChunkWrap`); `Receive` reassembles via an `_rxBuf` buffer (a chunk can cross a DATA packet
   boundary) before `WinboxStreamCrypto.Decrypt` runs.

Because the carrier is the only thing that differs, the mepty engine (`WinboxCliClient`) is shared
unchanged between `WinboxCli` and `WinboxCliMac` — only `IWinboxM2Channel`'s implementation changes.

---

## 9. MAC-layer findings specific to WinBox

- **ACK is `counter + payloadLen`**, via the shared `MacLayerTransport.AckData` — see
  [findings-mactelnet.md](findings-mactelnet.md) §1. Retransmission dedup runs off `_inCounter`.
- **`mac-winbox` is a separate router service from `mac-telnet`**: `/tool/mac-server/mac-winbox set
  allowed-interface-list=all` is required in addition to `/tool/mac-server`.
- **The u8/u32 SESSION_ID handling from §1 applies here too** — `M2Message` is shared code.
- MAC-layer WinBox is markedly slower than TCP: MNDP discovery, per-frame AES, and UDP polling with
  sleeps all add up. Set `RouterMac` to skip MNDP discovery in production.

---

## 10. Test coverage — WinboxCliMac (MAC)

`WinboxCliMacProtocolTest` mirrors §7's login/list/set+verify sequence over the MAC carrier, sharing the
mepty engine (§8) and asserting the same behaviour.

---

## 11. Protocol constants live in one file

Every M2 constant used by production code and tests — `Mepty.Login`/`Mepty.Data` command numbers, the
`Mepty.Key.Cols`/`Counter` collision, `Mproxy` command numbers, `SysKey`/`RecordKey`/`Command`/`Error`
codes — is centralized in [`WinboxM2Protocol.cs`](../tik4net/Winbox/WinboxM2Protocol.cs)
(`internal static`, grouped into `SysKey`/`RecordKey`/`Command`/`Error`/`Mproxy`/`SysInfo`/
`LegacyAuth`/`Mepty`/`Tlv`), which documents each constant's meaning and wire encoding, including the
`0xFE00xx` collisions between command numbers and error codes. `Mepty.Key.Cols` (key 3 on `Login`) and
`Mepty.Key.Counter` (key 3 on `Data`) are the same wire key with two different meanings depending on
which command carries it.

---

## 12. M2 request/response correlation

The M2 layer can run **lockstep** (one request, one reply, in order) because every fact below holds.
Verified live against RouterOS 7.21.4/7.23.2.

### 12.1 `0xFF0006` (RequestId) is echoed in the response — it is the correlation key

A single `/ip/address/print` over WinboxNative is three M2 exchanges (reference resolution: the address
names `ether1`, so a `getall` for interface and VRF follows):

| exchange | handler (`0xFF0001` To) | request `0xFF0006` | response `0xFF0006` | response `0xFF0003` |
|---|---|---|---|---|
| getall address | `[20,1]` | 2 | **2** | 2 |
| getall vrf | `[20,101]` | 3 | **3** | 2 |
| getall interface | `[20,0]` | 4 | **4** | 2 |

`0xFF0006` tracks the request exactly, which is what makes multiplexing (several requests in flight,
dispatched by id) sound. `WinboxM2Multiplexer` dispatches on it.

### 12.2 `0xFF0003` is not a correlation field

`0xFF0003` is undefined in `WinboxM2Protocol` and stays constant (`2`) across a session while the
request id keeps increasing — it resembles a session/reply-channel id, but its meaning is undetermined
(see §12.8) and it must not be used for dispatch: being constant, it cannot distinguish two concurrent
requests. In a trace of a *single* exchange it happens to equal the request id (both are `2`) — a
coincidence that only a multi-request trace exposes, which is why the table in §12.1 needs at least two
round trips before it means anything.

### 12.3 The frame crypto is stateless per frame

Despite the name, `WinboxStreamCrypto` is **not** a running stream cipher: `Encrypt` emits `[enc_len 2B
BE][IV 16B][ciphertext]` with a fresh random IV per frame, and `Decrypt` needs only that frame plus the
fixed keys from the handshake — no cross-frame state, no counter, no replay window. Frames can
therefore be decrypted independently and **completed out of order**, which is the property that makes
multiplexing cryptographically safe.

The remaining ordering constraint is framing, not crypto: `RecvChunked` assembles a sequence of chunks,
so reads must be serialized (one reader) and writes must not interleave (a write-lock over a chunk
sequence) — a reader loop plus a write lock, nothing more.

### 12.4 `0xFF0001`/`0xFF0002` (To/From) swap in the response

Request `To=[20,1] From=[0,8]` comes back as `To=[0,8] From=[20,1]`. The handler is a secondary signal
only — not unique, since two concurrent requests to the same handler are indistinguishable by it.
Dispatch stays exclusively on `0xFF0006`.

### 12.5 There are no unsolicited incoming frames today

Monitors are polling loops, not subscriptions: `MonitorLoop` does `StartMonitor` → repeated
`PollMonitor` → `CancelMonitor`, each step an ordinary request/response — which is exactly why lockstep
works. A multiplexed implementation still needs to discard an unmatched frame (a late response after a
timeout) as a robustness measure, not as the common path.

### 12.6 The request id is one byte

`NextReqIdField()` (`U8Sys(RequestId, (byte)(++_reqId))`) wraps at 256; a plain `int` counter is not
safe once senders can be concurrent (needs `Interlocked` and an 8-bit mask). Id `0` is never used (the
counter is pre-incremented), so it stays reserved as "no id".

### 12.7 `0xFE0019` is an object count, not "more frames follow"

Source of truth — the router's own webfig client (served at `/webfig/master-<hash>.js`; the hash
changes per build), the only two uses of the field in the whole file:

```js
// ObjectMap.prototype.getall  → onreply
if (rep.ufe0019 != null) me.objCount = rep.ufe0019;
// ObjectMap.prototype.listen  → notifyLstn
if (msg.ufe0019 != null) me.objCount = msg.ufe0019;
```

It is stored into `objCount` and never read for flow control — no loop condition, no termination check.
It is an informational total object count (hence `1` for a single-record exchange, and absent where the
handler doesn't send it). The completion rule stays "one request → exactly one response frame"; a
registration closes on the first frame with a matching `0xFF0006`.

#### 12.7.1 A continuation is a new request, not an unsolicited frame

There is no multi-frame paging: a continuation is always echoed back on a fresh request. webfig's
`post()` callback:

```js
else if ((rep.ufe0003 != null || rep.mfe0015) && !me.block) {
    if (rep.ufe0003 != null) req.ufe0003 = rep.ufe0003;
    if (rep.mfe0015 != null) req.mfe0015 = rep.mfe0015;
    post(req, onreply);            // ← new request, new id
}
```

A handler can page via **either or both** of two keys: `WinboxM2Protocol.RecordKey.Continuation`
(`ufe0003`, a u32) and `WinboxM2Protocol.RecordKey.ContinuationRaw` (`mfe0015`, a message-array).
`0xFE0015` has no other occurrence anywhere in `master*.js` — nothing names it, nothing reads inside it,
nothing constructs one — so to webfig it is opaque bytes that come back on the next request, and webfig
itself just echoes whichever keys the reply carried.

`WinboxM2Continuation` (`tik4net/Winbox/WinboxM2Continuation.cs`) does the same: it holds the **raw TLV
bytes** of whichever continuation key(s) a reply carried and appends them unchanged to the next
request, instead of decoding and re-encoding a value whose shape is undocumented — this also removes
the overflow hazard a decoded-u32 round trip would carry for a cursor at or above `0x80000000`.
`GetAllAsync` (`tik4net/Winbox/WinboxNativeM2Operations.cs`) calls `WinboxM2Continuation.From` on every
round trip and stops paginating when it returns `null` (no cursor of either kind means "last page") or
when the handler answers `Error.ObjectNonexistent`. So the registration model doesn't change for
multiplexing: each page is a separate registration with its own request id.

A `.jg` catalog only declares a window's **display fields**, so a pagination token's absence from any
catalog version says nothing about whether a handler uses it — `0xFE0003` (known to be used) and
`0xFE0015` are equally invisible there, since neither is ever shown in a window. Absence-from-catalog is
not evidence either way; only a live trace of a paging handler settles the question for a given key.

### 12.8 Parallel connections from one machine are not marked in the M2 layer

No M2 field identifies which connection sent a request — that distinction lives below the M2 layer:

| transport | what separates parallel sessions |
|---|---|
| WinBox TCP / TCP-MAC | the TCP socket (4-tuple); each session has its own connection |
| WinBox over the MAC layer | a random `_sessionKey` in the packet header (`MacLayerTransport`) |

`0xFF0003` (§12.2) never appears in the webfig JS at all, so its meaning stays undetermined — and it
would not help distinguish concurrent requests regardless, being constant within a session.

---

## 13. The router occasionally refuses a valid WinBox login

Roughly **0.5–1% of WinBox logins** on RouterOS 7.23.2 are refused by the router mid-handshake, even
though the credentials are correct: the router sends **33 bytes of ASCII** where the 32-byte
confirmation digest belongs —

```
69 6E 76 61 6C 69 64 20 75 73 65 72 20 6E 61 6D 65 20 6F 72 20 70 61 73 73 77 6F 72 64 20 28 36 29
"invalid user name or password (6)"
```

— logs `login failure for user admin … via winbox`, and accepts the identical credentials on a retry
milliseconds later. This is a transient refusal by the router's own EC-SRP5 implementation, not a
client bug or a real credential problem — see [Settled questions](#settled-questions--do-not-re-investigate)
below for the elimination evidence.

### 13.1 Mitigation: bounded retry, since the refusal's content cannot be told apart from a real one

A genuinely wrong password produces the **same** message — the router's normal path for a refusal — so
only persistence (a transient refusal disappears, a real one doesn't) can tell them apart.
`RouterLoginRetry` (`tik4net/Winbox/RouterLoginRetry.cs`) retries **exclusively** on
`TikConnectionLoginRefusedException`: 3 attempts, 100 ms apart, each attempt opening a **new channel**
(a refused handshake leaves the old one unusable). The cost is deliberate: a truly wrong password now
fails a couple hundred ms later than before, and leaves 3 `login failure` lines in the router's log
instead of one.

### 13.2 It is an EC-SRP5-handshake refusal, not a WinBox-specific one

MAC-Telnet carries the same EC-SRP5 exchange over different framing and is refused the same way — only
reported differently, and later: WinBox refuses **inside** the handshake with the text above;
MAC-Telnet completes the handshake, sends `CTRL_END_AUTH`, and only up to a second later writes `Login
failed, incorrect username or password` to the terminal and tears the session down with `PKT_END`. Both
log the same router-side line (`login failure for user … via winbox` / `via mac-telnet`), and both clear
on retry — which is why the retry is named `RouterLoginRetry` and keyed on the public
`TikConnectionLoginRefusedException` rather than a WinBox-only type. See
[findings-mactelnet.md](findings-mactelnet.md) §8 for the MAC-Telnet side.

---

## 14. Writing a singleton uses `SetSingleton` (`0xFE000E`), not `Set`

A singleton window (`.jg` `type:'item'`, e.g. `/system/identity`, `/ip/dns`, `/ip/settings`, `/snmp`,
`/system/note` — 35 `IsSingleton` entities in total) has no `.id`, so it cannot go through the generic
table path (`Command.Set` = `0xFE0003` + `RecordKey.Id`). `WinboxNativeConnection.WriteFieldsAsync`
detects an `IsSingleton` window (`IsSingletonWindow`) and calls `SetSingletonAsync`
(`Command.SetSingleton` = `0xFE000E`) instead, matching webfig's `ObjectHolder.setObject`:

```js
req.Uff0001 = this.attrs.path;
req.uff0007 = this.attrs.setcmd || 0xfe000e;
if ("ufe0001" in obj) req.ufe0001 = obj.ufe0001;   // .id only when the object itself carries it
```

`.id` is sent **optionally** — the only known case is the hidden "Change Password" window
(`setcmd:3`), which targets a user record. `WriteFieldsAsync` therefore only sends `.id` in its literal
`*HEX` form; resolving it by name would need a `getall`, which a singleton handler has nothing to
answer.

### 14.1 `/system/identity` returns its name under the `.jg` label `identity`, not `name`

Handler `[24,1]`:

```js
{title:'Identity',type:'item',path:[ 24,1 ],autostart:1,
 c:[{name:'Identity',type:'string',id:'sc'},{name:'Version',type:'string',id:'sd',nonpublic:1}]}
```

A read returns `{"version":"…","identity":"…"}`, while the API returns `{"name":"…"}`.
`WinboxFieldResolver` ships a field alias (`name ↔ identity`) for this handler.

The `version` field is not discarded: `nonpublic:1` does not mean "not an API field" — several fields
the API routinely returns carry it too (`MAC Address`, `Interface`, `L2 MTU`). Native records are
generally a superset of API fields, and the mapper ignores the extras it doesn't need.

### 14.2 `multilinestring` is a scalar string, not a list

`types.multilinestring = inherit(types.string)` in webfig — it differs from `types.string` only in
**view** (a textarea instead of a one-line input). Of every `multi*`-prefixed `.jg` UI type, this is the
only scalar one; the rest (`multinumber`, `multinumberrange`, `multiipaddr`, `multistring`, …) inherit
`types.multi` and are genuine lists. `WinboxFieldResolver.IsUnsupportedListType` treats any
`multi*`-prefixed UI type as an unencodable list except when `IsScalarDespiteMultiPrefix` recognizes
`multilinestring` — without that carve-out, a field like `/system/note`'s `note` cannot be written at
all.

### 14.3 A list field's element type is declared under `c`, not `values`

A dropdown reference (`enm`) normally declares its target table under `values`, but a **list** field
declares its element type as an unnamed child instead of carrying `values` itself:

```js
{name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:'dynamic',path:[ 3,3 ]}}]}
```

`WinboxJgCatalog.ExtractRefHandler` reads `values` first and falls back to the field's `c` (children)
when `values` is absent, so a list like `/log`'s `topics` decodes as `"script,error"` instead of the raw
handle `"[9,3]"`.

---

## 15. `DataAvailable` over the MAC layer means "a datagram arrived", not "a frame is ready"

`DataAvailable` gating (§3) is safe over TCP because `NetworkStream.DataAvailable` means "bytes of a
frame are here". Over UDP, `_udp.Available > 0` only means "some datagram arrived" — most traffic on
that socket is ACKs, PINGs, and retransmissions, none of which is the frame a caller is waiting for. A
caller that treats a false-positive `DataAvailable` as license to block on `Receive(5000)` pays the
**entire** frame timeout once per false positive:

| span | WinboxCli (TCP) | WinboxCliMac before the fix | WinboxCliMac after the fix |
|---|---|---|---|
| send → first byte | 25 ms | 25 ms | 25 ms |
| prompt → return | 166 ms | 5012 ms | 164 ms |
| total per command | 216 ms | 5039 ms | 193 ms |

The first byte arrives just as fast as over TCP; the entire loss sits right after the prompt and matches
the 5000 ms frame timeout exactly — a duplicate ACK or retransmission satisfies `DataAvailable`, the
read then blocks for the full timeout waiting for a frame that was never coming on that poll.

`MacLayerTransport.RecvAvailable(handler)` is the polling counterpart to `RecvUntil`: it drains
everything already sitting on the socket and returns immediately, sharing `ReceiveOne`'s body so
ACK/PING/duplicate handling is identical on both paths. `WinboxMacM2Session.DataAvailable` is built on
it and answers "is a complete M2 frame ready", not "did a datagram arrive"; a finished frame is held in
`_pendingFrame` for the next `RecvFrame` call to hand out. The getter therefore does perform I/O,
deliberately — it is a poll operation on the channel, used only by the single-threaded terminal loop in
`WinboxCliClient`. The native transport's reader loop services the socket continuously instead, which is
why a channel that streams unsolicited frames (mepty) must never be handed a
`WinboxM2Multiplexer` — `IWinboxM2Channel.SupportsReaderLoop` guards exactly that.

A property a caller treats as permission to block must actually be true. MAC-Telnet never had the same
defect, because it drives a background pump over a blocking socket instead of `DataAvailable` gating.

---

## 16. A stuck MAC-layer session shows total silence, not a rejection

A MAC-layer command can time out (`nothing was received within 30000 ms`) with the router's MAC layer
simply not acknowledging anything at all — no ACK, no PING, no retransmission of a still-unacknowledged
packet — even though the router's own accounting has no record of the session ending (no logout, no
error in `/log`). This is distinct from a slow command: the retransmit budget on the head-of-queue
packet is spent (`RETRANSMIT #n end=… highestAck=…`, where `highestAck` sits exactly at that command's
starting offset).

`IWinboxM2Channel.SendAbandoned` surfaces `MacLayerTransport.LastSendAbandoned` — "the head of the
unacknowledged queue was never taken by the router" — up into the CLI engine. Over TCP it is always
`false` (TCP has nothing to leave unacknowledged; a dead connection there shows up as FIN/RST).
`WinboxCliClient.ReadCommandResponseSync` throws `TikConnectionSessionClosedException` instead of
riding out the full 30 s when nothing has arrived **and** the router never took the bytes; it only does
this while the console has produced **no** output at all (`sb.Length == 0`) — once any output exists the
command provably arrived and could have run, so claiming otherwise would be a lie the caller has no way
to verify. `WinboxCliMacConnection` hangs a reopen plus `RouterLoginRetry` login off this exception,
mirroring `MacTelnetConnection`, except it does not reconnect inside Safe Mode (that is exactly what
Safe Mode protects against) or once any line has already been delivered to the caller (a restart would
duplicate it).

The value of this signal is entirely in telling "dead session" apart from "slow command" — it hinges on
the missing acknowledgment rather than on silence, since silence alone would misfire on any legitimately
long-running command. See §17 and §18 for what causes a MAC-layer session to be abandoned like this in
practice.

---

## 17. A Safe Mode rollback on a sibling MAC session kills an idle WinBox-over-MAC session

Holding a `WinboxCliMac` connection while, on a **different** connection, a Safe Mode session ends in a
rollback kills the held session — reproduced 5 times out of 5
(`Probe_SafeModeRollbackOnASibling_KillsTheHeldSession`). The rollback lands roughly 2 s after the
sibling closes, because RouterOS keeps the Safe Mode owner alive until its connection-tracking timeout
expires rather than until the socket closes, and §16 then recovers the held session via reopen + retry.

`MacTelnet` and `WinboxCli` (TCP) do **not** exhibit this: both service their socket continuously
between commands (MAC-Telnet via a receive pump since it was first written; WinBox-over-MAC only since
§18 added `StartIdleServicing`). Before that fix, `WinboxCliMac` was the only one of the three left
unattended between commands, which is why it alone lost the session — the difference was never in what
the router does, only in what the client does between commands.

**Consequence for library consumers:** holding a `WinboxCliMac` connection while running Safe Mode to
completion (including a rollback) on another connection loses that first connection; recovery costs
roughly the time for a reopen + login. The test suite's own `SafeModeTest.OnCleanup` disposes its shared
connection rather than relying on the transport's own recovery, because that test already polls until
the rollback lands — by which time the shared connection other tests reuse has already been affected.

See [Settled questions](#settled-questions--do-not-re-investigate) below for the mechanisms ruled out
before this cause was found.

---

## 18. A MAC-layer session must be serviced between commands, not only while a command is in flight

The RouterOS terminal is not purely request/reply: the router can write into it on its own (a log event
landing on the console, a Safe Mode rollback notice), and at the MAC layer every such write must be
acknowledged or the router keeps retransmitting it and eventually stops servicing the session at all.
`WinboxCliClient` only touched the channel while a command was running, so an idle session (no command
in flight for tens of seconds) left the router's unsolicited writes unacknowledged.

`IWinboxM2Channel.StartIdleServicing()`, called once after login, fixes this: over TCP it is a no-op
(the kernel acknowledges the byte stream); over the MAC layer it starts a background thread that, every
200 ms, takes the receive lock via a non-blocking `TryEnter` and drains whatever is on the socket. A
read holds the same lock for the entire duration of assembling a frame, so the pump can never grab a
packet mid-frame. The pump only acknowledges and answers `PING` — it never initiates anything on its
own, which matters: client-originated idle traffic would itself trigger the same asynchronous
router-side effects (Safe Mode rollback, log delivery) that the pump exists to service, and could
shorten the session's life instead of extending it.

`WinboxNativeMac` never had this problem: its reader loop (`WinboxM2Multiplexer`, §12) services the
socket continuously as a side effect of always having a reader running.

Measured effect on the Safe Mode rollback case from §17:

| | before `StartIdleServicing` | after |
|---|---|---|
| Safe Mode rollback on a sibling (`WinboxCliMac`) | wedge, every time, ~4.3 s recovery | ~0.34 s, no wedge |

> **Measurement trap:** a trace timestamp records when a session's socket was *read*, not when a packet
> arrived. An unserviced session shows **no gap** in its own trace, because its entire backlog dumps out
> at once on the next read — the same `counter=…` value can arrive ten times in a row at that moment,
> which is the router having retransmitted one unacknowledged packet the whole time. Reading "no gap in
> the trace" as "the router kept talking to us" gets this backwards.

A session left idle for the ~30 s RouterOS uses as its own idle-logout threshold for a MAC console still
loses the session — that is the router's own contract (confirmed symmetrically: `MacTelnet`, which has
had the pump from the start, wedges at 30 s the same way) — and is handled by the reopen + retry from
§16, not by servicing.

---

## 19. The MAC layer throttles only speculative pulls, never the caller's own command

ACK at the MAC layer is cumulative, so once the head of the unacknowledged queue stops being
acknowledged, the router cannot process anything sent behind it — every further packet is wasted
traffic that also buries the one packet that actually needs to get through. Left unthrottled, the
unacknowledged queue grows without bound while the head sits unacknowledged (observed as high as 23
packets deep against an idle session); `RetransmitIfUnacked` correctly resends only the head and
`MaxUnackedTracked` bounds the queue, but nothing throttled new sends on their own.

`MacLayerTransport.LastSendStalled` (surfaced as `IWinboxM2Channel.SendStalled`, always `false` over
TCP where the kernel owns the window) is true once the head of the queue has been retransmitted **at
least once** — i.e. stuck for at least `MinRetransmitIntervalMs` — and the terminal loop gates only its
**speculative** idle pulls on it. A user's actual command still goes out regardless, since dropping it
would mean silently dropping the caller's own request. With the gate in place the unacknowledged queue
stays at depth 2 instead of growing unbounded, without changing recovery time.

Two properties this rests on:

- **one retransmission, not zero** — ordinary in-flight traffic is acknowledged within a few
  milliseconds, so normal traffic must never trip the signal (or throttling would leak into streaming of
  large outputs, which relies on pulling);
- **the throttle releases itself** the moment the ACK arrives, and the pull schedule doesn't advance
  while it's active, so the very next pull goes out immediately — otherwise one lost datagram would
  silence the terminal for the rest of the session.

This is a softer, earlier signal than `SendAbandoned` (§16): that one means "the session is gone" and
sits at the very end of the budget; this one means "the stream is stuck but may still recover" and
governs what the client is allowed to **send**, not what it is allowed to report.

---

## Settled questions — do not re-investigate

- **The transient login refusal (§13) is not our EC-SRP5 arithmetic.** 4000 client↔server round-trips
  offline show zero divergences (`EcSrp5RoundTripTests`), and replaying the identical client key
  (`WinboxHandshakeLoopProbeTest.Probe_WinboxHandshake_SameKeyRetry`) after a refusal is accepted 9
  times out of 9 — the exact bytes the router just refused get accepted moments later. The only thing
  that changes between attempts is the router's own ephemeral key.
- **It is not a leading-zero byte in the client's ephemeral key.** Forcing that case deliberately
  succeeds 4 times out of 5 — the first observed sample was coincidence, not a pattern.
- **It is not rate-limiting or attempt frequency.** Login attempts at 0 ms, 250 ms and 1000 ms spacing
  show no trend in refusal rate.
- **It is not frame desync.** The refusal frame is a well-formed chunk with tag `0x06`; its 33-byte
  length matches the refusal text exactly, with nothing overflowed or missing.
- **It is not specific to WinBox's own transport.** The API transport shows zero refusals across 400
  fresh logins (one further `via api` log line could not be attributed to any test client and is
  excluded rather than counted as a fifth refusal) — the phenomenon follows the EC-SRP5 handshake
  itself (§13.2), not the WinBox framing around it.
- **A collision of the MAC-layer session key or local UDP port does not cause the §16 wedge.** Across
  27 traced sessions, no key or port ever repeated (the key is drawn randomly on every open).
- **The client's own retransmit flood is an effect of the §16 wedge, not its cause.** Packets do pile up
  behind an unacknowledged head once a command goes unanswered, but only *after* that command already
  went unanswered.
- **Closing a sibling session does not, by itself, cause the §16 wedge.**
  (`Probe_SiblingSessionTeardown_DoesNotKillTheHeldSession`, 20 cycles across WinBox-MAC, MAC-Telnet, and
  an API sibling: zero wedges.) A **Safe Mode rollback** on a sibling does — see §17 — which is a
  narrower claim than "any sibling teardown."
- **Traffic volume or a boundary in the byte stream does not cause the §16 wedge.** A single session ran
  400 commands and 101,099 outbound bytes without a hiccup, past two of the offsets where the wedge
  otherwise occurs.
- **The router's own log echoing into the terminal does not explain the §16 wedge.** Across a full
  traced run there is exactly one such echo, which could account for at most one of several observed
  wedges.
- **A session-count limit or eviction on the router does not explain the §16 wedge.** It occurs after as
  few as 2 and as many as 22 sessions opened in the same run, with never more than 1–2 alive
  concurrently.
- **An idle MAC console being logged out by the router after ~30 s is real (§18), but it is not what
  caused the two wedges investigated in §16/§17.** Both of those followed an idle gap under 60 s on a
  session nobody was reading; once idle servicing (§18) covers that gap, only the genuine ~30 s
  router-side idle-logout remains, and that is handled by reconnecting, not by servicing.
- **A previous session holding a local port, or two MAC sessions contending for one, cannot happen.**
  Every session binds an **ephemeral** local port (`Bind(new IPEndPoint(nic.LocalIp, 0))` with
  `ReuseAddress`); 20561 is the *router's* port, never the client's.
