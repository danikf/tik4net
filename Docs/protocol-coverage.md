# tik4net — Protocol Coverage Overview

> A summary view across all of MikroTik's communication protocols and the `TikConnectionCapability`
> flags each transport declares. The authoritative source is the code — specifically
> `tik4net/Rest/TikConnectionCapability.cs` (the flag definitions) and
> `tik4net.unittests/ConnectionCapabilityMatrixTests.cs` (which pins the exact flag set per
> `TikConnectionType` and fails the build if code and docs drift apart). This file, `README.md`'s
> transport table, and the *Connection types and capabilities* wiki page are expected to agree with
> that test at all times — if a flag changes, all three need a matching edit.

---

## Legend

| Symbol | Status |
|---|---|
| ✅ Production | Implemented in the library, unit/integration tested, shipped in a NuGet package |
| 📐 Design | Architecturally planned, not implemented |
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
| SSH | TCP | 22 | L3/IP | ✅ **Production** | `tik4net.ssh/SshConnection.cs` (satellite package, `Renci.SshNet` dependency) |
| MAC Telnet | UDP broadcast | 20561 | L2/MAC | ✅ **Production** | `tik4net/MacTelnet/MacTelnetConnection.cs` |
| WinBox CLI | TCP | 8291 | L3/IP | ✅ **Production** | `tik4net/WinboxCli/WinboxCliConnection.cs` |
| WinBox CLI/MAC | UDP | 20561 | L2/MAC | ✅ **Production** | `tik4net/WinboxCliMac/WinboxCliMacConnection.cs` |
| WinBox Native (structured M2) | TCP | 8291 | L3/IP | ✅ **Production** | `tik4net/WinboxNative/WinboxNativeConnection.cs` |
| WinBox Native/MAC (structured M2) | UDP | 20561 | L2/MAC | ✅ **Production** | `tik4net/WinboxNativeMac/WinboxNativeMacConnection.cs` |

That is 11 `TikConnectionType` values (`Api`, `ApiSsl`, `Rest`, `RestSsl`, `Ssh`, `Telnet`,
`MacTelnet`, `WinboxCli`, `WinboxCliMac`, `WinboxNative`, `WinboxNativeMac`) plus MNDP as a
standalone discovery helper — matching the "12 transports" figure in the top-level `CLAUDE.md`.
`Api_v2`/`ApiSsl_v2` are `[Obsolete]` aliases for `Api`/`ApiSsl` and are not separate transports.

---

## Capability flags

`TikConnectionCapability` (`tik4net/Rest/TikConnectionCapability.cs`) is a `[Flags]` enum:
`Crud`, `Listen`, `Streaming`, `RawSentences`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`,
`CancelInFlight`. A connection that does not implement `ITikConnectionCapabilities` is treated as
supporting **nothing** (fail-closed) — see `connection.Supports(...)` / `connection.Require(...)`.

Four flag sets recur across the transport families:

| Name | Flags | Who declares it |
|---|---|---|
| **Full** | `Crud`, `Listen`, `Streaming`, `RawSentences`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`, `CancelInFlight` | `Api`, `ApiSsl` |
| **Cli** | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands` | `Telnet`, `Ssh`, `MacTelnet`, `WinboxCli`, `WinboxCliMac` (all inherit `CliConnectionBase`) |
| **Native** | `Crud`, `Listen`, `SafeMode`, `AsyncCommands`, `CancelInFlight` | `WinboxNative`, `WinboxNativeMac` |
| **Rest** | `Crud`, `Listen`, `AsyncCommands`, `CancelInFlight` | `Rest`, `RestSsl` (stateless HTTP — no Streaming, no SafeMode) |

`Listen` outside the binary API is emulated by polling (re-issuing a snapshot on a background
worker), not server push — on REST because the router's own `listen` never flushes anything, which
is measured in [`findings-rest-api.md`](findings-rest-api.md) §12, not assumed from "REST is
stateless". `Streaming` (`ExecuteListWithDuration`, a blocking multi-row read within one command
exchange) is binary-API only. `RawCommand` on the CLI family sends a verbatim CLI line; WinBox
Native does **not** report it (its wire form is a numeric M2 message, not a string — use a CLI
transport for raw access over that channel).

`AsyncCommands` (the `Execute*Async` surface) and `CancelInFlight` (a token that stops a command
already on the wire and still leaves the connection usable) were rolled out per transport: REST
first, then the CLI family, then the binary API, then WinBox native (P2.8). Note what
`CancelInFlight` does **not** promise everywhere: that the router stops working. On REST it does not
— aborting the HTTP request frees the caller while RouterOS runs the command to the end (§12.1).

On the **binary API** it does, and that is the difference between a cancel and an abandon: the
client sends `/cancel tag=N`, the router answers the cancelled command with `!trap interrupted` +
`!done`, both are consumed, and the connection is left framed and immediately usable. This is the
one transport where cancelling is an operation the protocol defines rather than a decision to stop
listening. It rests on `Tagging`: an async command is therefore always tagged, whatever
`SendTagWithSyncCommand` says, because `/cancel` addresses a tag. Since P2.3 the API also has one
reader per connection dispatching each sentence to the tag that asked for it, so an async command
holds no thread of its own.

The CLI family declares `AsyncCommands` **without** `CancelInFlight`, and that gap is intrinsic
rather than scheduled: a RouterOS terminal answers with an unframed byte stream — no sentence
boundary, no request id — so a read abandoned mid-command leaves output that the *next* command
reads as its own. That does not throw; it returns the wrong answer. So the caller's token is never
handed to the transport read: it is honoured before dispatch, and after dispatch it is reported once
the response has been drained (`TikCancellationMode.Cooperative`, the default). A caller who would
rather lose the connection than wait sets `TikCancellationMode.AbandonAndClose`, which cuts the read
**and closes the session** — a close, never a silent skip. See the XML doc on
`TikConnectionCapability` for the full per-flag semantics, including how `/tool/torch` is handled
differently per transport family.

**WinBox native** declares both flags, and its `CancelInFlight` covers two cases with different
strength. For a **streaming window** — torch, ping, scan, traceroute, bandwidth-test — cancelling
sends the window's `.jg`-declared `cancelcmd`, which is exactly what WinBox sends when its window is
closed, and the router stops. The catalog declares one per `startcmd` for every streaming window (68
pairs on 7.23.2: roteros 44, wlan6 10, ppp 5, wave2 5, advtool 3, secure 1), so this is not a
best-effort guess about the protocol — it is the protocol's own stop, the M2 equivalent of
`/cancel tag=N`. For an **ordinary round trip** (getall/set/add) M2 has no cancel verb, so cancelling
frees the caller and drops the registration while the router finishes; that is safe because the
reader loop dispatches by request id, so the late reply is identified and discarded instead of being
handed to whichever command asked next. Same guarantee as REST, for the same reason.

Field **encoding and decoding** can themselves issue a `getall`, to translate a referenced record
between its name and its numeric id — a `list=lan` on a write, an interface id rendered back as
`ether1` on a read. Those lookups used to run synchronously on the awaiting thread; they no longer
do, and no round trip on an awaited command is made from a blocked thread.

The encoder and the decoder are still synchronous — `WinboxFieldResolver.EncodeField` takes a plain
`Func` and `DecodeRecord` returns a dictionary — because making them `async` would have rippled
through ~1000 lines of reverse-engineered encoder with no deterministic coverage. Instead the round
trips are **hoisted out and awaited first**, and both prefetches ask the synchronous code itself what
it will need rather than re-deriving it:

- **encode** runs the encoder twice. The first pass answers every reference lookup with a placeholder
  and records the question; its bytes are discarded. The recorded lookups are resolved in one awaited
  batch — one `getall` per distinct table, so several references to the same table cost one round trip
  rather than one each — and the second pass encodes against the answers. A command that references
  nothing (the common case) collects nothing and keeps the first pass, at exactly the previous cost.
- **decode** runs the decoder in a collecting mode that notes which referenced tables a row would make
  it read, fetches those, and then decodes for real.

Asking the decoder is not a stylistic preference: predicting the tables from the `.jg` field map
instead fetched tables the decode never consults, and since the id → name map is cached for the
connection's lifetime, one such fetch renders every record added afterwards as a bare numeric id.
Printing `/interface/list`, whose own rows reference interface lists, was enough to poison it.

The blocking lookups remain as the fallback and still serve the synchronous monitor round, which
drives its own loop.

---

## Detail: MikroTik API (✅ Production)

**TCP port 8728 (plain) / 8729 (SSL)**

The reference transport — declares the **Full** flag set explicitly rather than relying on the
"no interface = supports everything" fallback.

### Capabilities

| Capability | API | API/SSL |
|---|---|---|
| Opening a connection | ✅ | ✅ |
| Login (≥ 6.43 challenge-response) | ✅ | ✅ |
| Login (< 6.43 legacy MD5) | ✅ | ✅ |
| ExecuteNonQuery / ExecuteScalar / ExecuteList | ✅ | ✅ |
| `Crud`, `Listen`, `Streaming`, `RawSentences`, `Tagging`, `SafeMode`, `RawCommand` | ✅ all | ✅ all |
| O/R mapper (LoadAll, Save, Delete…) | ✅ | ✅ |
| Streaming (Torch, ongoing Ping via `ExecuteListWithDuration`) | ✅ | ✅ |
| Encryption | ❌ | ✅ TLS |
| Requires IP connectivity | ✅ (yes) | ✅ (yes) |

### Key classes

```
tik4net/Api/ApiConnection.cs      — ITikConnection implementation
tik4net/Api/ApiCommand.cs         — ITikCommand implementation
tik4net/ConnectionFactory.cs      — entry point
tik4net.objects/                  — O/R mapper + entity classes
```

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

## Detail: REST API (✅ Production)

**HTTP port 80 / HTTPS port 443, RouterOS ≥ 7.1**

### Capabilities

| Capability | Status |
|---|---|
| GET (print) / POST (add) / PATCH (set) / DELETE (remove) | ✅ |
| HTTP Basic auth | ✅ |
| HTTPS (SSL variant) | ✅ |
| `System.Text.Json` serialization (BCL, no extra dependency) | ✅ |
| `ITikConnectionCapabilities` — declares `Crud`, `Listen`, `AsyncCommands`, `CancelInFlight` | ✅ |
| Listen/push (`ExecuteAsync`) | ✅ polled — the router's own `listen` is accepted and delivers nothing (§12) |
| `Execute*Async` with `CancellationToken` | ✅ native (`HttpClient`), cancellable mid-request |
| Streaming (Torch, monitor-traffic follow) | ❌ not supported — the response arrives in one lump |
| SafeMode | ❌ not supported |
| `/unset` → default value | ⚠️ `PATCH {field:null}` sets an empty string, not the default |

### Key classes

```
tik4net/Rest/RestConnection.cs        — ITikConnection + ITikConnectionCapabilities
tik4net/Rest/RestCommand.cs           — ITikCommand
tik4net/Rest/RestRequestBuilder.cs    — mapping of API path → HTTP verb/URL/JSON
tik4net/TikConnectionSetup.cs         — CreateRestConnection() / CreateRestSslConnection()
```

---

## Detail: Telnet (✅ Production)

**TCP port 23**

The IP equivalent of MAC Telnet — identical terminal output (VT100), same RouterOS CLI. Declares
the **Cli** flag set (`Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`) via
`CliConnectionBase`.

### Capabilities

| Capability | Status |
|---|---|
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ |
| Plain text authentication (login/password prompt) | ✅ |
| Telnet IAC option negotiation (minimal, ~30 LOC) | ✅ |
| VT100 stripping (`VtStripper`) | ✅ |
| Shares the CLI layer with SSH, MAC Telnet, WinBox CLI family | ✅ |

### Key classes

```
tik4net/Telnet/TelnetConnection.cs     — ITikConnection : CliConnectionBase
tik4net/Cli/CliConnectionBase.cs       — shared CLI base
tik4net/Cli/VtStripper.cs             — ANSI escape remover (shared)
```

---

## Detail: SSH (✅ Production)

**TCP port 22, satellite package `tik4net.ssh` (dependency: `Renci.SshNet`)**

Drives the RouterOS CLI over an SSH PTY shell — same `CliConnectionBase` plumbing, parsing, and
capability set (**Cli**: `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`) as
Telnet/MAC-Telnet/WinBox CLI.
Kept out of the core `tik4net` package specifically so consumers who don't need SSH don't pull in
`Renci.SshNet`.

### Capabilities

| Capability | Status |
|---|---|
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ |
| `Crud`, `Listen` (poll-emulated), `SafeMode`, `RawCommand`, `AsyncCommands` | ✅ |
| Streaming (`ExecuteListWithDuration`) | ❌ not supported (CLI family, not binary API) |
| Registration via `ConnectionFactory` | requires one-time `tik4net.Ssh.Tik4NetSsh.Register()` |

### Key classes

```
tik4net.ssh/SshConnection.cs      — ITikConnection : CliConnectionBase
tik4net.ssh/SshShellClient.cs     — SSH.NET shell/PTY wrapper
tik4net.ssh/Tik4NetSsh.cs         — ConnectionFactory registration helper
```

### Usage

```csharp
using tik4net.Ssh;
Tik4NetSsh.Register(); // once at startup, to use TikConnectionType.Ssh via ConnectionFactory
// or: var connection = setup.CreateSshConnection();
```

Requires the `ssh` service to be enabled on the router (`/ip/service set ssh disabled=no`).

---

## Detail: MAC Telnet (✅ Production)

**UDP broadcast port 20561, L2/MAC**

Access to the router without IP connectivity — via MAC address. Declares the **Cli** flag set.

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
| No IP connectivity required | ✅ |

### Key classes

```
tik4net/MacTelnet/MacTelnetConnection.cs   — ITikConnection : CliConnectionBase
tik4net/MacTelnet/MacTelnetUdpClient.cs    — internal async UDP client
tik4net/MacTelnet/MacLayerTransport.cs     — public abstract base for the MAC layer
tik4net/Crypto/EcSrp5.cs                  — shared EC-SRP5 math (MAC + WinBox)
tik4net/Crypto/WinboxStreamCrypto.cs       — AES-128-CBC (shared with WinBox)
```

---

## Detail: WinBox CLI / WinBox CLI/MAC (✅ Production)

**WinBox CLI: TCP port 8291**
**WinBox CLI/MAC: UDP port 20561, L2/MAC, `client_type=0x0f90`**

CLI access via the WinBox M2 protocol — the client opens a mepty (PTY handler `[76]`) and works
inside it like a regular CLI transport (same parsing as Telnet/MAC-Telnet). Both declare the
**Cli** flag set.

### Capabilities

| Capability | Status |
|---|---|
| EC-SRP5 authentication + AES-128-CBC session | ✅ (both transports) |
| Mepty terminal (handler `[76]`, VT100 negotiation) | ✅ (both transports) |
| CLI access via `CliConnectionBase` (`ITikConnection`) | ✅ (both transports) |
| `Crud`, `Listen` (poll-emulated), `SafeMode`, `RawCommand`, `AsyncCommands` | ✅ (both transports) |
| TCP transport (port 8291) | ✅ (WinboxCli) |
| MAC/UDP transport (port 20561, `client_type=0x0f90`) | ✅ (WinboxCliMac) |
| Transport-agnostic mepty engine (`IWinboxM2Channel`) | ✅ |
| SESSION_ID > 255 as u32 | ✅ (root-cause fix vs. the original PoC) |
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

---

## Detail: WinBox Native / WinBox Native/MAC (✅ Production)

**WinBox Native: TCP port 8291**
**WinBox Native/MAC: UDP port 20561, L2/MAC, `client_type=0x0f90`**

Structured M2 CRUD — no terminal. Performs `getall`/`get-one`/`set`/`add`/`remove`/`move` as typed
M2 calls, translating numeric WinBox field keys to/from RouterOS API field names via a
version-matched `.jg` catalog, so the O/R mapper works unchanged on top of it. Both declare the
**Native** flag set (`Crud`, `Listen`, `SafeMode`, `AsyncCommands`, `CancelInFlight` — no
`Streaming`, `RawSentences`, or `Tagging`, and no `RawCommand`: its raw wire form is a numeric M2
message, not a string, so use a CLI transport for raw access over WinBox).

### Capabilities

| Capability | Status |
|---|---|
| EC-SRP5 authentication + AES-128-CBC session | ✅ (both transports) |
| Structured CRUD (`getall`/`get-one`/`set`/`add`/`remove`/`move`), no terminal | ✅ (both transports) |
| `.jg`-driven field-key ↔ API-name translation | ✅ |
| `Crud`, `Listen` (via `.jg` `type:'query'` monitor window), `SafeMode` (RouterOS 7.18+) | ✅ (both transports) |
| `AsyncCommands` — `Execute*Async` awaited through the id-dispatching M2 reader loop | ✅ (both transports) |
| `CancelInFlight` — router-side `cancelcmd` on a streaming window; registration drop on a plain round trip | ✅ (both transports) |
| TCP transport (port 8291) | ✅ (WinboxNative) |
| MAC/UDP transport (port 20561, `client_type=0x0f90`) | ✅ (WinboxNativeMac) |
| `/tool/torch` via the `.jg` `type:'query'` monitor window (typed M2 fields, not text) | ✅ |
| Shared crypto layer `tik4net/Crypto/` | ✅ |

### Key classes

```
tik4net/WinboxNative/WinboxNativeConnection.cs       — ITikConnection : ITikConnectionCapabilities (TCP)
tik4net/WinboxNativeMac/WinboxNativeMacConnection.cs — ITikConnection : ITikConnectionCapabilities (MAC)
```

See `Docs/winbox-native-m2-protocol.md` for the handler/command model and streaming monitor
protocol, and `Docs/jg-catalog-format.md` for the `.jg` catalog format itself.

### Path coverage vs. the binary API (measured 2026-08-15, RouterOS 7.23.2)

`WinboxNativePathMapAuditTest` (integration, `[Ignore]`d — run it by hand) reads every O/R-mapper
entity path over the binary API and over WinBox-native and compares row counts and field names.
Result: **every path the API reaches is reachable natively** except `/routing/bgp/advertisements`,
which WinBox exposes as an action on the BGP session window rather than as a table.

What the audit does NOT yet agree on is the FIELD vocabulary of some of those tables. These are
decode/encode-layer gaps, not path-map gaps, and they are the next piece of work:

| Class | Symptom | Examples |
|---|---|---|
| enum / set / sentinel decode | the raw wire form reaches the caller instead of the API value | `/ip/proxy` port `[8080]`, `/ip/ssh` ciphers `[0]`, `/ip/ipsec/proposal` pfs-group `2`, `/system/logging/action` syslog-severity `4294967295`, `/ip/proxy/access` method `''` |
| kind-scoped parameters | the API prefixes a parameter with the record's kind, WinBox labels it inside that kind's pane | `/queue/type` `pcq-rate`, `/system/logging/action` `memory-lines` / `disk-file-name` |
| API-only fields | the field exists in RouterOS but in no WinBox window, so a write that includes it cannot be sent | `/routing/ospf/*` `use-dn`, `/interface/wifi/security` `ft-preserve-vlanid` |
| action-window field collision | a `doit` window's arguments share the handler's field map with the record window, and the record's (read-only) field wins — the action's arguments are dropped | `/ip/ipsec/key/rsa` generate-key produced a nameless 1024-bit key instead of the requested name/2048 |

The last one is the same shape as the interface-subtype fix already made (fields belong to a WINDOW,
not to its handler); the fix is to scope a `doit`/`action` window's fields the same way.

Two limits below are the router's or the protocol's, not ours, and the suite skips only where they
are actually refused (`TestBase.SkipIfWinboxNativeCannot`): `add` of an interface SUBTYPE is refused
by the router itself (`0xFE0006 'unsupported device type'`), and list/array field writes
(`multinumber`, `multitristatearray`, `multi`) are not yet encodable over M2.

---

## Capability matrix (current status)

| Capability | Api / ApiSsl | Rest / RestSsl | Telnet | Ssh | MacTelnet | WinboxCli | WinboxCliMac | WinboxNative | WinboxNativeMac |
|---|---|---|---|---|---|---|---|---|---|
| Production code | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `Crud` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `Listen` (native push on API; poll-emulated elsewhere) | ✅ native | ❌ | ✅ poll | ✅ poll | ✅ poll | ✅ poll | ✅ poll | ✅ `.jg` query window | ✅ `.jg` query window |
| `Streaming` (`ExecuteListWithDuration`) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `RawSentences` | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `Tagging` (`.tag` multiplexing) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `SafeMode` | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ (RouterOS 7.18+) | ✅ (RouterOS 7.18+) |
| `RawCommand` | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| `AsyncCommands` (`Execute*Async`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `CancelInFlight` | ✅ `/cancel` | ✅ caller only | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `cancelcmd` on windows | ✅ `cancelcmd` on windows |
| Router discovery | ❌ | ❌ | ❌ | ❌ | ✅ MNDP | ❌ | ✅ MNDP | ❌ | ✅ MNDP |
| No IP connectivity required | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ | ✅ |
| Encryption | ❌ / ✅ TLS | ❌ / ✅ HTTPS | ❌ | ✅ SSH | ❌ | ✅ AES | ✅ AES | ✅ AES | ✅ AES |
| NuGet package | tik4net | tik4net | tik4net | **tik4net.ssh** | tik4net | tik4net | tik4net | tik4net | tik4net |

This table mirrors `tik4net.unittests/ConnectionCapabilityMatrixTests.cs` and `README.md`'s
transport table — `EveryTransportDeclaresTheDocumentedCapabilities` fails the build if a
transport's declared flags drift from what's written here.

---

## Test coverage

Two test projects, split by whether they need hardware — see the top-level `CLAUDE.md` and the
`mikrotik-tests` skill for the full picture:

- **`tik4net.unittests/`** (MSTest, `net8.0`, router-free, runs in CI) — includes
  `ConnectionCapabilityMatrixTests`, which pins the flag set per `TikConnectionType` shown above.
- **`tik4net.integrationtests/`** (MSTest, `net48`, ~410 methods, requires a live router) — one
  `*.runsettings` file per transport (`api`, `apissl`, `rest`, `restssl`, `telnet`, `ssh`,
  `mactelnet`, `winboxcli`, `winboxclimac`, `winboxnative`, `winboxnativemac`). A test that hits a
  capability its transport lacks reports **Inconclusive**, not a failure — that's the capability
  matrix above enforcing itself at test time rather than a skip to chase down.

Protocol-specific test classes live under `tik4net.integrationtests/Protocols/Tests/`
(`ApiProtocolTest`, `MacTelnetProtocolTest`, `WinboxCliProtocolTest`, `WinboxCliMacProtocolTest`,
`WinboxTcpProtocolTest`, `WinboxMacProtocolTest`, `WinboxNativeM2Test`, `WinboxNativeGetallTest`,
`WinboxNativeMacProtocolTest`, plus catalog/probe tests for the `.jg` format). Live pass/skip/fail
counts vary by router state and RouterOS version — run the suite (see the `mikrotik-tests` skill)
rather than trusting a snapshot committed here.

### Shared crypto layer (`tik4net/Crypto/`)

| File | Contents |
|---|---|
| `EcSrp5.cs` | Curve25519 Weierstrass + EC-SRP5 math (single copy, shared by MAC-Telnet + WinBox) |
| `WinboxStreamCrypto.cs` | `DeriveStreamKeys`, `HkdfExpand`, AES-128-CBC encrypt/decrypt |
