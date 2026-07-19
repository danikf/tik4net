# tik4net architecture (4.0)

A map of the codebase for contributors. For *usage* documentation see the [wiki](https://github.com/danikf/tik4net/wiki); for agent-facing working rules see [CLAUDE.md](CLAUDE.md).

## Layer cake

```
tik4net.objects  — O/R mapper: [TikEntity]/[TikProperty] → metadata cache → CRUD extensions,
                   change tracking, list merge
        │
ITikConnection / ITikCommand  — transport-neutral contract + capability model
        │
   ┌────┴───────────────────────────────────────────────────────────────┐
   │ ApiConnection             — binary sentence protocol (the reference)│
   │ TikCommandConnectionBase  — shared base for all command transports  │
   │   ├─ CliConnectionBase    — Telnet, MAC-Telnet, WinBox CLI ×2, SSH  │
   │   ├─ RestConnection       — RouterOS 7.1+ REST/JSON                 │
   │   └─ WinboxNativeConnection — structured M2 (+ MAC variant)         │
   └─────────────────────────────────────────────────────────────────────┘
   Support: Mndp (discovery), Crypto (EC-SRP5, WinBox stream cipher),
            TikPath, TikQueryStack, PollingMonitorEngine, capability flags
```

## Packages

| Package | Project | TFM | Notes |
|---|---|---|---|
| `tik4net` | `tik4net/` | `netstandard2.0` | Core. Only runtime dep: `System.Text.Json` |
| `tik4net.entities` | `tik4net.objects/` | `netstandard2.0` | O/R mapper, 169 entities |
| `tik4net.ssh` | `tik4net.ssh/` | `netstandard2.0` | Satellite — isolates the `Renci.SshNet` dependency |
| `tik4net.testing` | `tik4net.testing/` | `netstandard2.0` | `TikFakeConnection` for router-free consumer tests |
| `tik4net.mcp` | `Tools/tik4net.mcp/` | .NET tool | Dev/debug MCP helper, not a user-facing library |

Non-shipping: `tik4net.console`, `tik4net.coreconsole`, `tik4net.examples`, `tik4net.torch`,
`Tools/tik4net.entitygenerator`, `Tools/tik4net.entityWikiImporter` (several are still legacy
non-SDK csproj — see plan item P5.1).

## Layer 1 — `tik4net` core

### The contract

`ITikConnection` (`tik4net/ITikConnection.cs`) covers lifecycle (`Open`/`OpenAsync` ×4, `Close`,
`Dispose`), configuration (`Encoding`, `SendTagWithSyncCommand`, timeouts), diagnostic events,
Safe Mode (`SafeModeTake`/`Release`/`Unroll`/`Get`), command factories, and raw sentence I/O
(`CallCommandSync`, `CallCommandAsync`).

`ITikCommand` is ADO.NET-shaped: `ExecuteNonQuery`, `ExecuteScalar`, `ExecuteSingleRow`,
`ExecuteList`, `ExecuteListWithDuration`, `ExecuteAsync`. Parameters are `ITikCommandParameter`
with a `TikCommandParameterFormat` of `Filter` (`?name=value`) or `NameValue` (`=name=value`).

Response sentences: `ITikReSentence` (`!re`), `ITikDoneSentence` (`!done`),
`ITikTrapSentence` (`!trap`), `ITikFatalSentence` (`!fatal`).

### Capabilities — the key cross-transport pattern

Transports differ in what they can physically do, so features are gated by
`TikConnectionCapability` flags (`tik4net/Rest/TikConnectionCapability.cs`):

`Crud`, `Listen`, `Streaming`, `RawSentences`, `Tagging`, `SafeMode`, `RawCommand`.

A connection declares its set via `ITikConnectionCapabilities.Capabilities`. Consumers check
`connection.Supports(cap)`; feature entry points call `connection.Require(cap, "feature")`, which
throws `TikConnectionCapabilityNotSupportedException`.

**Fail-closed:** a connection that does not implement `ITikConnectionCapabilities` supports
*nothing*. When adding a transport or a feature, declare the flag explicitly — never assume.

### Transports

`TikConnectionType` (`tik4net/TikConnectionType.cs`) — 12 live values (two `Api_v2` entries are
`[Obsolete(error: true)]`):

| Type | Transport | Folder | Notes |
|---|---|---|---|
| `Api` / `ApiSsl` | TCP 8728 / 8729, binary sentences | `Api/` | Reference impl; legacy + v6.43 challenge-response login; the only transport with `Streaming` and `Tagging` |
| `Rest` / `RestSsl` | HTTP 80 / HTTPS 443, JSON | `Rest/` | RouterOS 7.1+. `Crud` only — stateless, so no Safe Mode |
| `Telnet` | TCP 23, PTY CLI | `Telnet/`, `Cli/` | `print as-value` driven |
| `Ssh` | TCP 22, PTY CLI | `tik4net.ssh/` | Satellite package; register via `Tik4NetSsh.Register()` to use it through `ConnectionFactory` |
| `MacTelnet` | UDP 20561, MAC layer | `MacTelnet/` | EC-SRP5 auth; router MAC found via MNDP unless preset |
| `WinboxCli` / `WinboxCliMac` | TCP 8291 / UDP 20561 | `WinboxCli/`, `WinboxCliMac/` | Encrypted WinBox channel driving the `mepty` terminal |
| `WinboxNative` / `WinboxNativeMac` | TCP 8291 / UDP 20561 | `WinboxNative/`, `WinboxNativeMac/` | Structured M2 `getall`/`get-one`/`set`/`add`/`remove`/`move`; numeric field keys mapped to API names via a version-matched `.jg` catalog |

### `TikCommandConnectionBase`

Every non-API transport derives from it (`tik4net/Connection/`). It implements the whole
`ITikConnection` surface and factors real work down to three hooks used by `TikGenericCommand`:

- `RunPrint(TikCommandDescriptor)` → rows
- `RunAdd(...)` → new `.id`
- `RunNonQuery(...)`

Supporting pieces in `Connection/`: `TikPath` (path normalization), `TikQueryStack` (filter →
transport query translation), `PollingMonitorEngine` + `TikMonitorHandle` (poll+diff emulation of
`Listen` where it isn't native), `TikRawCommandExtensions` (`CreateRawCommand` pass-through),
`TikCommandModel` (normalized command/params representation).

CLI specifics live in `Cli/`: `CliCommandBuilder`, `CliOutputParser`, `CliErrorParser`,
`CliMonitorVerbs`, `CliSafeModeParser`, `VtStripper`/`Vt100State` (terminal emulation),
`RouterOsCliLogin`, `ITikCliCompletion`.

`Mndp/` does neighbor discovery (used to resolve router MACs for the MAC-layer transports).
`Crypto/` holds `EcSrp5` and `WinboxStreamCrypto` — reverse-engineered, high-risk, treat as
load-bearing.

### Entry points — note the duplication

Two coexist:

- `ConnectionFactory` — classic. `OpenConnection(TikConnectionType, host, [port,] user, pass)`,
  plus `RegisterConnectionFactory` (how the SSH satellite plugs itself in).
- `TikConnectionSetup` — newer and preferred. Holds `Port`, `ConnectTimeout`,
  `AllowInvalidCertificate`, and exposes `Create<Transport>Connection[Async]()` per transport.

They are not yet unified, and `TikConnectionSetup` options are not honored by every transport
(see plan findings F1/F2/F18). Don't assume a setting applies everywhere — check the transport.

## Layer 2 — `tik4net.objects`

Entities are plain classes driven entirely by attributes:

- `[TikEntity("/ip/firewall/filter")]` — API path plus behaviour flags (`IsSingleton`,
  `IsOrdered`, `IsReadOnly`, …).
- `[TikProperty("src-address")]` — field mapping, with `IsReadOnly`, `IsMandatory`,
  `DefaultValue`, `UnsetOnDefault`.
- `[TikEnumAttribute("wire-value")]` on enum members.

Metadata is reflected once and cached in `TikEntityMetadataCache` → `TikEntityMetadata` →
`TikEntityPropertyAccessor` (conversion lives here).

CRUD via `TikConnectionExtensions`:

- Load: `LoadAll<T>`, `LoadList<T>`, `LoadSingle<T>`, `LoadSingleOrDefault<T>`, `LoadById<T>`,
  `LoadByName<T>`, `LoadWithDuration<T>`
- Async/monitor: `LoadAsync<T>`, `LoadListenAsync<T>` (both `Listen`-capability gated)
- Write: `Save<T>`, `Delete<T>`, `DeleteAll<T>`, `Move<T>`, `MoveToEnd<T>`
- Bulk: `SaveListDifferences<T>` and `CreateMerge<T>` (`TikListMerge`) — two overlapping APIs
- Raw: `ExecuteNonQuery`, `ExecuteScalar`

`Tracking/` (`TikChangeTracker`, `TikSnapshot`) attaches proplist-aware snapshots to loaded
entities via `ConditionalWeakTable`, so `Save` can send only changed fields. Lifetime semantics
are deliberate — read the class before touching it.

Helpers: `TikEntityObjectsExtensions` (`Clone<T>`, `EntityDescription`, `EntityDifference`),
`Ipv4Address`/`MacAddress`/`Ipv4AddressWithSubnet` value types, `TikDefaults`.

### Adding an entity

1. Class in `tik4net.objects/<Domain>/` (`Ip/`, `Interface/`, `System/`, `Tool/`, …).
2. `[TikEntity("/<api/path>")]` + `[TikProperty]` per field.
3. `Id` is always `[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]`.
4. Bool fields ("yes"/"false") convert automatically; enum members need `TikEnumAttribute` when
   the wire value isn't just the lowercased member name.

The `entity-generator` skill (backed by `Tools/tik4net.entitygenerator`) scaffolds these from a
live router.

## Tests

Tests are split by whether they need hardware.

### `tik4net.unittests/` — router-free

MSTest, **net8.0**, runs on Linux and Windows, gated by CI on every push and PR. Everything that
can be tested without a router belongs here: the sentence/word codecs, `CliOutputParser`,
`VtStripper`/`Vt100State`, `TikTimeHelper`, `EcSrp5`, `M2Message`, property/enum conversion,
change-tracker diffing, and `TikFakeConnection`-based consumer scenarios.

Internals of `tik4net` are visible to it (`InternalsVisibleTo`, see `tik4net/Properties/AssemblyInfo.cs`),
so codec-level types can be tested directly without widening the public API.

### `tik4net.integrationtests/` — live router required

MSTest, **net48 only**, ~410 test methods, nearly all requiring a live router. Not run by CI.

- Router coordinates and topology assumptions: `App.config` (`host`, `user`, `pass`, `routerMac`,
  `testInterface`, …) and `TestConstants.cs`.
- Transport selection: `*.runsettings` files (one per transport) set `tik.connectionType`;
  falls back to the `connectionType` app setting. The suite is meant to be run once per transport.
- `TestBase` caches one connection per run (`ReuseConnectionAcrossTests`) and self-heals it on
  failure; `WinboxNativeMac` opts out because of its lossy-UDP ACK state.
- Capability-gated tests call `EnsureCapability` and report **Inconclusive** rather than failing on
  transports that can't do the thing.
### CI

`.github/workflows/build.yml` — Windows builds the full solution (including the net48 projects),
Linux builds the cross-platform ones, both run `tik4net.unittests`, and a pack job validates the
NuGet outputs. Warnings are errors in CI only; `.editorconfig` keeps the missing-XML-doc backlog
(CS1591) silent while treating malformed docs as real warnings.

## Where the risk is

- `Crypto/`, `WinboxNative*/`, `MacTelnet/` are reverse-engineered protocol implementations with
  no deterministic test coverage. Change them only with live-router verification.
- `ApiConnection`'s reader/tag multiplexing is the most subtle code in the repo and is likewise
  only exercised against real hardware.
- `TikChangeTracker` and the `Save` default-vs-unset rules encode non-obvious semantics; a
  "cleanup" there will change observable behaviour.
