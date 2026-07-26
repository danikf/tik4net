---
name: winbox-native-dev
description: >
  Develop and extend the tik4net WinBoxNative (structured M2) transport: add a new path→handler
  mapping, a new field UI-type encoder/decoder, a singleton/list/range/enum/reference field, an action
  verb or streaming monitor — by cross-referencing the three sources of truth (the version-matched
  `.jg` catalog, the webfig `master*.js` `types.*` wire encodings, and live runtime M2 queries) against
  the C# resolver/codec/ops code. Use whenever a native read returns wrong/empty data or an M2 error
  (0xFE00xx), a field is silently dropped or mis-encoded, you need the M2 handler/key/wire-type for a
  RouterOS path or field, or you're implementing a new WinboxNative feature. For plain config queries
  use the `mikrotik` skill; for the CLI/terminal layer use `mikrotik-cli-probe`; this skill is the
  structured-M2 (nv/jsproxy) layer.
---

# WinBoxNative (M2) feature development

The native transport speaks the webfig **/jsproxy M2 protocol** (TCP 8291, EC-SRP5 + AES) — structured
`getall`/`get-singleton`/`set`/`add`/… , NOT a terminal. To add or fix anything here you triangulate
**three sources of truth** and map them onto the C# layer. **Never guess a wire encoding** — read it
from `master*.js` (`[[feedback_winbox_encoding_from_js]]`).

## The three sources of truth

### 1. `.jg` catalog — handler paths, field keys, wire types, window kind
A `.jg` is a tolerant JS object literal describing every WinBox window. It is the **version-volatile**
source for handler numbers + field keys/types. Format reference: `Docs/jg-catalog-format.md`.

- `path:[a,b]` → M2 handler (SYS_TO `0xFF0001`). `type:'map'` = list (getall), `'item'` = singleton
  (get-singleton), `'query'`/`'action'`+pollcmd = streaming monitor, `'doit'`/`'action'`+cmd = action verb.
- `id:'<prefix><hex>'` → a field: prefix letter = wire type (`u`=u32, `U`=u32[], `s`=string, `S`=string[],
  `b`=bool, `r`=raw, `m`=addr, `x`=u64; uppercase = array), hex = the M2 key.
- `type:'<uitype>'` on the node = the **UI-semantic type** that drives typed encoding (`ipaddr`,
  `network`, `macaddr`, `enm`, `set`, `multinumberrange`, `numberrangelist`, `addr`, …) — more specific
  than the wire type.
- Wrappers: `opt`(present-flag bool) / `not`(negation bool) wrap a value leaf; `maskid` = the netmask/
  range-end sibling key; `values:{type:'dynamic',path:[…]}` = a reference dropdown; `values:{map:…}` =
  a static enum; `nameval:'…'` = which field is the record's display name; `pred:{type:'board',…}` =
  board gating (a menu can have several windows for the same path, one per board class — see Gotchas).

**Where the `.jg` lives**
- Library runtime cache (what `WinboxNativeConnection` actually parses): `%TEMP%/tik4net/<jgVersion>/*.jg`
  (e.g. `3.42rc1`). Delete to force a re-fetch.
- Version-matched testbed copies for grepping offline: `../_notes/WinboxMessage/7.21.4-http/*.jg`,
  `7.17rc3-…/`, `6.45…/` — maintainer-local, outside the repo (MikroTik's files, not redistributed).
  If absent, dump them from a router with `WinboxDumpCatalogTest` or fetch them from webfig.

**Explore it**
```bash
JG="$TEMP/tik4net/3.42rc1"          # or ../_notes/WinboxMessage/7.21.4-http
# find a window's handler + its fields:
python - <<'PY'
s=open(f"{__import__('os').environ['JG']}/roteros.jg",encoding='utf-8',errors='replace').read()
i=s.find("path:[ 16,13 ]"); print(s[i:i+900])   # bridge-vlan window + fields
PY
# quick grep for a field/label or a handler:
grep -oiE ".{0,40}'Dst\. Port'.{0,160}" "$JG/roteros.jg"
grep -oiE "title:'[^']*VLAN[^']*',type:'map',path:\[[ 0-9,]+\]" "$JG/roteros.jg"
# structured catalog (handler→ops→fields), human + JSON:
python Tools/probes/jg_analyze.py "$JG"                       # summary
python Tools/probes/jg_analyze.py detail "$JG" "20,3"         # one HANDLER's windows+fields ([20,3]=fw filter)
python Tools/probes/jg_analyze.py report "$JG" out.txt        # full window+field catalog → file
python Tools/probes/jg_analyze.py "$JG" --json catalog.json
python Tools/probes/jg_analyze.py diff <dirA> <dirB>          # cross-version drift
```

### 2. webfig `master*.js` — the authoritative wire encoding for a UI type
`../_notes/WinboxMessage/webfig/master-d53cd8ec58cb.js` is the webfig client. Every `type:'<x>'` has a
`types.<x>.get/put/fromstr/tostr` that defines EXACTLY how the value rides on the wire. Read these
before implementing any new field type.
```bash
JS=../_notes/WinboxMessage/webfig/master-d53cd8ec58cb.js
python - <<'PY'
s=open("../_notes/WinboxMessage/webfig/master-d53cd8ec58cb.js",encoding='utf-8',errors='replace').read()
for name in ['types.multinumberrange.put','types.numberrange.fromstr','types.addr','types.set.tostr']:
    i=s.find(name+'=function');  print('###',name); print(s[i:i+300] if i>=0 else 'NOT FOUND'); print()
PY
```
Examples already decoded this way: `addr` = nested message `{0xFEFF20:u32}`; `multinumberrange`/
`numberrangelist` = flat `u32[]` of `[lo0,hi0,lo1,hi1,…]`; `set` = bitmask u32 over the `.jg` bit map;
`network` = address u32 + netmask u32 at `maskid`.

### 3. Live runtime — confirm against the actual router (always verify, never assume)
Two complementary tools:

**MCP `mikrotik_call`** (the `mikrotik` skill) — compare the SAME command across transports and dump raw
words. The MCP runs a *separately built* dll, so it reflects the LAST build of `Tools/tik4net.mcp`, not
your uncommitted edits — use it for the API ground truth, not to test your in-progress native change.
```
mikrotik_call host=… command=/system/health/print transport=Api   includeRawTrace=true   # ground truth
mikrotik_call host=… command=/system/health/print transport=WinboxNative includeRawTrace=true  # M2 trace
```

**Raw M2 probe (test project)** — to send arbitrary M2 commands / get-singleton / action cmds and read
hex, OR to trace your in-progress library change end-to-end (the test project rebuilds against your
edits). `tik4net.integrationtests/Protocols/Clients/WinboxM2Client.cs` is a low-level client:
`NativeGetAll`, `NativeGetOne`, `NativeSetRecord`, `ProbeCommandRaw(handler, cmd, ms, extraFields…)`,
`GetSystemInfo()` (board/arch/version). Pattern — a temporary `[TestClass]` (run with
`dotnet test … --filter FullyQualifiedName~YourProbe`, no `[Ignore]`), then **delete it**:
```csharp
using (var c = new WinboxM2Client()) {
    c.Connect(host, 8291); c.Authenticate(host, 8291, user, pass);
    var frames = c.ProbeCommandRaw(new[]{24,14}, 0xFE000D, 4000,   // get-singleton on [24,14]
                                   M2Message.U32Sys(0xFE000C, 0x10000005));
    foreach (var f in frames) Console.WriteLine($"status=0x{M2Message.ParseSysStatus(f):X} {M2Message.Describe(f)}");
}
```
To trace the **real connection's** writes (your encoder output), hook the row events on a live
`WinboxNativeConnection`: `conn.OnWriteRow += (s,e)=>Console.WriteLine("WRITE>> "+e.Word);` — it renders
each M2 request via `M2Message.Describe` (top-level keys `0xKEY=type:value`).

## C# code map (the layer you edit)

| Concern | File | Key symbols |
|---|---|---|
| CRUD dispatch (read/write/monitor/safe-mode), path resolution | `tik4net/WinboxNative/WinboxNativeConnection.cs` | `RunPrintCore`, `RunAdd`, `RunVerb`, `PreferSingletonHealthHandler`, `PathOverride`, `FieldOverride` |
| Raw M2 ops (the verbs) | `tik4net/Winbox/WinboxNativeM2Operations.cs` | `GetAll`, `GetSingleton`, `GetOne`, `Add`, `Set`, `Remove`, `Move`, `InvokeAction`, `StartMonitor`/`PollMonitor`/`CancelMonitor` |
| TLV build/parse + field encoders | `tik4net/Winbox/M2Message.cs` | `BuildM2`, `U32Sys`/`U8Sys`/`BoolSys`/`StringSys`/`RawSys`/`MessageSys`/`U32ArraySys`, `ParseAllFields`, `ParseRecords`, `Describe` |
| Protocol constants (commands, keys, errors) | `tik4net/Winbox/WinboxM2Protocol.cs` | `Command.*`, `RecordKey.*`, `SysKey.*`, `Error.*`, `GetAllFlags` |
| `.jg` parse → catalog | `tik4net/Winbox/WinboxJgCatalog.cs` | `GetHandlerFields`, `GetDerivedPaths`, `IsSingletonHandler`, `FindSingletonHandlerByLeaf`, `GetHandlerActions`, `GetMonitorByHandler`, `HasDynamicFields` |
| One field's metadata | `tik4net/Winbox/WinboxJgField.cs` | `Key`, `WireType`, `UiType`, `ReadOnly`, `EnumMap`, `MaskKey`, `IsRange`, `RefHandler`, `OptKey`, `NotKey` |
| apiName↔key + **typed value ENCODE (writes)** | `tik4net/Winbox/WinboxFieldResolver.cs` | `ResolveKey`, `EncodeField` (the `switch(uiType)` at ~L284), `BuildKeyToApiName` |
| M2 record → API field **DECODE (reads)** | `tik4net/Winbox/WinboxRecordCodec.cs` | `DecodeRecord`, `FormatTyped` (the `switch(jf.UiType)` at ~L81) |
| apiPath→handler aliases (stable text bridge) | `tik4net/Winbox/WinboxHandlerMap.cs` | `ShippedAlias`, `Resolve`, `AddOverride`, `TryResolveSubtypeFilter` |
| friendly-name → M2 .id | `tik4net/Winbox/WinboxIdResolver.cs` | `FindIdByName`, `ResolveReference` |

Design split (keep it): handler NUMBERS + field KEYS come **live from the `.jg`** (version-volatile);
only the **stable text** (apiPath↔menu-label aliases, apiName↔label) is shipped in C#. See
`[[project_winbox_native_resolver]]`, `[[ref_jg_catalog]]`, and `Docs/winbox-native-m2-protocol.md`.

## Workflows

### Add a new PATH (entity not reachable over native → "no M2 handler mapping")
1. Find the window in the `.jg` (`grep`/`jg_analyze detail`); note its `path:[…]` and the menu label.
2. If the menu-label breadcrumb already equals the API leaf, it resolves automatically. Otherwise add a
   shipped alias `apiPath → /menu-label/path` to `WinboxHandlerMap.ShippedAlias` (handler stays live from
   the `.jg`). One-off / experiment: `connection.PathOverride(apiPath, new[]{maj,min})`.
3. Verify: `LoadAll<Entity>()` over WinboxNative returns rows (or 0, not a throw).

### Add / fix a FIELD encoding
1. `jg_analyze detail` or grep the field in the window → get its `id` (key+wire type) and `type` (uitype),
   plus any `opt`/`not`/`maskid`/`values`.
2. If it's an existing uitype, the resolver/codec already handle it. For a **new uitype**: read
   `types.<uitype>.put`/`.get`/`.fromstr` in `master*.js`, then:
   - ENCODE: add a `case "<uitype>"` to `WinboxFieldResolver.EncodeField`'s `switch(uiType)` (use the
     `M2Message` encoders; emit `opt`/`not` flags if wrapped — see Gotcha below).
   - DECODE: add the matching `case` to `WinboxRecordCodec.FormatTyped`'s `switch(jf.UiType)`.
3. Trace the write (`OnWriteRow` / `Describe`) to confirm the exact bytes match what `master.js` produces,
   then test the round-trip live (write over native, read back, assert).

### Add an ACTION verb (e.g. `/x/run`) or a streaming MONITOR
- Actions: a `.jg` `doit`/`action` with `cmd:N` on the handler → `WinboxNativeM2Operations.InvokeAction`,
  dispatched by `RunVerb`'s default case via `WinboxJgCatalog.GetHandlerActions`. Call over
  `ExecuteNonQuery` (no result set).
- Monitors: a `type:'query'` (or `action`+`pollcmd`) window → `WinboxMonitorSpec` (start/poll/cancel cmds)
  via `GetMonitorByHandler`, driven by `MonitorLoop` (start → poll every `autorefresh` → cancel).

## Gotchas (live-verified — burn these in)

- **Board-gated windows.** One menu path can have several windows, each `pred:{type:'board',…}`. e.g.
  `/system/health` = `[24,29] map` (non-x86) + `[24,14] item` singleton (x86, get-singleton). The catalog
  ignores preds; the connection picks the singleton variant via `FindSingletonHandlerByLeaf`. If a path
  reads NotImplemented, check whether another window for the same label fits this board.
- **opt/not-wrapped scalars need their flag bool.** A field wrapped `opt→[not→]value` (e.g. firewall
  `protocol`) is IGNORED by the router unless you also send the `opt` present-flag bool (and `not` for a
  leading `!`). `EncodeField` now emits these on the enum-static-map and generic scalar paths; the `set`
  and multinumberrange paths emit their own. Symptom of forgetting: `'ports can be specified if proto is
  tcp,udp,…'` and similar "X requires Y" traps.
- **List/range fields = flat `u32[]`.** `multinumberrange`/`numberrangelist` (vlan-ids, dst-port, dscp,
  pcp) ride as `[lo0,hi0,lo1,hi1,…]` (a bare `n` → `[n,n]`). `multinumber` interface-ref lists
  (tagged/untagged) are NOT yet encoded → the resolver **throws loud** (`WinboxFieldResolutionException`)
  rather than dropping. New list types: implement, don't silently send a string.
- **Read command per window kind:** singleton (`item`) = `GetSingleton` (`0xFE000D`); list (`map`) =
  `GetAll` (`0xFE0004`) + `Flags 0x10000005` (+ stats bit when `HasDynamicFields`). Wrong one → 0xFE0002/3/4.
- **`.id`/SESSION_ID is u8 for ≤255, u32 above** — `M2Message.SessionIdField` auto-switches; a handle can
  exceed 255 (mproxy/monitor). A monitor `.id` can exceed `int.MaxValue` (true u32).
- **`0xA0` str_array trap** in TLV parsing — RouterOS 7.x sends it (e.g. `[msg-proxy-7.21.4]`); the parser
  must skip it or it misaligns (`M2Message.SkipTypeBytes`).
- **Error codes collide with command numbers** — an error only counts in `SysKey.ErrorCode` (`0xFF0008`).
  `0xFE0002`=NotImplemented, `0xFE0004`=ObjectNonexistent (also the getall terminator), `0xFE0006`=often
  "action failed" (RouterOS reuses it for "already have"/"unsupported device type" — match the error TEXT,
  see `WinboxNativeConnection.TranslateM2Error`).
- **`.jg` fetch:** the on-disk file is `<name>.jg.gz` (gzip), served by mproxy `[2,2] cmd=7` (static), NOT
  cmd=3 (/var/pckg, denied on CHR). Also available over plain HTTP `GET /webfig/<name>.jg` with
  `Accept-Encoding: gzip` (else HTTP 406).

## Verify like the rest of the suite
Run the native tests (`tik4net.integrationtests/`, transport via `winboxnative.runsettings`) and the `mikrotik-tests`
skill. A full WinboxNative pass is the regression net for any shared encode/decode change (the throw-loud
guard will surface previously-silent drops as failures — that's the point). Always delete temporary probe
test classes when done.

## References
- Memory: `[[ref_winbox_health_and_lists]]` (health board-gating, list encoding, opt-flag gotcha),
  `[[ref_jg_catalog]]`, `[[project_winbox_native_resolver]]`, `[[feedback_winbox_encoding_from_js]]`,
  `[[project_mcp_multitransport_trace]]`.
- Notes: `Docs/winbox-native-m2-protocol.md`, `Docs/jg-catalog-format.md`, `Docs/README.md`.
- Sibling skills: `mikrotik` (MCP queries), `mikrotik-cli-probe` (terminal layer), `mikrotik-tests`,
  `entity-generator` (scaffold the O/R entity once the native path works).
