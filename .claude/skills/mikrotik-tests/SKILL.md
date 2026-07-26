---
name: mikrotik-tests
description: >
  Skill for working with tik4net integration tests against a live MikroTik router.
  Use this skill whenever the user wants to: run tests (single transport or all 11 in series),
  create a new test class/method, inspect or clean router state, understand why a test was
  skipped (Inconclusive), debug failures, analyze orphan objects, or check router logs after
  a test run. Also trigger when the user mentions TestBase, EnsureCapability, runsettings,
  connectionType, or any test file in tik4net.integrationtests/. Includes known baseline failure catalog
  from a full 11-transport run (2026-06-20) with expected durations and CLI gotchas.
---

# MikroTik Tests Skill

## Project context

- Test project: `tik4net.integrationtests/` — MSTest, .NET 4.8
- Tests hit a **real MikroTik router** in HyperV. There are no mocks.
- **This skill covers the integration suite only.** Router-free tests live in a separate
  `tik4net.unittests/` (net8.0) project and run in CI — if a new test does not actually need
  hardware, write it there instead (`dotnet test tik4net.unittests/tik4net.unittests.csproj`).
- Router connection settings in `tik4net.integrationtests/App.config`:
  ```xml
  <add key="host"            value="<router IP>"/>
  <add key="user"            value="<user>"/>
  <add key="pass"            value="<password>"/>
  <add key="routerMac"       value="<router MAC>"/>
  <add key="routerIdentity"  value="MikroTik"/>
  <add key="connectionType"  value="Api"/>
  <add key="restPort"        value="80"/>
  <add key="restSslPort"     value="443"/>
  <add key="restAllowInvalidCert" value="true"/>
  ```

---

## Connection types — všech 11

| Enum / runsettings | Protokol | Port | Capability set | Approx. skip count |
|--------------------|----------|------|----------------|--------------------|
| `Api` / `api` | MikroTik API plain | 8728 | Crud+Listen+Streaming+RawSentences+Tagging | 60 |
| `ApiSsl` / `apissl` | MikroTik API TLS | 8729 | Crud+Listen+Streaming+RawSentences+Tagging | 60 |
| `Rest` / `rest` | REST HTTP | 80 | **Crud only** | 90 |
| `RestSsl` / `restssl` | REST HTTPS | 443 | **Crud only** | 90 |
| `Telnet` / `telnet` | CLI plain | 23 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `MacTelnet` / `mactelnet` | CLI přes MAC UDP | 20561 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `Ssh` / `ssh` | CLI přes SSH | 22 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxCli` / `winboxcli` | CLI v Winbox terminálu | 8291 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxCliMac` / `winboxclimac` | CLI v Winbox terminálu přes MAC | 20561 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxNative` / `winboxnative` | M2 nativní protokol | 8291 | **Crud+Listen\*+SafeMode** | 221 |
| `WinboxNativeMac` / `winboxnativemac` | M2 přes MAC | 20561 | **Crud+Listen\*+SafeMode** | 221 |

\* **Listen** je u CLI/native emulovaný pollingem (re-issue snapshot na pozadí), ne push. **Streaming** jen API.

**CLI transporty** = Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac — všechny sdílejí chování popsané v sekci *CLI transport gotchas*.

**WinboxNative/WinboxNativeMac** mají největší počet skipů (221 z 415): jejich capability set je nejužší (no Listen, no Streaming, no CLI-specifika). Testy závislé na CLI syntaxi jsou skipnuty.

### Capabilities (ověřeno z kódu)

```
Crud         — základní CRUD (všechny transporty)
Listen       — async watch (Api/ApiSsl push; CLI + WinboxNative pollingem)
Streaming    — ExecuteListWithDuration (Api/ApiSsl only)
RawSentences — raw sentence access (Api/ApiSsl only)
Tagging      — tag multiplexing (Api/ApiSsl only)
SafeMode     — Api/ApiSsl + CLI family + WinboxNative (NE Rest)
RawCommand   — Api/ApiSsl + CLI family (NE Rest, NE WinboxNative)
```

- **Api/ApiSsl**: vše. **Rest/RestSsl**: jen Crud (stateless HTTP). **CLI family** (Telnet/MacTelnet/
  Ssh/WinboxCli/WinboxCliMac): Crud+Listen\*+SafeMode+RawCommand. **WinboxNative/Mac**: Crud+Listen\*+SafeMode.
- `EnsureCapability(cap)` → `Inconclusive` (skip) pokud transport cap nepodporuje.

---

## Spouštění testů

### Jeden transport

```powershell
# S TRX výsledky (doporučeno):
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj \
  --settings tik4net.integrationtests/winboxcli.runsettings \
  --logger "trx;LogFileName=results_winboxcli.trx" \
  --results-directory TestResults \
  --verbosity normal

# Bez TRX (výstup jen na konzoli):
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj --settings tik4net.integrationtests/api.runsettings
```

### Všechny transporty sériově — doporučená strategie

```powershell
# Seřazení od nejrychlejšího (API) po nejpomalejší (WinboxCliMac).
# WinboxNative/WinboxNativeMac jsou rychlé díky velkému počtu skipů.
$transports = @("api","apissl","rest","restssl","telnet","ssh","mactelnet",
                "winboxnative","winboxnativemac","winboxcli","winboxclimac")

foreach ($t in $transports) {
    Write-Host "=== $t ===" -ForegroundColor Cyan
    dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
        --settings tik4net.integrationtests/$t.runsettings `
        --logger "trx;LogFileName=results_$t.trx" `
        --results-directory TestResults `
        --verbosity normal
}
```

**Pořadí je důležité:** CLI transporty zanechávají orphany (viz sekce Orphany). Spouštěj API-based first, pak CLI. WinboxCli před WinboxCliMac — jinak orphan z WinboxCli způsobí odlišnou chybu v WinboxCliMac.

### Opakování pouze spadlých testů

Po sériovém běhu parsuj TRX a opakuj jen selhání:

```powershell
# Získej seznam spadlých testů z TRX:
[xml]$trx = Get-Content "TestResults\results_winboxcli.trx"
$failed = $trx.TestRun.Results.UnitTestResult |
    Where-Object { $_.outcome -eq 'Failed' } |
    Select-Object -ExpandProperty testName

# Spusť jen je (--filter přijímá | jako OR):
$filter = ($failed | ForEach-Object { "Name=$_" }) -join "|"
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
    --settings tik4net.integrationtests/winboxcli.runsettings `
    --filter $filter
```

### Smoke subset pro větší změny (rychlá kontrola napříč transporty)

Full 11-transportní matice je na plné code review / release. Pro **běžné větší změny** (mimo
unit testy) stačí:

1. **Plný běh přes API** (`api.runsettings`) — nejrychlejší (~5 min) a nejširší capability set,
   odchytí většinu logických regresí.
2. **Lehký smoke subset přes zbytek transportů** — jen pár rychlých, samostatných testů, které
   nenechávají orphany a pokrývají základní CRUD + singleton load + connection handshake:

   ```powershell
   $smokeFilter = "FullyQualifiedName~ConnectionTest|FullyQualifiedName~SystemClockTest|FullyQualifiedName~InterfaceListTest|FullyQualifiedName~IpRouteTest"
   $transports = @("rest","restssl","telnet","ssh","mactelnet","winboxcli","winboxclimac","winboxnative","winboxnativemac")

   foreach ($t in $transports) {
       dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
           --settings tik4net.integrationtests/$t.runsettings `
           --filter $smokeFilter `
           --logger "trx;LogFileName=smoke_$t.trx" `
           --results-directory TestResults `
           --verbosity normal
   }
   ```

   `ConnectionTest` always exercises Api/ApiSsl directly (it doesn't read `tik.connectionType`),
   so it's really a fixed sanity check; the other three classes do respect the runsettings
   transport and give basic load/CRUD coverage per protocol without the ~50+ min cost of the
   full CLI suites.

3. Only run the **full 11-transport matrix** (see below) when the change touches a
   transport-specific area (`Crypto/`, `WinboxNative*/`, `MacTelnet/`, `ApiConnection`
   reader/tag multiplexing, CLI parsers) or before a release.

### Přibližné doby trvání celého běhu

| Transport | Doba |
|-----------|------|
| Api, ApiSsl | ~5 min |
| Rest, RestSsl | ~3 min |
| Telnet, Ssh | ~10–15 min |
| MacTelnet | ~15–20 min |
| WinboxNative, WinboxNativeMac | ~5–8 min (hodně skipů) |
| WinboxCli | **~52 min** |
| WinboxCliMac | **~60 min** |

WinboxCli a WinboxCliMac jsou pomalé kvůli Winbox terminálu: každé CLI volání prochází šifrovaným kanálem (EC-SRP5 + AES) a má vyšší latenci než přímý Telnet/SSH. Timeouty testů (SafeMode: 1 min, traceroute: skip) se nezmenšují.

### Výsledky ze všech 11 transportů

> Staré počty pass/fail (2026-06-20) byly **stale** — většina selhání po opravách + živém ověření
> nereprodukovala. Dispozici všech kategorií (A–K) a aktuální tabulku limitů viz
> [`TestResults/test-failures-report.md`](../../../TestResults/test-failures-report.md). Po čistém
> full běhu přegeneruj počty z TRX (sekce *Parsování TRX*).

---

## Parsování TRX výsledků

```powershell
# Souhrn všech TRX najednou:
foreach ($trx in (Get-ChildItem TestResults\results_*.trx | Sort-Object Name)) {
    [xml]$x = Get-Content $trx.FullName
    $c = $x.TestRun.ResultSummary.Counters
    "$($trx.Name): pass=$($c.passed) fail=$($c.failed)"
}

# Výpis selhání z jednoho TRX:
[xml]$x = Get-Content TestResults\results_winboxcli.trx
$x.TestRun.Results.UnitTestResult |
    Where-Object { $_.outcome -eq 'Failed' } |
    ForEach-Object {
        $msg = ($_.Output.ErrorInfo.Message -replace '\r?\n',' ').Trim()
        "$($_.testName) | $($msg.Substring(0,[Math]::Min(120,$msg.Length)))"
    }
```

---

## CLI transport gotchas

Tyto jevy se projevují na **Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac**.

### A — `add` vrátí prázdný string (objekt na routeru vznikne, ale bez ID)

`:put [/path add ...]` vrátí `""` místo nového `.id`. Knihovna nemá ID → `TikNoSuchItemException`. Objekt přitom **na routeru existuje** → orphan (cleanup selže — viz sekce Orphany).

```
CLI>> :put [/interface eoip add name=test-eoip ...]
CLI<<         ← prázdný string
→ TikNoSuchItemException: no such item /interface/eoip/add
```

Projevuje se **hlavně na WinboxCli/WinboxCliMac**. Na Telnet/SSH jen výjimečně (např. AddRadiusServerWillNotFail na MacTelnet).  
Příčina: WinboxCli terminál má vyšší latenci; odpověď z routeru přijde mimo read-window.

### B — Singleton `LoadSingle` — druhý `print` vrátí prázdno

`LoadSingle<T>` volá `print as-value` dvakrát (první pro detekci prázdného výsledku, druhé pro data). Na WinboxCli/WinboxCliMac druhé volání vrátí `""`.

```
CLI>> :put [/ip settings print as-value]
CLI<< ip-forward=yes;...
CLI>> :put [/ip settings print as-value]
CLI<<         ← prázdné
→ TikNoSuchItemException: no such item /ip/settings/print
```

Dotčené testy: `LoadIpSettingsWillNotFail`, `LoadIpTrafficFlowWillNotFail`, `LoadPppAaaWilNotFail`, `LoadSnmpWillNotFail`, `LoadMacServerWillNotFail`, `ExecuteSingleRow_With_Tag_Parameter`.

### C — ~~Truncace terminálu~~ multi-value pole: `Missing field 'name'` ✅ OPRAVENO

**Původní diagnóza (truncace) byla CHYBNÁ.** Skutečná příčina: RouterOS renderuje
multi-value (list) pole v `as-value` výstupu s oddělovačem `;` — TÝMŽ znakem jako mezi poli:
`key-usage=key-cert-sign;crl-sign;name=mikrotik-CA`. Parser splitoval na `;`, takže `name`
skončilo pod sloučeným klíčem `crl-sign;name` → `GetResponseField("name")` selhalo.

Oprava: `CliOutputParser.ParseOrderedFields` — `;`-token bez `=` je pokračování (element)
předchozího multi-value pole (spojené čárkou, jako API). Platí pro VŠECHNY CLI transporty
(sdílený parser), žádná „jiná šířka terminálu". Ověřeno: Certificate/HotspotProfile/File/Pptp
procházejí přes Telnet i WinboxCli.

### D — `fib=yes` odmítnuto (presence-flag) ✅ OPRAVENO

RouterOS CLI odmítá `fib=yes` — `fib` je presence-flag: nastavuje se holým názvem (`… fib`),
`=hodnota` vrátí `expected end of command`. Binární API/REST `fib=yes` akceptují (proto selhával
jen CLI). Ověřeno živě: `/routing/table/add` tab-completion uvádí `fib`; `fib=yes` → chyba na
sloupci s `=`.

Oprava: `CliCommandBuilder` — `CliPresenceFlagFields = { "fib" }`; truthy → holý název,
falsy → vynechat. Rozšiřitelné o další presence-flagy. Ověřeno: AddRoutingTable přes Telnet.

### E — SafeMode rollback po disconnect

`SafeMode_DisconnectWithoutRelease_RollsBack` očekává rollback po disconnect bez release. **Předpoklad
„CLI nerollbackne" nebyl ověřen** → `SkipOnNonApi` odstraněn. Test teď chování **pozoruje** (30 s poll):
projde, pokud transport rollbackne (i CLI/native), jinak `Inconclusive` (ne fail). `[Timeout(90000)]` je
jen pojistka proti zaseknutému routeru.

### G — RunScript log race condition

Script se spustí, ale `:put [/log print as-value]` ho nezachytí v době dotazu. Projevuje se na MacTelnet, WinboxCli, WinboxCliMac.

---

## WinboxNative / WinboxNativeMac gotchas

### Nemapované cesty (.jg katalog)

Native CRUD jede jen po cestách ve verzově-spárovaném `.jg` katalogu. Cesta v žádném WinBox okně →
`WinBox native: no M2 handler mapping for path '…'`. Ověřeno nemapované: `/tool/netwatch`,
`/routing/bgp/advertisements`. Řešení: `connection.PathOverride(path, new[]{maj,min})` nebo CLI/API.
Guard v testech: `SkipOnWinboxNativeUnmappedPath(path)`.

### I — bool-DefaultValue (NE chybějící mapping)

`AddSystemScriptWillNotFail` padal, protože `bool` se serializuje na `"no"/"yes"`, ale entity měla
`DefaultValue="false"` → `HasDefaultValue` nikdy nesedělo → pole se vždy posílalo → native nemělo M2 key.
Opraveno (`"no"`) + plošný audit všech `bool` entit. **Nebyl to chybějící katalog.** Pozor na tento vzor
u nových entit: `bool` default vždy wire forma `"no"/"yes"`, ne `"false"/"true"`.

### J — `/system/health` native ✅ OPRAVENO (board-gated singleton)

Root cause: health je board-gated. Alias mířil na `map` okno `[24,29]` → `getall` = `0xFE0002 NotImplemented`
na x86/CHR. Správné okno na x86 je singleton `item` `[24,14]` čtené **get-singleton** (`0xFE000D`, ověřeno
živě). Fix: `WinboxNativeConnection.PreferSingletonHealthHandler` → `WinboxJgCatalog.FindSingletonHandlerByLeaf("health")`
(handler živě z `.jg`, ne hardcode). `LoadSingle<SystemHealth>` přes native **projde**. Pozn.:
`state`/`state-after-reboot` jsou API/CLI-only — WinBox health okno je read-only HW-senzor display
(`on:'lm87'`), na CHR prázdné → genuine WinBox limit. Guard `catch when (IsWinboxNativeUnsupported)` zůstává
jako safety net.

### K — bridge-vlan `vlan-ids` native ✅ OPRAVENO (multinumberrange)

`vlan-ids` = `multinumberrange` (`[16,13]` id `U1`, u32[]). webfig `types.multinumberrange.put` (bez id2)
flatuje rozsahy na u32[] `[lo0,hi0,…]` (`"3999"` → `[3999,3999]`). Fix: `WinboxFieldResolver.EncodeField`
enkóduje (`U32ArraySys`), `WinboxRecordCodec` dekóduje zpět; round-trip ověřen živě. **Navíc:** resolver
HODÍ loud (`WinboxFieldResolutionException`) u nepodporovaných list/array polí (wireType `…[]` nebo uiType
`multi…`) místo tichého zahození. Zbývající TODO: `tagged`/`untagged` (multinumber interface-listy) a native
**vytvoření** bridge (`add type=bridge` → `0xFE0006`, separátní gap — test ho safety-net skipne když není
existující bridge).

---

## Mezirunová kontaminace — orphany

**Klíčový problém:** CLI add (Kat. A) zanechá objekt na routeru bez sledování ID. Cleanup selže. Příští transport pak dostane odlišnou chybu (`already have interface with name X` místo `no such item`).

```
WinboxCli:
  AddEoipWillNotFail → add vrátí "" → fail → orphan test-eoip na routeru

WinboxCliMac (po):
  AddEoipWillNotFail → already have interface with name test-eoip → jiná chyba!
```

**Objekty náchylné k orphan problému** (CLI transporty):
- IPsec peery (`AddIpsecIdentityWillNotFail`, `AddIpsecPolicyWillNotFail`) → způsobí `ipsec,error` flood v logu
- Eoip rozhraní (`AddEoipWillNotFail`)
- L2TP klienty (`AddL2tpClientWillNotFail`)
- WiFi channel/security
- Bridge filter pravidla
- Hotspot profily, hotspot users
- Firewall filter/raw pravidla

---

## Kontrola orphanů a logu po každém běhu

Po každém transportním běhu ověř stav routeru:

```python
# Přes MCP:
/ip/ipsec/peer/print                        # IPsec peery (způsobí error flood)
/interface/eoip/print                       # Eoip orphany
/interface/l2tp-client/print                # L2TP orphany
/ip/hotspot/profile/print  ?name~TEST_      # Hotspot profily
/interface/bridge/filter/print              # Bridge filter rules
/interface/wifi/channel/print               # WiFi channels
/interface/wifi/security/print              # WiFi securities
```

**Posledních 100 řádků logu** (detekce error flood):

```python
# Přes MCP — filtr dnešního dne:
command: /log/print
parameters: ["?>time=2026-06-20 00:00:00"]
# (uprav datum)
```

Hledej:
- `ipsec,error initiator can't find identity` — orphan IPsec peer, smaž přes `/ip/ipsec/peer/remove`
- `dhcp,error bonding1: DHCP offer rejected` — konfigurace routeru, nesouvisí s testy
- Opakující se záznamy stejné chyby = něco je špatně

**Ruční čištění orphanů:**
```python
/ip/ipsec/peer/remove  params: ["=.id=*X"]       # konkrétní ID
/interface/eoip/remove params: ["=name=test-eoip"]
```

---

## Známá baseline selhání (stav 2026-06-20)

> **POZOR — report `TestResults/test-failures-report.md` (2026-06-20) je z velké části ZASTARALÝ.**
> Při ověření na aktuálním zdroji většina „selhání" A/B vůbec nereprodukovala (add vrací id,
> singleton load funguje). Mnoho položek byly orphan-kontaminace nebo flaky timing, ne bugy.
> Níže je stav PO opravách v této session. Při nejasnosti vždy spusť konkrétní test živě —
> nevěř starému reportu.

### ✅ Opraveno v knihovně (procházejí na všech transportech)

| Kat. | Co | Oprava |
|------|----|--------|
| C | `Certificate`/`HotspotProfile`/`File`/`Pptp` — `Missing field 'name'` | `CliOutputParser`: multi-value `;`-elementy = pokračování pole (ne nové pole) |
| D | `AddRoutingTableWillNotFail` — `fib=yes` | `CliCommandBuilder.CliPresenceFlagFields` — holý `fib` |
| H | `GenerateAndDeleteIpsecKeyWillNotFail` (REST) | `RestRequestBuilder._writeVerbs` += `generate-key`/`export-pub-key`/`import` — bez nich se přidalo `/print` |
| I | `AddSystemScriptWillNotFail` (WinboxNative) | `bool` DefaultValue `"false"/"true"`→`"no"/"yes"` — **plošně ve všech `bool` entitách** (17 souborů); `YesNoOptions` enum ponechán (`[TikEnum("false")]`) |
| A/B | add/singleton flaky timeout na WinboxCli | `WinboxCliClient`: pre-send `DrainSync` když jsou reziduální data (proti desyncu) |
| G | `RunScript_Issue53_WillNotFail` — log race | test pollne log ~5 s místo jediného checku |
| J | `/system/health` native (board-gated) | `PreferSingletonHealthHandler` → singleton `[24,14]` get-singleton (handler živě z `.jg`) |
| K | bridge-vlan `vlan-ids` native (tichý drop) | `multinumberrange` enkódování/dekódování (u32[]) + loud-throw u nepodporovaných list typů |
| a | `/tool/netwatch` native unmapped path | shipped alias `/tool/netwatch` → `[51,1]` ve `WinboxHandlerMap` |

> **POZN. (H):** REST action verby FUNGUJÍ (`POST /rest/<path>/<verb>`, ověřeno `…/generate-key`→200).
> Chyba byla v knihovně (verb nebyl rozpoznán → přidal se `/print`). NE skip — oprava v builderu.

### ✅ Skip-guardy — vázané na konkrétní limit (`Inconclusive`, ne fail)

| Kat. | Test | Guard | Pozn. |
|------|------|-------|-------|
| J | `LoadSystemHealthWillNotFail` | ✅ OPRAVENO — `catch when (IsWinboxNativeUnsupported)` zůstává jen jako safety net | native teď čte health get-singleton `[24,14]`; LoadSingle projde |
| K | `AddBridgeVlanWillNotFail` | ✅ OPRAVENO — `vlan-ids` round-trip asertován pro všechny transporty; `catch when (IsWinboxNativeUnsupported)` jen safety net | safety net teď chytá native bridge-**creation** gap (`0xFE0006`), když není existující bridge |
| E | `SafeMode_DisconnectWithoutRelease_RollsBack` | žádný skip — runtime poll → pass/`Inconclusive` | předpoklad neověřitelný přes stateless MCP → test pozoruje |
| — | bgp/advertisements (a další nepokryté) | `SkipOnWinboxNativeUnmappedPath` | cesta není v `.jg` / handler-mapě. **netwatch už OPRAVENO** (alias `[51,1]`) |

> **Princip:** preferuj feature/runtime-bound skip (`IsWinboxNativeUnsupported` — chytá KONKRÉTNÍ M2
> chybu, nemaskuje jiné bugy a sám zmizí, až transport feature podpoří) před blanket transport-name
> skipem. **Než nastavíš gate, ověř živě, že to není falešný předpoklad** (jako bylo `SkipOnRest`/
> `SkipOnNonApi`/stará Kat. K). `IsNonApiTransport` zůstává jen pro větvení **asercí** (ne skip).

### ⚠️ Orphan-kontaminace (NE bug — uklízej router před/mezi běhy)

`AddEoipWillNotFail` apod. spadnou s `already have interface with name test-eoip`, když
předchozí (starý) běh nechal orphan. Smaž orphany přes API (viz sekce *Kontrola orphanů*).
Na čistém routeru add projde.

### ⚠️ Flaky (intermittent, ne deterministicky) — při selhání opakuj

`LoadIpTrafficFlowWillNotFail`, `LoadListenAsync_*`, `*Async*`, `ParallelSniff*`,
`PingLocalhostAsyncWillNotFail` — polling/async přes pomalé CLI transporty. Pre-send drain
(Kat. A/B oprava) flakiness zmírnil; přesto při ojedinělém selhání opakuj jen daný test.

---

## TestBase — klíčové metody

```csharp
public TestContext TestContext { get; set; }   // injected by MSTest
protected ITikConnection Connection { get; }   // created in [TestInitialize]

protected TikConnectionType ResolveConnectionType()
// priority: runsettings "tik.connectionType" > App.config > "Api"

protected void RecreateConnection(int retryTimeoutSeconds = 20)
protected void EnsureCapability(TikConnectionCapability cap, string feature = null)
protected void EnsureMinRouterOsVersion(int minimumMajor, string featureDescription = null)
protected void EnsureMaxRouterOsVersion(int removedInMajor, string featureDescription = null)
protected void EnsureCommandAvailable(string commandPath)
protected Version GetMikrotikVersion()

// Skip helpers (Assert.Inconclusive). PREFER runtime/feature-bound nad transport-name skipy:
protected static bool IsWinboxNativeUnsupported(Exception ex) // catch-when: konkrétní M2 error/field-resolve → Inconclusive
protected void SkipOnWinboxNativeUnmappedPath(string feature) // path absent from .jg catalog (ověř, že fakt chybí)
protected bool IsNonApiTransport()                            // JEN pro větvení asercí, NE jako skip-gate
// (SkipOnNonApi a SkipOnRest/SkipOnWinboxNative byly odstraněny — slepé/neověřené transport-name gaty)
```

---

## Vytvoření nového O/R mapper testu

```csharp
[TestClass]
public class IpDhcpServerTest : TestBase
{
    [TestMethod]
    public void ListDhcpServersWillNotFail()
    {
        EnsureCommandAvailable("/ip/dhcp-server");
        var list = Connection.LoadAll<DhcpServer>();
        Assert.IsNotNull(list);
    }

    [TestMethod]
    public void AddDhcpServerWillNotFail()
    {
        EnsureCommandAvailable("/ip/dhcp-server");
        string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var entry = new DhcpServer { Name = marker, Interface = "ether1" };
        Connection.Save(entry);
        try {
            var loaded = Connection.LoadById<DhcpServer>(entry.Id);
            Assert.IsNotNull(loaded);
        } finally {
            if (entry.Id != null) Connection.Delete(entry);  // vždy cleanup!
        }
    }
}
```

**Patterns:**
- Vždy `try/finally` cleanup — i při fail musí test smazat co vytvořil.
- `EnsureCapability`, `EnsureMinRouterOsVersion`, `EnsureCommandAvailable` na začátek.
- Prefix `t4n` + GUID suffix pro testovací objekty (snadno dohledatelné na routeru).
- `Console.WriteLine(...)` pro debug — MSTest zachytí stdout.

---

## Vytvoření protokolového PoC testu

Protokolové testy (Winbox, MacTelnet, raw API) **nepoužívají TestBase** — spravují vlastní spojení.

```csharp
[TestClass]
public class MyProtocolTest
{
    [TestMethod]
    public void Protocol_DoSomething_Works()
    {
        var host = ConfigurationManager.AppSettings["host"];
        var user = ConfigurationManager.AppSettings["user"];
        var pass = ConfigurationManager.AppSettings["pass"] ?? "";
        // raw TCP/UDP, vlastní client, assertions
    }
}
```

**Pozor:** Protokolové testy neskipují při jiných transportech! Pokud spustíš `winboxnative.runsettings`, WinboxTcpProtocolTest stále poběží (vlastní spojení). Selhání protokolového testu v jiném transportním běhu = resource/timing kolize, ne transport bug.

---

## Inspekce routeru přes MCP

```python
# Verze a identita
/system/resource/print
/system/identity/print

# Stav po testech — hledej orphany
/ip/ipsec/peer/print                    # → smaž vše s name~t4n
/interface/eoip/print                   # → smaž name=test-eoip
/interface/l2tp-client/print            # → smaž name~t4ntest-l2tp
/ip/hotspot/profile/print               # → smaž name~TEST_
/interface/bridge/filter/print          # → smaž s GUID komentáři
/interface/wifi/channel/print           # → smaž name~test-
/interface/wifi/security/print          # → smaž name~test-

# Log po testech (poslednich ~100 zaznamu = dnesni den)
/log/print  params: ["?>time=2026-06-20 00:00:00"]
# Hledej: ipsec,error + opakující se stejná zpráva = orphan flood
```

---

## Souborová struktura

```
tik4net.integrationtests/
├── TestBase.cs                            — base class
├── App.config                             — router connection settings
├── api.runsettings ... winboxnativemac.runsettings  — 11 transport settings
├── TestResults/
│   ├── results_api.trx ... results_winboxnativemac.trx
│   └── test-failures-report.md           — dispozice kategorií A–K + matice limitů transportů
├── Protocols/
│   ├── _Shared/                           — EcSrp5, WinboxStreamCrypto, M2Message, VT100
│   ├── Transport/                         — TCP, MAC layer helpers
│   ├── Clients/                           — WinboxM2Client, MacTelnetClient, ...
│   └── Tests/
│       ├── ApiProtocolTest.cs
│       ├── WinboxTcpProtocolTest.cs
│       ├── WinboxMacProtocolTest.cs
│       ├── MacTelnetProtocolTest.cs
│       └── WinboxDumpCatalogTest.cs
└── [domain tests]/
    ├── Interface/, Ip/, Routing/, System/, Tool/, ...
```
