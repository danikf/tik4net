# AGENTS.md

Entry point for AI coding agents working in this repository. Everything here is either
**non-obvious** (you would get it wrong by default) or a **pointer** to the document that holds the
detail. Nothing is duplicated from the documents it links to — follow the link rather than assuming.

Language: this is a public open-source project, so **all documentation in this repository is English**,
including agent-facing files.

## What this is

tik4net is a .NET library for talking to MikroTik RouterOS devices. It is **not** an API-only library:
as of 4.0 it ships 11 transports (binary API, REST, and a family of CLI/WinBox/MAC-layer channels)
behind one connection contract, plus an attribute-driven O/R mapper on top. The five shipping projects
multi-target `netstandard2.0;net8.0` — netstandard2.0 is kept deliberately (Unity/Xamarin/.NET Framework
reach), and the net8.0 leg is where the modern surface lives, e.g. the `IAsyncEnumerable` streaming reads.
The test and tool projects are net8.0 / net48.

## Reference map — read the right file, don't guess

| Question | Read |
|---|---|
| How the code is laid out; layers, transports, capability model, O/R mapper internals, where the risk is | [ARCHITECTURE.md](ARCHITECTURE.md) |
| What the router actually does on the wire (per-transport protocol ground truth) | [Docs/README.md](Docs/README.md) — index of the `Docs/` findings |
| What the project is, for humans; packages, features, install | [README.md](README.md) |
| Why something is the way it is today; superseded diagnoses; dated incidents | [Docs/HISTORY.md](Docs/HISTORY.md) |
| What a shipping or non-shipping project does | that project's own `README.md` |
| End-user usage documentation | the [wiki](https://github.com/danikf/tik4net/wiki) (cloned next to this repo — ask the maintainer for its local path) |

**Read ARCHITECTURE.md before any non-trivial change.** It maps the transport family and tells you
which code is reverse-engineered and must not be refactored opportunistically.

## Build

```bash
dotnet build tik4net.sln
```

Pack (output to `./Build/`): `tik4net.package`, `tik4net.testing`, `tik4net.ssh`. `tik4net/` and
`tik4net.objects/` are `IsPackable=false` — the single `tik4net` package is assembled by
`tik4net.package/`. If you touch a packaging project, unzip the resulting `.nupkg` and check `lib/`
and the `.nuspec` dependencies: a wrong `ProjectReference` silently produces a package that depends
on a nonexistent ID.

**Warnings are errors in CI**, with no exclusions. The solution is warning-clean and must stay that way.
CI (`.github/workflows/build.yml`) builds on Windows and Linux, runs the unit tests, and validates `dotnet pack`.

**Nullable reference types are on** (`<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`)
in all four shipping projects — `tik4net`, `tik4net.objects`, `tik4net.ssh`, `tik4net.testing`. Every
mapped reference-typed `[TikEntity]` property is `string?` by convention (see
[Adding an entity](#adding-an-entity) below); write new code nullable-clean rather than adding `#nullable
disable`.

## Tests

Two projects, split by whether they need hardware.

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj          # router-free, net8.0, runs in CI
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj   # needs a live router, net48
```

When adding a test, **ask first whether it actually needs a router**. If it doesn't, it belongs in
`tik4net.unittests` where CI runs it on every PR.

The integration suite is driven by the **`mikrotik-tests` skill** — transport selection, interpreting
Inconclusive skips, orphan cleanup, and which subset to run for a given change size. Use it rather
than reconstructing the commands.

For any non-trivial change: unit tests, plus a full integration pass on the binary API and a smoke
subset on the other transports. Reserve the full 11-transport matrix for transport-specific changes
and release prep.

### Never just report a pre-existing failure

"That test was already red" is not an outcome. Either fix it in the current change, or write up the
**diagnosis** — not just the symptom — and hand it to the maintainer as work scheduled immediately
after what's in flight.

**Always consider orphaned router state first.** A test that fails on an unexpected `.id`, a name
collision, or a stale count is usually reacting to residue left by an earlier run, not to a code
defect. When that is the cause, the fix belongs in **the test that leaves the residue** — deleting
the orphans by hand just resets the clock until the next run.

## Working rules

### Capabilities are fail-closed

Transports differ in what they can do. Never assume a feature works everywhere: guard entry points
with `connection.Require(TikConnectionCapability.X, "feature")` and check with `connection.Supports(...)`.
A connection not implementing `ITikConnectionCapabilities` supports **nothing**. When adding a
transport, declare its flags explicitly.

### Assume feature parity across transports until the router proves otherwise

A gap in one transport is a **bug in our client until the router proves otherwise**, not an accepted
limitation. CLI and WinBox are interchangeable in function, differing only in delivery style, and REST
is expected to match them. When a path works on one transport and not another, probe the router
directly (curl for REST, the `mikrotik-cli-probe` skill for CLI/PTY) and confirm what it actually
accepts. An Inconclusive skip is legitimate only once the router itself has refused a correctly-formed
request.

Note the asymmetry with the rule above: fail-closed capability *flags* are about what we promise
callers at runtime; this rule is about what we assume while diagnosing. **Never use a capability flag
to paper over an unproven gap.**

### One entry point, and one place an option is applied

`TikConnectionSetup` is the entry point; `ConnectionFactory` is a compatibility shim over the same
registry that hands out connections with their own defaults and no options. **Apply options through
`TikConnectionSetup.ApplyTo` rather than copying properties by hand** — a transport declares what it can
honour by implementing `ITikTlsConnection` / `ITikMacLayerConnection` / `ITikCancellationModeConnection`,
and the option matrix in `tik4net.unittests` fails when a new option or transport skips that route.
Adding an option means adding it to the matrix in the same change, and checking *what* the value is
applied to, not just that it is read.

### Public API changes require wiki + XML-doc updates

If a change touches public API surface (new/renamed/removed public types, members, or behavior),
update **both** the XML doc comments in the source and the corresponding wiki page(s) in the same
change. Don't leave docs to a follow-up.

### Commit directly to master

This project commits straight to `master` — no feature branches. When incorporating a PR, preserve
the original author (prefer `gh pr merge`; otherwise add a `Co-Authored-By` trailer).

### High-risk areas — do not refactor opportunistically

`Crypto/` (EC-SRP5, WinBox stream cipher), `WinboxNative*/`, `MacTelnet/`, and `ApiConnection`'s
reader/tag multiplexing are reverse-engineered or subtle, and have no deterministic test coverage.
Change them only with live-router verification.

Inside `Crypto/`, watch for `someArray.Reverse()`: under the current `LangVersion` it binds to
`MemoryExtensions.Reverse`, a `Span` extension that reverses **in place and returns void**, not
`Enumerable.Reverse`. Spell it out as `Enumerable.Reverse(x)`. A call whose result is discarded compiles
either way but silently corrupts the buffer with the wrong one.

`TikChangeTracker` and the `Save` default-vs-unset rules encode deliberate, non-obvious semantics — a
tidy-up there changes observable behaviour.

### Adding an entity

Prefer the **`entity-generator` skill** over hand-writing — it scaffolds from a live router and applies
the documented conventions. The conventions themselves are in
[ARCHITECTURE.md](ARCHITECTURE.md#adding-an-entity), including the nullable-reference-type convention:
every mapped reference-typed property (`string`) is declared `string?`.

### Secrets and local paths

No credentials, router addresses, MAC addresses, or machine-local absolute paths in tracked files —
this repository is public. Router coordinates belong in `tik4net.integrationtests/App.config`, and
docs and skills must **read** them from there rather than restating them. Maintainer-local material
(working notes, vendor assets, the wiki clone) lives outside the repository; reference it by role, and
say how to obtain or regenerate it, not by a path only one machine has.

### Documentation is present-tense

Reference documentation describes **how things work now**. It does not narrate how they used to be
broken. Dated incidents, superseded diagnoses and "this used to fail because…" belong in the history
files, and in exactly two places: [Docs/HISTORY.md](Docs/HISTORY.md) — the index, the transferable
lessons and the pinned measurements — or a `<doc>-history.md` sitting beside the document whose subject
it covers (`findings-winbox-history.md`, `findings-cli-history.md`, …). Each fact lives in one of them,
never both; [Docs/README.md](Docs/README.md) states the rule in full. Everywhere else is present-tense.

## Skills

Project skills live in `.claude/skills/`. Invoke them by name rather than reconstructing their content:

| Skill | Use for |
|---|---|
| `mikrotik` | Query/modify a router over any transport (via the tik4net MCP server); wire tracing |
| `mikrotik-tests` | Run and debug the integration suite; transports, skips, orphans |
| `mikrotik-cli-probe` | Ground truth for what the router emits over the CLI/PTY layer |
| `winbox-native-dev` | Structured-M2 transport work: `.jg` catalog, wire encodings, field mapping |
| `entity-generator` | Scaffold O/R mapper entities |
| `chr-test-router-init` | Re-provision the CHR test router after a restore/reset |
| `tik4net-mcp-install` | Refresh the MCP server after changing it — the in-repo dev launcher, or the global tool |
| `wiki-cleanup` | Review/clean the end-user wiki: truth, as-is phrasing, examples, page scope and placement |

`Docs/` holds transport ground truth established by live probing; source XML docs cite these files by
name. Read the relevant one before changing a transport, and update it when live behaviour contradicts
it. Standalone diagnostic and harness scripts live in [`Tools/probes/`](Tools/probes/README.md).

The phased architecture review and roadmap are the maintainer's local working notes, outside this
repository — ask if a structural change needs to land in a particular phase.
