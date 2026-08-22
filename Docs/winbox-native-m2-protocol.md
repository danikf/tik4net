# WinBox native M2 — protocol reference

Durable protocol facts for the `WinboxNative` transport, verified against a live RouterOS router
and/or read out of the webfig client. The C# implementation (`tik4net/Winbox/`,
`tik4net/WinboxNative/`) cites these sections by number, so the section numbers below are stable —
do not renumber a heading without checking who cites it.

Companion documents: [`jg-catalog-format.md`](jg-catalog-format.md) for the `.jg` catalog encoding,
[`findings-winbox.md`](findings-winbox.md) for the transport/session layer, and
[`winbox-m2-multiplexing-design.md`](winbox-m2-multiplexing-design.md) for the channel model. Dated
incidents and superseded diagnoses from this area live in [`HISTORY.md`](HISTORY.md).

> Superseded diagnoses, incidents and pinned measurements for this area are in
> [`winbox-native-m2-protocol-history.md`](winbox-native-m2-protocol-history.md); this document describes current behaviour only.

---

## 0. `.jg` files are plain-text JS object literals

`.jg` files are **not** binary or gzip on the wire once decompressed — they are **plain-text
JavaScript object literals** (pseudo-JSON), 100% printable ASCII. Example (`advtool.jg`):

```js
[{name:'IP Scan',title:'IP Scan',group:'Tools',c:[{title:'IP Scan',type:'query',
  path:[ 101,1 ],autorefresh:1000,cancelcmd:2,request:[...],startcmd:1,c:[
  {name:'Address',type:'ipaddr',id:'u1',width:90},
  {name:'MAC Address',type:'macaddr',id:'r2',opt:1,width:120}, ... ]}]}]
```

### `.jg` → M2 protocol mapping

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

Uppercase letter = array variant of the same type. The hex suffix reads as a hexadecimal number
(`fe0010` → 0xFE0010), covering both the system namespace (`0xFExxxx`) and the user namespace.

The `.jg` prefix letter only tells you the field's *shape* (scalar vs. array, roughly which
family). The exact TLV type byte on the wire is derived from `ftype` and size flags, not from the
prefix letter — see §23.2 for the authoritative table.

---

## 6. System field keys and error codes

Authoritative field constants, transcribed from tenable/routeros `common/winbox_message.cpp`:

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

Builtin commands `0xFE0000–0xFE0016` are **system-level** (`0xFE0001`=cmdGetPolicies), and — as
§10 establishes from the webfig source — a large subset of them double as the generic CRUD verbs
(getall, get-one, set, add, remove, …), not a separate "system-only" range.

---

## 10. Native CRUD command catalog (from webfig `master.js`)

**Source of truth = the webfig client script** `master-<hash>.js`, served by the router at
`/webfig/` (the hash changes per RouterOS build, so fetch it from the router under test rather than
reusing a saved filename; the `winbox-native-dev` skill has the routes). It is the only non-crypto
webfig script, and it implements the M2 protocol in JS over HTTP `/jsproxy`. Functions
`msg2buffer`/`buffer2msg` (serialization), `ObjectMap.getall/fetch/setObject` (CRUD), `subscribe`
(push model) are the relevant entry points.

### Complete command catalog (`uff0007`, from webfig)
| cmd | constant | meaning | request fields | reply |
|---|---|---|---|---|
| `0xfe0004` | getallcmd | **list all** | `ufe000c`=flags, `ufe0018`=maxobjs (a cap, §29), paging `ufe0003` **and/or** `mfe0015` (echoed back as received) | `Mfe0002` records, `ufe0019` count, `ufe0003` and/or `mfe0015` cont. token |
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

`.jg` doesn't list these commands for a `type:'map'` window because they are the **generic
defaults** — the numbers only show up in `.jg` when a window overrides one of them.

### Key system fields (writeId: 3B key LE + 1B type in the top byte)
- `Uff0001` (u32[]) = **SYS_TO** = path `[20,0]`. **Uppercase U = array!**
- `uff0007` (u32) = **SYS_CMD**.  `Sff001c` = trace (webfig, can be omitted).
- `Uff0002` (u32[]) = SYS_FROM (in a push notification = which subscription).
- `uff0008` = error code, `sff0009` = error string.
- `ufe0001` = **.id** (record handle).  `ufe000c` = getall/get **flags** (getall requires
  `0x10000005` = `refetchonopen | refreshfilter` — without it the handler returns no rows).
- `ufe0018` = maxobjs — a row **cap**, not a page size; the router picks the page size and ignores
  this for it (§29).  `ufe0003` = getall continuation token.  `ufe0019` = count.
- `Mfe0002` = **records** (message-array, wire type `0xA8`).  `ufe0005` = next-id (ordered).
- `ufe0013` = removed flag.  `mfe001d` = default config (`setDefaultConf` cmd `0xfe0004`+`ufe000c=0x20000000`).

### Field-key convention (from `.jg` id + webfig)
- **comment = `sfe0009`** = string key **`0xFE0009`** (webfig `types.comment.get/put`). Confirmed live.
- Name = `s10006` (0x10006).  .id = `ufe0001` (0xFE0001).  type = `u10001`.
- Type-byte = `(ftype<<3)|sizeFlags`; ftype 5=message, 21=message[]; flags short=0x01 long=0x02;
  length/count: short=1B, normal=2B, long=4B (webfig `readLen`). Full ftype table: §23.2.

### Wire-format detail (from `msg2buffer`)
- An M2 message = `'M2'` + fields. A sub-message (message/message-array element) ALSO carries an `'M2'` prefix.
- Field header (4B): `[key_lo][key_mid][key_hi][typeByte]` — 24-bit LE key, type+flags in the top byte.
- message-array (0xA8): `[2B count][ (2B elemLen + M2-submsg) × count ]`.

### Edit = "modify everything, save"
webfig's `setObject` sends the ENTIRE object (`update(req,obj._exportObj||obj)`) + cmd `0xfe0003` +
`ufe0001`=.id. In practice, sending .id plus only the changed fields is enough (verified live: setting only `sfe0009`).

### Live verification (router 7.21.4, `WinboxNativeGetallTest.cs`)
- `Native_GetAllInterfaces` — `[20,0]` getall → names **match the API** (`CollectionAssert`).
- `Native_GetAllIpAddresses` — `[20,1]` getall → 1 record (generic across tables).
- `Native_SetAndRestoreEther1Comment` — get-one ether1 (.id=2, `sfe0009`="My comment" = API),
  set `sfe0009`="native-m2-ok" → API confirms the change → restore → API confirms. `status=0`.

---

## 20. Streaming monitors are client-side polling, not a server push

A `.jg` window of `type:'query'` (torch, ip-scan, ping, traceroute, profile) or `type:'action'`
with a `pollcmd` (bandwidth-test, cable-test, ping actions) drives **continuous/streamed
monitoring**: instead of one getall, the caller repeatedly asks the router for the next batch of
rows. Ground truth is webfig `master.js` (`ObjectQuery`, `ObjectAction`, `ObjectMap.getall`): the
**client re-polls on a timer** over the normal synchronous request/reply channel — the router never
pushes a monitor row unsolicited. No async server-push reader or dispatch on subscription-id is
needed; a worker thread doing start → poll → cancel over the existing M2 session is sufficient.
(`subscribe`/`0xFE0012`, described in §10, is a real but separate mechanism for config-table
change push — not used for monitor windows.)

### `.jg` window → cmd triple (all SYS_CMD `uff0007` on `Uff0001`=path)
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

### Model A — `ObjectQuery` (`type:'query'`: torch, ip-scan, ping, traceroute, profile)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001` = **id**.
2. **poll loop**: `map.getall(id)` = `post({Uff0001=path, uff0007=getallcmd||0xfe0004, ufe000c=0x10000005,
   ufe0018=maxobjs, ufe0001=id})`. Rows in `rep.Mfe0002` (same decoding as an ordinary getall!), keyed by
   `obj.ufe0001`. **Pagination within a pass**: `rep.ufe0003` (continuation token) / `rep.mfe0015` → re-post
   with that token. **End of pass**: `rep.uff0008===0xfe0004` (ObjectNonexistent). Once a pass completes, a
   timer waits `autorefresh` ms → next pass.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.

### Model B — `ObjectAction` (`type:'action'` + `pollcmd`: bandwidth-test, cable-test, ping actions)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001`=id, `started=true`.
2. **fetch (poll)**: `post({Uff0001=path, uff0007=pollcmd, ufe0001=id})` → reply = **a single status record**
   (`update(rep)`, not a row map). Timer `autorefresh` → next fetch.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.

Difference A↔B: A returns a **row map** (Mfe0002) per pass; B returns **one status** per poll.

### Shared stop conditions
- **`bfe000b`** (key `0xFE000B`, bool) = "**finished/done**" → ends the stream (the router signals completion,
  e.g. traceroute reached its target). webfig: `if(rep.bfe000b){this.stop();return;}`.
- The caller unsubscribes (unlisten) → cancel. An error (other than the `0xFE0004` terminator) → stop.

### Implementation model
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
`TikConnectionCapability.Listen` capability.

### Runtime architecture
`RunMonitorAsync` dispatches by verb:
- `listen` → **poll+diff** (`ListenLoop`): getall every 1s, diff by `.id` using a signature over
  **config fields only** (`RowSignature` skips ro:1 counters — `ReadOnlyFieldNames` from `.jg`), a
  deleted `.id` becomes a synthetic `.dead=true` record (O/R `LoadListenAsync` → onDeleted). webfig
  does the same thing (it polls config tables).
- `print`/`getall` → **async list** (`AsyncListOnce`): runs `RunPrint` off-thread, emits rows, done.
- otherwise → **streaming monitor** (`MonitorLoop`): the spec comes from `WinboxJgCatalog.GetMonitorByHandler`,
  start/poll/cancel via `WinboxNativeM2Operations.{Start,Poll,Cancel}Monitor` (poll is
  `PollMonitorRound`, one request/reply round per call — see §21 for why a pass is not bounded by a
  fixed round count). Request fields are encoded **in the worker** (not synchronously) → a resolve
  failure (e.g. a nonexistent interface) goes async through `onError` like the API does, rather than
  throwing synchronously.
- **Close/Cancel during a monitor is graceful**: `MonitorStopping` (`CancelRequested || !IsOpened`) swallows the error.

Monitor id is **uint** everywhere — a session handle can exceed `int.MaxValue` (Profile echoes
`0xFFFFFFFD`) — carried via `M2Message.SessionIdField(uint)`. Monitor support is opt-in
(`ITikMonitorTransport`, `tik4net/Connection/TikMonitorHandle.cs`) rather than living in the
neutral `TikCommandConnectionBase`; `TikGenericCommand.ExecuteAsync` routes through
`is ITikMonitorTransport` and reports `NotSupported` otherwise. `WinboxNativeConnection` implements
it (`Capabilities = Crud | Listen`); the CLI transports do not.

`RunPrint` evaluates the native query-stack filters — the `?#|`/`?#&`/`?#!` postfix stack plus
`?<`/`?>` — rather than a naive AND.

**Field aliases**: `WinboxFieldResolver` (`ApiToJg`/`JgToApi`/`KeyToApi`/`KeyUiType`, keyed by
apiPath) ships stable text/key aliases the same way `WinboxHandlerMap.ShippedAlias` ships path
aliases. Only stable text/keys are shipped — types still come live from `.jg`.

**Ping** (`/ping`→[22], query, start `0xFE000F`/cancel `0xFE0011`/poll `0xFE0004`):
- aliases: address→`ping-to`, count→`packet-count`, size→`packet-size`, min/avg/max-rtt; the reply's
  **host = key 0x1** (u32 ipaddr, unnamed in `.jg` → resolved via `KeyToApi`+`KeyUiType`).
- the address rides as the `addr` composite described in §23.1 (nested message under key `0x16`,
  IPv4 on sub-key `0xFEFF20`). Request fields go through even for `ro:1` (`allowReadOnly`).

**Interface `type` label**: `.jg` type is a number (`0x10001`); the API string ("ether"/"loopback")
lives in the record at **`0x1001E`** (verified live + cross-checked against the API). `/interface`
alias: `0x1001E`→`type`, `0x10001`→`type-id`.

**Two `M2Message` classes exist** in the solution: the library one
(`tik4net/Winbox/M2Message.cs`, which has `MessageSys`) and a separate one in the integration test
project (`tik4net.integrationtests/Protocols/_Shared/M2Message.cs`, which does not) — don't confuse
them when tracing.

---

## 21. A query-window poll returns one record and blocks until the next exists

A `type:'query'` window **has no `pollcmd`** (verified across the entire catalog: 18 plugins / 805
windows — no query window carries a pollcmd, only `action` windows have one). So a poll is just an
ordinary `getall` on the monitor id, and its reply shape is:

> The router replies with **one record plus a continuation token** (`ufe0003`), and requesting the
> next continuation **BLOCKS until another record exists**. The final reply carries `bfe000b`
> (Finished) and no further token.

Measured live, `/ping` = handler `[22]`, `count=30`:

```
REQ  cmd=0xFE000F (start)  0x16={0xFEFF20=127.0.0.1}  0x11=30      → reply ufe0001=2  (monitor id)
REQ  cmd=0xFE0004 (getall) ufe0001=2 ufe000c=flags                → 1 record (seq 0) + ufe0003=1
REQ  cmd=0xFE0004          ufe0001=2 ufe000c=flags ufe0003=1      → …+1000 ms… 1 record (seq 1) + ufe0003=2
…
count=3: the third reply carries bfe000b=True and no further token → end
```

`PollMonitorRound` therefore does a single request/reply round; the pass itself is driven by
`MonitorLoop`, which owns the cancel handle and the emit. `continuation != null` ⇒ go straight to
the next round (no sleep), `Finished` ⇒ done, end of pass without `Finished` ⇒ wait `autorefresh`
and start a new pass (that's the model for snapshot windows like Torch/Scan). There is no time or
round cap on a pass — a pass ends only on what the router says, or on cancel. The gate is held
**per round**, not per pass, so a 30-second monitor doesn't block CRUD.

A fixed round or time budget on the poll loop reproduces a specific silent failure: on a long pass
the budget expires before the router does, the continuation cursor is discarded, and the next poll
sends a token-less `getall` — to which the router replies `uff0008=0xFE0004` (ObjectNonexistent,
its normal "no more rows" signal). The monitor then ends quietly with a handful of rows and no
error, indistinguishable from a short command that finished on its own.

**Two shapes of query window** — both are `type:'query'` and only distinguishable at runtime:
- **stream** (ping, traceroute, profile): the pass runs for a long time, ending in `Finished`.
- **snapshot** (torch, scan, ip-scan): the pass completes immediately, `Finished` never arrives, and it
  repeats every `autorefresh`.

`action` windows (`pollcmd`) remain unchanged: one reply = one status record, and continuation is
deliberately not tracked for them (webfig's `ObjectAction` doesn't either).

---

## 22. A monitor window has rows only during a monitor cycle, not from a plain getall

A monitor window (`.jg` `type:'query'`, or `action`+`pollcmd`) **is not a table**: its rows only
come into existence once a client runs the start → poll → cancel cycle. A synchronous read
therefore cannot be answered with an ordinary getall on the window's handler — the router replies
with no records and no error, which reads as an empty result rather than a wrong request.
`RunMonitorWindowSync` runs the cycle on the calling thread and returns whatever it produced:

- **until the router sets Finished** — for a self-terminating command (`ping count=N`, see §23.4),
- **or until the first pass ends** — for a continuous window, whose pass *is* a single snapshot.

This is the same rule CLI transports get from the `once`/`count=1` modifier, and it matches what the
binary API returns for the same command.

**`once` is never sent over M2.** RouterOS needs it because a monitor otherwise runs forever both over
the API and in the terminal. A WinBox window has no such input — "a single reading" is decided by the
client — and attempting to encode it results in a `WinboxFieldResolutionException` on a field the
caller never meant as data (`IsMonitorSnapshotModifier`).

**Known limitation:** `/ping` without `count` (and `/tool/torch` via `ExecuteList`) run until
something stops them; a synchronous read is bounded by `ReceiveTimeout` and ends in a
`TikConnectionReceiveTimeoutException` instead of holding the thread forever.

## 23. `addr` is a compound field, and IPv6 rides as its own ftype

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

Sending anything other than IPv4 as a bare string on the field's own key is silently ignored by the
router — the field behaves as if it never arrived, which surfaces as a router complaint about the
*value* ("no address was specified") rather than about the shape of the request. `%iface`/`@vrf` are
rejected loudly by the codec (`WinboxFieldResolutionException`) rather than guessed — there is
currently no way to tell a DNS name apart from a dropdown selection, and dropping the qualifier
would address something else entirely.

### 23.1a Reading an `addr`: `%iface` can be the whole value, and `0xFEFF1F` is only a tag

A record's `addr` carries `0xFEFF1F` alongside the shape sub-keys — a numeric tag for which shape is in
force (`1` = IPv4, `2` = IPv6, `9` = an interface and nothing else). It is not a value; the sub-keys are.
A decoder that falls through to "render the nested message" therefore prints the tag: on 7.24 a connected
route's gateway came back as the literal `9`.

The `%iface` qualifier is not only a suffix. A field whose value is `{0xFEFF22=2, 0xFEFF1F=9}` has no
address at all — it IS the interface, which is what RouterOS prints for a connected route
(`gateway=ether1`). Where an address is also present the qualifier is a suffix as expected:
`{0xFEFF22=2, 0xFEFF1F=1, 0xFEFF20=…}` is `192.168.4.236%ether1`. Either way the id names a row of the
generic interface table `[20,0]` and resolves through the same reference cache a dynamic `enm` uses.

### 23.2 The IPv6 field is `FT_ADDR6` (type byte `0x18`), not `raw`

The ftype table from `master*.js` (`msg2buffer`), where the type byte = `ftype << 3 | size-flags`:

| ftype | 0 | 1 | 2 | **3** | 4 | 5 | 6 |
|---|---|---|---|---|---|---|---|
| scalar | bool `0x00` | u32 `0x08` | u64 `0x10` | **addr6 `0x18`** | string `0x20` | message `0x28` | raw `0x30` |
| array (+16) | `0x80` | `0x88` | `0x90` | `0x98` | `0xA0` | `0xA8` | `0xB0` |

`FT_ADDR6` is **16 bytes with no length prefix** — the only variable-width-looking type that lacks one.
Sending IPv6 as `raw` puts a length byte where the address's first byte belongs, so the router ignores
the field.

**Every ftype must have a case in `SkipTypeBytes`, even when there's no decoder for it.** A missing
case is not "unsupported type" — the parser falls into `default: return 0`, so the value's bytes get
misread as the next key+type and the rest of the message scrambles into nonsense keys, silently. An
empty-looking `0x1=[{}]` on traceroute was exactly this: a hop is `union{ip6addr a1 allowipv4,
string s2}` inside a `multi`, and the parser was skipping its 16 bytes as zero. The same trap applies
to the `0xA0` string-array type and to any future ftype the table doesn't yet cover.

### 23.3 `/interface/monitor-traffic` is live fields on the interface list window, not a monitor window

Across the entire catalog (18 plugins) there is no monitor window for traffic at all. WinBox shows
throughput as live columns on the interface list, which a normal `getall` with the stats bit
returns directly:

| API name | key | `.jg` label | ftype |
|---|---|---|---|
| `rx-bits-per-second` | `0x100D3` | `Rx` | bigbitrate |
| `tx-bits-per-second` | `0x100D4` | `Tx` | bigbitrate |
| `rx-packets-per-second` | `0x100CB` | `Rx Packet` | decimal p/s |
| `tx-packets-per-second` | `0x100CD` | `Tx Packet` | decimal p/s |
| `rx-byte` | `0x100FC` | `Rx Bytes` | bigbytes |
| `rx-packet` | `0x100FE` | `Rx Packets` | bigdecimal |

Two different `.jg` labels ("Rx" the live rate, "Rx Bytes" the counter) collide under a label-based
lookup, so the entire traffic block is mapped **by key**
(`ShippedFieldAliases["/interface"].KeyToApi`), and the alias set is inherited by subpaths
(`/interface/ethernet`, `/interface/monitor-traffic`) that read the same handler.

Verified: at the same moment, both API and native report **matching** `rx-bits/s=3584, rx-pkt/s=3`.

### 23.4 A self-terminating monitor must wait for Finished

A pass that ends without `Finished` means "this is a snapshot" for a continuous window, but means
"still working" for `ping`/`traceroute`. A traceroute to an unreachable address publishes a longer
table each second: the first pass = 1 hop. So for self-terminating commands
(`TikMonitorVerbs.SelfTerminating`), a synchronous read keeps polling until the router says Finished —
20 rows in 5.2 s, the same shape as over the API — bounded by `ReceiveTimeout`.

## 24. A `range:1` network field stores start+end, and an `opt` field needs its flag

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

### 24.2 The catalog must copy `range` when it flattens `opt`/`not`

`WinboxJgCatalog.AddOptionField` — which drills through `opt`/`not` wrappers to the value leaf —
copies `ro`, `maskid`, `allow` and the enum map from the wrapped field, and must also copy `range`.
Without it `IsRange` is false and the end address goes through the netmask path instead
(`MaskToPrefix`, which counts set bits) — silently producing the wrong prefix for any wrapped range
field, since the catalog has two places that build a field and any attribute the wrapped path
forgets is absent only for wrapped fields, not for unwrapped ones.

### 24.3 An `opt` field is ignored unless its flag is sent — and cleared by the same flag

`EncodeField` must emit the `opt`/`not` bools **before** the typed switch, not after: `network`,
`ipaddr`, `macaddr` and `addr` all `return` from inside the switch, so a flag emitted afterward never
goes out. Without the flag the router discards the field entirely — `/ip/firewall/filter/add
src-address=203.0.113.5` sent without the `opt` bool creates a rule with no `src-address` at all,
one that matches every source address, and the write reports success.

The same bool clears the field: an empty value emits `opt=false`. It has to travel **alongside** the
cleared value rather than replacing it — a string field is cleared by writing it empty, and sending
only the flag makes `unset` report a success that changed nothing. For a typed field whose branch
sends nothing for an empty value, that bool is the whole write, and without it a mapper-level `unset`
of an optional field produces an empty M2 message that `unset` refuses as naming no field.

**Coverage:** `VerbMatrixTest.CrossTransport_AddressValues_MatchTheBinaryApi` compares all three forms
against the binary API on whatever transport is under test; the rendering rule and the flattening are
pinned router-free by `WinboxAddressRangeTests` and `WinboxJgFieldFlagTests`.

---

## 25. An action's arguments are attributed to the action, not just its handler

### 25.1 An action invocation must send the caller's fields

`DispatchActionVerb` invokes the `.jg` SYS_CMD with the caller's fields, not `fields: null`. An
argument-less request is not a no-op: the router applies its own defaults for every field the caller
didn't send. On the wire (7.23.2), a `generate-key name=X key-size=2048` request that reached the
router without arguments got back a reply row carrying `0x1=1024` — the router's default key size —
while still reporting success.

### 25.2 An action's arguments are resolved against the action, not the handler's record map

`secure.jg` declares the IPsec 'Keys' window `[85,5]` as a record list **and** three global `doit`s
on the same handler:

```js
{name:'IPsec Key',title:'Keys',type:'map',path:[ 85,5 ],c:[
  {name:'Name',type:'string',id:'sfe0010',min:1},
  {name:'Key Size',type:'number',id:'u1',ro:1},                       // list COLUMN, read-only
  {name:'Generate Key',type:'doit',cmd:1,global:1,c:[
    {name:'Name',type:'string',id:'sfe0010',min:1},
    {name:'Key Size',type:'enm',id:'u1',                              // ARGUMENT, writable
     values:{type:'static',map:{1024:'1024',2048:'2048',4096:'4096'}}}]}]}
```

Both are labelled 'Key Size'. A handler-wide field map keyed by label can only keep one of them —
first label wins — so a read-only column shadows a writable argument sharing its name, and because
`EncodeField` drops read-only fields, the argument encodes to nothing even once 25.1 is fixed.

An action's fields are therefore attributed to the **action** as well as to its handler
(`WinboxJgCatalog.GetActionFields`), and an invocation resolves against handler → window → action,
the action having the last word (`WinboxFieldResolver`, `WinboxNativeConnection.MakeActionResolver`).
The handler map keeps exactly the content it had — that map is what a `getall` row decodes against,
where the column really is the right answer, and it is also the only map a standalone action window
(Wake on LAN `[82]`, whose handler backs no list) ever had.

**Coverage:** `IpsecKeyTest.GenerateAndDeleteIpsecKeyWillNotFail` asserts the name and the 2048-bit
size on whatever transport is under test; the catalog scoping and the encode are pinned router-free by
`WinboxJgActionFieldTests`.

---

## 26. Enum maps can nest under wrappers, and "not set" is an absent field

Each rule below was settled by asking the router what it prints for the same record, not by reading
webfig alone:

| path | field | native (wire form) | the API says |
|---|---|---|---|
| `/ip/proxy` | `port` | `[8080]` | `8080` |
| `/ip/ssh` | `ciphers` | `[0]` | `auto` |
| `/ip/ipsec/proposal` | `pfs-group` | `2` | `modp1024` |
| `/system/logging/action` | `syslog-severity` | `4294967295` | *(field absent)* |
| `/ip/proxy/access` | `method` | `''` | *(field absent)* |
| `/certificate` (unsigned) | `digest-algorithm` | `0` | *(field absent)* |

### 26.1 The static map is not always the first thing under `values`

RouterOS wraps a value list in `enumfilter` (which members this board offers), `defenum` (a sentinel
id/name in front of the real list) and `pair` (a static sentinel list beside a dynamic table) — and
nests them. An IPsec proposal's PFS group is `enumfilter → defenum → static`:

```js
{name:'PFS Group',type:'enm',id:'u4',values:{type:'enumfilter',filters:[{id:0},{id:2},…],
  values:{type:'defenum',defid:0,defname:'none',values:{type:'static',
    map:{1:'modp768 (1)',2:'modp1024 (2)',5:'modp1536 (5)',…}}}}}
```

Reading only the top level leaves the field with no map at all, in both directions: the value
decodes as the bare number and `pfs-group=modp1024` cannot be encoded either. The chain must be
followed all the way down, and the `defenum`'s own `defid`/`defname` joins the map as a member. The
runtime-computed wrappers (`queryenum`, `offsetenum`, `slotenum`, `remapenum`) are deliberately
**not** followed — their members come from a live query or from another field's value, so there is
no static map to read.

The labels also spell the numeric value out: WinBox shows `modp1024 (2)` where the API says `modp1024`.
The suffix is stripped only when the number in it IS the key, so a label that genuinely ends in a
parenthesised number is left alone. Twelve labels in the 7.23.2 catalog carry it, all DH groups.

### 26.2 A `multinumber` list of literals

`{name:'Port',type:'multinumber',id:'U2',c:[{type:'number'}]}` and
`{name:'Ciphers',type:'multinumber',id:'Ub',c:[{type:'enm',values:{type:'static',map:{0:'Auto',…}}}]}`
are u32 arrays whose elements are a plain number and a static enum, decoded independently of the
*reference* flavour of the same shape (the log's `topics`). Each renders one element per value,
comma-joined, each through the element's map when it has one. An element the map does not name stays
numeric rather than being dropped — a shorter list would read as "the router has fewer of these".

### 26.2b The list family is one family, and the ELEMENT decides how it reads

`multinumber` is not a special case, it is the one member of the family that had a decoder. `master*.js`
says so outright:

```js
types.multiipaddr = types.multiip6addr = types.multistring = types.multiraw
    = inherit(types.multinumber);              // which inherits types.multi
types.multi.tostr = function(attrs,val){ … ftype(attrs.c[0]).tostr(attrs.c[0], v) … }
```

So the list's own type name says nothing about how an element reads — the unnamed element child
(`c:[{type:'ipaddr'}]`) does, and `types.multi.tostr` hands every element to that type's own `tostr`.
Three consequences, each measured against the API on 7.23.2:

| path | field | native was | the API says |
|---|---|---|---|
| `/ppp/profile`, `/ip/hotspot/user/profile` | `address-list` | `[]` | *(empty)* |
| `/tool/romon`, `/tool/romon/port` | `secrets` | `[]` | *(empty)* |
| `/system/ntp/server` | `broadcast-addresses` | `[]` | *(empty)* |
| `/ip/dns` | `dynamic-servers` | `17082560,3445500682,…` | `192.168.4.1,10.43.94.205,…` |

The last one is a plain `multi` whose child is `addr`, so each element is a compound submessage and
renders through `types.addr.tostr` — the generic "first member of the submessage" fallback gave the raw
u32 instead.

One thing the family does **not** share: a `multistring` element's `values:{type:'dynamic',path:[…]}` is
the dropdown's SOURCE, not its wire form. The element already carries text, so it must not go through
the id→name reference resolution that a `multinumber` of ids (the log's `topics`) needs. `multibits` is
also excluded — it inherits `types.multi` directly and is a bitmask, not a list.

### 26.2c A `macaddr` arrives as hex text, not as bytes

`M2Message` renders an `FT_RAW` value as unseparated uppercase hex (`00155D041F03`), never as a byte
array — so the `macaddr` decoder, which only handled `byte[]`, had a case no live value ever reached and
every MAC fell through to its raw text. `/interface/ethernet`, `/ip/arp`, `/ip/neighbor` and `/tool/romon`
all reported one 12-digit run where RouterOS prints `00:15:5D:04:1F:03`. The `.jg` types all four
`macaddr` correctly; the catalog was never the problem, which is why this looked like four separate path
defects in the audit and was one decoder.

The regrouping keys on the value actually being hex, so a field that answers with something else
(`not-available`) is reported as it came rather than sliced into pairs.

### 26.2d An `interval` is a duration, and it may be scaled — an `age` is not a duration at all

```js
types.interval.tostr = function(attrs,val){
    val ??= attrs.def||0;
    return enum2string(attrs.values,val) || interval2string(val, attrs.scale||1); };
```

So an `interval` is a count of **1/`scale`-second** units, and a value the field NAMES wins before any
formatting happens. Both halves mattered: `/system/watchdog`'s 'Ping Start After Boot' declares
`scale:100`, so its 30000 is 300 s — read as raw seconds it would have said `8h20m`, a plausible-looking
wrong answer rather than an obviously raw one.

What we render is the **API's** duration text (`1w`, `5m`, `2d17h30m3s`, `0s` — non-zero units only,
largest first, no separators), not webfig's `interval2string`, which produces `1d 02:03:04` for the UI.
Zero is `0s` and not the empty string: the field has a value, and empty would read as "not set".

Closed by this: `cache-max-ttl`, `interim-update` (three paths), `group-key-update`,
`ping-start-after-boot`, and `/ip/ipsec/proposal` `lifetime` (`1800` → `30m`).

**Writing one needs the inverse, and did not have it.** `WinboxFieldResolver.EncodeField` had no
`interval` case, so a duration fell through to the generic `u32` branch, failed `long.TryParse` on "5m",
and went out as the **string** `"5m"` on a numeric key — which the router accepts, answers with status 0,
and ignores. A write that reported success and changed nothing, on every interval field. The encoder now
mirrors this section: a named value first, then the duration, times `scale`.

RouterOS spells a duration three ways and accepts all three, so all three are read back in: the unit form
(`5m`, `1w2d3h`, `500ms`), the **clock form** (`00:05:00`, `1:00:00`, with an optional `1d ` prefix and a
`.500` fraction) — which is what `/system/scheduler` prints for `interval` and `/ip/hotspot/user` for
`limit-uptime`, so an encoder that only knew the unit form would refuse the router's own spelling — and a
bare number, meaning seconds and scaled like anything else. Anything else raises
`WinboxFieldResolutionException` rather than being sent as text; a two-part clock (`05:00`) is refused
too, because five minutes and five hours are equally plausible readings of it.

Measured live: `/ip/hotspot/profile` `radius-interim-update=7m` over `WinboxNative` goes out as
`0x8F=u32:420` (was `0x8F=str:7m`) and the API reads back `7m`.

**`type:'age'` is a different thing wearing the same clothes** and is NOT decoded yet:

```js
types.age.tostr = function(attrs,val){ … var uptime = getUptime();
    if (val > 0x7fffffff) val = uptime + Math.abs(val-0xffffffff);
    else                  val = Math.abs(val - uptime);
    return interval2string(val,1); };
```

An `age` value is a timestamp on the router's **uptime clock**, so rendering it needs the router's
current uptime — it is not a duration that can be formatted from the record alone. Measured before the
JS was read and agreeing with it exactly: `/certificate` `expires-after` and `/ip/dhcp-client`
`expires-after` both read **89828 s** higher than the API on the same audit run, while uptime at that
moment was 89828 s. Two paths still carry this in `KnownValueGaps`.

### 26.2e Sentinels: three different reasons one number read raw

The audit filed six paths under "sentinel". They were three unrelated defects and one genuine table.

**A u32 enum member above `int.MaxValue` was unreachable.** `0xFFFFFFFF` is a real member on several maps
(`passthrough` on `eap-methods`, `all` on traffic-flow `interfaces`). The catalog held it — the parser
reads the key as a `long` and casts unchecked — but the LIST lookup parsed the wire element with
`int.TryParse`, which `4294967295` fails, so the lookup never ran. The scalar path had always done it
correctly; only the list path disagreed with itself.

**A map four wrappers deep was cut off by the depth guard.** `/ip/traffic-flow` 'Interfaces' is
`enm → pair → pair → static`, and each wrapper costs two levels (the node, then the list under its `c`) —
nine levels down. The guard stopped at 5, so the field had no map at all. It is 10 now; it exists to stop
a pathological catalog, not to bound legitimate nesting.

**A `union` node's `opt` never reached the field.** `/system/watchdog` 'Watch Address' carries `opt:1` on
the union, and `AddUnionField` read `ro`/`maskid`/`range`/`allow` from it but not `opt`, so the field
looked mandatory and none of the "not set" rules could fire on it.

**And a small table of words RouterOS spells and the `.jg` does not.** `MRRU` declares `min:1500` with
`def:0`, so its zero is outside its own domain: there is no member to map and no sentinel `def` to
recognise, yet the API prints `mrru=disabled`. Four fields need this, and every word is the router's own —
three come from its tab completion, which completes each to exactly one value:

```
/interface/l2tp-server/server set mrru=          →  mrru=disabled
/interface/l2tp-server/server set max-sessions=  →  max-sessions=unlimited
/ip/cloud set ddns-update-interval=              →  ddns-update-interval=none
```

`watch-address` completes to nothing (it is a free-form address), so its word comes from the API's own
print of the row the wire carries as `0.0.0.0`. The table is keyed by FIELD NAME, not by path — an `mrru`
means the same thing on all four PPP server menus — and is gated on the field being `opt` and the value
being exactly zero, so a real count is never rewritten.

### 26.2f Epochs and offsets — and why the set ORDER is not fixable the same way

`dateandtime` and `clockdate` are Unix epoch seconds in **UTC**, not local time: webfig's `date2string` is
`new Date(val*1000).toISOString().substring(0,10)`, and the live values agree with the API to the second
(a certificate's `1784975092` **is** `2026-07-25 10:24:52`). `clockdate` is the same value printed as a
date alone. `timezone` is a signed second offset rendered `±HH:MM` — the wire carries it unsigned, so a
negative offset arrives wrapped and must be unwrapped before the sign is taken.

**The `set` ORDER is per field, and measured.** A bitmask decodes here by ascending bit index, and
RouterOS prints its own order — which for two fields is exactly the reverse:

| field | `.jg` bit map | the API prints |
|---|---|---|
| `authentication` (3 PPP server menus) | 1 mschap2, 2 mschap1, 3 chap, 4 pap | `pap,chap,mschap1,mschap2` |
| `dh-group` (`/ip/ipsec/profile`) | 1 modp768, 2 modp1024, 14 modp2048, 19 ecp256, 22 x25519 | `x25519,ecp256,modp2048,modp1024,modp768` |

Two questions had to be answered before anything could be done about it.

*Is the order even information the wire carries?* Yes: writing `authentication=mschap2,pap` reads back as
`pap,mschap2`, so RouterOS **normalises** rather than echoing insertion order. Had it echoed, a bitmask
could not have carried the order at all and nothing would have been recoverable.

*Is "the API prints descending" the rule?* No, and this is the trap. A firewall rule written as
`connection-state=new,untracked,invalid,related,established` prints back `invalid,established,related,new,untracked`
— bits 0,1,2,3,8, strictly **ascending**, which is also webfig's own order (`types.set.tostr` loops
`i = 0..31`). Flipping every set field would have broken every one that already agreed.

Nor is the direction derivable. WinBox and RouterOS simply number these two fields in opposite directions
(WinBox lists the strongest first, RouterOS the weakest), and nothing in the `.jg`, in `master*.js` or in tab
completion says which — completion answers `chap mschap1 mschap2 pap`, alphabetical, matching neither.

So the direction is a two-entry table measured against the router, one field at a time, and a field that is
not listed keeps ascending. A wrong entry can only affect the field it names, and the path-map audit compares
every one of them against the API on every run.

**A side effect worth its own note.** The parenthesised suffix in a DH group label is the **DH group
number**, which merely *coincides* with the bit key for the classic MODP groups. The suffix is therefore
always dropped, never only when the number equals the key: `x25519 (31)` sits at bit 22, and a
key-equality rule would leave it reading as `x25519-(31)`. A sweep of the whole 7.23.2 catalog finds
exactly twelve labels of this shape and every one is a DH group, so no narrower rule is needed.

### 26.2g An `age` is a timestamp on the uptime clock

```js
types.age.tostr = function(attrs,val){ … var uptime = getUptime();
    if (val > 0x7fffffff) val = uptime + Math.abs(val-0xffffffff);
    else                  val = Math.abs(val - uptime);
    return interval2string(val,1); };
function getUptime(){ return getNow() + sysres.uptimediff; }
```

So an `age` is not a duration but a point on the router's uptime clock, and the duration is its distance from
now. Measured before the JS was read and agreeing with it exactly: `/certificate` and `/ip/dhcp-client`
`expires-after` both read **89828 s** higher than the API on one audit run, with uptime 89828 s at that moment.

The uptime itself comes from where webfig gets it — `fetchBoardInfo` posts a get-singleton to handler
**`[24,2]`** and reads **`u1`** — and is cached per connection and advanced by the local clock, which is
`sysres.uptimediff` under another name. That is not an optimisation: an `age` appears on many rows of one
table, and a round trip per row would make a certificate list N+1 requests. When the singleton cannot be read
the field keeps its raw value; a duration computed from a guessed origin would look entirely plausible and be
wrong by the router's uptime.

`/ip/dhcp-client` `expires-after` now agrees with the API **to within one second** — the gap between the two
reads — so it joins the counters in the audit's volatile list rather than the gap table.

### 26.2h Which numbers are signed — and it is not "all of them"

The wire is unsigned u32 throughout. Two UI types reinterpret the top half as negative, and webfig names
them precisely:

```js
function num2int(v){ return v >= 0x80000000 ? v - 0x100000000 : v; }
types.integer.get      = function(attrs,obj){ … return num2int(val); };
types.fixedpoint.tostr = function(attrs,val){ var scale = attrs.scale||1; val = num2int(…); … };
```

So `integer` is signed and the plain `number` it inherits from is **not** — widening the rule to every
number would turn a legitimately large u32 negative. `fixedpoint` is `integer` plus a `scale`, printed as
`floor(|v|/scale)` `.` the remainder padded to the scale's digit count (`fraction2string`).

`/system/ntp/client` `freq-drift` (`scale:1000`) read as `4294925573` and is `-41.723`, which is the API's
value exactly. Its neighbour `system-offset` is an `integer` of whole milliseconds, so it now reads `-21`
where the API says `-21.508`: the sign is right and the fraction is simply not on the wire — an information
difference rather than a decode gap, on a value that drifts continuously anyway.

### 26.3 "Not set" is an absent field, not an empty one

Three ways the router says a field is not set, and in all of them the API answers by leaving the field
**out of the row**:

- an **`opt`-wrapped** field whose flag bool is `false`. Verified live: a `/ip/proxy/access` rule
  created with only `dst-host` and `action` comes back over the API with no `method`, `src-address`,
  `dst-port` or `path`, while the M2 record carries all of those keys with the flags down.
- the **u32 unset marker** `0xFFFFFFFF` on a field that declares it as its `def`
  (`{name:'Syslog Severity',type:'number',id:'ue',def:4294967295,max:7,…}` — its real domain is 0–7).
- a **static enum carrying a value its own map has no member for**, on a field with the `opt`
  ATTRIBUTE. This is the one webfig states outright, at the end of `types.enm.tostr`:

  ```js
  let str = enum2string(attrs.values, val); if (str != null) return str;
  …
  if (attrs.opt) return '';        // not set
  return 'unknown';                // the catalog and the router disagree
  ```

  So `opt` is what separates "not set" from "we have no name for this". Live on 7.23.2, an **unsigned**
  `/certificate` row carries `Digest Algorithm` (`{type:'enm',id:'u85',opt:1,values:{type:'static',
  map:{4:'md5',64:'sha1',672:'sha256',…}}}`) as `0`, and the API prints no `digest-algorithm` for that
  row; a signed one carries `672` and the API prints `sha256`. Its neighbour `Key Type` is `opt:1` too
  and carries `1` on the same row — mapped, so an ordinary value.

Note that this `opt` is the **attribute**, not the `type:'opt'` **wrapper** of the first rule: an
attribute-optional field has no flag key at all, which is why the two are carried separately
(`WinboxJgField.IsOptional` vs `OptKey`).

Only the marker is treated this way, never "the value equals its default": an IPsec proposal declares
`def:1800` for its lifetime and the API prints `lifetime=30m` for a row carrying exactly that. And a
`def` of `0xFFFFFFFF` that the catalog NAMES stays a value — `/ip/proxy` `max-cache-size=unlimited` is
that number on the wire. Symmetrically, an unmapped enum value on a field that is **not** `opt` keeps
its raw text and is traced on `wbx.codec` rather than dropped: dropping it would hide a stale cached
`.jg` after a RouterOS upgrade.

**Coverage:** `IpProxyTest`, `IpSshTest`, `IpsecProposalTest`, `SystemLoggingActionTest` and
`CertificateTest` on whatever transport is under test; the rules are pinned router-free by
`WinboxEnumAndUnsetDecodeTests`.

---

## 27. A `deck` pane is a kind of record; RouterOS flattens every kind into one record

WinBox shows one window per table and hides the settings that do not apply to the record you selected, in a
`type:'deck'` whose panes are chosen by another field (`selon`). RouterOS has no panes: it puts every
kind's parameters in one flat record and tells them apart by **prefixing the kind**.

```js
{name:'Kind',type:'enm',id:'u15',values:{type:'static',map:['','bfifo','pfifo','red','sfq','pcq',…]}},
{type:'deck',selon:'Kind',panes:[
  {vals:[ 3 ],c:[{name:'RED Queue Size',title:'Queue Size',id:'u12d'},{name:'Burst',id:'u130'},…]},
  {vals:[ 5 ],c:[{name:'Rate',id:'u1f5'},{name:'Queue Size',id:'u1f6'},…]},
  {vals:[ 8 ],c:[{name:'Limit',id:'u2bd'},{name:'Interval',id:'u2be'},…]},     // codel
  {vals:[ 9 ],c:[{name:'Limit',id:'u321'},{name:'Interval',id:'u322'},…]}]}    // fq codel
```

### 27.1 A pane field is filed under both its plain label and its kind-prefixed label

A catalog keyed by label alone can only keep one pane's field under a shared label — the first one
registered. Every later pane's same-named field (codel's and fq-codel's `Limit`, disk's and memory's
`Stop on Full`, …) would then be unreachable, for reading and for writing.

A pane field is therefore filed under **both** its plain label and `<kind>-<label>`, the kind coming
live from the selector's own enum map. The prefix is not doubled when the label already carries it:
WinBox writes 'Remote Port' inside the remote pane and 'PFIFO Queue Size' inside the pfifo one, where
the API says `remote-port` and `pfifo-limit`. Writes work for every pane, everywhere in the catalog,
with no shipped data at all.

### 27.2 Which spelling a READ reports is a per-path table, not derivable from the catalog

`/queue/type` prefixes every pane without exception (`pcq-rate`, `red-burst`, `codel-limit`,
`fq-codel-limit`), but `/system/logging/action` prefixes memory, disk and email and leaves the remote, echo
and script panes alone — the API calls those `src-address`, `syslog-facility`, `remember`, `script`. Both
lists were read off the router with tab completion (`/queue/type add ?`,
`/system/logging/action add ?`); nothing in the `.jg` distinguishes the two cases.

So the reported spelling is a shipped per-path table (`WinboxFieldResolver.PanePrefixedPaths`), defaulting
to "leave the name alone". An unlisted path decodes exactly as it always has — the 7.23.2 catalog has
~70 deck windows and ground truth exists for two. The leaves whose text the API spells differently ride
in the existing per-path alias set — `pcq-limit` ↔ WinBox 'Queue Size', `red-avg-packet` ↔
'Avg. Packet Size', `remember` ↔ the echo pane's 'Save'.

An alias is only shipped when the NAME **and** the VALUE match what the router prints. The remote pane's
'Timestamp Format' looks like the API's `syslog-time-format` and is deliberately left unaliased: the API
reports that field only when the log format is BSD syslog (`on:'timestamp'`, a condition this catalog does
not model) and spells the value `bsd-syslog` where the window's enum says `BSD`. Aliasing it would hand the
mapper a field the API had not reported, carrying a value it could not convert.

### 27.3 A record only carries the fields of its own kind

The router sends **every** pane's keys on every row — a memory logging action's M2 record carries both
'Stop on Full' bools — while the API reports only the live pane's. A field whose pane does not cover the
record's selector value is dropped, which is also what makes `/ip/ipsec/identity` read correctly: its
'My ID' pane covers fqdn/user-fqdn/key-id, so on an `auto` identity neither pane applies and the API's
`my-id=auto` is what the mapper's default produces. A record that does not carry the selector at all keeps
every pane: without the kind there is no honest way to say which one is live.

**Coverage:** `QueueTypeTest` (four methods — including a window labelled 'Type Name' whose API field is
`name`), plus `SystemLoggingActionTest` and `IpsecIdentityTest`; the pane rules are pinned router-free by
`WinboxDeckPaneTests`.

---

## 28. A window owns its field numbering, and a list field is written whole

Two rules that finish the write path for the fields RouterOS has and the catalog could not name or encode.

### 28.1 Two windows on one handler number their fields from scratch

A handler is not a table — it is a *menu entry point*, and several windows may hang off it. `[28,0]` is
both the UPnP settings singleton (`b1` 'Enabled', `b2` 'Allow To Disable External Interface') and the UPnP
interface list (`u1` 'Interface', `u2` 'Type'); `[96,1]` is the web-proxy settings item and its connections
list. Each window numbers its fields from 1, so the same key means different things depending on which
window you are addressing.

Merged into one per-handler map, both names are present — nothing overwrites anything, because
`enabled` and `interface` are different entries — but the map cannot be **inverted**. Asked "what is key
1?", it answers with whichever window the catalog parsed first, and `/ip/upnp/interfaces` read back rows
carrying `enabled` and no `interface` at all (the O/R mapper: "Missing field 'interface'").

Every window's fields are therefore also filed under the window itself, and the resolver consults
**action → window → handler**, most specific first, for both directions:

| Direction | Rule |
|---|---|
| name → field (writes) | the window's entry overwrites the handler's |
| key → name, key → type (reads) | the window's entry is enumerated first and wins |

The per-handler map keeps byte-for-byte the first-wins content it always had, so a path reached by a raw
`PathOverride` — which derives no window — resolves exactly as before. Interface subtype windows are the
one kind deliberately excluded from their handler's map: they disagree about keys under identical labels
('Remote Address' is `0x7E1` on EoIP and `0x7E5` on GRE), so merging them into `[20,0]` would hand every
subtype but the first-parsed one a key belonging to another interface kind (§ the subtype harvest).

### 28.2 A `multinumber` list is one u32[] write, element by element

`types.multinumber` stores a flat `u32[]` in the order given, and its ELEMENTS are exactly what the scalar
encoders already handle: a dynamic dropdown (bridge-vlan `tagged`/`untagged` → interface ids), a static
enum (a log rule's `topics`), or a plain number (`/ip/proxy` `port`). The decode side had read all three
since the reference-list work; the encode side refused the whole family loudly. It now encodes each element
by the same three rules, in the same order, which is what makes the round trip agree.

An element that matches none of the three is an **error**, never a dropped element — a shorter list is one
the router accepts without complaint, so `tagged=ether1,typo` would tag `ether1` alone and report success.
A shape with no encoder still refuses loudly rather than sending a wrong-typed scalar (§32.4).

### 28.3 A list carries its present-flag on the node, and a tri-state list rides on two keys

Two attributes of a `.jg` list node that nothing read, both of which decide whether the write lands at all.

**`optid`** is the present-flag, and webfig writes it from the list's LENGTH
(`types.multi.put`: `obj[attrs.optid] = val.length>0`). It is the same thing an enclosing `opt` wrapper's
bool is, spelled on the node instead — so it feeds the same `OptKey`, and the encoder that already emits
that flag needs no new case. 21 firewall/bridge match lists carry it (`dst-port`, `protocol`, `vlan-id`,
`ttl`, …); without it those writes reached the router with the option down and were ignored, silently.

**`oid`** is the second half of a `multitristatearray` — today only `/system/logging` `topics`.
`types.multitristatearray.put` splits ONE API list into two arrays by each element's negation flag: the
plain members go to `id`, the negated ones to `oid`. So `topics=info,!debug` is
`{U1:[info], U2:[debug]}`, and the API's leading `!` is a different **key**, not a value prefix — which is
what distinguishes it from the `not` flag on a scalar. Both arrays are always sent, empty included, since
that is the only way to clear the other half.

Get the split wrong and it shows immediately: measured on 7.23.2, `/system/logging` reads back
`topics = [1]` where the API prints `info`, and `/system/logging add` is refused outright.

### 28.4 A field WinBox does not have cannot be written, and that is the honest answer

Not every gap is ours. `/routing/ospf/instance` `use-dn` is a real API field (`/routing/ospf/instance add ?`
lists it) that appears in no WinBox window, so it has no M2 key and native refuses it by name. Verified the
other way round before accepting that: `ft-preserve-vlanid` and `radius-accounting` *looked* API-only and
were not — WinBox calls them 'FT Preserve VLAN ID' (one hyphen apart) and plain 'Accounting' (the RADIUS tab
drops the prefix the API spells out), and both are now aliased.

What made `use-dn` surface at all is a separate defect in the O/R mapper, not in this transport: a
non-nullable `bool` whose `DefaultValue` is `"yes"` never equals its own CLR default, so the mapper sends
`use-dn=no` on **every** add — over every transport — whether or not the caller touched it. `/routing/bgp/advertisements`
is the third shape: WinBox exposes it as the BGP session window's `dump-adv` action, not as a table, so it
stays unmapped by design (the path-map audit records it as NO-WINDOW).

**Coverage:** `IpUpnpTest`, `WifiSecurityTest`, `HotspotServerTest`, `OspfAreaTest`/`OspfInstanceTest`/
`OspfInterfaceTemplateTest`, `SystemLoggingTest` and `BridgeVlanTest`; the window-scope and list-write
rules are pinned router-free by `WinboxWindowScopeAndListWriteTests`.

---

## 29. A filtered read fetches the whole table — and a timeout has to say which end is quiet

Native has no server-side query: `RunPrintCore` reads every row the handler has and applies the `?name=value`
filters **in memory**. That is not a shortcut — webfig filters on the client too (`types.def.matcher`,
`types.number.filters` build the UI's own predicates) — but it has a consequence worth stating plainly:
`/ip/firewall/connection/print ?src-address=…` transfers the entire connection-tracking table over M2
before a single row is discarded, where the binary API returns only the matches.

That is the whole of the intermittent `TimeoutException` on `IpFirewallTest.ConnectionList_DirectCall_WillNotFail`
(2026-08-16): the read is proportional to the table, not to the answer, so it fails as the eighth transport of
a full matrix and passes minutes later on a rested router — which is what the shared
[throughput ceiling](findings-router-throughput-ceiling.md) predicts and what a broken path would not. The two
compound: a big table over a channel already at the ceiling.

**A timeout must carry what the other side said**, so it now reports both halves separately, because they want
opposite fixes:

- the multiplexer says what the CHANNEL has been doing — `No frame at all has arrived on this channel since it
  opened` (a dead connection) versus `The channel has read 412 frame(s), the last 30 ms ago, with 1 request(s)
  waiting` (alive, and this request is simply outstanding);
- the `getall` cursor loop adds how much of the TABLE arrived — `timed out after 30000 ms with 8400 row(s) from
  3 completed page(s)`, which separates "this table is bigger than the deadline" from "the request never got
  going".

**There is no page-size knob, and `maxObjs` is not one.** `ufe0018` reads like one and the `.jg` says
otherwise: only three windows declare `maxobjs` (routes, connections, proxy cache) and each pairs it with a
`maxobjsmsg` — "There are too many records to show them all" — so it is a **cap the router refuses past**,
not a page boundary. Asked directly (RouterOS 7.23.2, `/log` = handler `[3,4]`, ~1000 rows), values of
0/10/50/200/10000 all produced the identical five pages of 208/201/209/205/177: the router picks the page
size and ignores `ufe0018` for it. `tik4net.integrationtests/Protocols/Tests/G2MaxObjsProbeTest.cs` is the
probe.

So the read stays proportional to the table, and the only honest client-side move is to **fail rather than
truncate**. The cursor loop is bounded by the connection's `ReceiveTimeout` for the whole read (each page
gets what is left of it) and throws when the budget runs out, naming how many rows from how many completed
pages are being discarded. It used to stop silently at a hidden 8-second budget and 256 rounds and return
the pages that fit — a short list is indistinguishable from a router that has that many rows, which is the
one outcome worse than the timeout. For a table this size on a busy channel, raise `ReceiveTimeout` or read
it over a transport that can filter router-side.

## 30. One M2 key can carry two fields, and a list element can be a compound

Six decode rules, each read off `master*.js` or measured against the router's own API, that between them
closed the last of the value-level disagreements the path-map audit was carrying. Audit after:
`OK=148 KNOWN-GAP=7 MISMATCH=0 VALUE-DIFF=0 UNMAPPED=0`.

### One key, two fields — told apart by the wire type

`/ip/dhcp-client`'s window declares **`u12`** ('Add Default Route', a scalar enum) and **`U12`** ('DHCP
Options', a `multinumber` of references into `[43,5]`). Same M2 key `0x12`; only the TLV type separates them.

The router really does send both. A duplicate-tolerant TLV dump of one getall reply on 7.23.2:

```
0x12  u32[]  [4294967286,4294967285]      ← DHCP Options: the built-in hostname/clientid rows
0x12  u8     1                            ← Add Default Route: 'yes'
```

`M2Message.ParseAllFields` keys a record by its M2 key and was first-wins, so the second of the two was
dropped **at parse time** — `add-default-route` never reached the decoder at all, and `dhcp-options` inherited
its name. A duplicate whose ARRAYNESS differs from the one already stored is now filed under an
arrayness-qualified key (`WinboxM2Protocol.TypedKey`: bit 24 for an array, bit 25 for a scalar, above the
24-bit wire key space), and `WinboxFieldResolver` registers both spellings for exactly the keys two `.jg`
fields contest. Decode asks for the qualified key first and falls back to the plain one, so a window with no
such collision — nearly all of them — builds the map it always built, and the answer does not depend on which
of the two the router happens to send first. A duplicate of the SAME arrayness is still first-wins: that is
the catalog's own aliasing (`/system/resource` has 'freq' and 'CPU Frequency' at `u5`).

A sweep of the 7.23.2 catalog finds this shape in a handful of windows; the rest are cross-WINDOW collisions,
which the window scoping of §28 already resolves.

### A name belongs to the most specific window that claims it

`NTP Client` and `NTP Server` are two `type:'item'` windows on the SAME handler `[47,1]`, and each has an
'Enabled' — `b4` and `b6`. The singleton record carries both. Window scoping named `0x6` correctly for
`/system/ntp/server`, but the HANDLER map still named `0x4` 'enabled' too, and `DecodeRecord` takes the first
name it can use — so the answer depended on the record's field order, and `/system/ntp/server` reported
`enabled=true` where the API says `false`. `BuildKeyToApiName` is now first-wins on the NAME as well as on the
key; because it enumerates action → window → handler, the specific window wins.

### A list element can be a `union` or a `tuple`

`types.multi.tostr` renders each element through the ELEMENT type's own `tostr`, and two element types are
compounds whose parts live under keys of their own:

| Path | Field | `.jg` element | API |
|---|---|---|---|
| `/snmp/community` | `addresses` | `union{network u8/u9, network6 a16/u17}` | `::/0` |
| `/certificate` | `subject-alt-name` | `tuple sep:':' {enm u7f, union{ip6addr a7e, ipaddr u7d, string}}` | `IP:192.168.4.236` |

Both were falling through to the generic nested-message dump, which returns the first member and drops
everything else — `::` for the first, the bare `3959728320` for the second. `WinboxJgField.ElementParts` now
carries the parts, and the codec renders them webfig's way: `types.union.get` with `single:1` takes the first
member the element actually carries, `types.tuple.tostr` joins the parts with the declared `sep` and
contributes no separator for a part that renders empty.

The mask sibling means different things on the two network families, which is why they cannot share a
formatter: `types.network.tostr` runs a NETMASK through `len2netmask`, while `types.network6.tostr` is
`addr + '/' + (val[1]||0)` — the prefix LENGTH itself, and a bare address at 128.

### `multibits` is a `set` under another name

```js
types.multibits.get = function(attrs,obj){ var val=obj[attrs.id]; …
    for(var i=0;i<32;++i){ if(val&(1<<i)) a.push({0:i}); } return a; };
```

It has an `EnumMap`, so it was falling through to the SCALAR enum branch and reading the whole bitmask as one
member: `/ip/neighbor`'s `system-caps` of `0` — no bits, which the API prints as nothing — came back as
`other`, the member the map happens to hold at index 0. It now decodes exactly like `set`.

### A `postfix` is a unit the API puts in the value

webfig does not put `postfix` in `tostr`; the view paints it beside the input box. RouterOS's API has no such
split. `/ip/ipsec/profile`'s `dpd-interval` is an `enm` (`def:8`, `postfix:'s'`) whose only enum member is
`disable-dpd` at 0 and whose `c:[{type:'number'}]` child renders everything else — so 8 read as a bare `8`
where the API prints `8s`. A value that falls through the enum map on a `postfix:'s'` field is now rendered as
a duration. Only `'s'` is acted on: `'min'`, `'PPM'`, `'ms'` and the rest are units the API spells the same way
WinBox does, and appending them would invent text the router never prints.

### Two boxes, one API field: `address:port`

`/ip/hotspot/profile` has 'HTTP Proxy' (`u83`) beside 'HTTP Proxy Port' (`u84`) exactly as it has 'SMTP Server'
(`u87`) beside nothing — and the API prints `http-proxy=0.0.0.0:0` against `smtp-server=0.0.0.0`. Nothing in
the `.jg` says the two boxes are one field, so the pairing is shipped per path
(`WinboxFieldResolver.AddrPortPairs`). The port key rides in the synthetic field's `MaskKey` — the same
"my value needs a sibling" slot a `network`'s netmask uses — and is consumed, so it does not also surface as a
field the API never reports.

### What is left, and why it is not decode work

Three paths still disagree on a value, and on two of them the API is the side that knows less:

- **`/routing/table` `fib`** — a valueless presence flag over the API (`fib=`), which the mapper can only read
  as `false`. Native reads the router's own bool and says `true`. Making native match would be discarding a
  correct answer to reproduce a lossy one.
- **`/system/ntp/client` `system-offset`** — a whole-millisecond `integer` on the wire where the API reports
  fractions (`-23` vs `-23.622`), and it drifts constantly. `freq-drift`, which the wire carries as a
  `fixedpoint`, agrees exactly.
- **`/interface/ethernet` `auto-negotiation`** — WinBox's field is the LINK's live state
  (`not-available` on a CHR's virtual NIC), the API's is the SETTING (`true`). Two fields, one label.

### A normalized label is right until it merges two members

Every `.jg` label is normalized — lowercased, whitespace folded to hyphens, abbreviation dots dropped — and
that is what makes a label match the API's spelling at all (`'as username'` is `as-username`, `'key 0'` is
`key-0`). A handful of enum maps distinguish their members by exactly those characters. A sweep of the whole
7.24 catalog finds three, and in all three the RAW label is what RouterOS prints and accepts:

| map | labels | normalized | why they collide |
|---|---|---|---|
| `/interface/wireless/security-profiles` 'MAC Format' | 14 | 6 | the same seven formats twice, upper then lower — and the case selects how the MAC reaches the RADIUS server |
| `/ip/hotspot/profile` 'MAC Format' | 7 | 6 | `'XX XX XX XX XX XX'` and `'XX-XX-XX-XX-XX-XX'` fold together |
| 'Rate' | 13 | 12 | `'2.5Gbps'` and `'25Gbps'` both fold to `25gbps` — the dot rule. The API prints `rate=1Gbps` |

So a map whose labels would merge keeps them raw; the detection is from the map itself, not a per-field list,
so a new such map on a later RouterOS needs no code change. On the write side an EXACT match now wins and the
case-insensitive comparison is only a fallback: matching insensitively in one pass returns whichever member
comes first, so every lowercase MAC format was written as its uppercase twin — accepted, answered, and a
different setting from the one asked for.

### A tuple is two API fields

`/interface/wireless/security-profiles`' Static Keys tab declares each key as `type:'tuple'` — `'Key 0'` …
`'Key 3'`, `'St. Private Key'` — holding an unnamed `enm` (the algorithm) and an unnamed `secret` (the key),
where RouterOS splits every one into two fields. The record carries them: with `mode=static-keys-required`
and `static-algo-0=40bit-wep` a getall answers `0x7=1` and `0xB=…`. Named per element (u7/sb, u8/sc, u9/sd,
ua/se, u10/s11), with the algorithm's map spelled the way the router spells it — the `.jg` says
`'104 bit wep'`, which normalizes to `104-bit-wep`, a value RouterOS refuses.

One is left, and it is not a decode question at all: `/routing/bgp/advertisements`. WinBox reaches
advertisements only through the BGP session window's *Dump Adv.* action —
`{title:'Dump Adv.',type:'doit',path:[44,33],cmd:9,c:[…,{name:'Save To',type:'string',id:'s2c2035'}]}` —
a command that writes a **file**. There is no window listing advertisements, so there is nothing to
`getall`. The transport says so: a path in `WinboxHandlerMap.NoWinboxWindow` raises
`TikPathNotMappedException` with "has no WinBox window … this is not a mapping gap", instead of the
"add a `PathAlias`" advice that fits a genuine mapping gap and would send a caller looking for a window
that does not exist.

`/system/health` was on that list and is not any more. Its reason said `state` and `state-after-reboot`
were "API-only fields with no WinBox equivalent"; the router sends both. A getall on `[24,14]` answers
`0x8=bool:False 0x9=bool:True` against the API's `state=disabled state-after-reboot=enabled`, and the
pairing was confirmed by setting `state-after-reboot=disabled` and watching `0x9` go True → False while
`0x8` stayed put (`0x8` is the read-only one — `/system/health set` tab-completes to `state-after-reboot`
and nothing else). Neither `[24,14]` window declares those keys — 'Settings' is fan control, 'System
Health' is the x86-gated voltages and temperatures — and the decoder drops keys nothing names, so the path
read as `caps` alone. They are supplied as SYNTHETIC fields carrying key, wire type and enum map, so they
read, resolve and write like catalogued ones. Note the two bools are spelled as words: a mapped value is
now written at the field's own wire type, because a `u32` on a bool key is a request the router accepts,
answers and ignores.

The board-gated sensor half of that window stays genuinely empty on a CHR — as it is over the API.

`/ip/route` and `/interface/wireless/sniffer` were on that list and are not any more (G3.4, G3.5). Both
were the same mistake in two costumes: the path named a window that shares a handler with the one the API
prints, so the read succeeded and answered from the wrong table. The sniffer named the running capture's
statistics rather than its settings; `/ip/route` named the hidden `All Routes` base rather than the IPv4
subtype over it, so it returned the IPv6 routes as well and carried only the columns the list view
sketches. What `/ip/route` still does not read is named rather than left to be rediscovered:
`immediate-gw` (the 'Immediate Gateway' hyperlink `u21`, an unresolved handle into `[44,16]`), `dynamic`,
and the `dhcp`/`connect` source flags — for which native reports the same fact under one field,
`belongs-to` (`dhcp` / `connected` / `interface`).

## 31. A field written with the wrong wire type is accepted and ignored

RouterOS reads the **type byte**, not just the value. A `u64` field written in the `u32` form
(ftype 1 instead of 2) is taken, answered with status 0, and dropped — the write reports success and
the value on the router does not move. The value fitting in 32 bits changes nothing; it is the type
that is checked.

That is what made `/queue/simple`'s rate fields look read-only. They are not: the encoder resolved
them to the right keys, produced bytes, and sent the wrong shape.

```
[encode] upload-max-limit -> key=0xD8 value='5000000' wire=u64 ui=bigunit ro=False
```

**The reach, surveyed against the live 7.24 catalog: 356 writable fields are `u64`** — every rate and
limit on `/queue/simple` and `/queue/tree`, the hotspot and PPP transfer limits, interface `speed`,
the accounting counters. None of them could be written over native.

The rest of the encode table was surveyed at the same time, and nothing else has this shape:

| Wire type | Writable fields | Encoder |
|---|---|---|
| `u32` | 7246 | u32 — correct |
| `string` | 2796 | string — correct |
| `bool` | 2461 | bool — correct |
| `u64` | 356 | **was u32; fixed** |
| `raw` / `addr` / `ip6` | 287 / 181 / 82 | typed encoders — correct |
| `u32[]`, `addr[]`, `string[]`, `raw[]`, `ip6[]` | 506 / 344 / 123 / 17 / 2 | array encoders — see §32 |
| `?` (unknown prefix) | 48 | all `multibignumber` byte/packet counters; nothing writes them |
| `dur`, `time`, `i32` | none | share the u32 branch, which no live field exercises |

The array types were the one remaining gap, and a **loud** one — a write to a list-typed field threw
`WinboxFieldResolutionException` rather than silently doing nothing, which is how `/queue/simple`'s
`target` was found. They are encodable now; §32 has the wire shapes and what still refuses.

**The test that catches a regression here has to read back over a DIFFERENT transport.** A native write
followed by a native read passes on a router that never stored the value, because the failure is in what
was sent, not in what was decoded.

## 32. The list and bitmask shapes, and what each one is on the wire

Everything RouterOS spells as a comma-separated value is one of a handful of shapes, and the `.jg` UI type
says which. They divide into two families that look alike in the API and are nothing alike on the wire.

### 32.1 The array family — one key, one array TLV

Read from `master*.js`'s own writer, not from the shape of a captured frame:

| Wire type | Type byte | Layout |
|---|---|---|
| `bool[]` | `0x80` | count(2B) + 1 byte per element |
| `u32[]` | `0x88` | count(2B) + 4 bytes per element |
| `u64[]` | `0x90` | count(2B) + 8 bytes per element |
| `ip6[]` (FT_ADDR6_ARRAY) | `0x98` | count(2B) + **16 fixed bytes**, no per-element length |
| `string[]` | `0xA0` | count(2B) + per element: length(2B) + UTF-8 |
| `addr[]` (FT_MESSAGE_ARRAY) | `0xA8` | count(2B) + per element: length(2B) + a whole `'M2'` submessage |
| `raw[]` | `0xB0` | count(2B) + per element: length(2B) + bytes |

The count and the element lengths share one width, taken from the type's size flags — 1 byte for the short
form, 2 for the normal one, 4 for the long one. (webfig's `raw[]` *writer* has the two the wrong way round;
its reader agrees with every other array, and the reader is what RouterOS agrees with.)

What an ELEMENT means is the element type's business, never the list's: `types.multistring`,
`multiipaddr`, `multiip6addr` and `multiraw` all `inherit(types.multinumber)` and differ only in what one
element is. So an `ipaddr` element is the same u32 a scalar `ipaddr` is, a `macaddr` element the same six
bytes, and an `addr` element the same nested submessage — the scalar encoders are reused rather than
re-derived, which is what keeps the allow-mask rules and the refusal of an unencodable `%iface` qualifier
in one place.

**An empty list is the empty array, not a dropped field.** A key the router is not told about keeps
whatever it already holds, so a clear that sends nothing reports success and changes nothing.

### 32.1.1 A message array's element is a whole submessage, and it has four shapes

`addr[]` in the prefix table is `FT_MESSAGE_ARRAY` — every element is its own `'M2'` message — and what
that message holds is what the `.jg` says the element type is:

| Element | Shape | Live field |
|---|---|---|
| `addr` | the `addr` compound, one sub-key per address FORM | `/ip/dns` `servers` |
| one leaf | the value at the leaf's own key | `/snmp` `trap-interfaces` (one interface id) |
| `union` | the same, with a key per FAMILY; the element carries exactly one | `/queue/simple` `target` |
| `tuple` | the parts in `.jg` order, each at its own key, joined in the text by `sep` | a switch port's `priority-to-queue`, `3:1` |

A part is encoded by the rules its scalar counterpart uses, in the same order: a static map, then a
dropdown lookup, then the wire type. A `union` tries its families in `.jg` order and takes the first that
can hold the value — the mirror of `types.union.get`, which reads back the first one present. A `tuple`
may arrive with FEWER pieces than it has parts (`types.tuple.tostr` omits a part that renders empty) but
never more.

A part's map matters as much as the field's: `/snmp`'s trap-interfaces is one dropdown per element, and
without resolving it the field read as `2` where the API prints `ether1`.

**A wrapper is not a value.** `{type:'not',id:'b1',c:[…]}` carries an id of its own, so a rule that reads
"the element has an id, that is where the value goes" writes the caller's number into the NEGATION FLAG
and drops the value — which is the shape of P2.33, a firewall address that reached the router as a rule
matching everything. The wrapper is therefore UNWRAPPED — the inner node is the element, and the wrapper's
id is remembered as a flag key — and a single leaf is only taken as the value when its type is on a
**whitelist** of value types.

### 32.1.2 A `not`-wrapped element negates one entry, and the flag rides inside it

`types.not.tostr` renders the element as `(flag ? '!' : '') + inner.tostr(…)`, so the flag sits in the
element's own submessage beside the value. That is a third, distinct place a `!` can live, and the three
are not interchangeable:

| Shape | Where the negation is | Scope |
|---|---|---|
| a scalar's `NotKey` | a sibling bool of the field | the whole value |
| `multitristate` / `multitristatearray` | a second KEY (`maskid`) or a second ARRAY (`oid`) | one member |
| a `not`-wrapped element | a bool INSIDE the element's submessage | one element |

`/tool/sniffer`'s filters are the live family — seventeen fields, on hardware as ordinary as a CHR.
`filter-ip-address="!192.168.251.0/24,10.0.0.1/32"` is one message array of two elements carrying
`{b1=true,u2,u3}` and `{b1=false,u2,u3}`. The flag is written **both ways round, never only when true**: a
key the router is not told about keeps what it holds, so an omitted `false` leaves a stale `!` on an
element being rewritten as plain — and `false` is what the router itself sends back.

The inner leaf can be any value type, including a `macnetwork` (six address bytes at `id`, six mask bytes
at `maskid`). RouterOS prints that mask even when it is the all-ones one —
`filter-mac-address` reads back `AA:BB:CC:DD:EE:FF/FF:FF:FF:FF:FF:FF` — where webfig's own `tostr` hides
it; the API's text is what this codec answers in.

### 32.1.3 `multinetwork` is a list of PAIRS, and where the halves live is the field's business

`types.multinetwork` inherits `types.multinumberrange`, and `types.multimacnetwork` inherits it in turn:

| The field | Layout |
|---|---|
| has a `maskid` | two PARALLEL arrays, one entry each per element (`id[i]`, `maskid[i]`) |
| has none | one FLATTENED array, `[a0,b0,a1,b1,…]` |

What the second half MEANS is the ELEMENT's business, exactly as on a scalar: the range END when the
element declares `range:1`, a netmask otherwise (`types.network.tostr` reads `attrs.range`).

`/ip/pool`'s `ranges` is the flattened, ranged form. A pool of
`192.168.251.10-192.168.251.20,192.168.252.5,192.168.253.0/24` arrives as six u32s, and the last pair is
`192.168.253.0`–`192.168.253.255`: **the router expands a prefix into a range and the API re-collapses an
exactly-aligned one on the way back**, which is why the text round-trips even though the wire form does
not remember which way it was written.

The parallel mask array is not a field of its own — it holds the second half of every element of the one
list the API prints — so it is consumed by its owner rather than reported beside it.

### 32.1.4 Two fields of one window can share a label, and the tab is what tells them apart

The packet sniffer has a streaming `Port` (a number) and a filter `Port` (a list of port matches). The
catalog is first-wins per handler, so the second was reachable under **no name at all** — not a wrong key,
no key. The tab it sits under is what distinguishes them, and is what RouterOS spells the second with:
`filter-port`. The qualified name is registered only for the LOSER of a collision, so no name that resolved
before changes meaning. (This is the same rule the deck panes already use for `memory-stop-on-full` vs
`disk-stop-on-full`, one level further down the window.)

### 32.2 The bitmask family — one number, sometimes two

| UI type | Members from | Negation |
|---|---|---|
| `set` | a static `.jg` map, or a TABLE (`values:{type:'dynamic',path:[…]}`) | `maskid`, when the field has one |
| `multibits` | a static `.jg` map | none in the live catalog |
| `multitristate` | the ELEMENT type's map (`c:[{type:'tristate',values:…}]`) | `maskid`, always |
| `multitristatearray` | the element type | a second ARRAY key (`oid`) |

The bit INDEX is the member's numeric identity: the map key for a static map, and the referenced row's
`.id` for a table-backed one (webfig's `SetView` keys its checkboxes by `obj.ufe0001` and ORs `1<<id`).

A tri-state's members are declared on the element, not on the field, so a catalog that stops at the field
finds no map at all and the whole bitmask reaches the caller as a bare number — `tcp-flags` read as `2`
where the API prints `syn`.

**Order on the way out:** the router prints the plain members first and the negated ones after, each in bit
order. `tcp-flags=!fin,syn,!urg,ack` reads back `syn,ack,!fin,!urg` (7.24).

**Order on the way in — the mask is what the write DENIES, not what it omits.** Measured on 7.24, both
directions: `/user/group set policy=read,write,winbox` on a group that already had `test` keeps `test`, and
an `add` with `policy=read,test` comes out with every other member denied because the ROUTER fills the rest
in. So a write sends the members it names — grants in the value key, `!`-members in the mask — and nothing
else. WinBox's own editor always sends the complete checkbox state instead, because it always has it; doing
the same from a client turns `policy=read,write` into a rewrite of all seventeen permissions.

A table-backed set that cannot read its member table **refuses**. Every token would miss, and the field
would go out as a clean, well-formed zero — a write that reports success and leaves the group allowed
nothing.

### 32.3 A dropdown's name and a bit set's member name are different fields

A window declares which of its fields is the row's display value (`nameval`). Usually that is the field
called `Name` and the distinction never surfaces. The policy table `[13,3]` is where it does: its `Name` is
the sentence *"read router configuration"* and its `Alias` is the word the API prints, `read`. Bit-set
members are therefore read by `nameval`, kept in their own cache next to the ordinary id → name map of the
same table — six handlers in the 7.24 catalog have a `nameval` that differs from a `Name` field they also
have.

### 32.4 What has no encoder, and why each one is left

Counted over the 7.24 `.jg` set:

| Shape | Fields | Why |
|---|---|---|
| `multibignumber` | 255 | its id prefix `Q` is missing from `WinboxJgCatalog.Prefix`, so the fields are not in the catalog at all — see below |
| `multilinestring` | 27 | not a list: `inherit(types.string)`, overriding only the view. Encodes as the string it is |
| `stringarray` | 4 | all `ro:1` |
| `numbertable` | 3 | a read-only table of named columns, all three on radio hardware |
| `multinetwork6` | 2 | the `multinetwork` shape over `ip6[]`; both fields are traffic-generator templates |
| `gridmultinumber` | 1 | one wireless field |

**The `Q` prefix.** `Q` = `u64[]` is settled ground:
[jg-catalog-format.md](jg-catalog-format.md) has carried it since the 7.17 survey and the 7.24 recount
finds it 257 times. `WinboxJgCatalog.Prefix` has `q` and not `Q`, so `DecodeId` returns nothing for those
ids and the field is dropped at harvest time — it does not become mistyped, it ceases to exist. What the
element is follows from webfig alone (`types.multibignumber = inherit(types.multinumber)`, one u64 each).

What is missing is a way to check the result: **not one of the 257 is observable on a CHR.** They are
interface, LCD, container and wireless statistics; a `/interface/list/member` record created for the
purpose comes back with no `Q` key at all. Registering 257 new fields can change which field an existing
NAME resolves to (first-wins per handler), and nothing on this hardware would show it — so the gap is
recorded rather than closed. The same table also carries `x`/`X`, which appear in no catalog of either
version.

## 33. Row state rides on system keys the catalog never names

A record carries more than the window's fields. Four flags in the `0xFE` namespace describe the ROW rather
than its configuration, and **no `.jg` window in the 7.24 catalog declares any of them** — not under those
names, not under any. A catalog-driven decode therefore cannot name them, and every one of them reached no
caller: `/ip/arp` reported nine fields where the API prints twelve.

| Key | API spelling | Meaning |
|---|---|---|
| `0xFE0007` | `dynamic` | the row was learned or generated by the router |
| `0xFE0008` | `invalid` (`inactive` on `/interface`) | configured, but not in effect |
| `0xFE000D` | `default` (`builtin` on `/interface/list`) | the row ships with the router |

**Each pairing was established by moving it, not by matching names.** One table, one variable, both values
in a single read:

* `/ip/arp` holding two learned rows and one added by hand answers `0xFE0007` True, True, False against the
  API's `dynamic=true,true,false`, with every other key of the three rows identical.
* An `/ip/address` on an interface that is then disabled answers `0xFE0008` True where the row beside it
  answers False, against the API's `invalid`. The same read re-confirms `0xFE0007` on a second table.
* `/interface/list` answers `0xFE000D` True for its four shipped lists and False for one created for the
  test; `/system/logging`, `/system/logging/action` and `/queue/type` answer True for every row the API
  calls `default=true`.

**One flag, two API spellings.** `0xFE000D` is `default` on the tables that ship rows you may edit (queue
types, logging rules and actions, hotspot and IPsec profiles — fourteen paths) and `builtin` on the few
that ship rows you may not. Same for `0xFE0008`: `invalid` on a config table, `inactive` on `/interface`.
The majority spelling is the universal seed and the other is a per-path key alias, so a caller always gets
the word RouterOS itself uses on that path — never both at once, and never one RouterOS does not use there.

**They are state, not configuration, so they resolve for a READ only.** The router computes them and the
API refuses to write one (`unknown parameter dynamic`); a seed that resolved for a write would send an
untyped value at a bool key, which the router accepts, answers, and ignores. Writing one raises a
`WinboxFieldResolutionException` instead. They are also filled in LAST, so a window that declares a field
at one of those keys keeps its own name for it.

### 33.1 What the audit could not see

`WinboxNativePathMapAuditTest` passes a path when native reports at least HALF the API's field names, so
everything between half and all of that vocabulary was invisible in a green tally. Counting it instead of
thresholding it: **82 of 676 API field names (12%) are still not reported by native**, across all 61
field-bearing paths — down from 111 before the row-state keys, and the report now names the missing fields
on the passing lines rather than only on the failing ones.

(The first count of this said 172/737. Sixty-one of those were `.tag`, which is not a router field at all
but the API sentence's own tag word that tik4net writes and only that transport carries — an audit
comparing itself. It is excluded now.)

The largest remaining groups are ordinary naming differences rather than a shape the decode cannot read:
`address` where WinBox says `ip-address` (4 paths), `default` on 4 paths whose records do not carry
`0xFE000D` at all (the API derives it elsewhere), and a long tail of one-path fields.

## Settled questions — do not re-investigate

- **Black-box M2 probing without the webfig source is not the way to recover the CRUD command
  catalog.** Sweeping command numbers against a live handler produces a plausible but wrong model
  (§10) — the webfig client (`master*.js`) hands over the complete command catalog directly and is
  the source of truth for every wire encoding.
- **Streaming monitors are not server push.** RouterOS never sends a monitor row unsolicited; the
  client re-polls on a timer over the ordinary request/reply channel (§20). No async reader or
  dispatch on subscription-id is needed for `type:'query'`/`type:'action'` windows.
