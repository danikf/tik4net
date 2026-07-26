# WinBox `.jg` katalog — formát a M2 mapping (RE 2026-06-07)

Tento adresář = lokální kopie WinBox plugin cache + analyzátor. Slouží jako zdroj
katalogu operací pro **nativní M2 volání** (bez mepty konzole).

## Obsah

```
6.45.9-48807417/        10× .jg  (RouterOS 6.45.9, z WinBox cache)
6.45beta63-2796463005/   9× .jg  (z WinBox cache)
7.17rc3-3521562961/      9× .jg  (RouterOS 7.17rc3, z WinBox cache)
7.21.4-http/            18× .jg + 3 png + list  ← VERSION-MATCHED testbed, přes HTTP webfig
jg_analyze.py            parser + extraktor + diff/detail/report
catalog-7.21.json        všechny path-ops z 7.21.4 (testbed, strojově čitelné)
catalog-7.17.json / catalog-6.45.json   starší verze
catalog-*-windows.txt    lidsky čitelný katalog oken+polí
```

Dva zdroje `.jg`:
1. **WinBox cache** (`%APPDATA%\MikroTik\WinBox\<verze>-<id>\`) — per-verze,
   plní se když WinBox.exe připojí router. Offline, ale závisí na WinBox instalaci.
2. **HTTP webfig** (⭐ DYNAMICKÉ, version-matched) — viz níže. Bez WinBox, bez auth.

## ⭐ Dynamické stažení `.jg` z routeru přes HTTP webfig (W4 — VYŘEŠENO 2026-06-07)

`.jg` **nejde** přes mproxy `[2,2]` (cmd=3/7 → "cannot open source file" na CHR), ALE
webfig je servíruje přes HTTP **gzipované**, **bez autentizace**:

```bash
# Katalog (plain):
GET http://<router>/webfig/list                      → 200, text { crc,size,name,unique,version }

# Plugin .jg — VYŽADUJE Accept-Encoding: gzip (jinak HTTP 406 Not Acceptable!):
GET http://<router>/webfig/roteros.jg
    Accept-Encoding: gzip                             → 200, gzip → rozbal → JS literal
```

- Past: **bez `Accept-Encoding: gzip` vrací webfig HTTP 406** (servíruje jen komprimované).
- `unique` název netřeba — stačí plain `name` (`roteros.jg`). Auth netřeba (statické UI assety).
- Wire size = `size` z `list`; po gunzipu plný JS literal (roteros.jg 7.21.4: 109706 B → 918451 B).
- ⇒ **version-matched katalog** kdykoliv, čistě HTTP. Stažen celý 7.21.4 do `7.21.4-http/`.
- Pozn.: HTTPS varianta na portu 443 (`/webfig/`) by měla fungovat stejně pro SSL-only routery.

## ⭐⭐ Dynamické stažení přes WinBox M2 / mproxy (port 8291) — OVĚŘENO 2026-06-07

**Preferovaná cesta** — jeden port pro vše (auth+data), funguje i když je www služba vypnutá.
Dřívější „`.jg` nejde přes mproxy" bylo MYLNÉ — dvě příčiny, obě opravené:

1. **Špatné jméno souboru.** Soubor na disku je `<name>.jg.gz` (gzipovaný), ne `<name>.jg`.
   mproxy `[2,2] cmd=7 open "roteros.jg.gz"` → multi-chunk `cmd=4` read → klientský gunzip.
2. **File handle >255 jako u8.** mproxy open vrací session handle, který může být >255
   (stejně jako mepty SESSION_ID 265, kap. G). `M2Message.SessionIdField` ho kódoval jako u8
   → useknutí → read mířil na špatnou session → prázdná odpověď (i pro `list`!).
   **Fix:** `SessionIdField` auto-switch u8(≤255)/u32 (jako produkční `tik4net/Winbox/M2Message`).

Ověřeno `WinboxJgFetchTest.Winbox_FetchJgGz_ViaMproxy_Works`: `roteros.jg.gz` 109706 B →
gunzip 918451 B JS literál, **bajtově identické s HTTP variantou**. Uloženo do `via-mproxy/`.

| Cesta | Port | Auth | Šifrování | Stav |
|---|---|---|---|---|
| **WinBox mproxy** | 8291 | ✅ EC-SRP5 | ✅ AES | ✅ **OVĚŘENO** (`<name>.jg.gz` + gunzip) |
| HTTP webfig | 80 | ne | ne | ✅ ověřeno (`Accept-Encoding: gzip`) |
| HTTPS webfig | 443 | ne | ✅ TLS | (předpoklad, neověřeno) |

## Formát `.jg`

**Plain-text JS object literal** (NE binárka, NE gzip), 100 % tisknutelné ASCII. Strom
oken/dialogů WinBox UI. Příklad:

```js
[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],autorefresh:1000,
  generic:'iface',nameval:'Name',c:[
    {name:'Name',type:'string',id:'s10006',min:1,width:100},
    {name:'MTU', type:'number',id:'u10064',def:1500},
    {name:'running',type:'flag',id:'b1000e'}, ... ]}]
```

## Mapping `.jg` → M2 protokol (KLÍČOVÉ)

| `.jg` | M2 | Pozn. |
|---|---|---|
| `path:[ a,b ]` | **SYS_TO** handler array (`0xFF0001`) | `[20,0]`=/interface, `[13,4]`=sysinfo, `[2,2]`=mproxy/file |
| `cmd`/`startcmd`/`pollcmd`/`cancelcmd`/`setcmd` | **SYS_CMD** (`0xFF0007`) | viz „Příkazy" níže |
| `id:'<typ><hexKey>'` | field key + typ v zprávě | `s10006` → key `0x10006`, typ string |
| `nameval:'Name'` | které pole je „primární jméno" záznamu | |
| `generic:'iface'` | aplikuje generickou šablonu příkazů | proto `map` nemá explicitní cmd |

### Typové prefixy (1 písmeno; zbytek = hex key)

| prefix | typ | array varianta |
|---|---|---|
| `u` | u32 | `U` = u32[] |
| `q` | u64 | `Q` = u64[] |
| `s` | string | `S` = string[] |
| `b` | bool | `B` = bool[] |
| `r` | raw (mac 6B) | `R` = raw[] |
| `m` | addr (ip/ip6) | `M` = addr[] |
| `a` | ip6addr 16B | `A` = ip6[] |

Velké písmeno = pole. Hex suffix se čte hexadecimálně (`sfe0010` → key `0xFE0010`).
Histogram (7.17, 9 souborů): u×4773, b×2442, s×1437, U×334, q×287, r×241, Q×141,
M×134, a×133, m×105, S×57, R×8, A×6. **Žádné jiné typové kódy** → tabulka je úplná.

### Namespace klíčů

- **User namespace** (`0x00xxxx`): per-objekt pole (`0x10006` Name, `0x10064` MTU…).
- **System namespace** (`0xFExxxx`): well-known pole.
  - `0xFE0001` = `.id` (record handle; = `M2Message.SessionIdField`). V katalogu jako
    `ufe0001 'Interface'` = „vyber objekt podle id".
  - `0xFE0008` = interface `inactive` flag.
  - **comment** = generický `{type:'comment'}` element **bez `id`** → well-known key
    (kandidát `0xFE0009`, ověřit empiricky ve Fázi 3).

### Příkazy (SYS_CMD `0xFF0007`)

Generická okna `type:'map'`/`item` mají **`cmds={}`** — list/get/set/add/remove jsou
**winbox-builtin konstanty** (nejsou v `.jg`, plynou z `generic:`). Explicitní `cmd` mají
jen speciální akce (`doit`, `action`, `query`). Histogram explicitních cmd (7.17):

```
1 ×32   0xFE0011 ×28   0xFE000F ×23   2 ×21   6 ×15   3 ×15   0xFE0010 ×9
7 ×7    5 ×5    1006 ×4   10/9/8 ×4   ...
```

- `0xFE000F/0xFE0010/0xFE0011` = standardní **monitor** start/poll/cancel (mnoho `action` oken).
- Malá čísla (1–12) = per-handler subpříkazy.
- Standardní getall/set pro generický objekt = **TODO empiricky** (Fáze 3), nejsou v `.jg`.

## Stabilita napříč verzemi (6.45.9 → 7.17rc3)

`python jg_analyze.py diff 6.45.9-48807417 7.17rc3-3521562961`:

- paths: A=403, B=484, společných 327, jen v A 76, jen v B **+157**.
- **259/327** společných paths má klíče A ⊆ B (stabilní/rozšířené), 66 přidalo klíče.
- 68 paths „ztratilo" klíč — ale koncentrované v `[120,*]` (wireless) a `[16,*]` →
  **major rewrite wireless subsystému** (wlan6 → wave2 v 7.x), ne náhodné přejmenování.
- **Závěr:** core handlery (interface `[20,0]`, …) jsou stabilní; protokol se rozšiřuje,
  nepřejmenovává (potvrzeno). ⇒ WinboxCli používající stabilní cesty je oprávněný;
  pro nativní volání lze katalog 7.17 použít i proti 7.21.4 pro základní objekty.

### Version-exact diff 7.17rc3 → 7.21.4 (testbed, přes HTTP)

`python jg_analyze.py diff 7.17rc3-3521562961 7.21.4-http`:
- paths: A=484, B=599, společných **479**, jen v A **5**, v B **+120**.
- **451/479** společných paths má klíče A ⊆ B (stabilní/rozšířené), 67 přidalo klíče.
- jen **28** paths „ztratilo" klíč, vesměs po 1 klíči (drobné, např. `[138,4]` -0x6002).
- Interface `[20,0]` má v 7.21.4 **identická core pole** jako 7.17 (Name 0x10006, type 0x10001…).
- ⇒ Tvrdé potvrzení: mezi minor verzemi se mění **<6 %** paths, core 100 % stabilní.
  **Proč tedy WinBox stahuje .jg per-verze?** Kvůli těch +120 nových paths/příkazů a +67
  rozšířených — tj. PŘÍRŮSTKY, ne přejmenování. Pro základní příkazy stačí starší katalog;
  pro plné/nejnovější pokrytí je nutný version-matched (proto W4 = HTTP fetch).

## Interface handler `[20,0]` — pole pro PoC (Fáze 3)

`python jg_analyze.py detail 7.17rc3-3521562961 20,0`:

```
key=0x10006 string  'Name'
key=0x10001 u32     'type'  RO
key=0x10002 u32     'caps'  RO
key=0x10064 u32     'MTU'
key=0x1000e bool    'running'
... + statistiky Tx/Rx (q100d4 …)
comment  = generický {type:'comment'} → well-known key (ověřit)
.id      = 0xFE0001
```

## Nástroj `jg_analyze.py`

```
python jg_analyze.py <dir>                      # souhrn + --json out.json
python jg_analyze.py detail <dir> 20,0          # okna na handleru + pole
python jg_analyze.py diff <dirA> <dirB>         # stabilita paths/keys
python jg_analyze.py report <dir> out.txt       # lidsky čitelný katalog
```
Pozn.: na cp1250 konzoli nastav `$env:PYTHONUTF8=1` (kvůli `⊆` ve výpisu diff).
