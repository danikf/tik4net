# WinBox `.jg` catalog — format and M2 mapping (RE 2026-06-07)

This directory = local copy of the WinBox plugin cache plus an analyzer. It serves as the
source of the operation catalog for **native M2 calls** (without the mepty console).

## Contents

```
6.45.9-48807417/        10× .jg  (RouterOS 6.45.9, from WinBox cache)
6.45beta63-2796463005/   9× .jg  (from WinBox cache)
7.17rc3-3521562961/      9× .jg  (RouterOS 7.17rc3, from WinBox cache)
7.21.4-http/            18× .jg + 3 png + list  ← VERSION-MATCHED testbed, via HTTP webfig
jg_analyze.py            parser + extractor + diff/detail/report
catalog-7.21.json        all path-ops from 7.21.4 (testbed, machine-readable)
catalog-7.17.json / catalog-6.45.json   older versions
catalog-*-windows.txt    human-readable catalog of windows+fields
```

Two sources of `.jg`:
1. **WinBox cache** (`%APPDATA%\MikroTik\WinBox\<version>-<id>\`) — per-version,
   populated when WinBox.exe connects to a router. Offline, but depends on a WinBox install.
2. **HTTP webfig** (⭐ DYNAMIC, version-matched) — see below. No WinBox, no auth.

## ⭐ Dynamically fetching `.jg` from the router via HTTP webfig (W4 — RESOLVED 2026-06-07)

`.jg` **cannot** be fetched via mproxy `[2,2]` (cmd=3/7 → "cannot open source file" on CHR), BUT
webfig serves it over HTTP **gzip-compressed**, **without authentication**:

```bash
# Catalog (plain):
GET http://<router>/webfig/list                      → 200, text { crc,size,name,unique,version }

# Plugin .jg — REQUIRES Accept-Encoding: gzip (otherwise HTTP 406 Not Acceptable!):
GET http://<router>/webfig/roteros.jg
    Accept-Encoding: gzip                             → 200, gzip → unpack → JS literal
```

- Gotcha: **without `Accept-Encoding: gzip` webfig returns HTTP 406** (it only serves compressed content).
- The `unique` name isn't needed — the plain `name` (`roteros.jg`) is enough. No auth needed (static UI assets).
- Wire size = `size` from `list`; after gunzip, the full JS literal (roteros.jg 7.21.4: 109706 B → 918451 B).
- ⇒ **version-matched catalog** on demand, purely over HTTP. Downloaded the full 7.21.4 into `7.21.4-http/`.
- Note: the HTTPS variant on port 443 (`/webfig/`) should work the same way for SSL-only routers.

## Fetching dynamically via WinBox M2 / mproxy (port 8291)

**Preferred path** — a single port for everything (auth and data), and it works even when the `www`
service is disabled. Two details decide whether it works at all:

1. **The file on disk is `<name>.jg.gz`** (gzip-compressed), not `<name>.jg`.
   mproxy `[2,2] cmd=7 open "roteros.jg.gz"` → multi-chunk `cmd=4` read → client-side gunzip.
2. **The file handle can exceed 255 and must not be encoded as u8.** mproxy `open` returns a session
   handle that can exceed 255, as mepty session ids do. Encoding it as u8 truncates it, so the read
   targets the wrong session and the response comes back empty — including for `list`.
   `M2Message.SessionIdField` therefore auto-switches between u8 (≤255) and u32.

Verified by `WinboxJgFetchTest.Winbox_FetchJgGz_ViaMproxy_Works`: `roteros.jg.gz` 109706 B →
gunzip 918451 B JS literal, **byte-identical to the HTTP variant**. Saved into `via-mproxy/`.

| Path | Port | Auth | Encryption | Status |
|---|---|---|---|---|
| **WinBox mproxy** | 8291 | ✅ EC-SRP5 | ✅ AES | ✅ **VERIFIED** (`<name>.jg.gz` + gunzip) |
| HTTP webfig | 80 | none | none | ✅ verified (`Accept-Encoding: gzip`) |
| HTTPS webfig | 443 | none | ✅ TLS | (assumed, not verified) |

## `.jg` format

**Plain-text JS object literal** (NOT binary, NOT gzip), 100% printable ASCII. A tree of
WinBox UI windows/dialogs. Example:

```js
[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],autorefresh:1000,
  generic:'iface',nameval:'Name',c:[
    {name:'Name',type:'string',id:'s10006',min:1,width:100},
    {name:'MTU', type:'number',id:'u10064',def:1500},
    {name:'running',type:'flag',id:'b1000e'}, ... ]}]
```

## Mapping `.jg` → M2 protocol (KEY)

| `.jg` | M2 | Note |
|---|---|---|
| `path:[ a,b ]` | **SYS_TO** handler array (`0xFF0001`) | `[20,0]`=/interface, `[13,4]`=sysinfo, `[2,2]`=mproxy/file |
| `cmd`/`startcmd`/`pollcmd`/`cancelcmd`/`setcmd` | **SYS_CMD** (`0xFF0007`) | see "Commands" below |
| `id:'<type><hexKey>'` | field key + type in the message | `s10006` → key `0x10006`, type string |
| `nameval:'Name'` | which field is the record's "primary name" | |
| `generic:'iface'` | applies a generic command template | that's why `map` has no explicit cmd |

### Type prefixes (1 letter; rest = hex key)

| prefix | type | array variant |
|---|---|---|
| `u` | u32 | `U` = u32[] |
| `q` | u64 | `Q` = u64[] |
| `s` | string | `S` = string[] |
| `b` | bool | `B` = bool[] |
| `r` | raw (mac 6B) | `R` = raw[] |
| `m` | addr (ip/ip6) | `M` = addr[] |
| `a` | ip6addr 16B | `A` = ip6[] |

Uppercase letter = array. The hex suffix is read as hexadecimal (`sfe0010` → key `0xFE0010`).

⚠️ **The prefix is the value's type, not the field's wire type.** `a` (ip6addr) travels as its
own ftype, `FT_ADDR6` = type byte `0x18`, 16 bytes **without a length prefix** — not as `raw`.
`m` (`addr`) is not a scalar at all, but a nested message whose members are determined by the
field's `allow` attribute (`4`/`6`/`D`/`m`/`R`/`/`/`i`/`v` → sub-keys `0xFEFF20`/`21`/`26`/`2F`/`27`/`25`/`22`/`23`).
Without `allow`, `addr` cannot be encoded at all; the catalog therefore carries it in
`WinboxJgField.Allow`. Details in
[winbox-native-m2-protocol.md §23](winbox-native-m2-protocol.md).
Histogram (7.17, 9 files): u×4773, b×2442, s×1437, U×334, q×287, r×241, Q×141,
M×134, a×133, m×105, S×57, R×8, A×6. **No other type codes appear** → the table is complete.

⚠️ **The key does not identify the field on its own — the prefix's CASE is part of it.** A window may
declare both `u12` and `U12`, one scalar and one array on key `0x12`, and the router sends both in the
same record: `/ip/dhcp-client` has 'Add Default Route' at `u12` and 'DHCP Options' at `U12`. A parser
keyed on the numeric key alone drops one of them silently. See
[winbox-native-m2-protocol.md §30](winbox-native-m2-protocol.md).

### Key namespace

- **User namespace** (`0x00xxxx`): per-object fields (`0x10006` Name, `0x10064` MTU…).
- **System namespace** (`0xFExxxx`): well-known fields.
  - `0xFE0001` = `.id` (record handle; = `M2Message.SessionIdField`). In the catalog as
    `ufe0001 'Interface'` = "select object by id".
  - `0xFE0008` = interface `inactive` flag.
  - **comment** = generic `{type:'comment'}` element **without an `id`** → well-known key
    (candidate `0xFE0009`, to be verified empirically in Phase 3).

### Window inheritance: `generic` / `inherit` / `typeon` / `typevalue`

RouterOS interface subtypes are **not** separate M2 handlers. They all live in the generic interface
table (`generic:'iface'`, `path:[20,0]`) and are told apart by a numeric discriminator field. The catalog
declares that with four attributes, and reading only part of them loses whole families of windows:

| attribute | meaning |
|---|---|
| `generic:'X'` | this window is a **base** other windows may extend by name `X` |
| `inherit:'X'` | this window **is** base `X`'s table, narrowed to one subtype |
| `typeon:'<field>'` | the discriminator this window hands its **children** (default `type`) |
| `typevalue:N` | this window's value of the **parent's** `typeon` field |
| `prefix:'l2tp-out'` | the RouterOS interface-name prefix, i.e. the `type` string the API reports |

The chain is deeper than one level, in both directions:

```
Interface (generic:'iface', path:[20,0], typeon:'type')
├── EoIP Tunnel        inherit:'iface'  typevalue:17          → type == 17
├── Ethernet           inherit:'iface'  typevalue:1           → type == 1
├── Interface (PPP)    generic:'ppp'  inherit:'iface'  typeon:'type'  typevalue:4294967295
│   ├── L2TP Client    inherit:'ppp'    typevalue:34  prefix:'l2tp-out'
│   └── PPPoE Client   inherit:'ppp'    typevalue:18  prefix:'pppoe-out'
└── WiFi Interfaces    generic:'wlan' inherit:'iface' typeon:'type'  typevalue:4294967295
    └── Wireless       generic:'ath'  inherit:'wlan'  typeon:'hwtype' typevalue:35
        └── Wireless (Hardware) … → discriminated by hwtype, NOT by type
```

Two values are **not** subtype filters and must be skipped rather than used:

* `typevalue:4294967295` (`0xFFFFFFFF`) on a base window means "any of my subtypes" — a set, not a value.
  Filtering `type == 4294967295` matches nothing.
* a `typevalue` under a base whose `typeon` is something other than `type` (the wireless hardware variants,
  discriminated by `hwtype`) is a filter on a **different field**; applying it to `type` yields a plausible
  wrong answer rather than an error.

The window's leaf name is its **`title`**, not its `name` — every subtype window is `name:'Interface'`,
and only the title carries "L2TP Client" / "EoIP Tunnel".

### `item` is a property of the WINDOW, not of the handler

`type:'item'` (singleton, read with get-singleton) and `type:'map'` (record list, read with getall) can sit
on the **same** `path:[…]`. `[28,0]` is both the *UPnP Settings* item and the *UPnP Interfaces* map;
`[96,1]` is the web-proxy settings item and its connections list. So singleton-ness must be recorded per
derived window path — deciding it per handler answers "singleton" for the list too, and returns one record
where the router has many.

### Commands (SYS_CMD `0xFF0007`)

Generic `type:'map'`/`item` windows have **`cmds={}`** — list/get/set/add/remove are
**WinBox-builtin constants** (not present in `.jg`, they follow from `generic:`). Only special
actions (`doit`, `action`, `query`) have an explicit `cmd`. Histogram of explicit cmds (7.17):

```
1 ×32   0xFE0011 ×28   0xFE000F ×23   2 ×21   6 ×15   3 ×15   0xFE0010 ×9
7 ×7    5 ×5    1006 ×4   10/9/8 ×4   ...
```

- `0xFE000F/0xFE0010/0xFE0011` = standard **monitor** start/poll/cancel (used by many `action` windows).
- Small numbers (1–12) = per-handler sub-commands.
- Standard getall/set for a generic object = **TODO empirically** (Phase 3), not present in `.jg`.

## Stability across versions (6.45.9 → 7.17rc3)

`python jg_analyze.py diff 6.45.9-48807417 7.17rc3-3521562961`:

- paths: A=403, B=484, common 327, only in A 76, only in B **+157**.
- **259/327** common paths have keys A ⊆ B (stable/extended), 66 added keys.
- 68 paths "lost" a key — but these are concentrated in `[120,*]` (wireless) and `[16,*]` →
  a **major rewrite of the wireless subsystem** (wlan6 → wave2 in 7.x), not random renaming.
- **Conclusion:** core handlers (interface `[20,0]`, …) are stable; the protocol expands,
  it doesn't rename (confirmed). ⇒ WinboxCli relying on stable paths is justified;
  for native calls, the 7.17 catalog can be used even against 7.21.4 for basic objects.

### Version-exact diff 7.17rc3 → 7.21.4 (testbed, via HTTP)

`python jg_analyze.py diff 7.17rc3-3521562961 7.21.4-http`:
- paths: A=484, B=599, common **479**, only in A **5**, in B **+120**.
- **451/479** common paths have keys A ⊆ B (stable/extended), 67 added keys.
- only **28** paths "lost" a key, mostly by 1 key (minor, e.g. `[138,4]` -0x6002).
- Interface `[20,0]` has **identical core fields** in 7.21.4 as in 7.17 (Name 0x10006, type 0x10001…).
- ⇒ Hard confirmation: between minor versions, **<6%** of paths change, core is 100% stable.
  **So why does WinBox download `.jg` per version?** Because of those +120 new paths/commands and
  the +67 extended ones — i.e. ADDITIONS, not renames. For basic commands an older catalog
  suffices; for full/latest coverage a version-matched one is required (hence W4 = HTTP fetch).

## Interface handler `[20,0]` — fields for the PoC (Phase 3)

`python jg_analyze.py detail 7.17rc3-3521562961 20,0`:

```
key=0x10006 string  'Name'
key=0x10001 u32     'type'  RO
key=0x10002 u32     'caps'  RO
key=0x10064 u32     'MTU'
key=0x1000e bool    'running'
... + Tx/Rx statistics (q100d4 …)
comment  = generic {type:'comment'} → well-known key (to verify)
.id      = 0xFE0001
```

## Tool `jg_analyze.py`

```
python jg_analyze.py <dir>                      # summary + --json out.json
python jg_analyze.py detail <dir> 20,0          # windows on a handler + fields
python jg_analyze.py diff <dirA> <dirB>         # path/key stability
python jg_analyze.py report <dir> out.txt       # human-readable catalog
```
Note: on a cp1250 console, set `$env:PYTHONUTF8=1` (because of the `⊆` character in the diff output).
