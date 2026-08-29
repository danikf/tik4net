# tik4net — Protocol Coverage Overview

> A summary view across all of MikroTik's communication protocols and the `TikConnectionCapability`
> flags each transport declares. The authoritative source is the code — specifically
> `tik4net/TikConnectionCapability.cs` (the flag definitions) and
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

---

## Capability flags

`TikConnectionCapability` (`tik4net/TikConnectionCapability.cs`) is a `[Flags]` enum:
`Crud`, `Listen`, `Streaming`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`,
`CancelInFlight`. A connection that does not implement `ITikConnectionCapabilities` is treated as
supporting **nothing** (fail-closed) — see `connection.Supports(...)` / `connection.Require(...)`.

Four flag sets recur across the transport families:

| Name | Flags | Who declares it |
|---|---|---|
| **Full** | `Crud`, `Listen`, `Streaming`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`, `CancelInFlight` | `Api`, `ApiSsl` |
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
already on the wire and still leaves the connection usable) are declared by every in-tree transport.
Note what `CancelInFlight` does **not** promise everywhere: that the router stops working. On REST
it does not — aborting the HTTP request frees the caller while RouterOS runs the command to the end
(§12.1).

On the **binary API** it does, and that is the difference between a cancel and an abandon: the
client sends `/cancel tag=N`, the router answers the cancelled command with `!trap interrupted` +
`!done`, both are consumed, and the connection is left framed and immediately usable. This is the
one transport where cancelling is an operation the protocol defines rather than a decision to stop
listening. It rests on `Tagging`: an async command is therefore always tagged, whatever
`SendTagWithSyncCommand` says, because `/cancel` addresses a tag. The API has one reader per
connection dispatching each sentence to the tag that asked for it, so an async command holds no
thread of its own.

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
`ether1` on a read. Those round trips are hoisted out of the awaited command and run off the
awaiting thread first, so no round trip on an awaited command is made from a blocked thread.

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

Asking the decoder which tables it needs is not a stylistic preference: predicting them from the
`.jg` field map instead can fetch tables the decode never consults — and because the id → name map
is cached for the connection's lifetime, one stray fetch renders every record added afterwards as a
bare numeric id instead of a name.

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
| GET (print) / PUT (add) / PATCH or POST (set) / DELETE (remove) | ✅ |
| HTTP Basic auth | ✅ |
| HTTPS (SSL variant) | ✅ |
| `System.Text.Json` serialization (BCL, no extra dependency) | ✅ |
| `ITikConnectionCapabilities` — declares `Crud`, `Listen`, `AsyncCommands`, `CancelInFlight` | ✅ |
| Listen/push (`ExecuteAsync`) | ✅ polled — the router's own `listen` is accepted and delivers nothing (§12) |
| `Execute*Async` with `CancellationToken` | ✅ native (`HttpClient`), cancellable mid-request |
| Streaming (Torch, monitor-traffic follow) | ❌ not supported — the response arrives in one lump |
| SafeMode | ❌ not supported |
| `/unset` | ✅ `POST /rest/<path>/unset` with `{".id", "value-name"}` — the router's own spelling, same as the binary API |

`add` is `PUT` to the path itself; `set` is `PATCH {id}` for a normal record but `POST <path>/set`
for a singleton (no `.id` to address); a bare action verb (`/tool/wol`, `/ip/ipsec/key/rsa
generate-key`, …) is `POST <path>/<verb>`. `unset` is the dedicated `POST .../unset` endpoint,
which clears a field regardless of its type — RouterOS validates a value against the field's own
type before accepting it, and `null` is no exception.

### Key classes

```
tik4net/Rest/RestConnection.cs        — ITikConnection + ITikConnectionCapabilities; HTTP-status/body -> trap-kind classification
tik4net/Connection/TikGenericCommand.cs — ITikCommand (shared by REST, the CLI family and WinBox native)
tik4net/Rest/RestRequestBuilder.cs    — mapping of API path → HTTP verb/URL/JSON
tik4net/TikTrapClassifier.cs          — message-text -> TikTrapKind, shared with the API and CLI transports
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
| SESSION_ID encoded as u32 (values are not limited to 255) | ✅ (both transports) |
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

See [winbox-native-m2-protocol.md](winbox-native-m2-protocol.md) for the handler/command model and
streaming monitor protocol, and [jg-catalog-format.md](jg-catalog-format.md) for the `.jg` catalog
format itself.

### Path coverage vs. the binary API

`TransportPathMapAuditTest` (integration, `[Ignore]`d — run it by hand from Test Explorer, or
`--filter AuditPathMapAgainstApi`, after touching the alias table, the `.jg` harvest, or on a new
RouterOS version) reads every O/R-mapper entity path over the binary API and over WinBox-native and
compares row counts and field names. **Every path the API reaches is reachable natively** except
`/routing/bgp/advertisements`, which WinBox exposes as an action on the BGP session window
(`dump-adv`) rather than as a table — there is nothing to `getall`.

A handful of paths reach the right window but still disagree with the API on field vocabulary —
decode-layer gaps, not path-map gaps. The audit's own `KnownFieldGaps` table is the source of truth;
as of the last run (RouterOS 7.23.2) it lists:

| Path | Gap |
|---|---|
| `/ip/route` | native lists routes the API's `print` filters out; distance/scope/vrf not decoded |
| `/interface/wireless/sniffer` | handler `[88,9]` returns sniffer statistics; the API returns settings |
| `/system/health` | board-gated singleton with no hardware sensors on CHR; `state`/`state-after-reboot` are API-only fields with no WinBox equivalent |

### The audit is not native's alone

The comparison is between the binary API and **one other transport**, named by
`TIK4NET_AUDIT_TRANSPORT`; nothing in it is WinBox-specific except the "no WinBox window" excuse. It
defaults to `WinboxNative`, which is the transport it was written for.

**Every transport has now been measured**, RouterOS 7.24, in one router state. `ROUTER-N/A=7` on all of
them — menus this router does not have — and `MISMATCH=0`, `VALUE-DIFF=0` and `WRITES 227/0/0` everywhere:

| Transport | OK | field names missing | run |
|---|---|---|---|
| API/SSL | 155 | **0/1341 (0%)** | 48 s |
| REST | 155 | **0/1341 (0%)** | 47 s |
| REST/SSL | 155 | **0/1344 (0%)** | 49 s |
| Telnet | 155 | 84/1344 (6%) | 2 m 36 s |
| SSH | 155 | 84/1341 (6%) | 2 m 14 s |
| MAC-Telnet | 155 | 84/1344 (6%) | 2 m 20 s |
| WinBox CLI | 155 | 84/1344 (6%) | 2 m 53 s |
| WinBox CLI/MAC | 155 | 84/1341 (6%) | 2 m 37 s |
| WinBox native | 154 + 1 KNOWN-GAP | 102/1328 (7%) | 44 s |
| WinBox native/MAC | 154 + 1 KNOWN-GAP | 105/1344 (7%) | 1 m 04 s |

The MAC-layer variants cost about a minute more than their TCP siblings, not the hours the per-command
latency suggested — a full audit over MAC-Telnet runs in the same 2-3 minutes as Telnet. The denominator
moves by a few fields between runs because the API's field count depends on which rows exist at the time.

**The four API-shaped transports report every field name the API does.** The CLI family is short 84, WinBox
native 102 — those are the remaining gap, and they are NAMES, not wrong values.

WinBox native also carries `KNOWN-GAP=1` (a menu with no WinBox window) and `VALUES-UNCOMPARED=1`; it is
the only transport with either, because it is the only one that resolves paths to M2 handlers rather than
typing the path.

**REST matches the binary API exactly** — same field names, same spellings, no writes refused. It renders
from the same internal representation the API does, so none of the CLI value classes below arise there.

Running the audit with an API-shaped transport as the PROBE found a defect in the audit itself: `.tag` is
the sentence sequence number our own API layer stamps on, not a field the router sent, and it counts up
per connection. Against Api it had never been compared, because no other transport produces it; the first
ApiSsl run reported 126 of 155 paths as `VALUE-DIFF`, every one of them `.tag`. The audit was comparing
itself. It is excluded from the value comparison now, as `.id` already was.

**The CLI family is interchangeable**, which is what sharing one parser and one command builder is
supposed to mean: Telnet, SSH and WinBox CLI now read identically, down to the single remaining
difference they all share.

The value differences are not defects one per line. They are four renderings, because a CLI read is
`:put [… print as-value]` and `as-value` gives the router's INTERNAL spelling where the API's `print`
gives the documented one:

| Class | API | CLI | |
|---|---|---|---|
| durations | `15s`, `1w`, `1d`, `5m` | `00:00:15`, `1w00:00:00`, `1d00:00:00`, `00:05:00` | **closed** |
| a number the API prints as a word | `mtu=auto`, `ttl=auto`, `horizon=none`, `mrru=disabled`, `max-sessions=unlimited`, `dscp=inherit` | `0`, `0`, `0`, `0`, `0`, `256` | **closed** |
| scaled fixed-point | `bucket-size=5`, `freq-drift=-47.516`, `gmt-offset=+02:00` | `5000`, `-47516`, `7200` | **closed** |
| IPv4 in an IPv6 slot | `local=192.168.4.236` | `::ffff:192.168.4.236` | **closed** |

All four are handled by `CliValueNormalizer`, in two different ways. A **duration** and an **IPv4-mapped
IPv6 address** say what they are, so they are recognised by shape. The other two cannot be: `mtu=0` is
`auto` and `mrru=0` is `disabled` while a `0` elsewhere is a zero, and `bucket-size=5000` is `5` only
because that field is scaled by a thousand. Those are keyed by FIELD NAME — which is exactly what the
parser is handed, so the gap was never the missing metadata it was first written up as.

Every entry in those tables was pinned by setting a NON-sentinel value on the router and reading it back
both ways, which is what keeps the rule from being a guess that happens to fit. It also caught one that
would have been wrong: **`dscp=0` is a real DSCP class**, read back as `0` over both transports — the
sentinel for `inherit` is `256`, outside the field's 0..63 range. Mapping its zero the way the five other
fields map theirs would have corrupted a legitimate value.

### A value with newlines in it: the JSON read, and what it costs

`as-value` is a `;`-separated `key=value` stream with no escaping, so a value carrying real line breaks —
`/file` `contents` — splits across what the parser reads as several fields and arrives as a DUPLICATE KEY:
the API's `<html>\n<head>…` comes back as `contents` twice, joined `<html>,<head>`.

The answer is `:put [:serialize to=json […]]`, which the CLI transports implement (`CliJsonParser`) and
which an entity opts into by marking the property `IsFreeText`. Thirteen properties are marked today — a
file body, script sources and `on-event`/`up-script` handlers, two regexps. The audit reads through the
same marker, so it measures the request the library actually makes rather than one it never sends.

**The JSON read is not a free upgrade**, which is why it is per-entity rather than the default: it renders
a duration as a date counted from the Unix epoch and truncates sub-second precision
(`arp-interval=100ms` → `00:00:00`). `CliValueNormalizer` converts the dates back for the duration fields
measured to need it; the milliseconds are gone for good. See
[findings-cli.md](findings-cli.md) for the measurements, including why `comment` is deliberately NOT on
this path.

### Still open

- **A comment containing `;`, `=` or a newline** is read incorrectly over a CLI transport unless its
  entity is on the JSON path for another reason — and it does not merely truncate, it invents a field.
  The trade for fixing it (every entity on the lossy JSON path) is worse than the failure.
- **A duplicate key from a truncated read.** The same signature as the newline case appeared once on
  WinBox CLI in a large `/ip/firewall/connection` read (`dstnat` = `false,dyi`, the tail of the following
  `dying=false`) and did not reproduce. A mis-split `as-value` stream has one recognisable shape whatever
  caused it, so the parser could refuse it rather than pass a joined value on.

The one thing a duration cannot say about itself is which unit its ZERO is in: as-value gives `00:00:00`
for both a `0s` field and a `0ms` one. `0s` is emitted; the millisecond fields
(`/interface/bonding` `up-delay`/`down-delay`) read `0s` where the API says `0ms`, which is the same
duration in another unit.

Reproducing the class needed no harness at all: `/ip/dns/print` over `Api` said `doh-timeout=5s`, over
`Telnet` `00:00:05`.

**`detail`, and what it hid.** A CLI read without `detail` returns the summary columns only, so the audit's
plain `print` made the CLI look short of a fifth of the API's vocabulary. Asking for `detail` on both sides
is wrong too — the binary API takes it as a word and 31 of the audited menus refuse it — so the audit
attempts it and falls back per path.

Adding it surfaced something better: asking `/ip/dns` for `detail` answers `bad parameter detail`, and the
CLI layer was turning that into an EMPTY RESULT. Every singleton menu then looked like a transport that
could not read singletons at all. The positional rule that already covered monitor snapshots — as-value
output is `key=value;…` or nothing, so text that parses to no record is the router saying why — now covers
ordinary reads as well.

### Value-rendering differences

The audit compares VALUES as well as names (rows paired by `.id`, volatile fields excluded). The first
run that did so found 26 paths whose window and field names were right while a value's rendering was
not; they were a handful of missing decoders rather than 26 defects, and **all of them are now
closed** — `interval`/duration (including the `.jg` `scale`), raw MAC, the zero-spelled-as-a-word
sentinels, the empty list, set order, date/epoch, the `age` uptime clock, one M2 key carrying two
fields, a list element that is a `union` or a `tuple`, the `multibits` bitmask, an `enm`'s unit
`postfix`, and the address:port pair. See `Docs/winbox-native-m2-protocol.md` §26–§30.

Three value differences remain, and on two of them the binary API is the side that knows less. They
are recorded in the audit's `KnownValueGaps` with the reason named, so a NEW disagreement on one of
them still fails the run.

| Path | Field | Difference |
|---|---|---|
| `/routing/table` | `fib` | a valueless presence flag over the API (`fib=`), which the mapper can only read as `false`; native reads the router's own bool and reports the real state |
| `/system/ntp/client` | `system-offset` | whole milliseconds on the wire, fractions over the API (`-23` vs `-23.622`), and it drifts constantly; `freq-drift` agrees exactly |
| `/interface/ethernet` | `auto-negotiation` | WinBox's field is the LINK's live state (`not-available` on a CHR's virtual NIC), the API's is the SETTING (`true`) — two fields, one label |

Last run, 2026-08-29 on RouterOS 7.24: `OK=154 KNOWN-GAP=1 MISMATCH=0 VALUE-DIFF=0 VALUES-UNCOMPARED=1
UNMAPPED=0 ROUTER-N/A=7`, and `FIELD-NAMES not reported by native: 96/1342 (7%)`. The same tally is pinned
with the run's date in [HISTORY.md](HISTORY.md#measurements-pinned-to-a-moment) — update both, or neither
will be trustworthy. The vocabulary the run measures
against is the one 62 seeded rows expose, which is why both halves of that fraction grew when the
fixtures did — an empty table has no fields to disagree about.

That second number is not an assertion — it is the shortfall the pass/fail check cannot see, because the
name check passes a path at half the API's vocabulary. It is reported so a green run cannot hide it.

For a name only the API reports, the run also proposes what native calls it, by naming the native-only
field carrying the same value on every row (`api-name?=winbox-name`). A proposal on a distinctive value is
usually the pairing; a proposal on a bool or a zero is a coincidence and has to be settled by writing a
value. See [winbox-native-m2-protocol.md §33.1–33.4](winbox-native-m2-protocol.md).

### Writes are audited the same way

All six write verbs are measured against the API by the same differential (§33.2d–e of
[winbox-native-m2-protocol.md](winbox-native-m2-protocol.md)): the same row is made, changed, cleared,
toggled, moved and removed over each transport, and only the fields the recipe set are compared. Last run:
`WRITES ok=204 different=0 refused=0 not-probeable=59`.

`enable`/`disable`, `unset` and `move` are not verbs on the native side at all — each is translated into
a field write, so one implementation has as many correct answers as there are field types. That is what a
per-transport verb test on a single path cannot see, and it is where `unset` turned out to be writing an
empty value instead of listing the field in the catalog's own unset array. A refusal counts as a finding only when the API's identical write succeeds — a table
already holding the fixture row refuses BOTH transports, and that is the router talking about the row,
not about native.

This matters because a read audit cannot see an empty field: a value never written is a value neither
side can disagree about. Two of the defects the write half found were read bugs as well.

The `not-probeable` residue is those already-occupied tables, where the probe cannot vary the field the
row keys on.

### One remaining limit, and it is ours

Every list shape the live catalog declares now encodes, bar the six in
[winbox-native-m2-protocol.md §32.4](winbox-native-m2-protocol.md) — four of them read-only, one of them
not a list at all, and the largest (`multibignumber`, 255 fields) blocked one layer earlier by an id
prefix the catalog does not know. What is left is declined by `WinboxFieldResolver.EncodeField` with a
loud `WinboxFieldResolutionException` rather than a silently wrong-typed scalar — **our encoder's gap,
not a protocol one**: the M2 wire format already carries array types for writes.

---

## Settled questions — do not re-investigate

- **`/ip/proxy` port, `/ip/ssh` ciphers, `/ip/ipsec/proposal` pfs-group, `/system/logging/action`
  syslog-severity, `/ip/proxy/access` method over WinBox native.** Not a decode gap. Fixed by
  following the full `enumfilter`/`defenum`/`pair` wrapper chain to a field's static enum map,
  decoding a literal (non-reference) `multinumber` per element, and treating only the M2 "flag down"
  / catalog-declared sentinel as unset rather than any field printing empty.
- **`/ip/ipsec/key/rsa` generate-key producing a nameless 1024-bit key over WinBox native.** Not a
  live defect — fixed by attributing an action window's fields to the action, not just its handler.
- **Deck-pane field writes being silently dropped on a label collision (e.g. `fq-codel`'s five
  fields losing to `codel`'s under the same labels).** Not a live defect — a pane field is now filed
  under both `<kind>-<label>` and its plain label, and `/queue/type` and `/system/logging/action`
  both agree with the API in the audit. What is still open is *read spelling* on the OTHER deck
  windows: only those two have a verified ground-truth table, so any other one may still report a
  pane field under the derived name rather than the API's.
- **`/ip/upnp/interfaces` reading rows with no `interface` field ("Missing field 'interface'").** Not
  a live defect — two windows share handler `[28,0]` and each numbers its fields from 1, so the
  per-handler map could not be inverted. Window-scoped field maps fixed it.
- **`/system/logging` `topics` reading as `[1]`, and its `add` being refused.** Not a live defect —
  `multitristatearray` now encodes and decodes; the `!` is a second KEY, not a value prefix.

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

This table mirrors the `Expected` flag sets pinned in
`tik4net.unittests/ConnectionCapabilityMatrixTests.cs` and `README.md`'s transport table.
`EveryTransportDeclaresTheDocumentedCapabilities` fails the build the moment a transport's declared
flags drift from that pinned set, so a change here that isn't also made in the test (and vice versa)
is the signal that one of the three has gone stale.

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
