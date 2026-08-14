# WinBox native M2 — protocol reference

Durable protocol findings for the `WinboxNative` transport, extracted from the reverse-engineering
work log. Everything here was verified against a live RouterOS 7.21.4 router and/or read out of the
webfig client, and the C# implementation (`tik4net/Winbox/`, `tik4net/WinboxNative/`) cites these
sections by number — the original numbering is preserved for that reason.

Companion documents: [`jg-catalog-format.md`](jg-catalog-format.md) for the `.jg` catalog encoding,
[`findings-winbox.md`](findings-winbox.md) for the transport/session layer, and
[`winbox-m2-multiplexing-design.md`](winbox-m2-multiplexing-design.md) for the channel model.

---

## 0. Key discovery (2026-06-07): `.jg` = JS object literal

`.jg` files are **NOT** binary or gzip — they are **plain-text JavaScript object literals**
(pseudo-JSON), 100% printable ASCII. Example (`advtool.jg`):

```js
[{name:'IP Scan',title:'IP Scan',group:'Tools',c:[{title:'IP Scan',type:'query',
  path:[ 101,1 ],autorefresh:1000,cancelcmd:2,request:[...],startcmd:1,c:[
  {name:'Address',type:'ipaddr',id:'u1',width:90},
  {name:'MAC Address',type:'macaddr',id:'r2',opt:1,width:120}, ... ]}]}]
```

### `.jg` → M2 protocol mapping (essential)

| `.jg` construct | M2 meaning |
|---|---|
| `path:[ 20,0 ]` | **SYS_TO** handler array (`0xFF0001`). `[20,0]`=/interface, `[13,4]`=sysinfo, `[2,2]`=mproxy, `[101,1]`=ip-scan, `[51,1]`=netwatch |
| `cmd:N`, `startcmd:N`, `pollcmd:N`, `cancelcmd:N` | **SYS_CMD** (`0xFF0007`) command number. Small numbers (1,2,3) = per-handler subcmd; large ones (`16646160`=`0xFE0010`) = system base `0xFE0000` |
| `id:'u1'` | field **key=0x1**, **type=u32** (`u`) |
| `id:'s10006'` | field **key=0x10006**, **type=string** (`s`) |
| `id:'b3'` | field **key=0x3**, **type=bool** (`b`) |
| `id:'r2'` | field **key=0x2**, **type=raw**/mac (`r`) |
| `id:'ma'` | field **key=0xa**, **type=addr** (`m`) |
| `id:'S11'` | field **key=0x11**, **type=string-array** (uppercase `S`) |
| `id:'U3d'` | field **key=0x3d**, **type=u32-array** (uppercase `U`) |
| `type:'map'/'query'/'item'/'doit'/'action'` | window kind → implies default commands (list/get/set/add/remove) |

### Prefix → TLV type (hypothesis, to be verified in Phase 2)

| prefix | meaning | TLV type (from memory ref_mikrotik_api) |
|---|---|---|
| `u` | u32 | 0x08 (or 0x09 u8 depending on size) |
| `s` | string | 0x21 |
| `b` | bool | 0x00/0x01 (bool sys) |
| `r` | raw (mac 6B) | 0x31 |
| `m` | addr (ip/ip6) | raw |
| `a` | ip6addr | raw 16B |
| `U` (uppercase) | u32 array | 0x88 |
| `S` (uppercase) | string array | 0xA0 |
| `x`/`q` | u64? | to be verified |

> Uppercase letter = array variant of the same type. The hex suffix reads as a hexadecimal
> number (`fe0010` → 0xFE0010), covering both the system namespace (`0xFExxxx`) and the user namespace.

---

## 6. Phase 3 empirical results (live router 7.21.4, 2026-06-07)

### Authoritative field constants (tenable/routeros `common/winbox_message.cpp`)

| name | key | | error code | meaning |
|---|---|---|---|---|
| `k_sys_to` | `0xFF0001` | | `0xFE0002` | k_not_implemented |
| `k_from` | `0xFF0002` | | `0xFE0004` | k_obj_nonexistant |
| `k_reply_expected` | `0xFF0005` | | `0xFE0009` | k_not_permitted |
| `k_request_id` | `0xFF0006` | | `0xFE000D` | k_timeout |
| `k_command` | `0xFF0007` | | `0xFE0012` | k_busy |
| `k_error_code` | `0xFF0008` | | | |
| `k_error_string` | `0xFF0009` | | | |
| `k_session_id` = **`.id`** | `0xFE0001` | | | |

Builtin commands `0xFE0000–0xFE0016` are **system-level** (`0xFE0001`=cmdGetPolicies),
NOT object CRUD → CRUD is per-handler small numbers.

### Probe results on handler `[20,0]` (/interface)

- **`cmd=3` (no id) = getall-ids** ✅ → returns user key `0x000001` = u32[] of all .ids
  (live: 47 ids: `[1,3,10,11,...,77]`). **Native read works.**
- `cmd=3 + .id` → ignores the id, always returns the full id-list.
- `cmd=2 + .id` → empty ACK with no error (probably **set**, a no-op with no fields).
- `cmd=2` without id → `0xFE0004` obj_nonexistant (set requires an id).
- `cmd=1,4,5,6,7,8 (with and without .id)` → `0xFE0009` k_not_permitted (the commands exist, but are gated/missing an argument).
- Sweep `0x00–0x28` with id as both `.id` and `u1`: **none returns the full record** (Name `0x10006`).

### Open question: how to get the full record (fields)?

The "getall-ids → get-one-by-id" model doesn't work this way. Hypotheses (for Phase 3b):
1. **Streaming**: `cmd=3` with `reply_expected=false` → the handler streams rows (each = 1 frame).
2. **Column subscription**: the request carries a list of requested field-keys (related to `.jg` `refreshfilter`).
3. **get-one** is a command outside 0x00–0x28, or the id belongs to a different field.
References to investigate: tenable/routeros `bytheway/src/main.cpp`, the "Make It Rain" article,
subixonfire/winbox-terminal-protocol (auth+session infra).


---

## 10. ✅✅ BREAKTHROUGH (2026-06-09): native CRUD solved from webfig master.js

**Source of truth = `_notes/WinboxMessage/webfig/master-d53cd8ec58cb.js`** (the only non-crypto
webfig script, the M2 protocol implemented in JS over HTTP `/jsproxy`). Functions `msg2buffer`/`buffer2msg`
(serialization), `ObjectMap.getall/fetch/setObject` (CRUD), `subscribe` (push model).
Black-box probing was a dead end — webfig handed over the complete command catalog directly.

### Three mistakes from earlier attempts (ALL fixed)
1. **Wrong command.** getall = **`0xfe0004`** (webfig default `getallcmd`), NOT a small number.
   `cmd=3` on `[20,0]` returns the TYPE registry, not instances. Earlier agents had labeled
   `0xfe0000–0xfe0016` as "system-level, not CRUD" — that was WRONG. Those numbers ARE CRUD
   (generic defaults, which is why `.jg` doesn't list them for `type:'map'`).
2. **Missing flag field.** getall requires **`ufe000c`** (key `0xFE000C`, u32) =
   `0x10000005` (`| refetchonopen | refreshfilter`). Without it the handler returns no rows.
3. **Records are a MESSAGE-ARRAY** under key **`0xFE0002`** (webfig `Mfe0002`, wire type
   **0xA8**). The old parser had no case for 0xA8 and `SkipTypeBytes` defaulted to 0 → it stopped →
   rows NEVER showed up. Fixed: message (0x28/9/A) + message-array (0xA8/9/A)
   in `M2Message.ParseAllFields` + `ParseRecords` + `SkipTypeBytes`.

### Complete command catalog (uff0007, from webfig)
| cmd | constant | meaning | request fields | reply |
|---|---|---|---|---|
| `0xfe0004` | getallcmd | **list all** | `ufe000c`=flags, `ufe0018`=maxobjs, paging `ufe0003` **and/or** `mfe0015` (echoed back as received) | `Mfe0002` records, `ufe0019` count, `ufe0003` and/or `mfe0015` cont. token |
| `0xfe0002` | — | **get one** | `ufe0001`=.id | record (in `Mfe0002` or top-level) |
| `0xfe0003` | setcmd(map) | **set/change** | `ufe0001`=.id + changed fields | status |
| `0xfe0005` | — | **add** | fields (no .id) | `ufe0001`=new .id |
| `0xfe0006` | — | **remove** | `ufe0001`=.id | status |
| `0xfe0007` | — | **move** (ordered) | `ufe0001`=.id, `ufe0005`=next-id | |
| `0xfe000d` | getcmd | get singleton | `ufe000c`=flags | record |
| `0xfe000e` | setcmd(holder) | set singleton | fields | |
| `0xfe0008` | — | setup/wizard step | `mfe000f`=obj, `ufe000e`=page | |
| `0xfe0012` | — | **subscribe** (push) | path in `Uff0001` | async push key `Uff0002`=path |
| `0xfe0013` | — | unsubscribe | | |

### Key system fields (writeId: 3B key LE + 1B type in the top byte)
- `Uff0001` (u32[]) = **SYS_TO** = path `[20,0]`. **Uppercase U = array!**
- `uff0007` (u32) = **SYS_CMD**.  `Sff001c` = trace (webfig, can be omitted).
- `Uff0002` (u32[]) = SYS_FROM (in a push notification = which subscription).
- `uff0008` = error code, `sff0009` = error string.
- `ufe0001` = **.id** (record handle).  `ufe000c` = getall/get **flags**.
- `ufe0018` = maxobjs.  `ufe0003` = getall continuation token.  `ufe0019` = count.
- `Mfe0002` = **records** (message-array).  `ufe0005` = next-id (ordered).
- `ufe0013` = removed flag.  `mfe001d` = default config (`setDefaultConf` cmd `0xfe0004`+`ufe000c=0x20000000`).

### Field-key convention (from `.jg` id + webfig)
- **comment = `sfe0009`** = string key **`0xFE0009`** (webfig `types.comment.get/put`). Confirmed live.
- Name = `s10006` (0x10006).  .id = `ufe0001` (0xFE0001).  type = `u10001`.
- Type-byte = `(ftype<<3)|sizeFlags`; ftype 5=message, 21=message[]; flags short=0x01 long=0x02;
  length/count: short=1B, normal=2B, long=4B (webfig `readLen`).

### Wire-format detail (from `msg2buffer`)
- An M2 message = `'M2'` + fields. A sub-message (message/message-array element) ALSO carries an `'M2'` prefix.
- Field header (4B): `[key_lo][key_mid][key_hi][typeByte]` — 24-bit LE key, type+flags in the top byte.
- message-array (0xA8): `[2B count][ (2B elemLen + M2-submsg) × count ]`.

### Edit = "modify everything, save" (confirmed)
webfig's `setObject` sends the ENTIRE object (`update(req,obj._exportObj||obj)`) + cmd `0xfe0003` +
`ufe0001`=.id. In practice, sending .id plus only the changed fields is enough (verified live: setting only `sfe0009`).

### Live verification (router 7.21.4, `WinboxNativeGetallTest.cs`, 3/3 ✅)
- `Native_GetAllInterfaces` — `[20,0]` getall → names **match the API** (`CollectionAssert`).
- `Native_GetAllIpAddresses` — `[20,1]` getall → 1 record (generic across tables).
- `Native_SetAndRestoreEther1Comment` — get-one ether1 (.id=2, `sfe0009`="My comment" = API),
  set `sfe0009`="native-m2-ok" → API confirms the change → restore → API confirms. **status=0.**

### Next step (W5 — production resolver)
Catalog-driven `Resolve(path, op) → (handler, cmd, fields)` from `.jg` (W4 fetch done) +
`NativeGetAll/GetOne/SetRecord` in `WinboxM2Client`. add/remove/move per the table above.
Promote into `tik4net/Winbox/` (`WinboxNativeM2Session`) — the infrastructure (auth, AES, M2) already exists.

---

## 20. CHAPTER: Full streaming monitor for WinboxNative (started 2026-06-13)

### Goal
Native support for **continuous/streamed monitoring** — `.jg` windows of `type:'query'` with
`autorefresh` + `startcmd`/`pollcmd`/`cancelcmd`, where instead of a single getall the router
**repeatedly pushes updated rows** (torch, ip-scan, netwatch, ethernet monitor with a live rate,
traffic-monitor…). Today the native transport only supports **once-shot** (getall + filter, §18) —
live values (rate, auto-negotiation) are missing from a plain getall.

### What's already in place (building blocks)
- `WinboxM2Protocol.Command`: `Subscribe=0xFE0012`, `Unsubscribe=0xFE0013` + the monitor triple
  `0xFE000F/10/11` (startcmd/pollcmd/cancelcmd — see the §17 note). The constants already exist.
- The `.jg` parser already reads `type:'query'`, harvesting the window path into `_derivedPaths`
  (query is in `WindowTypes`). Missing: harvesting the `startcmd/pollcmd/cancelcmd` numbers and the
  `request:[…]` fields.
- The M2 session can send a request and read a frame; the subscribe push model is described in §10
  (cmd `0xFE0012`, async push under key `Uff0002`=path).

### Open questions (to investigate from webfig master.js + .jg)
1. **Two models**: (a) CRUD `subscribe 0xFE0012` (config-table push, autorefresh windows like firewall),
   (b) per-handler `startcmd/pollcmd/cancelcmd` (tool windows like torch/ip-scan from `advtool.jg`).
   Determine which window uses which — `.jg` carries this (`startcmd:N` present ⇒ model b).
2. How does tik4net's API expose this? There is `ExecuteAsync`(callback) + `LoadAsync<T>` in the O/R mapper
   (binary API streaming, see `TikCommandTest.ExecuteAsync_OnDoneCallback_Called`). The native transport
   must fulfil `TikConnectionCapability.Listen` / `Streaming`, or remain unsupported.
3. Read thread: the M2 channel is currently request/reply. A push model means async frames arriving
   unrequested → needs a reader loop + dispatch on request-id/subscription-id.

### ✅ RE DONE (2026-06-13) — KEY DISCOVERY: streaming = CLIENT POLLING, not server push

Ground-truth from webfig `master.js` (`ObjectQuery`, `ObjectAction`, `ObjectMap.getall`) + `.jg` query/action
windows. **The earlier §9 hypothesis ("the router pushes rows asynchronously, autorefresh:1000") was WRONG.**
Reality: webfig **repeatedly requests rows itself** (a timer every `autorefresh` ms) over normal
request/reply on the same channel. No async server-push reader is needed — the existing synchronous
M2 session is sufficient; the monitor just re-posts requests from a worker thread.

#### `.jg` window → cmd triple (all SYS_CMD `uff0007` on `Uff0001`=path)
| `.jg` field | meaning | example |
|---|---|---|
| `startcmd:N` | starts the monitor → reply carries **`ufe0001`=id** (session handle) | Torch [45,5] `startcmd:1`, IP Scan [101,1] `startcmd:1` |
| `getallcmd:N` (query) / `pollcmd:N` (action) | one poll pass; default getall = `0xfe0004` | Monitor Slaves `getallcmd:0xFE0010`, Bandwidth Test `pollcmd:1` |
| `cancelcmd:N` | stops the monitor (`ufe0001`=id) | Torch `cancelcmd:2`, system-level `0xFE0011`=16646161 |
| `autorefresh:ms` | re-poll interval (typically 1000) | |
| `request:[…]` | input parameters (Interface enm, Address Range network, …) | |
| `c:[…]` | result columns (rows decode like a normal getall — `Mfe0002`) | |

The system-level monitor triple (windows without their own numbers): `0xFE000F`=start, `0xFE0010`=poll/getall,
`0xFE0011`=cancel. Sentinel `0xFFFFFFFF` = "no cmd" (`startcmd==0xffffffff && autorefresh==null`
⇒ the window is actually just a one-shot getall, not a stream).

#### Model A — `ObjectQuery` (`type:'query'`: torch, ip-scan, ping, traceroute, profile)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001` = **id**.
2. **poll loop**: `map.getall(id)` = `post({Uff0001=path, uff0007=getallcmd||0xfe0004, ufe000c=0x10000005,
   ufe0018=maxobjs, ufe0001=id})`. Rows in `rep.Mfe0002` (same decoding as an ordinary getall!), keyed by
   `obj.ufe0001`. **Pagination within a pass**: `rep.ufe0003` (continuation token) / `rep.mfe0015` → re-post
   with that token. **End of pass**: `rep.uff0008===0xfe0004` (ObjectNonexistent). Once a pass completes, a
   timer waits `autorefresh` ms → next pass.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.

#### Model B — `ObjectAction` (`type:'action'` + `pollcmd`: bandwidth-test, cable-test, ping actions)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001`=id, `started=true`.
2. **fetch (poll)**: `post({Uff0001=path, uff0007=pollcmd, ufe0001=id})` → reply = **a single status record**
   (`update(rep)`, not a row map). Timer `autorefresh` → next fetch.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.
Difference A↔B: A returns a **row map** (Mfe0002) per pass; B returns **one status** per poll.

#### Shared stop conditions
- **`bfe000b`** (key `0xFE000B`, bool) = "**finished/done**" → ends the stream (the router signals completion,
  e.g. traceroute reached its target). webfig: `if(rep.bfe000b){this.stop();return;}`.
- The caller unsubscribes (unlisten) → cancel. An error (other than the 0xFE0004 terminator) → stop.

#### Implication for the implementation (a simplification!)
We do NOT need an async push reader or dispatch on subscription-id. A **worker thread** with a poll loop
over the existing request/reply M2 session is enough:
```
id = Post(startcmd, requestFields).ufe0001
while (!cancelled):
    do:  rep = Post(pollOrGetallCmd, id, continuationToken?)
         emit rows(rep.Mfe0002)         // Model A; Model B = emit single rep
         token = rep.ufe0003            // pagination within pass
    while (token != null && rep.uff0008 != 0xFE0004)
    if (rep.bfe000b) break
    sleep(autorefresh)
Post(cancelcmd, id)
```
This maps 1:1 onto tik4net's `ExecuteAsync(onReply, onError, onDone)` + `CancelAndJoin()` and the
`TikConnectionCapability.Listen` capability. `subscribe 0xFE0012` (config-table change push) is a SEPARATE
mechanism — not used for monitor windows, and out of scope for now.

### ✅ LIVE PoC DONE (2026-06-13) — start→poll→cancel verified against the router
Test `WinboxNativeM2Test.Native_MonitorCycle_Profile` ([Ignore] PoC, run via `--filter`).
Target = the Profile window **[49]** (CPU profiler — no dependency on traffic, works on any router):
- **start** = `0xFE000F` + request `u1=0xFFFFFFFD` ("total") → reply status 0, **id in `.id` (0xFE0001) =
  `0xFFFFFFFD`** (Profile echoes the CPU selector as the id; note: u32 > int.MaxValue → must be carried as uint,
  re-encoded via `SessionIdFieldU32`).
- **poll** = `0xFE0004` (default getall) + `.id` + flags `0x10000005` → rows under `Mfe0002` (1–2 CPU
  profile records/pass), repeated every 1000 ms.
- **cancel** = `0xFE0011` + `.id` → status 0.
Output: `monitor cycle OK: 4 total rows across 3 passes`. **The hypothesis was 100% confirmed live** —
streaming IS client-polling over the request/reply channel, no push. This removes the previously assumed
blocker (an async reader).

### Implementation proposal (next step)
1. **Catalog**: harvest `startcmd/pollcmd/getallcmd/cancelcmd/autorefresh` and `request:[…]` fields from
   `WinboxJgCatalog` `type:'query'/'action'` windows → a new `WinboxMonitorSpec` structure keyed by derived path
   (`/tool/torch`, `/tool/ip-scan`, …). Same principle as `_actionsByHandler` (§17).
2. **Operations**: `WinboxNativeM2Operations.StartMonitor(handler, startcmd, requestFields) → id`,
   `PollMonitor(handler, cmd, id, token) → (rows, nextToken, done)`, `CancelMonitor(handler, cancelcmd, id)`.
3. **Connection**: implement an async path in `WinboxNativeConnection` (`RunAsync`/base equivalent) —
   a worker thread with a poll loop, callbacks via the existing `ITikCommand.ExecuteAsync`. Set
   `Supports(Listen)=true`.
4. **Capability + tests**: `EthernetMonitorForEth1` (live rate), a torch test across transports
   (`EnsureCapability(Listen)` skips on CLI transports). Wiki: update the "Capability" section.

### ✅ DONE (2026-06-14) — streaming monitor + listen + async list, suite 0 fail
Full `winboxnative` suite: **163 pass / 0 fail / 81 skip** (all uncommitted, 4.x).

**Architecture** (following feedback "RunMonitor in the base class breaks the abstraction + the ID must be uint"):
- opt-in `ITikMonitorTransport` (`TikMonitorHandle.cs`) — NOT in the neutral `TikCommandConnectionBase`.
  `TikGenericCommand.ExecuteAsync` routes through `is ITikMonitorTransport` (otherwise `NotSupported`).
  `WinboxNativeConnection` implements it (`Capabilities = Crud | Listen`); CLI does not.
- Monitor id is **uint** everywhere (Profile echoes 0xFFFFFFFD > int.MaxValue). `M2Message.SessionIdField(uint)`.
- `ExecuteAsync` normalizes a multiline command (`?type=ether\n?#|`) into Filter fields — previously only the sync path did this.

**Dispatch by verb** in `RunMonitorAsync`:
- `listen` → **poll+diff** (`ListenLoop`): getall every 1s, diff by `.id` using a signature over **config
  fields only** (`RowSignature` skips ro:1 counters — `ReadOnlyFieldNames` from `.jg`), a deleted `.id` becomes a
  synthetic `.dead=true` record (O/R `LoadListenAsync` → onDeleted). webfig does the same thing (it polls config tables).
- `print`/`getall` → **async list** (`AsyncListOnce`): runs `RunPrint` off-thread, emits rows, done.
- otherwise → **streaming monitor** (`MonitorLoop`): the spec comes from `WinboxJgCatalog.GetMonitorByHandler`,
  start/poll/cancel via `WinboxNativeM2Operations.{Start,Poll,Cancel}Monitor`. Request fields are encoded
  **in the worker** (not synchronously) → a resolve failure (e.g. a nonexistent interface) goes async through
  `onError` like the API does, rather than throwing synchronously.
- **Close/Cancel during a monitor is graceful** (`MonitorStopping` = CancelRequested || !IsOpened → swallows the error).

**Native query-stack filters**: `RunPrint` evaluates the `?#|`/`?#&`/`?#!` postfix stack + `?<`/`?>` (not a naive AND).

**Shipped field aliases** (a new subsystem, `WinboxFieldResolver`, analogous to `WinboxHandlerMap.ShippedAlias`):
`ApiToJg`/`JgToApi`/`KeyToApi`/`KeyUiType`, keyed by apiPath. Only stable text/keys — types still come live from `.jg`.

**Ping** (`/ping`→[22], query, start `0xFE000F`/cancel `0xFE0011`/poll `0xFE0004`):
- aliases: address→`ping-to`, count→`packet-count`, size→`packet-size`, min/avg/max-rtt; the reply's
  **host = key 0x1** (u32 ipaddr, unnamed in `.jg` → resolved via `KeyToApi`+`KeyUiType`).
- **`addr` composite** (master.js `types.addr`): a nested message (`M2Message.MessageSys`, wire `0x29`) under 0x16,
  with IPv4 as a u32 on sub-key **0xFEFF20**. Request fields go through even for ro:1 (`allowReadOnly`).

**Interface `type` label**: `.jg` type is a number (0x10001); the API string ("ether"/"loopback") lives in the
record at **0x1001E** (verified live + cross-checked against the API). `/interface` alias: 0x1001E→`type`,
0x10001→`type-id`. **No registry/hardcoding.**

**Note**: there are two `M2Message` classes — the library one (which has `MessageSys`) vs.
`tik4net.tests/Protocols/_Shared/M2Message.cs` (which does not).
New files: `TikMonitorHandle.cs`, `WinboxMonitorSpec.cs`.

---

## 21. ✅ A query window is ONE long getall pass, not a paged snapshot (P2.45, 2026-07-31)

A `type:'query'` window **has no `pollcmd`** (verified across the entire catalog: 18 plugins / 805
windows — *no* query window carries a pollcmd, only `action` windows have one). So a poll is just an
ordinary `getall` on the monitor id, and its reply has a shape §20 didn't describe:

> The router replies with **one record plus a continuation token** (`ufe0003`), and requesting the
> next continuation **BLOCKS until another record exists**. The final reply carries `bfe000b`
> (Finished) and no further token.

Measured live on 7.23.2, `/ping` = handler `[22]`, `count=30`:

```
REQ  cmd=0xFE000F (start)  0x16={0xFEFF20=127.0.0.1}  0x11=30      → reply ufe0001=2  (monitor id)
REQ  cmd=0xFE0004 (getall) ufe0001=2 ufe000c=flags                → 1 record (seq 0) + ufe0003=1
REQ  cmd=0xFE0004          ufe0001=2 ufe000c=flags ufe0003=1      → …+1000 ms… 1 record (seq 1) + ufe0003=2
…
count=3: the third reply carries bfe000b=True and no further token → end
```

**Our defect (P2.45):** `PollMonitor` ran under a 4s / 256-round budget, because it had been written for
a paged snapshot. On a 30-second ping the budget expired mid-pass, **the continuation cursor was
discarded**, and the next poll sent a `getall` without a token — to which the router responds with
`uff0008=0xFE0004` (ObjectNonexistent = "no more rows"). From that point the monitor went silent: no
error, no onDone, 5 rows and done. Rows also arrived **in a 4-second batch**, not continuously.

**Fix:** `PollMonitorRound` does a single request/reply round; the pass itself is driven by `MonitorLoop`,
which owns the cancel handle and the emit. `continuation != null` ⇒ go straight to the next round (no
sleep), `Finished` ⇒ done, end of pass without `Finished` ⇒ wait `autorefresh` and start a new pass
(that's the model for snapshot windows like Torch/Scan). There is no time or round cap — a pass ends
only on what the router says, or on cancel. The gate is held **per round**, not per pass, so a
30-second monitor doesn't block CRUD.

**Watch out for two shapes of query window** — both are `type:'query'` and only distinguishable at
runtime:
- **stream** (ping, traceroute, profile): the pass runs for a long time, ending in `Finished`.
- **snapshot** (torch, scan, ip-scan): the pass completes immediately, `Finished` never arrives, and it
  repeats every `autorefresh`.

`action` windows (`pollcmd`) remain unchanged: one reply = one status record, and continuation is
deliberately not tracked for them (webfig's `ObjectAction` doesn't either).

---

## 22. ✅ A monitor window has no rows outside the monitor cycle (P2.51, 2026-08-01)

`RunPrintCore` only knew how to handle a monitor window asynchronously. A synchronous read (`ExecuteList` /
`LoadList`) fell through to a generic `getall` on the monitor's handler — and the router replies **with
no records**:

```
/ping =address=127.0.0.1 =count=2  (WinboxNative, before the fix)
  >> M2 0xFF0001=u32[]:[22] 0xFE000C=u32:268435463        (getall on handler [22])
  << M2 (no 0xFE0002 records)
  → caller sees "OK (no data returned)"                    ← silent failure
```

A monitor window (`.jg` `type:'query'`, or `action`+`pollcmd`) **is not a table**: its rows only come
into existence once the client runs the cycle. `RunMonitorWindowSync` therefore does start → poll →
cancel on the calling thread and returns whatever the cycle produced:

- **until the router sets Finished** — for a self-terminating command (`ping count=N`),
- **or until the first pass ends** — for a continuous window, whose pass *is* a single snapshot.

This is the same rule CLI transports get from the `once`/`count=1` modifier, and it matches what the
binary API returns for the same command.

**`once` is never sent over M2.** RouterOS needs it because a monitor otherwise runs forever both over
the API and in the terminal. A WinBox window has no such input — "a single reading" is decided by the
client — and attempting to encode it results in a `WinboxFieldResolutionException` on a field the
caller never meant as data (`IsMonitorSnapshotModifier`).

### What still doesn't work as a result (measured, not glossed over)

Nothing from the original list — chapter 23 below resolved all three points. Only this remains:
`/ping` without `count` (and `/tool/torch` via `ExecuteList`) run until something stops them; a
synchronous read is therefore bounded by `ReceiveTimeout` and ends in a
`TikConnectionReceiveTimeoutException` instead of holding the thread forever.

## 23. ✅ `addr` is not a string, and IPv6 is its own ftype (P2.52 + P2.53, 2026-08-01)

Three symptoms recorded in chapter 22 as three separate gaps actually shared **two root causes**, both
in the codec, not in the router. The diagnosis started by reading the **requests** instead of the
responses:

```
/ping address=127.0.0.1    >> 0x16=msg:{0xFEFF20=16777343}         ← works
/ping address=example.com  >> 0x16=str:example.com                  ← router: "no address was specified"
/ping address=2001:db8::1  >> 0x16=str:2001:db8::1                  ← same
```

So the router wasn't reporting anything wrong: **it was answering our malformed query.**

### 23.1 `addr` is a compound — each address shape has its own sub-key

`master*.js` (`types.addr.fromstr`) tries the shapes in this order, gated by the `allow` mask from `.jg`:

| shape | sub-key | wire | `allow` |
|---|---|---|---|
| IPv4 | `0xFEFF20` | u32 (octet-LSB) | `4` |
| IPv6 | `0xFEFF21` | **FT_ADDR6** | `6` |
| DNS name | `0xFEFF26` | string (the **entire** input, not just the part before a separator) | `D` |
| route distinguisher | `0xFEFF27` | string | `R` |
| MAC | `0xFEFF2F` | raw 6 B | `m` |
| `/len` | `0xFEFF25` | u32 | `/` |
| `%iface` / `@vrf` | `0xFEFF22` / `0xFEFF23` | u32 (id from the dropdown) | `i` / `v` |

Before P2.53 the codec only handled IPv4 and, for anything else, **fell back to a bare string on the
field's own key**. The router doesn't read that shape — it behaves as if the field never arrived. So
pinging a hostname or an IPv6 address was silently broken. `%iface`/`@vrf` are now rejected loudly
(we can't yet tell a name apart from a dropdown selection) — dropping the qualifier would mean
addressing something else entirely.

### 23.2 The IPv6 field is `FT_ADDR6` (type byte `0x18`), not `raw`

The ftype table from `master*.js` (`msg2buffer`), where the type byte = `ftype << 3 | size-flags`:

| ftype | 0 | 1 | 2 | **3** | 4 | 5 | 6 |
|---|---|---|---|---|---|---|---|
| scalar | bool `0x00` | u32 `0x08` | u64 `0x10` | **addr6 `0x18`** | string `0x20` | message `0x28` | raw `0x30` |
| array (+16) | `0x80` | `0x88` | `0x90` | `0x98` | `0xA0` | `0xA8` | `0xB0` |

`FT_ADDR6` is **16 bytes with no length prefix** — the only variable-width-looking type that lacks one.
Sending IPv6 as `raw` puts a length byte where the address's first byte belongs, so the router ignores
the field.

**A second, worse consequence:** the *parser* didn't know `0x18` either. An unknown type fell into
`default: return 0`, so the value got misread as the next key+type and **the rest of the message
scrambled into nonsense keys** — silently. This was exactly the "empty `0x1=[{}]`" seen with
traceroute: a hop is `union{ip6addr a1 allowipv4, string s2}` inside a `multi`, and the parser skipped
its 16 bytes as zero. After adding `0x18` (and the missing `0x80/0x90/0x98/0xB0` cases), traceroute
returns `address=127.0.0.1` with no change to the resolver at all.

> Lesson for the whole table: **every ftype must have a case in `SkipTypeBytes`**, even when there's no
> decoder for it. A missing case isn't "unsupported type" — it's a silent scramble of everything that
> follows, the same trap the `0xA0 str_array` case demonstrated earlier.

### 23.3 `/interface/monitor-traffic` is live fields on the interface list window, not a monitor window

Across the entire catalog (18 plugins, `jg_analyze.py`) **there is no monitor window for traffic at
all**. WinBox shows throughput as live columns on the interface list, which a normal `getall` with the
stats bit returns directly:

| API name | key | `.jg` label | ftype |
|---|---|---|---|
| `rx-bits-per-second` | `0x100D3` | `Rx` | bigbitrate |
| `tx-bits-per-second` | `0x100D4` | `Tx` | bigbitrate |
| `rx-packets-per-second` | `0x100CB` | `Rx Packet` | decimal p/s |
| `tx-packets-per-second` | `0x100CD` | `Tx Packet` | decimal p/s |
| `rx-byte` | `0x100FC` | `Rx Bytes` | bigbytes |
| `rx-packet` | `0x100FE` | `Rx Packets` | bigdecimal |

**And there was a second, separate bug here:** the label normalizer had `'Rx' → rx-byte`, so the API
name `rx-byte` ended up getting the **rate**. On ether1, native returned `rx-byte=5536` while the API
reported `rx-byte=76024833` for the same record — right name, wrong value, off by five orders of
magnitude. Because of this, the entire traffic block is now mapped **by key**
(`ShippedFieldAliases["/interface"].KeyToApi`), and the alias set is inherited by subpaths
(`/interface/ethernet`, `/interface/monitor-traffic`) that read the same handler.

Verified: at the same moment, both API and native report **matching** `rx-bits/s=3584, rx-pkt/s=3`.

### 23.4 A self-terminating monitor must wait for Finished

A pass that ends without `Finished` means "this is a snapshot" for a continuous window, but means
"still working" for `ping`/`traceroute`. A traceroute to an unreachable address publishes a longer
table each second: the first pass = 1 hop. So for self-terminating commands
(`TikMonitorVerbs.SelfTerminating`), a synchronous read keeps polling until the router says Finished —
20 rows in 5.2 s, the same shape as over the API — bounded by `ReceiveTimeout`.

## 24. ✅ A `range:1` network field stores start+end, and an `opt` field needs its flag (P2.33, 2026-08-02)

Filed as "native rewrites an IP field into CIDR, so `192.0.2.74` reads back as `192.0.2.74/32`". The
value was wrong in a different way (`192.0.2.74/6`), the cause was in the **catalog parser** rather
than the codec, and the same root cause was corrupting **writes** far more seriously than reads.

### 24.1 The sibling key is the range END, not a netmask

Every firewall address field is declared in `roteros.jg` as an `opt` → `not` → `network` with
`range:1`:

```js
{name:'Src. Address',type:'opt',id:'b1a2',c:[
  {type:'not',id:'bc8',c:[{type:'network',id:'u32',maskid:'u33',range:1}]}]}
```

`range:1` means the `maskid` sibling carries the **last address of the range**, not a netmask. Read
live from the router (`0x32` = start, `0x33` = end):

| stored value | `0x32` | `0x33` | what the API prints |
|---|---|---|---|
| `192.0.2.74` | 192.0.2.74 | 192.0.2.74 | `192.0.2.74` |
| `192.0.2.0/24` | 192.0.2.0 | 192.0.2.255 | `192.0.2.0/24` |
| `192.0.2.10-192.0.2.20` | 192.0.2.10 | 192.0.2.20 | `192.0.2.10-192.0.2.20` |
| `192.0.2.0-192.0.2.3` | 192.0.2.0 | 192.0.2.3 | **`192.0.2.0/30`** |

So RouterOS picks the rendering from the **span**, not from how the value was entered: `start == end`
is a bare host (never `/32`), an aligned power-of-two block is CIDR, anything else is `a-b`. That rule
is `WinboxFieldResolver.FormatV4Range` / `TryParseV4Range`, and matching it exactly is what makes the
record read identically over native and over every other transport.

### 24.2 The flag was lost in the opt/not flattening, not in the codec

`WinboxJgCatalog.AddOptionField` — which drills through `opt`/`not` wrappers to the value leaf — read
`ro`, `maskid`, `allow` and the enum map, but **not `range`**. So `IsRange` was false for exactly the
fields WinBox makes optional, i.e. all of them here, and the end address went through the netmask
path: `MaskToPrefix` counts set bits, and 192.0.2.74 as a "netmask" has six of them — hence `/6`.

The lesson generalises past this field: the catalog has **two** places that build a field, and any
attribute the wrapped path forgets is silently absent only for wrapped fields.

### 24.3 An `opt` field is ignored unless its flag is sent — and cleared by the same flag

`EncodeField` emitted the `opt`/`not` bools *after* the typed switch, but `network`, `ipaddr`,
`macaddr` and `addr` all `return` from inside it. So those fields went out **without** the opt flag,
and the router **discarded them**: `/ip/firewall/filter/add src-address=203.0.113.5` over native
created a rule with no `src-address` at all — one that matches every source address. Verified live
before and after; the flags are now emitted once, before the switch.

The same bool clears the field: an empty value emits `opt=false`. It has to travel **alongside** the
cleared value rather than replacing it — a string field is cleared by writing it empty, and sending
only the flag makes `unset` report a success that changed nothing. For a typed field whose branch
sends nothing for an empty value, that bool is the whole write, and without it a mapper-level `unset`
of an optional field produced an empty M2 message that `unset` refused as naming no field.

**Coverage:** `VerbMatrixTest.CrossTransport_AddressValues_MatchTheBinaryApi` compares all three forms
against the binary API on whatever transport is under test (verified RED pre-fix); the rendering rule
and the flattening are pinned router-free by `WinboxAddressRangeTests` and `WinboxJgFieldFlagTests`.
