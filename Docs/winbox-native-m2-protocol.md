# WinBox native M2 — protocol reference

Durable protocol findings for the `WinboxNative` transport, extracted from the reverse-engineering
work log. Everything here was verified against a live RouterOS 7.21.4 router and/or read out of the
webfig client, and the C# implementation (`tik4net/Winbox/`, `tik4net/WinboxNative/`) cites these
sections by number — the original numbering is preserved for that reason.

Companion documents: [`jg-catalog-format.md`](jg-catalog-format.md) for the `.jg` catalog encoding,
[`findings-winbox.md`](findings-winbox.md) for the transport/session layer, and
[`winbox-m2-multiplexing-design.md`](winbox-m2-multiplexing-design.md) for the channel model.

---

## 0. Klíčový objev (2026-06-07): `.jg` = JS object literal

`.jg` soubory **NEJSOU** binární ani gzip — jsou to **plain-text JavaScript object literály**
(pseudo-JSON), 100 % tisknutelné ASCII. Příklad (`advtool.jg`):

```js
[{name:'IP Scan',title:'IP Scan',group:'Tools',c:[{title:'IP Scan',type:'query',
  path:[ 101,1 ],autorefresh:1000,cancelcmd:2,request:[...],startcmd:1,c:[
  {name:'Address',type:'ipaddr',id:'u1',width:90},
  {name:'MAC Address',type:'macaddr',id:'r2',opt:1,width:120}, ... ]}]}]
```

### Mapování `.jg` → M2 protokol (zásadní)

| `.jg` konstrukt | M2 význam |
|---|---|
| `path:[ 20,0 ]` | **SYS_TO** handler array (`0xFF0001`). `[20,0]`=/interface, `[13,4]`=sysinfo, `[2,2]`=mproxy, `[101,1]`=ip-scan, `[51,1]`=netwatch |
| `cmd:N`, `startcmd:N`, `pollcmd:N`, `cancelcmd:N` | **SYS_CMD** (`0xFF0007`) číslo příkazu. Malá čísla (1,2,3) = per-handler subcmd; velká (`16646160`=`0xFE0010`) = systémové base `0xFE0000` |
| `id:'u1'` | field **key=0x1**, **typ=u32** (`u`) |
| `id:'s10006'` | field **key=0x10006**, **typ=string** (`s`) |
| `id:'b3'` | field **key=0x3**, **typ=bool** (`b`) |
| `id:'r2'` | field **key=0x2**, **typ=raw**/mac (`r`) |
| `id:'ma'` | field **key=0xa**, **typ=addr** (`m`) |
| `id:'S11'` | field **key=0x11**, **typ=string-array** (velké `S`) |
| `id:'U3d'` | field **key=0x3d**, **typ=u32-array** (velké `U`) |
| `type:'map'/'query'/'item'/'doit'/'action'` | druh okna → implikuje default příkazy (list/get/set/add/remove) |

### Prefix → TLV typ (hypotéza, ověřit ve Fázi 2)

| prefix | význam | TLV typ (z memory ref_mikrotik_api) |
|---|---|---|
| `u` | u32 | 0x08 (nebo 0x09 u8 dle velikosti) |
| `s` | string | 0x21 |
| `b` | bool | 0x00/0x01 (bool sys) |
| `r` | raw (mac 6B) | 0x31 |
| `m` | addr (ip/ip6) | raw |
| `a` | ip6addr | raw 16B |
| `U` (velké) | u32 array | 0x88 |
| `S` (velké) | string array | 0xA0 |
| `x`/`q` | u64? | ověřit |

> Velké písmeno = pole (array) varianta téhož typu. Hex suffix se čte jako hexadecimální
> číslo (`fe0010` → 0xFE0010), takže klíče v system namespace (`0xFExxxx`) i user namespace.

---

## 6. Empirické výsledky Fáze 3 (živý router 7.21.4, 2026-06-07)

### Autoritativní field konstanty (tenable/routeros `common/winbox_message.cpp`)

| název | klíč | | error kód | význam |
|---|---|---|---|---|
| `k_sys_to` | `0xFF0001` | | `0xFE0002` | k_not_implemented |
| `k_from` | `0xFF0002` | | `0xFE0004` | k_obj_nonexistant |
| `k_reply_expected` | `0xFF0005` | | `0xFE0009` | k_not_permitted |
| `k_request_id` | `0xFF0006` | | `0xFE000D` | k_timeout |
| `k_command` | `0xFF0007` | | `0xFE0012` | k_busy |
| `k_error_code` | `0xFF0008` | | | |
| `k_error_string` | `0xFF0009` | | | |
| `k_session_id` = **`.id`** | `0xFE0001` | | | |

Builtin commands `0xFE0000–0xFE0016` jsou **systémové** (`0xFE0001`=cmdGetPolicies),
NE objektové CRUD → CRUD jsou per-handler malá čísla.

### Probe výsledky na handleru `[20,0]` (/interface)

- **`cmd=3` (bez id) = getall-ids** ✅ → vrací user key `0x000001` = u32[] všech .id
  (živě 47 ids: `[1,3,10,11,...,77]`). **Nativní read funguje.**
- `cmd=3 + .id` → ignoruje id, vrací vždy celý id-list.
- `cmd=2 + .id` → prázdný ACK bez erroru (pravděpodobně **set**, no-op bez polí).
- `cmd=2` bez id → `0xFE0004` obj_nonexistant (set vyžaduje id).
- `cmd=1,4,5,6,7,8 (+.id i bez)` → `0xFE0009` k_not_permitted (příkazy existují, ale gated/chybí arg).
- Sweep `0x00–0x28` s id jako `.id` i `u1`: **žádný nevrací plný záznam** (Name `0x10006`).

### Otevřená otázka: jak získat plný záznam (fields)?

Model „getall-ids → get-one-by-id" takhle nefunguje. Hypotézy (pro Fázi 3b):
1. **Streaming**: `cmd=3` s `reply_expected=false` → handler streamuje řádky (každý = 1 frame).
2. **Column subscription**: request nese seznam požadovaných field-keys (souvisí s `.jg` `refreshfilter`).
3. **get-one** je příkaz mimo 0x00–0x28, nebo id patří do jiného pole.
Reference k prozkoumání: tenable/routeros `bytheway/src/main.cpp`, „Make It Rain" článek,
subixonfire/winbox-terminal-protocol (auth+session infra).


---

## 10. ✅✅ PRŮLOM (2026-06-09): nativní CRUD vyřešen z webfig master.js

**Zdroj pravdy = `_notes/WinboxMessage/webfig/master-d53cd8ec58cb.js`** (jediný ne-crypto
webfig script, M2 protokol v JS přes HTTP `/jsproxy`). Funkce `msg2buffer`/`buffer2msg`
(serializace), `ObjectMap.getall/fetch/setObject` (CRUD), `subscribe` (push model).
Black-box probing byl slepá ulička — webfig dal kompletní katalog příkazů přímo.

### Tři chyby předchozích pokusů (VŠECHNY opraveny)
1. **Špatný příkaz.** getall = **`0xfe0004`** (webfig default `getallcmd`), NE malé číslo.
   `cmd=3` na `[20,0]` vrací TYPE registry, ne instance. Agenti svépisně označili
   `0xfe0000–0xfe0016` za „systémové, ne CRUD" — to bylo MYLNÉ. CRUD JSOU tato čísla
   (generické defaulty, proto je `.jg` u `type:'map'` neuvádí).
2. **Chybějící flag field.** getall vyžaduje **`ufe000c`** (klíč `0xFE000C`, u32) =
   `0x10000005` (`| refetchonopen | refreshfilter`). Bez něj handler nevrací řádky.
3. **Records = MESSAGE-ARRAY** pod klíčem **`0xFE0002`** (webfig `Mfe0002`, wire type
   **0xA8**). Starý parser neměl case pro 0xA8 a `SkipTypeBytes` default=0 → zastavil se →
   řádky se NIKDY neobjevily. Opraveno: message (0x28/9/A) + message-array (0xA8/9/A)
   v `M2Message.ParseAllFields` + `ParseRecords` + `SkipTypeBytes`.

### Kompletní katalog příkazů (uff0007, z webfig)
| cmd | konstanta | význam | request pole | reply |
|---|---|---|---|---|
| `0xfe0004` | getallcmd | **list all** | `ufe000c`=flags, `ufe0018`=maxobjs, pagin. `ufe0003` | `Mfe0002` records, `ufe0019` count, `ufe0003` cont. token |
| `0xfe0002` | — | **get one** | `ufe0001`=.id | record (v `Mfe0002` nebo top-level) |
| `0xfe0003` | setcmd(map) | **set/change** | `ufe0001`=.id + změněná pole | status |
| `0xfe0005` | — | **add** | pole (bez .id) | `ufe0001`=nové .id |
| `0xfe0006` | — | **remove** | `ufe0001`=.id | status |
| `0xfe0007` | — | **move** (ordered) | `ufe0001`=.id, `ufe0005`=next-id | |
| `0xfe000d` | getcmd | get singleton | `ufe000c`=flags | record |
| `0xfe000e` | setcmd(holder) | set singleton | pole | |
| `0xfe0008` | — | setup/wizard krok | `mfe000f`=obj, `ufe000e`=page | |
| `0xfe0012` | — | **subscribe** (push) | path v `Uff0001` | async push klíč `Uff0002`=path |
| `0xfe0013` | — | unsubscribe | | |

### Klíčová systémová pole (writeId: 3B key LE + 1B type v horním bajtu)
- `Uff0001` (u32[]) = **SYS_TO** = path `[20,0]`. **Velké U = array!**
- `uff0007` (u32) = **SYS_CMD**.  `Sff001c` = trace (webfig, lze vynechat).
- `Uff0002` (u32[]) = SYS_FROM (v push notifikaci = která subscription).
- `uff0008` = error code, `sff0009` = error string.
- `ufe0001` = **.id** (record handle).  `ufe000c` = getall/get **flags**.
- `ufe0018` = maxobjs.  `ufe0003` = getall continuation token.  `ufe0019` = count.
- `Mfe0002` = **records** (message-array).  `ufe0005` = next-id (ordered).
- `ufe0013` = removed flag.  `mfe001d` = default config (`setDefaultConf` cmd `0xfe0004`+`ufe000c=0x20000000`).

### Field-key konvence (z `.jg` id + webfig)
- **comment = `sfe0009`** = string klíč **`0xFE0009`** (webfig `types.comment.get/put`). Potvrzeno živě.
- Name = `s10006` (0x10006).  .id = `ufe0001` (0xFE0001).  type = `u10001`.
- Typ-byte = `(ftype<<3)|sizeFlags`; ftype 5=message, 21=message[]; flags short=0x01 long=0x02;
  délka/count: short=1B, normal=2B, long=4B (webfig `readLen`).

### Wire-format detail (z `msg2buffer`)
- M2 zpráva = `'M2'` + pole. Sub-message (message/message-array element) má TAKÉ `'M2'` prefix.
- Field header (4B): `[key_lo][key_mid][key_hi][typeByte]` — key 24-bit LE, typ+flags v horním bajtu.
- message-array (0xA8): `[2B count][ (2B elemLen + M2-submsg) × count ]`.

### Edit = „uprav vše, ulož" (potvrzeno)
webfig `setObject` pošle CELÝ objekt (`update(req,obj._exportObj||obj)`) + cmd `0xfe0003` +
`ufe0001`=.id. Prakticky stačí poslat .id + jen změněná pole (živě ověřeno: set jen `sfe0009`).

### Živé ověření (router 7.21.4, `WinboxNativeGetallTest.cs`, 3/3 ✅)
- `Native_GetAllInterfaces` — `[20,0]` getall → názvy **shodné s API** (`CollectionAssert`).
- `Native_GetAllIpAddresses` — `[20,1]` getall → 1 record (generický napříč tabulkami).
- `Native_SetAndRestoreEther1Comment` — get-one ether1 (.id=2, `sfe0009`="My comment" = API),
  set `sfe0009`="native-m2-ok" → API potvrdí změnu → restore → API potvrdí. **status=0.**

### Další krok (W5 — produkční resolver)
Katalog-driven `Resolve(path, op) → (handler, cmd, fields)` z `.jg` (W4 fetch hotový) +
`NativeGetAll/GetOne/SetRecord` v `WinboxM2Client`. add/remove/move dle tabulky výše.
Povýšit do `tik4net/Winbox/` (`WinboxNativeM2Session`) — infra (auth, AES, M2) už existuje.

---

## 20. KAPITOLA: Plný streaming monitor pro WinboxNative (zahájeno 2026-06-13)

### Cíl
Nativní podpora **kontinuálního/streamovaného monitoringu** — `.jg` okna typu `type:'query'` s
`autorefresh` + `startcmd`/`pollcmd`/`cancelcmd`, kde router místo jednoho getall **opakovaně pushuje
aktualizované řádky** (torch, ip-scan, netwatch, ethernet monitor s živým rate, traffic-monitor…).
Dnes nativní transport umí jen **once-shot** (getall + filtr, §18) — živé hodnoty (rate,
auto-negotiation) base getall vynechá.

### Co už je hotové (stavební kameny)
- `WinboxM2Protocol.Command`: `Subscribe=0xFE0012`, `Unsubscribe=0xFE0013` + monitor trojice
  `0xFE000F/10/11` (startcmd/pollcmd/cancelcmd — viz §17 pozn.). Konstanty existují.
- `.jg` parser už čte `type:'query'`, harvestuje window path do `_derivedPaths` (query je ve
  `WindowTypes`). Chybí harvest `startcmd/pollcmd/cancelcmd` čísel a `request:[…]` polí.
- M2 session umí poslat request + číst frame; subscribe push model je popsán v §10 (cmd `0xFE0012`,
  async push pod klíčem `Uff0002`=path).

### Otevřené otázky (k prozkoumání z webfig master.js + .jg)
1. **Dva modely**: (a) CRUD `subscribe 0xFE0012` (config-table push, autorefresh okna jako firewall),
   (b) per-handler `startcmd/pollcmd/cancelcmd` (tool okna jako torch/ip-scan z `advtool.jg`).
   Zjistit, které okno používá který — `.jg` to nese (`startcmd:N` přítomen ⇒ model b).
2. Jak tik4net API tohle vystaví? Existuje `ExecuteAsync`(callback) + `LoadAsync<T>` v O/R mapperu
   (binární API streaming, viz `TikCommandTest.ExecuteAsync_OnDoneCallback_Called`). Nativní transport
   musí naplnit `TikConnectionCapability.Listen` / `Streaming`, nebo zůstat unsupported.
3. Vlákno čtení: M2 kanál je dnes request/reply. Push model = async frames bez vyžádání → potřebuje
   reader smyčku + dispatch na request-id/subscription-id.

### ✅ RE HOTOVO (2026-06-13) — KLÍČOVÝ OBJEV: streaming = CLIENT POLLING, ne server push

Ground-truth z webfig `master.js` (`ObjectQuery`, `ObjectAction`, `ObjectMap.getall`) + `.jg` query/action
oken. **Předchozí hypotéza §9 („router pushuje řádky asynchronně, autorefresh:1000") byla MYLNÁ.**
Realita: webfig si řádky **sám opakovaně vyžádá** (timer každých `autorefresh` ms) přes normální
request/reply na témže kanálu. Žádný async server-push reader není potřeba — stávající synchronní
M2 session stačí, monitor jen re-postuje requesty z worker vlákna.

#### `.jg` okno → cmd trojice (vše SYS_CMD `uff0007` na `Uff0001`=path)
| pole v `.jg` | význam | příklad |
|---|---|---|
| `startcmd:N` | spustí monitor → reply nese **`ufe0001`=id** (session handle) | Torch [45,5] `startcmd:1`, IP Scan [101,1] `startcmd:1` |
| `getallcmd:N` (query) / `pollcmd:N` (action) | jeden poll pass; default getall = `0xfe0004` | Monitor Slaves `getallcmd:0xFE0010`, Bandwidth Test `pollcmd:1` |
| `cancelcmd:N` | zastaví monitor (`ufe0001`=id) | Torch `cancelcmd:2`, systémové `0xFE0011`=16646161 |
| `autorefresh:ms` | interval re-pollu (typicky 1000) | |
| `request:[…]` | vstupní parametry (Interface enm, Address Range network, …) | |
| `c:[…]` | výsledné sloupce (řádky decode jako normální getall — `Mfe0002`) | |

Systémová monitor trojice (okna bez vlastních čísel): `0xFE000F`=start, `0xFE0010`=poll/getall,
`0xFE0011`=cancel. Sentinel `0xFFFFFFFF` = „žádný cmd" (`startcmd==0xffffffff && autorefresh==null`
⇒ okno je ve skutečnosti jen one-shot getall, ne stream).

#### Model A — `ObjectQuery` (`type:'query'`: torch, ip-scan, ping, traceroute, profile)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001` = **id**.
2. **poll loop**: `map.getall(id)` = `post({Uff0001=path, uff0007=getallcmd||0xfe0004, ufe000c=0x10000005,
   ufe0018=maxobjs, ufe0001=id})`. Řádky v `rep.Mfe0002` (stejný decode jako běžný getall!), klíčované
   `obj.ufe0001`. **Paginace v rámci passu**: `rep.ufe0003` (continuation token) / `rep.mfe0015` → re-post
   s tím tokenem. **Konec passu**: `rep.uff0008===0xfe0004` (ObjectNonexistent). Po dokončení passu timer
   `autorefresh` ms → další pass.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.

#### Model B — `ObjectAction` (`type:'action'` + `pollcmd`: bandwidth-test, cable-test, ping-akce)
1. **start**: `post({…request, Uff0001=path, uff0007=startcmd})` → reply `ufe0001`=id, `started=true`.
2. **fetch (poll)**: `post({Uff0001=path, uff0007=pollcmd, ufe0001=id})` → reply = **jeden status record**
   (`update(rep)`, ne mapa řádků). Timer `autorefresh` → další fetch.
3. **stop**: `post({Uff0001=path, uff0007=cancelcmd, ufe0001=id})`.
Rozdíl A↔B: A vrací **mapu řádků** (Mfe0002) per pass; B vrací **jeden status** per poll.

#### Společné stop podmínky
- **`bfe000b`** (klíč `0xFE000B`, bool) = „**finished/done**" → ukončit stream (router signalizuje konec, např.
  traceroute dorazil k cíli). webfig: `if(rep.bfe000b){this.stop();return;}`.
- Volající odhlásí (unlisten) → cancel. Chyba (jiná než 0xFE0004 terminátor) → stop.

#### Důsledek pro implementaci (zjednodušení!)
NEpotřebujeme async push reader ani dispatch na subscription-id. Stačí **worker vlákno** s poll smyčkou
na stávajícím request/reply M2 session:
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
Mapuje se 1:1 na tik4net `ExecuteAsync(onReply, onError, onDone)` + `CancelAndJoin()` a kapabilitu
`TikConnectionCapability.Listen`. `subscribe 0xFE0012` (config-table change push) je SAMOSTATNÝ
mechanismus — pro monitor okna se nepoužívá, neřešíme ho teď.

### ✅ ŽIVÝ PoC HOTOVÝ (2026-06-13) — start→poll→cancel ověřen proti routeru
Test `WinboxNativeM2Test.Native_MonitorCycle_Profile` ([Ignore] PoC, run přes `--filter`).
Target = Profile okno **[49]** (CPU profiler — bez závislosti na provozu, na každém routeru):
- **start** = `0xFE000F` + request `u1=0xFFFFFFFD` ("total") → reply status 0, **id v `.id` (0xFE0001) =
  `0xFFFFFFFD`** (Profile echo-uje CPU selector jako id; pozor: u32 > int.MaxValue → nosit jako uint,
  re-enkódovat `SessionIdFieldU32`).
- **poll** = `0xFE0004` (default getall) + `.id` + flags `0x10000005` → řádky pod `Mfe0002` (1–2 CPU
  profile recordy/pass), opakováno á 1000 ms.
- **cancel** = `0xFE0011` + `.id` → status 0.
Výstup: `monitor cycle OK: 4 total rows across 3 passes`. **RE 100% potvrzena živě** — streaming JE
client-polling na request/reply kanálu, žádný push. Tím odpadá dříve domnělý blocker (async reader).

### Návrh implementace (další krok)
1. **Katalog**: `WinboxJgCatalog` harvestovat z `type:'query'/'action'` oken `startcmd/pollcmd/getallcmd/
   cancelcmd/autorefresh` + `request:[…]` pole → nová struktura `WinboxMonitorSpec` keyovaná derived path
   (`/tool/torch`, `/tool/ip-scan`, …). Princip stejný jako `_actionsByHandler` (§17).
2. **Operace**: `WinboxNativeM2Operations.StartMonitor(handler, startcmd, requestFields) → id`,
   `PollMonitor(handler, cmd, id, token) → (rows, nextToken, done)`, `CancelMonitor(handler, cancelcmd, id)`.
3. **Connection**: `WinboxNativeConnection` implementovat async cestu (`RunAsync`/ekvivalent v bázi) —
   worker vlákno s poll smyčkou, callbacky přes existující `ITikCommand.ExecuteAsync`. Nastavit
   `Supports(Listen)=true`.
4. **Capability + testy**: `EthernetMonitorForEth1` (živý rate), torch test napříč transporty
   (`EnsureCapability(Listen)` na CLI transportech skipne). Wiki: aktualizovat „Capability" sekci.

### ✅ HOTOVO (2026-06-14) — streaming monitor + listen + async list, suite 0 fail
Plný `winboxnative` suite: **163 pass / 0 fail / 81 skip** (vše uncommitted, 4.x).

**Architektura** (po feedbacku „RunMonitor v bázi bourá abstrakci + ID musí být uint"):
- opt-in `ITikMonitorTransport` (`TikMonitorHandle.cs`) — NE v neutrální `TikCommandConnectionBase`.
  `TikGenericCommand.ExecuteAsync` routuje přes `is ITikMonitorTransport` (jinak `NotSupported`).
  `WinboxNativeConnection` ho implementuje (`Capabilities = Crud | Listen`); CLI ne.
- Monitor id = **uint** všude (Profile echo 0xFFFFFFFD > int.MaxValue). `M2Message.SessionIdField(uint)`.
- `ExecuteAsync` normalizuje multiline command (`?type=ether\n?#|`) na Filter pole — dřív jen sync cesta.

**Dispatch dle verbu** v `RunMonitorAsync`:
- `listen` → **poll+diff** (`ListenLoop`): getall á 1s, diff dle `.id` přes signaturu **jen config polí**
  (`RowSignature` vynechá ro:1 countery — `ReadOnlyFieldNames` z `.jg`), smazaný `.id` → syntetický
  `.dead=true` record (O/R `LoadListenAsync` → onDeleted). Webfig dělá totéž (polluje config tabulky).
- `print`/`getall` → **async list** (`AsyncListOnce`): RunPrint off-thread, emit řádky, done.
- jinak → **streaming monitor** (`MonitorLoop`): spec z `WinboxJgCatalog.GetMonitorByHandler`, start/poll/cancel
  přes `WinboxNativeM2Operations.{Start,Poll,Cancel}Monitor`. Request pole se enkódují **ve workeru** (ne sync)
  → resolve-fail (neexist. iface) jde async přes `onError` jako API, ne sync throw.
- **Close/Cancel během monitoru = graceful** (`MonitorStopping` = CancelRequested || !IsOpened → polkni error).

**Native query-stack filtry**: `RunPrint` vyhodnocuje `?#|`/`?#&`/`?#!` postfix stack + `?<`/`?>` (ne naivní AND).

**Shipped field aliasy** (nový subsystém `WinboxFieldResolver`, analogie `WinboxHandlerMap.ShippedAlias`):
`ApiToJg`/`JgToApi`/`KeyToApi`/`KeyUiType`, klíčováno apiPath. Jen stabilní text/klíč — typy živě z `.jg`.

**Ping** (`/ping`→[22], query, start `0xFE000F`/cancel `0xFE0011`/poll `0xFE0004`):
- aliasy: address→`ping-to`, count→`packet-count`, size→`packet-size`, min/avg/max-rtt; reply **host=klíč 0x1**
  (u32 ipaddr, v `.jg` bezejmenný → `KeyToApi`+`KeyUiType`).
- **`addr` kompozit** (master.js `types.addr`): nested message (`M2Message.MessageSys`, wire `0x29`) pod 0x16,
  IPv4 jako u32 na sub-klíči **0xFEFF20**. Request pole jdou i ro:1 (`allowReadOnly`).

**Interface `type` label**: `.jg` type = číslo (0x10001); API string ("ether"/"loopback") je v recordu na **0x1001E**
(živě ověřeno + API cross-check). Alias `/interface`: 0x1001E→`type`, 0x10001→`type-id`. **Žádný registry/hardcode.**

**Pozn.**: dvě `M2Message` — knihovní (má `MessageSys`) vs `tik4net.tests/Protocols/_Shared/M2Message.cs` (nemá).
Nové soubory: `TikMonitorHandle.cs`, `WinboxMonitorSpec.cs`.

---

## 21. ✅ Query okno = JEDEN dlouhý getall pass, ne stránkovaný snapshot (P2.45, 2026-07-31)

`type:'query'` okno **nemá `pollcmd`** (ověřeno na celém katalogu: 18 pluginů / 805 oken — *žádné*
query okno pollcmd nenese, mají ho jen `action` okna). Poll je tedy obyčejný `getall` na monitor id,
a jeho odpověď má tvar, který §20 nepopisoval:

> Router odpoví **jedním recordem + continuation tokenem** (`ufe0003`) a **další continuation
> BLOKUJE, dokud nevznikne další record**. Poslední odpověď nese `bfe000b` (Finished) a už žádný token.

Živě změřeno na 7.23.2, `/ping` = handler `[22]`, `count=30`:

```
REQ  cmd=0xFE000F (start)  0x16={0xFEFF20=127.0.0.1}  0x11=30      → reply ufe0001=2  (monitor id)
REQ  cmd=0xFE0004 (getall) ufe0001=2 ufe000c=flags                → 1 record (seq 0) + ufe0003=1
REQ  cmd=0xFE0004          ufe0001=2 ufe000c=flags ufe0003=1      → …+1000 ms… 1 record (seq 1) + ufe0003=2
…
count=3: třetí odpověď nese bfe000b=True a token už ne → konec
```

**Náš defekt (P2.45):** `PollMonitor` běžel pod rozpočtem 4 s / 256 kol, protože byl psaný pro
stránkovaný snapshot. U 30sekundového pingu rozpočet vypršel uprostřed passu, **continuation kurzor
se zahodil**, a další poll poslal `getall` bez tokenu — na to router odpovídá `uff0008=0xFE0004`
(ObjectNonexistent = „žádné další řádky"). Od té chvíle monitor mlčel: bez chyby, bez onDone, 5 řádků
a konec. Řádky navíc chodily **v dávce po 4 s**, ne průběžně.

**Oprava:** `PollMonitorRound` dělá jedno request/reply kolo; pass řídí `MonitorLoop`, kde žije cancel
handle i emit. `continuation != null` ⇒ hned další kolo (bez spánku), `Finished` ⇒ konec, konec passu
bez `Finished` ⇒ počkej `autorefresh` a začni nový pass (to je model snapshot oken jako Torch/Scan).
Žádný časový ani kolový strop — pass končí jen tím, co řekne router, nebo cancelem. Gate se drží
**per kolo**, ne per pass, takže 30sekundový monitor neblokuje CRUD.

**Pozor na dva tvary query okna** — oba jsou `type:'query'` a rozliší se až za běhu:
- **stream** (ping, traceroute, profile): pass běží dlouho, končí `Finished`.
- **snapshot** (torch, scan, ip-scan): pass doběhne hned, `Finished` nepřijde, opakuje se á `autorefresh`.

`action` okna (`pollcmd`) zůstávají beze změny: jedna odpověď = jeden status record, continuation se
u nich záměrně nesleduje (webfig `ObjectAction` taky ne).

---

## 22. ✅ Monitor okno nemá řádky mimo monitor cyklus (P2.51, 2026-08-01)

`RunPrintCore` uměl monitor okno jen asynchronně. Synchronní čtení (`ExecuteList` / `LoadList`)
spadlo do obecného `getall` na handleru monitoru — a ten router odpoví **bez záznamů**:

```
/ping =address=127.0.0.1 =count=2  (WinboxNative, před opravou)
  >> M2 0xFF0001=u32[]:[22] 0xFE000C=u32:268435463        (getall na handler [22])
  << M2 (žádné 0xFE0002 záznamy)
  → volajícímu "OK (no data returned)"                     ← tichá chyba
```

Monitor okno (`.jg` `type:'query'`, resp. `action`+`pollcmd`) **není tabulka**: jeho řádky vznikají
až tím, že klient spustí cyklus. `RunMonitorWindowSync` proto dělá start → poll → cancel na volajícím
vlákně a vrátí, co cyklus vyprodukoval:

- **dokud router nenastaví Finished** — sebeukončující příkaz (`ping count=N`),
- **nebo dokud neskončí první pass** — průběžné okno, jehož pass *je* jeden snímek.

Je to totéž pravidlo, které CLI transporty dostávají z modifikátoru `once`/`count=1`, a shoduje se
s tím, co na stejný příkaz vrátí binární API.

**`once` se na M2 neposílá.** RouterOS ho potřebuje, protože monitor na API i v terminálu jinak běží
donekonečna. WinBox okno takový vstup nemá — „jeden odečet" rozhoduje klient — a pokus zakódovat ho
skončí `WinboxFieldResolutionException` na poli, které volající jako data nikdy nemyslel
(`IsMonitorSnapshotModifier`).

### Co tím ještě nefunguje (změřeno, nezahlazeno)

Nic z původního seznamu — všechny tři body vyřešila kapitola 23 níž. Zůstalo jen tohle:
`/ping` bez `count` (a `/tool/torch` přes `ExecuteList`) běží, dokud ho něco nezastaví; sync čtení
ho proto ohraničuje `ReceiveTimeout` a skončí `TikConnectionReceiveTimeoutException` místo toho, aby
drželo vlákno navždy.

## 23. ✅ `addr` není string a IPv6 je vlastní ftype (P2.52 + P2.53, 2026-08-01)

Tři symptomy zapsané v kapitole 22 jako tři různé mezery měly **dvě společné příčiny**, obě v kodeku,
ne v routeru. Diagnóza začala tím, že se místo odpovědí četly **requesty**:

```
/ping address=127.0.0.1    >> 0x16=msg:{0xFEFF20=16777343}         ← funguje
/ping address=example.com  >> 0x16=str:example.com                  ← router: "no address was specified"
/ping address=2001:db8::1  >> 0x16=str:2001:db8::1                  ← totéž
```

Router tedy nic nehlásil špatně: **odpovídal na náš zmršený dotaz.**

### 23.1 `addr` = compound, každý tvar adresy má vlastní sub-klíč

`master*.js` (`types.addr.fromstr`) zkouší tvary v tomto pořadí a podle masky `allow` z `.jg`:

| tvar | sub-klíč | wire | `allow` |
|---|---|---|---|
| IPv4 | `0xFEFF20` | u32 (octet-LSB) | `4` |
| IPv6 | `0xFEFF21` | **FT_ADDR6** | `6` |
| DNS jméno | `0xFEFF26` | string (**celý** vstup, ne část před oddělovačem) | `D` |
| route distinguisher | `0xFEFF27` | string | `R` |
| MAC | `0xFEFF2F` | raw 6 B | `m` |
| `/len` | `0xFEFF25` | u32 | `/` |
| `%iface` / `@vrf` | `0xFEFF22` / `0xFEFF23` | u32 (id z dropdownu) | `i` / `v` |

Do P2.53 uměl kodek jen IPv4 a na cokoli jiného **spadl na holý string na klíči pole**. Router takový
tvar nečte — chová se, jako by pole nepřišlo. Ping na jméno i na IPv6 byl tedy tiše rozbitý.
`%iface`/`@vrf` se teď odmítají hlasitě (rozlišení jména proti dropdownu zatím neumíme) — zahodit
kvalifikátor by znamenalo adresovat něco jiného.

### 23.2 IPv6 pole je `FT_ADDR6` (typový bajt `0x18`), ne `raw`

Tabulka ftype z `master*.js` (`msg2buffer`), typový bajt = `ftype << 3 | size-flags`:

| ftype | 0 | 1 | 2 | **3** | 4 | 5 | 6 |
|---|---|---|---|---|---|---|---|
| skalár | bool `0x00` | u32 `0x08` | u64 `0x10` | **addr6 `0x18`** | string `0x20` | message `0x28` | raw `0x30` |
| pole (+16) | `0x80` | `0x88` | `0x90` | `0x98` | `0xA0` | `0xA8` | `0xB0` |

`FT_ADDR6` je **16 bajtů bez délkového prefixu** — jediný variabilně široký typ, který ho nemá.
Poslat IPv6 jako `raw` znamená dát na místo prvního bajtu adresy délku, takže router pole ignoruje.

**Druhý, horší důsledek:** `0x18` neuměl ani *parser*. Neznámý typ padal do `default: return 0`, takže
se hodnota přečetla jako další klíč+typ a **zbytek zprávy se rozsypal do nesmyslných klíčů** — tiše.
Přesně tohle byl ten „prázdný `0x1=[{}]`" u traceroute: hop je `union{ip6addr a1 allowipv4, string s2}`
uvnitř `multi` a jeho 16 bajtů parser přeskočil o nulu. Po doplnění `0x18` (a chybějících polí
`0x80/0x90/0x98/0xB0`) traceroute vrací `address=127.0.0.1` bez jediné změny v resolveru.

> Poučení pro celou tabulku: **každý ftype musí mít případ v `SkipTypeBytes`**, i když pro něj není
> dekodér. Chybějící case není „nepodporovaný typ", ale tichý rozsyp všeho, co následuje — stejná
> past, jakou dřív předvedlo `0xA0 str_array`.

### 23.3 `/interface/monitor-traffic` = živá pole okna rozhraní, ne monitor okno

V celém katalogu (18 pluginů, `jg_analyze.py`) **žádné monitor okno pro traffic není**. WinBox ukazuje
průtok jako živé sloupce seznamu rozhraní, které `getall` se stats bitem vrací normálně:

| API jméno | klíč | `.jg` label | ftype |
|---|---|---|---|
| `rx-bits-per-second` | `0x100D3` | `Rx` | bigbitrate |
| `tx-bits-per-second` | `0x100D4` | `Tx` | bigbitrate |
| `rx-packets-per-second` | `0x100CB` | `Rx Packet` | decimal p/s |
| `tx-packets-per-second` | `0x100CD` | `Tx Packet` | decimal p/s |
| `rx-byte` | `0x100FC` | `Rx Bytes` | bigbytes |
| `rx-packet` | `0x100FE` | `Rx Packets` | bigdecimal |

**A tady byla ještě jedna, samostatná chyba:** normalizér labelů má `'Rx' → rx-byte`, takže API jméno
`rx-byte` dostal **rate**. Na ether1 vracelo native `rx-byte=5536`, zatímco API pro tentýž záznam
`rx-byte=76024833` — správné jméno, špatná hodnota, o pět řádů. Proto se celý traffic blok mapuje
**podle klíčů** (`ShippedFieldAliases["/interface"].KeyToApi`) a alias set se dědí i na podcesty
(`/interface/ethernet`, `/interface/monitor-traffic`), které čtou stejný handler.

Ověřeno: API i native hlásí ve stejné chvíli **shodně** `rx-bits/s=3584, rx-pkt/s=3`.

### 23.4 Sebeukončující monitor se musí dočkat Finished

Pass, který skončí bez `Finished`, znamená u průběžného okna „tohle je snímek", ale u `ping`/
`traceroute` znamená „ještě pracuju". Traceroute na nedosažitelnou adresu publikuje každou sekundu
delší tabulku: první pass = 1 hop. Sync čtení proto u sebeukončujících příkazů
(`TikMonitorVerbs.SelfTerminating`) pollimuje dál, dokud router neřekne Finished — 20 řádků za 5,2 s,
stejný tvar jako přes API — a je ohraničené `ReceiveTimeout`.
