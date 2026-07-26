# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## Overview

tik4net is a .NET library for talking to MikroTik RouterOS devices. It is **not** an API-only
library any more — as of 4.0 it ships 12 transports (binary API, REST, and a family of
CLI/WinBox/MAC-layer channels) behind one connection contract, plus an O/R mapper on top.

Shipping packages:

- **tik4net** — one package, two assemblies: `tik4net/` (core: connection contract, all in-tree
  transports, capability model) and `tik4net.objects/` (attribute-driven O/R mapper, 169
  entities). Packed by `tik4net.package/`; both source projects are `IsPackable=false`.
  Until 4.0 the mapper shipped separately as `tik4net.objects`, then `tik4net.entities`.
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
dotnet pack tik4net.package/tik4net.package.csproj   # -> tik4net (core + O/R mapper)
dotnet pack tik4net.testing/tik4net.testing.csproj
dotnet pack tik4net.ssh/tik4net.ssh.csproj
```

`tik4net/` and `tik4net.objects/` are not packable on their own — `tik4net.package/` collects
both assemblies into the single `tik4net` package. If you touch any of the packaging projects,
verify the result by unzipping the `.nupkg` and checking `lib/` and the `.nuspec` dependencies;
a wrong `ProjectReference` silently produces a package that depends on a nonexistent ID.

CI runs on push to `master` and on every PR (`.github/workflows/build.yml`): Windows builds the
whole solution, Linux builds the cross-platform projects, both run the unit tests, and a third job
validates `dotnet pack`. **Warnings are errors in CI**, with no exclusions — the whole solution is
warning-clean today and must stay that way. `publish-nuget.yml` remains tag-triggered for releases.

## Tests

Two test projects, split by whether they need hardware:

**`tik4net.unittests/`** — MSTest, `net8.0`, router-free, runs everywhere including CI.
This is where pure-logic tests belong: codecs, parsers, the O/R mapper's conversion and
change-tracking rules, `TikFakeConnection`-based consumer scenarios.

```
dotnet test tik4net.unittests/tik4net.unittests.csproj
```

**`tik4net.integrationtests/`** — MSTest, `net48`, ~410 methods, **almost all require a live
router**. Never runs in CI.

- Router coordinates live in `tik4net.integrationtests/App.config` (`host`, `user`, `pass`, `routerMac`,
  plus topology assumptions consumed by `TestConstants.cs`).
- The transport under test comes from the `tik.connectionType` run parameter — one
  `*.runsettings` file per transport (`api`, `apissl`, `rest`, `restssl`, `telnet`, `ssh`,
  `mactelnet`, `winboxcli`, `winboxclimac`, `winboxnative`, `winboxnativemac`). The full matrix
  means running the suite 11 times.
- A test that hits a capability its transport lacks reports **Inconclusive**, not a failure. When
  a test is skipped, check the capability flags before "fixing" it.

Both projects are SDK-style, so new `.cs` files are picked up automatically — no `.csproj` edit.
When adding a test, ask first whether it actually needs a router; if it doesn't, it belongs in
`tik4net.unittests` where CI will run it on every PR.

Use the **`mikrotik-tests` skill** for running the suite, interpreting skips, and cleaning up
orphaned router state.

### Never just report a pre-existing failure

"That test was already red" is not an outcome. Either fix it in the current change, or write up the
**diagnosis** — not just the symptom — and hand it to the maintainer as work scheduled immediately
after what's in flight. Reporting it and moving on is not an option.

**Always consider orphaned router state first.** A test that fails on an unexpected `.id`, a name
collision, or a stale count is usually reacting to residue left by an earlier run, not to a code
defect. When that turns out to be the cause, the fix belongs in **the test that leaves the residue**
(register the entity with `TestBase.SaveTracked`/`TrackForCleanup` so teardown removes it) — deleting
the orphans by hand just resets the clock until the next run.

For any non-trivial change, run the unit tests plus a reasonable integration check before calling
it done: a full pass via `api.runsettings`, and a fast smoke subset (`ConnectionTest`,
`SystemClockTest`, `InterfaceListTest`, `IpRouteTest`) via the other transport runsettings files.
Reserve the full 11-transport matrix for transport-specific changes or release prep. See the
"Smoke subset" section of the `mikrotik-tests` skill for the exact `--filter` command.

## Working rules

### Public API changes require wiki + XML-doc updates

If a change touches public API surface (new/renamed/removed public types, members, or behavior),
update both the XML doc comments in the source and the corresponding page(s) in
[tik4net.wiki](https://github.com/tik4net/tik4net/wiki) (cloned locally at
`../tik4net.wiki`, see the "tik4net wiki location" note) in the same change — don't leave docs
to a follow-up.

### Assume feature parity across transports until proven otherwise

A gap in one transport is a **bug in our client until the router proves otherwise**, not an accepted
limitation. Default assumption: every transport can do everything. CLI and WinBox are outright
interchangeable in function (differing only in delivery style — streaming vs. pooled and the like),
and REST is expected to match them.

So when a path works on one transport and not another, do NOT write it off as "that transport
doesn't expose it" — probe the router directly (curl for REST, the `mikrotik-cli-probe` skill for
CLI/PTY) and confirm what it actually accepts. `/tool/wol` is the cautionary tale: it was recorded
as a probable REST gap, when in fact our builder was posting `/tool/wol/print` and had never asked
for `/tool/wol` at all. An Inconclusive skip is only legitimate once the router itself has refused
the correctly-formed request.

Note the asymmetry with the rule below: fail-closed capability *flags* are about what we promise
callers at runtime; this rule is about what we assume while diagnosing. Never use a capability flag
to paper over an unproven gap.

### Capabilities are fail-closed

Transports differ in what they can do. Never assume a feature works everywhere: guard entry
points with `connection.Require(TikConnectionCapability.X, "feature")` and check with
`connection.Supports(...)`. A connection not implementing `ITikConnectionCapabilities` supports
nothing. When adding a transport, declare its flags explicitly.

### Two entry points, not yet unified

`ConnectionFactory` (classic) and `TikConnectionSetup` (preferred) coexist, and
`TikConnectionSetup`'s options are **not** honored by every transport — e.g. `ConnectTimeout` is
honored by API, Telnet, MAC-Telnet and the WinBox transports, but **not** by REST (no clean way to
bound just the connect phase on `netstandard2.0`'s `HttpClient` — see the P1.2 REST note in the
improvement plan). Verify per transport rather than trusting the property name, and check *what*
the value is applied to, not just that it is read: the WinBox TCP transports took it as far as the
socket but spent it on `ReceiveTimeout`/`SendTimeout` while leaving the connect itself unbounded
(fixed in P1.8). (Tracked as F2/F18 in the improvement plan.)

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
- `chr-test-router-init` — re-provision the CHR test router after a restore/reset (packages, NTP,
  services, api-ssl certs, transport smoke) and reconcile the RouterOS version promised in README/wiki
- `mikrotik-cli-probe` — ground truth for what the router actually emits over the CLI/PTY layer
- `winbox-native-dev` — structured-M2 transport work (`.jg` catalog, wire encodings)
- `entity-generator` — scaffold O/R mapper entities

## Protocol documentation

`Docs/` holds the transport ground truth — what the router actually does on the wire, established by
live probing and by reading MikroTik's own clients. Source XML docs cite these files by name. Read
the relevant one before changing a transport, and update it when live behaviour contradicts it.
`Docs/README.md` is the index. Standalone diagnostic scripts live in `Tools/probes/`.

The phased architecture review and roadmap are the maintainer's local working notes, outside this
repository — ask if a structural change needs to land in a particular phase.
