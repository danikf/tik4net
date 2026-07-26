# tik4net — Přehled pokrytí protokolů

> Lokální soubor, není v gitu. Naposledy aktualizováno: 2026-06-07.
> Souhrnný pohled přes všechny komunikační protokoly MikroTiku.

---

## Legenda

| Symbol | Stav |
|---|---|
| ✅ Produkce | Implementováno v lib, unit testy, NuGet |
| 🔬 PoC | Funkční kód, ale jen v testovacím souboru, ne v lib |
| 📄 Research | Protokol zdokumentován, žádný kód |
| 📐 Design | Architektonicky navrženo pro v4.x, není implementace |
| ❌ Neprobádáno | Neznámé |

---

## Matice protokolů

| Protokol | Transport | Port | Vrstva | Stav | Soubor |
|---|---|---|---|---|---|
| MikroTik API | TCP | 8728 | L3/IP | ✅ **Produkce** | `tik4net/Api/ApiConnection.cs` |
| MikroTik API/SSL | TCP+TLS | 8729 | L3/IP | ✅ **Produkce** | `tik4net/Api/ApiConnection.cs` (isSsl flag) |
| MNDP Discovery | UDP broadcast | 5678 | L3/IP | ✅ **Produkce** | `tik4net/Mndp/MndpHelper.cs` |
| REST API | HTTP(S) | 80/443 | L3/IP | ✅ **Produkce** | `tik4net/Rest/RestConnection.cs` |
| Telnet | TCP | 23 | L3/IP | ✅ **Produkce** | `tik4net/Telnet/TelnetConnection.cs` |
| MAC Telnet | UDP broadcast | 20561 | L2/MAC | ✅ **Produkce** | `tik4net/MacTelnet/MacTelnetConnection.cs` |
| WinBox CLI | TCP | 8291 | L3/IP | ✅ **Produkce** | `tik4net/WinboxCli/WinboxCliConnection.cs` |
| WinBox CLI/MAC | UDP | 20561 | L2/MAC | ✅ **Produkce** | `tik4net/WinboxCliMac/WinboxCliMacConnection.cs` |
| Winbox M2 (native) | TCP | 8291 | L3/IP | 🔬 **PoC** | `tik4net.tests/Protocols/Clients/WinboxM2Client.cs` |
| SSH | TCP | 22 | L3/IP | 📐 **Design** | `_notes/4x-ideas.md` (vyžaduje SSH.NET) |

---

## Detail: MikroTik API (✅ Produkce)

**TCP port 8728 (plain) / 8729 (SSL)**

Produkční implementace, dvě NuGet vrstvy.

### Schopnosti

| Schopnost | API | API/SSL |
|---|---|---|
| Otevření spojení | ✅ | ✅ |
| Login (≥ 6.43 challenge-response) | ✅ | ✅ |
| Login (< 6.43 MD5 legacy) | ✅ | ✅ |
| ExecuteNonQuery / ExecuteScalar / ExecuteList | ✅ | ✅ |
| ExecuteAsync (callback push = Listen) | ✅ | ✅ |
| Tags (tagy pro synchronizaci) | ✅ | ✅ |
| O/R mapper (LoadAll, Save, Delete…) | ✅ | ✅ |
| Streaming (Torch, Ping průběžně) | ✅ | ✅ |
| Šifrování | ❌ | ✅ |
| Vyžaduje IP konektivitu | ✅ (ano) | ✅ (ano) |

### Klíčové třídy

```
tik4net/Api/ApiConnection.cs      — ITikConnection implementace
tik4net/Api/ApiCommand.cs         — ITikCommand implementace
tik4net/ConnectionFactory.cs      — entry point
tik4net.objects/                  — O/R mapper + entity classes
```

### Testovací pokrytí

`ConnectionTest.cs`, `CrudTest.cs`, `InterfaceTest.cs`, `IpFirewallTest.cs`, … (15+ test tříd)

---

## Detail: MNDP Discovery (✅ Produkce)

**UDP broadcast port 5678 (IPv4) + multicast ff02::1 (IPv6)**

Router na MNDP broadcast odpovídá záznamem o sobě.

### Schopnosti

| Schopnost | Stav |
|---|---|
| IPv4 broadcast discovery | ✅ |
| IPv6 multicast discovery | ✅ |
| Parsování MAC, IPv4, IPv6, verze, board, identity, uptime | ✅ |
| `stopWhenFirstFound` optimalizace | ✅ |
| Zápis / management routerů | ❌ (jen read-only discovery) |

### Použití

```csharp
IEnumerable<TikInstanceDescriptor> routers = MndpHelper.Discover(stopWhenFirstFound: true);
// TikInstanceDescriptor: Mac, IPv4, IPv6, Version, BoardName, Identity, Uptime, Platform, ...
```

---

## Detail: Winbox M2 (🔬 PoC)

**TCP port 8291**

Implementace: `tik4net.tests/WinboxM2CatalogTest.cs` (~1700 řádků, self-contained).  
Netestuje Winbox UI logiku — testuje přístup k datům přes Winbox protokol přímo z .NET.

### Schopnosti PoC

| Schopnost | Stav | Test |
|---|---|---|
| EC-SRP5 autentizace (RouterOS ≥ 6.43) | ✅ | všechny catalog testy |
| Legacy MD5 autentizace (starší ROS) | ✅ | fallback v `Authenticate()` |
| AES-128-CBC šifrovaná session | ✅ | všechny testy po auth |
| IP-layer smoke test (raw TCP handshake) | ✅ | `WinboxM2_IpLayer_TcpPort8291_*` |
| Čtení souborů přes mproxy [2,2] | ✅ | `WinboxM2_ReadListCatalog_*` |
| Parsování plugin katalogu (`/home/web/webfig/list`) | ✅ | `WinboxM2_ParseCatalog_*` |
| System info (verze, board, arch, identity) | ✅ | `WinboxM2_GetSystemInfo_*` |
| Mepty terminál (PTY session handler [76]) | ✅ | `WinboxM2_ListInterfaces_*` |
| VT100 negociace (cursor dimension probes) | ✅ | třída `Vt100State` |
| Příkaz přes terminál + parsování výstupu | ✅ | `/interface print` → `List<InterfaceEntry>` |
| Set/get interface comment přes mepty | ✅ | `WinboxM2_SetAndVerify_InterfaceEther1Comment` |

### Co PoC zatím neumí

- Winbox přes MAC adresu (Layer 2 Winbox) — L2 transport neprobádán
- Keepalive / reconnect šifrované session
- Plný katalog handlerů (desítky, zmapované jen [2,2], [13,4], [76])
- Není v produkční lib, jen v testech

### Klíčové třídy v PoC

```
WinboxM2Client    — transport + EC-SRP5 + AES + mproxy + mepty
Vt100State        — VT100 cursor state machine pro terminal negotiation
CatalogEntry      — plugin katalog entry (name, version, size, crc)
SystemInfo        — board, version, arch, identity z handleru [13,4]
InterfaceEntry    — výsledek parsování /interface print
```

### Důležité technické detaily (viz také memory/project_winbox_m2_poc.md a _notes/winbox-terminal-findings.md)

- **DataAvailable polling**: nikdy nevolej `RecvAndDecrypt` s krátkým timeoutem — mid-frame timeout korumpuje TCP stream
- **TLV typ 0xA0 (str_array)**: musí být explicitně obsloužen v `SkipTypeBytes`, jinak misalignment parseru
- **8-bit CSI 0x9B**: RouterOS 7.x používá jako alternativu ESC[
- **"Change your password" nag**: RouterOS zobrazí prompt před CLI, nutno odeslat Ctrl-C (0x03)
- **RouterOS comment formát**: zobrazuje se jako `;;; text` (triple-semicolon), ne jako `comment=text`
- **Phase 2 break condition**: `TrimEnd().EndsWith("] >")` — ne `Contains`, kvůli echu příkazu
- **DrainEncryptedFrames(600 ms)**: povinné mezi sekcemi — bez toho nová session dostane stará data

---

## Detail: MAC Telnet (✅ Produkce)

**UDP broadcast port 20561, L2/MAC** — implementováno v kapitole E (2026-06-04)

Přístup k routeru bez IP konektivity — přes MAC adresu.

### Schopnosti

| Schopnost | Stav |
|---|---|
| EC-SRP5 autentizace (Curve25519 Weierstrass, RouterOS ≥ 6.43) | ✅ |
| Legacy MD5 autentizace (starší ROS) | ✅ |
| L2 UDP transport (pure .NET, žádný Pcap) | ✅ |
| CLI přístup přes `CliConnectionBase` (`ITikConnection`) | ✅ |
| MNDP discovery pro nalezení routeru | ✅ |
| Konfigurovatelný login timeout | ✅ |
| Sdílená krypto vrstva v `tik4net/Crypto/` | ✅ |

### Klíčové třídy

```
tik4net/MacTelnet/MacTelnetConnection.cs   — ITikConnection : CliConnectionBase
tik4net/MacTelnet/MacTelnetUdpClient.cs    — internal async UDP klient
tik4net/MacTelnet/MacLayerTransport.cs     — public abstract base pro MAC vrstvu
tik4net/Crypto/EcSrp5.cs                  — sdílená EC-SRP5 matematika (MAC + Winbox)
tik4net/Crypto/WinboxStreamCrypto.cs       — AES-128-CBC (sdíleno s Winbox)
```

### Testovací pokrytí

`MacTelnetProtocolTest.cs` — login + list interfaces + set comment, 3 testy zelené.

---

## Detail: REST API (✅ Produkce)

**HTTP port 80 / HTTPS port 443, RouterOS ≥ 7.1** — implementováno v kapitole A (2026-05-31)

### Schopnosti

| Schopnost | Stav |
|---|---|
| GET (print) / POST (add) / PATCH (set) / DELETE (remove) | ✅ |
| HTTP Basic auth | ✅ |
| HTTPS (SSL varianta) | ✅ |
| `System.Text.Json` serializace (BCL, žádná extra závislost) | ✅ |
| `ITikConnectionCapabilities` — capability gating | ✅ |
| Listen/push (`ExecuteAsync`) | ❌ `NotSupportedException` |
| Streaming (Torch, monitor-traffic follow) | ❌ `NotSupportedException` |
| `/unset` → default hodnota | ⚠️ `PATCH {field:null}` nastaví prázdný string, ne default |

### Klíčové třídy

```
tik4net/Rest/RestConnection.cs        — ITikConnection + ITikConnectionCapabilities
tik4net/Rest/RestCommand.cs           — ITikCommand
tik4net/Rest/RestRequestBuilder.cs    — mapování API path → HTTP verb/URL/JSON
tik4net/TikConnectionSetup.cs         — CreateRestConnection() / CreateRestSslConnection()
```

### Testovací pokrytí

136 pass, 34 skip (streaming/listen), 10 fail (preexisting / CLI). RouterOS 7.21.4.

---

## Detail: Telnet (✅ Produkce)

**TCP port 23** — implementováno v kapitole C (2026-05-31)

IP ekvivalent MAC Telnet — identický terminálový výstup (VT100), stejné CLI RouterOS.

### Schopnosti

| Schopnost | Stav |
|---|---|
| CLI přístup přes `CliConnectionBase` (`ITikConnection`) | ✅ |
| Plain text autentizace (login/password prompt) | ✅ |
| Telnet IAC option negotiation (minimální, ~30 LOC) | ✅ |
| VT100 stripping (`VtStripper`) | ✅ |
| Sdílí CLI Layer s MAC Telnet, SSH | ✅ |

### Klíčové třídy

```
tik4net/Telnet/TelnetConnection.cs     — ITikConnection : CliConnectionBase
tik4net/Cli/CliConnectionBase.cs       — společná CLI základna
tik4net/Cli/VtStripper.cs             — ANSI escape remover (sdíleno)
```

### Testovací pokrytí

139 pass, 41 skip, 0 fail.

---

## Detail: WinBox CLI / WinBox CLI/MAC (✅ Produkce)

**WinBox CLI: TCP port 8291** — implementováno v kapitole G (2026-06-05)  
**WinBox CLI/MAC: UDP port 20561, L2/MAC** — implementováno v kapitole H (2026-06-05)

CLI přístup přes WinBox M2 protokol — klient otevře mepty (PTY handler [76]), v něm pracuje
jako běžný CLI transport (stejný parsing jako Telnet/MAC-Telnet).

### Schopnosti

| Schopnost | Stav |
|---|---|
| EC-SRP5 autentizace + AES-128-CBC session | ✅ (oba transporty) |
| Mepty terminál (handler [76], VT100 negociace) | ✅ (oba transporty) |
| CLI přístup přes `CliConnectionBase` (`ITikConnection`) | ✅ (oba transporty) |
| TCP transport (port 8291) | ✅ (WinboxCli) |
| MAC/UDP transport (port 20561, client_type 0x0f90) | ✅ (WinboxCliMac) |
| Transport-agnostický mepty engine (`IWinboxM2Channel`) | ✅ |
| SESSION_ID > 255 jako u32 | ✅ (root-cause fix vs. PoC) |
| Sdílená krypto vrstva `tik4net/Crypto/` | ✅ |

### Klíčové třídy

```
tik4net/WinboxCli/WinboxCliConnection.cs       — ITikConnection : CliConnectionBase (TCP)
tik4net/WinboxCliMac/WinboxCliMacConnection.cs — ITikConnection : CliConnectionBase (MAC)
tik4net/WinboxCli/WinboxCliClient.cs           — mepty [76] + VT100, transport-agnostický
tik4net/Winbox/IWinboxM2Channel.cs             — abstrakce kanálu (TCP/MAC)
tik4net/Winbox/WinboxM2Session.cs              — TCP kanál (EC-SRP5+AES+Send/Receive)
tik4net/Winbox/WinboxMacM2Session.cs           — MAC UDP kanál (dědí MacLayerTransport)
tik4net/Winbox/M2Message.cs                    — TLV builder + parser
```

### Testovací pokrytí

`WinboxCliProtocolTest`: 2/2 zelené + InterfaceTest 9 pass / 6 skip.  
`WinboxCliMacProtocolTest`: 2/2 zelené (WinboxCli + WinboxCliMac + MacTelnet regrese 6/6).

---

## Detail: SSH (📐 Design)

**TCP port 22, vyžaduje SSH.NET (Renci.SshNet)**

Dvě úrovně: `SshConnection : ITikConnection` přes `exec + print as-value` a  
`SshTerminalSession : ITikSession` pro interaktivní PTY.  
Viz `_notes/4x-ideas.md` bod 4 a `_notes/4x-package-architecture.md`.

---

## Capability matice (aktuální stav)

| Capability | API | API/SSL | MNDP | REST | Telnet | MAC Telnet | WinboxCli | WinboxCliMac | SSH |
|---|---|---|---|---|---|---|---|---|---|
| Produkční kód | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| PoC kód | — | — | — | — | — | — | — | — | ❌ |
| CRUD (read/write) | ✅ | ✅ | ❌ | ✅ | ⚠️ CLI | ⚠️ CLI | ⚠️ CLI | ⚠️ CLI | 📐 |
| Listen (push) | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Terminálový přístup | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | 📐 |
| Discovery routerů | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ MNDP | ❌ | ✅ MNDP | ❌ |
| Bez IP konektivity | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ |
| Šifrování | ❌ | ✅ TLS | ❌ | ✅ HTTPS | ❌ | ❌ | ✅ AES | ✅ AES | ✅ SSH |
| NuGet balíček | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 📐 |

Legenda: ⚠️ CLI = CRUD přes CLI parsing (`print as-value`), omezené capabilities (bez Listen/Streaming)

---

## 7. Přehled stavu testů (po kapitolách A–H)

> Naposledy aktualizováno: 2026-06-07.

### Produkční testy (`tik4net.tests/`)

| Test třída | Protokol | Transport | Stav | Výsledek |
|---|---|---|---|---|
| `ApiProtocolTest` | MikroTik API | TCP 8728 | ✅ **zelená** | 15+ tříd, plné pokrytí |
| `RestProtocolTest` / TestBase | REST API | HTTP/HTTPS | ✅ **zelená** | 136 pass, 34 skip |
| `TelnetProtocolTest` / TestBase | Telnet | TCP 23 | ✅ **zelená** | 139 pass, 41 skip, 0 fail |
| `MacTelnetProtocolTest` / TestBase | MAC-Telnet | UDP 20561 ct=0x0015 | ✅ **zelená** | 3 testy pass |
| `WinboxCliProtocolTest` / TestBase | WinBox CLI | TCP 8291 | ✅ **zelená** | 2+9 pass, 6 skip |
| `WinboxCliMacProtocolTest` / TestBase | WinBox CLI/MAC | UDP 20561 ct=0x0f90 | ✅ **zelená** | 2 testy pass |

### PoC / experimental testy (`tik4net.tests/Protocols/`)

| Test třída | Protokol | Stav | Poznámka |
|---|---|---|---|
| `WinboxTcpProtocolTest` | Winbox M2 native (TCP) | ✅ 7/7 | EC-SRP5 + AES + mproxy + mepty v PoC klientech |
| `WinboxMacProtocolTest` | Winbox M2 native (MAC) | ⚠️ `[Ignore]` EXPERIMENTAL | WinboxMacClient existuje, neverifikovaný |
| `MacLayerTest` (starý) | MAC-Telnet PoC | superseded | Nahrazen produkční kapitolou E |

### Sdílená krypto vrstva (`tik4net/Crypto/`)

Po přesunu z PoC do core (kapitoly E, G):

| Soubor | Obsah |
|---|---|
| `EcSrp5.cs` | Curve25519 Weierstrass + EC-SRP5 matematika (jediná kopie, sdílena MAC-Telnet + Winbox) |
| `WinboxStreamCrypto.cs` | `DeriveStreamKeys`, `HkdfExpand`, AES-128-CBC encrypt/decrypt |
