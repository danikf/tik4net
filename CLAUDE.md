# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## Overview

tik4net is a .NET library for talking to MikroTik RouterOS devices. It is **not** an API-only
library any more — as of 4.0 it ships 12 transports (binary API, REST, and a family of
CLI/WinBox/MAC-layer channels) behind one connection contract, plus an O/R mapper on top.

Shipping packages:

- **tik4net** (`tik4net/`) — core: connection contract, all in-tree transports, capability model
- **tik4net.entities** (`tik4net.objects/`) — attribute-driven O/R mapper, 169 entities
- **tik4net.ssh** — SSH transport satellite (isolates the `Renci.SshNet` dependency)
- **tik4net.testing** — `TikFakeConnection` for router-free consumer tests
- **tik4net.mcp** (`Tools/tik4net.mcp/`) — dev/debug MCP helper published as a .NET tool, not a
  user-facing library

Everything targets `netstandard2.0` except the tests (`net48`) and the tool projects.

**Read [ARCHITECTURE.md](ARCHITECTURE.md) before non-trivial work** — it maps the transport
family, the capability model, the O/R mapper internals, and where the risky code lives.

## Build

```
dotnet build tik4net.sln
dotnet build tik4net/tik4net.csproj
```

Pack (output to `./Build/`):

```
dotnet pack tik4net/tik4net.csproj
dotnet pack tik4net.objects/tik4net.objects.csproj
dotnet pack tik4net.ssh/tik4net.ssh.csproj
```

There is currently **no build/test CI** — the only workflow (`publish-nuget.yml`) is
tag-triggered. Build locally before claiming a change compiles.

## Tests

`tik4net.tests/` — MSTest, `net48`, 413 methods, **almost all require a live router**.

- Router coordinates live in `tik4net.tests/App.config` (`host`, `user`, `pass`, `routerMac`,
  plus topology assumptions consumed by `TestConstants.cs`).
- The transport under test comes from the `tik.connectionType` run parameter — one
  `*.runsettings` file per transport (`api`, `apissl`, `rest`, `restssl`, `telnet`, `ssh`,
  `mactelnet`, `winboxcli`, `winboxclimac`, `winboxnative`, `winboxnativemac`). The full matrix
  means running the suite 11 times.
- A test that hits a capability its transport lacks reports **Inconclusive**, not a failure. When
  a test is skipped, check the capability flags before "fixing" it.
- Router-free today: `CliCompletionParserTest`, `TikTimeHelperTests`, `FakeConnectionSampleTest`.

The project is SDK-style, so new `.cs` files are picked up automatically — no `.csproj` edit.

Use the **`mikrotik-tests` skill** for running the suite, interpreting skips, and cleaning up
orphaned router state.

## Working rules

### Capabilities are fail-closed

Transports differ in what they can do. Never assume a feature works everywhere: guard entry
points with `connection.Require(TikConnectionCapability.X, "feature")` and check with
`connection.Supports(...)`. A connection not implementing `ITikConnectionCapabilities` supports
nothing. When adding a transport, declare its flags explicitly.

### Two entry points, not yet unified

`ConnectionFactory` (classic) and `TikConnectionSetup` (preferred) coexist, and
`TikConnectionSetup`'s options are **not** honored by every transport — e.g.
`AllowInvalidCertificate` is wired to REST but not API-SSL, and `ConnectTimeout` is honored by
the MAC/WinBox transports but not API/REST/Telnet. Verify per transport rather than trusting the
property name. (Tracked as F1/F2/F18 in the improvement plan.)

### High-risk areas

`Crypto/` (EC-SRP5, WinBox stream cipher), `WinboxNative*/`, `MacTelnet/`, and `ApiConnection`'s
reader/tag multiplexing are reverse-engineered or subtle, and have no deterministic test
coverage. Change them only with live-router verification, and don't refactor them opportunistically.

`TikChangeTracker` and the `Save` default-vs-unset rules encode deliberate, non-obvious semantics
— a tidy-up there changes observable behaviour.

### Adding an entity

1. Class in `tik4net.objects/<Domain>/` (`Ip/`, `Interface/`, `System/`, `Tool/`, …).
2. `[TikEntity("/<api/path>")]` + `[TikProperty("<field-name>")]` per property.
3. `Id` is always `[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]`.
4. Bool ("yes"/"false") converts automatically; enum members carry `[TikEnum("wire-value")]` when
   the wire value isn't the lowercased member name.
5. Read-only counters must be marked `IsReadOnly`.

Prefer the **`entity-generator` skill** over hand-writing — it scaffolds from a live router and
applies the documented conventions.

## Skills

- `mikrotik` — query/modify a router over any transport (via the tik4net MCP server)
- `mikrotik-tests` — run and debug the integration suite
- `mikrotik-cli-probe` — ground truth for what the router actually emits over the CLI/PTY layer
- `winbox-native-dev` — structured-M2 transport work (`.jg` catalog, wire encodings)
- `entity-generator` — scaffold O/R mapper entities

## Current improvement plan

`_notes/Reviews/ARCHITECTUREIMPROVEMENTPLAN.md` (local-only) holds the phased source/architecture
review and plan. Consult it before starting structural work so changes land in the intended phase,
and tick items off there as they're completed.
