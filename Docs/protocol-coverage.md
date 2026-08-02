# tik4net — Protocol Coverage Overview

> Local file, not tracked in git. Last updated: 2026-06-07.
> A summary view across all of MikroTik's communication protocols.

---

## Legend

| Symbol | Status |
|---|---|
| ✅ Production | Implemented in the library, unit tests, NuGet |
| 🔬 PoC | Working code, but only in a test file, not in the library |
| 📄 Research | Protocol documented, no code |
| 📐 Design | Architecturally designed for v4.x, not implemented |
| ❌ Unexplored | Unknown |

---

## Protocol matrix

| Protocol | Transport | Port | Layer | Status | File |
|---|---|---|---|---|---|
| MikroTik API | TCP | 8728 | L3/IP | ✅ **Production** | `tik4net/Api/ApiConnection.cs` |
| MikroTik API/SSL | TCP+TLS | 8729 | L3/IP | ✅ **Production** | `tik4net/Api/ApiConnection.cs` (isSsl flag) |
| MNDP Discovery | UDP broadcast | 5678 | L3/IP | ✅ **Production** | `tik4net/Mndp/MndpHelper.cs` |
| REST API | HTTP(S) | 80/443 | L3/IP | ✅ **Production** | `tik4net/Rest/RestConnection.cs` |
| Telnet | TCP | 23 | L3/IP | ✅ **Production** | `tik4net/Telnet/TelnetConnection.cs` |
| MAC Telnet | UDP broadcast | 20561 | L2/MAC | ✅ **Production** | `tik4net/MacTelnet/MacTelnetConnection.cs` |
| WinBox CLI | TCP | 8291 | L3/IP | ✅ **Production** | `tik4net/WinboxCli/WinboxCliConnection.cs` |
| WinBox CLI/MAC | UDP | 20561 | L2/MAC | ✅ **Production** | `tik4net/WinboxCliMac/WinboxCliMacConnection.cs` |
| Winbox M2 (native) | TCP | 8291 | L3/IP | 🔬 **PoC** | `tik4net.tests/Protocols/Clients/WinboxM2Client.cs` |
| SSH | TCP | 22 | L3/IP | 📐 **Design** | `_notes/4x-ideas.md` (requires SSH.NET) |

---

## Detail: MikroTik API (✅ Production)

**TCP port 8728 (plain) / 8729 (SSL)**

Production implementation, two NuGet layers.

### Capabilities

| Capability | API | API/SSL |
|---|---|---|
| Opening a connection | ✅ | ✅ |
| Login (≥ 6.43 challenge-response) | ✅ | ✅ |
| Login (< 6.43 legacy MD5) | ✅ | ✅ |
| ExecuteNonQuery / ExecuteScalar / ExecuteList | ✅ | ✅ |
| ExecuteAsync (callback push = Listen) | ✅ | ✅ |
| Tags (synchronization tags) | ✅ | ✅ |
| O/R mapper (LoadAll, Save, Delete…) | ✅ | ✅ |
| Streaming (Torch, ongoing Ping) | ✅ | ✅ |
| Encryption | ❌ | ✅ |
| Requires IP connectivity | ✅ (yes) | ✅ (yes) |

### Key classes

```
tik4net/Api/ApiConnection.cs      — ITikConnection implementation
tik4net/Api/ApiCommand.cs         — ITikCommand implementation
tik4net/ConnectionFactory.cs      — entry point
tik4net.objects/                  — O/R mapper + entity classes
```

### Test coverage

`ConnectionTest.cs`, `CrudTest.cs`, `InterfaceTest.cs`, `IpFirewallTest.cs`, … (15+ test classes)

---

## Detail: MNDP Discovery (✅ Production)

**UDP broadcast port 5678 (IPv4) + multicast ff02::1 (IPv6)**

The router responds to an MNDP broadcast with a record describing itself.

### Capabilities

| Capability | Status |
|---|---|
| IPv4 broadcast discovery | ✅ |
| IPv6 multicast discovery | ✅ |
| Parsing MAC, IPv4, IPv6, version, board, identity, uptime | ✅ |
| `stopWhenFirstFound` optimization | ✅ |
| Writing / router management | ❌ (read-only discovery only) |

### Usage

```csharp
IEnumerable<TikInstanceDescriptor> routers = MndpHelper.Discover(stopWhenFirstFound: true);
// TikInstanceDescriptor: Mac, IPv4, IPv6, Version, BoardName, Identity, Uptime, Platform, ...
```

---

## Detail: Winbox M2 (🔬 PoC)

**TCP port 8291**

Implementation: `tik4net.tests/WinboxM2CatalogTest.cs` (~1700 lines, self-contained).
It doesn't test the Winbox UI logic — it tests data access over the Winbox protocol directly from .NET.

### PoC capabilities

| Capability | Status | Test |
|---|---|---|
| EC-SRP5 authentication (RouterOS ≥ 6.43) | ✅ | all catalog tests |
| Legacy MD5 authentication (older ROS) | ✅ | fallback in `Authenticate()` |
| AES-128-CBC encrypted session | ✅ | all tests after auth |
| IP-layer smoke test (raw TCP handshake) | ✅ | `WinboxM2_IpLayer_TcpPort8291_*` |
| Reading files via mproxy [2,2] | ✅ | `WinboxM2_ReadListCatalog_*` |
| Parsing the plugin catalog (`/home/web/webfig/list`) | ✅ | `WinboxM2_ParseCatalog_*` |
| System info (version, board, arch, identity) | ✅ | `WinboxM2_GetSystemInfo_*` |
| Mepty terminal (PTY session handler [76]) | ✅ | `WinboxM2_ListInterfaces_*` |
| VT100 negotiation (cursor dimension probes) | ✅ | `Vt100State` class |
| Command via terminal + output parsing | ✅ | `/interface print` → `List<InterfaceEntry>` |
| Set/get interface comment via mepty | ✅ | `WinboxM2_SetAndVerify_InterfaceEther1Comment` |

### What the PoC still can't do

- Winbox over MAC address (Layer 2 Winbox) — L2 transport unexplored
- Keepalive / reconnect of the encrypted session
- Full handler catalog (dozens exist, only [2,2], [13,4], [76] mapped)
- Not in the production library, only in tests

### Key classes in the PoC

```
WinboxM2Client    — transport + EC-SRP5 + AES + mproxy + mepty
Vt100State        — VT100 cursor state machine for terminal negotiation
CatalogEntry      — plugin catalog entry (name, version, size, crc)
SystemInfo        — board, version, arch, identity from handler [13,4]
InterfaceEntry    — result of parsing /interface print
```

### Important technical details (see also memory/project_winbox_m2_poc.md and _notes/winbox-terminal-findings.md)

- **DataAvailable polling**: never call `RecvAndDecrypt` with a short timeout — a mid-frame timeout corrupts the TCP stream
- **TLV type 0xA0 (str_array)**: must be handled explicitly in `SkipTypeBytes`, otherwise the parser misaligns
- **8-bit CSI 0x9B**: RouterOS 7.x uses this as an alternative to ESC[
- **"Change your password" nag**: RouterOS shows a prompt before the CLI; Ctrl-C (0x03) must be sent
- **RouterOS comment format**: displayed as `;;; text` (triple-semicolon), not as `comment=text`
- **Phase 2 break condition**: `TrimEnd().EndsWith("] >")` — not `Contains`, because of command echo
- **DrainEncryptedFrames(600 ms)**: mandatory between sections — without it, a new session receives stale data

---

## Detail: MAC Telnet (✅ Production)

**UDP broadcast port 20561, L2/MAC** — implemented in chapter E (2026-06-04)

Access to the router without IP connectivity — via MAC address.

### Capabilities

| Capability | Status |
|---|---|
| EC-SRP5 authentication (Curve25519 Weierstrass, RouterOS ≥ 6.43) | ✅ |
| Legacy MD5 authentication (older ROS) | ✅ |
| L2 UDP transport (pure .NET, no Pcap) | ✅ |
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ |
| MNDP discovery to locate the router | ✅ |
| Configurable login timeout | ✅ |
| Shared crypto layer in `tik4net/Crypto/` | ✅ |

### Key classes

```
tik4net/MacTelnet/MacTelnetConnection.cs   — ITikConnection : CliConnectionBase
tik4net/MacTelnet/MacTelnetUdpClient.cs    — internal async UDP client
tik4net/MacTelnet/MacLayerTransport.cs     — public abstract base for the MAC layer
tik4net/Crypto/EcSrp5.cs                  — shared EC-SRP5 math (MAC + Winbox)
tik4net/Crypto/WinboxStreamCrypto.cs       — AES-128-CBC (shared with Winbox)
```

### Test coverage

`MacTelnetProtocolTest.cs` — login + list interfaces + set comment, 3 tests green.

---

## Detail: REST API (✅ Production)

**HTTP port 80 / HTTPS port 443, RouterOS ≥ 7.1** — implemented in chapter A (2026-05-31)

### Capabilities

| Capability | Status |
|---|---|
| GET (print) / POST (add) / PATCH (set) / DELETE (remove) | ✅ |
| HTTP Basic auth | ✅ |
| HTTPS (SSL variant) | ✅ |
| `System.Text.Json` serialization (BCL, no extra dependency) | ✅ |
| `ITikConnectionCapabilities` — capability gating | ✅ |
| Listen/push (`ExecuteAsync`) | ❌ `NotSupportedException` |
| Streaming (Torch, monitor-traffic follow) | ❌ `NotSupportedException` |
| `/unset` → default value | ⚠️ `PATCH {field:null}` sets an empty string, not the default |

### Key classes

```
tik4net/Rest/RestConnection.cs        — ITikConnection + ITikConnectionCapabilities
tik4net/Rest/RestCommand.cs           — ITikCommand
tik4net/Rest/RestRequestBuilder.cs    — mapping of API path → HTTP verb/URL/JSON
tik4net/TikConnectionSetup.cs         — CreateRestConnection() / CreateRestSslConnection()
```

### Test coverage

136 pass, 34 skip (streaming/listen), 10 fail (preexisting / CLI). RouterOS 7.21.4.

---

## Detail: Telnet (✅ Production)

**TCP port 23** — implemented in chapter C (2026-05-31)

The IP equivalent of MAC Telnet — identical terminal output (VT100), same RouterOS CLI.

### Capabilities

| Capability | Status |
|---|---|
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ |
| Plain text authentication (login/password prompt) | ✅ |
| Telnet IAC option negotiation (minimal, ~30 LOC) | ✅ |
| VT100 stripping (`VtStripper`) | ✅ |
| Shares the CLI layer with MAC Telnet, SSH | ✅ |

### Key classes

```
tik4net/Telnet/TelnetConnection.cs     — ITikConnection : CliConnectionBase
tik4net/Cli/CliConnectionBase.cs       — shared CLI base
tik4net/Cli/VtStripper.cs             — ANSI escape remover (shared)
```

### Test coverage

139 pass, 41 skip, 0 fail.

---

## Detail: WinBox CLI / WinBox CLI/MAC (✅ Production)

**WinBox CLI: TCP port 8291** — implemented in chapter G (2026-06-05)
**WinBox CLI/MAC: UDP port 20561, L2/MAC** — implemented in chapter H (2026-06-05)

CLI access via the WinBox M2 protocol — the client opens a mepty (PTY handler [76]) and works
inside it like a regular CLI transport (same parsing as Telnet/MAC-Telnet).

### Capabilities

| Capability | Status |
|---|---|
| EC-SRP5 authentication + AES-128-CBC session | ✅ (both transports) |
| Mepty terminal (handler [76], VT100 negotiation) | ✅ (both transports) |
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ (both transports) |
| TCP transport (port 8291) | ✅ (WinboxCli) |
| MAC/UDP transport (port 20561, client_type 0x0f90) | ✅ (WinboxCliMac) |
| Transport-agnostic mepty engine (`IWinboxM2Channel`) | ✅ |
| SESSION_ID > 255 as u32 | ✅ (root-cause fix vs. PoC) |
| Shared crypto layer `tik4net/Crypto/` | ✅ |

### Key classes

```
tik4net/WinboxCli/WinboxCliConnection.cs       — ITikConnection : CliConnectionBase (TCP)
tik4net/WinboxCliMac/WinboxCliMacConnection.cs — ITikConnection : CliConnectionBase (MAC)
tik4net/WinboxCli/WinboxCliClient.cs           — mepty [76] + VT100, transport-agnostic
tik4net/Winbox/IWinboxM2Channel.cs             — channel abstraction (TCP/MAC)
tik4net/Winbox/WinboxM2Session.cs              — TCP channel (EC-SRP5+AES+Send/Receive)
tik4net/Winbox/WinboxMacM2Session.cs           — MAC UDP channel (inherits MacLayerTransport)
tik4net/Winbox/M2Message.cs                    — TLV builder + parser
```

### Test coverage

`WinboxCliProtocolTest`: 2/2 green + InterfaceTest 9 pass / 6 skip.
`WinboxCliMacProtocolTest`: 2/2 green (WinboxCli + WinboxCliMac + MacTelnet regression 6/6).

---

## Detail: SSH (📐 Design)

**TCP port 22, requires SSH.NET (Renci.SshNet)**

Two levels: `SshConnection : ITikConnection` via `exec + print as-value` and
`SshTerminalSession : ITikSession` for an interactive PTY.
See `_notes/4x-ideas.md` item 4 and `_notes/4x-package-architecture.md`.

---

## Capability matrix (current status)

| Capability | API | API/SSL | MNDP | REST | Telnet | MAC Telnet | WinboxCli | WinboxCliMac | SSH |
|---|---|---|---|---|---|---|---|---|---|
| Production code | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| PoC code | — | — | — | — | — | — | — | — | ❌ |
| CRUD (read/write) | ✅ | ✅ | ❌ | ✅ | ⚠️ CLI | ⚠️ CLI | ⚠️ CLI | ⚠️ CLI | 📐 |
| Listen (push) | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Terminal access | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ | 📐 |
| Router discovery | ❌ | ❌ | ✅ | ❌ | ❌ | ✅ MNDP | ❌ | ✅ MNDP | ❌ |
| No IP connectivity required | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ |
| Encryption | ❌ | ✅ TLS | ❌ | ✅ HTTPS | ❌ | ❌ | ✅ AES | ✅ AES | ✅ SSH |
| NuGet package | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 📐 |

Legend: ⚠️ CLI = CRUD via CLI parsing (`print as-value`), limited capabilities (no Listen/Streaming)

---

## 7. Test status overview (through chapters A–H)

> Last updated: 2026-06-07.

### Production tests (`tik4net.tests/`)

| Test class | Protocol | Transport | Status | Result |
|---|---|---|---|---|
| `ApiProtocolTest` | MikroTik API | TCP 8728 | ✅ **green** | 15+ classes, full coverage |
| `RestProtocolTest` / TestBase | REST API | HTTP/HTTPS | ✅ **green** | 136 pass, 34 skip |
| `TelnetProtocolTest` / TestBase | Telnet | TCP 23 | ✅ **green** | 139 pass, 41 skip, 0 fail |
| `MacTelnetProtocolTest` / TestBase | MAC-Telnet | UDP 20561 ct=0x0015 | ✅ **green** | 3 tests pass |
| `WinboxCliProtocolTest` / TestBase | WinBox CLI | TCP 8291 | ✅ **green** | 2+9 pass, 6 skip |
| `WinboxCliMacProtocolTest` / TestBase | WinBox CLI/MAC | UDP 20561 ct=0x0f90 | ✅ **green** | 2 tests pass |

### PoC / experimental tests (`tik4net.tests/Protocols/`)

| Test class | Protocol | Status | Note |
|---|---|---|---|
| `WinboxTcpProtocolTest` | Winbox M2 native (TCP) | ✅ 7/7 | EC-SRP5 + AES + mproxy + mepty in the PoC clients |
| `WinboxMacProtocolTest` | Winbox M2 native (MAC) | ⚠️ `[Ignore]` EXPERIMENTAL | WinboxMacClient exists, unverified |
| `MacLayerTest` (old) | MAC-Telnet PoC | superseded | Replaced by production chapter E |

### Shared crypto layer (`tik4net/Crypto/`)

After the move from PoC into core (chapters E, G):

| File | Contents |
|---|---|
| `EcSrp5.cs` | Curve25519 Weierstrass + EC-SRP5 math (single copy, shared by MAC-Telnet + Winbox) |
| `WinboxStreamCrypto.cs` | `DeriveStreamKeys`, `HkdfExpand`, AES-128-CBC encrypt/decrypt |
