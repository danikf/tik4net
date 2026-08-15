---
name: mikrotik-tests
description: >
  Skill for working with tik4net integration tests against a live MikroTik router.
  Use this skill whenever the user wants to: run tests (single transport or the full matrix),
  create a new test class/method, inspect or clean router state, understand why a test was
  skipped (Inconclusive), debug failures, analyze orphan objects, or check router logs after
  a test run. Also trigger when the user mentions TestBase, EnsureCapability, runsettings,
  connectionType, or any test file in tik4net.integrationtests/.
---

# MikroTik integration tests

Covers the **integration suite only** — `tik4net.integrationtests/` (MSTest, net48), which needs a
live router and never runs in CI. Router-free tests live in `tik4net.unittests/` (net8.0) and run in
CI on every push; if a new test does not actually need hardware, write it there instead.

```bash
dotnet test tik4net.unittests/tik4net.unittests.csproj
```

Router coordinates come from `tik4net.integrationtests/App.config` (`host`, `user`, `pass`,
`routerMac`, plus the topology keys consumed by `TestConstants.cs`). That file is the single source of
truth — read it, never restate its values.

## Transports

One `*.runsettings` file per transport sets `tik.connectionType`; the suite is run once per transport.

| Runsettings | Protocol | Port | Approximate skips (of ~415) |
|---|---|---|---|
| `api` / `apissl` | binary API, plain / TLS | 8728 / 8729 | 60 |
| `rest` / `restssl` | REST HTTP / HTTPS | 80 / 443 | 90 |
| `telnet` | CLI | 23 | 77 |
| `ssh` | CLI over SSH | 22 | 77 |
| `mactelnet` | CLI over MAC layer | 20561 | 77 |
| `winboxcli` / `winboxclimac` | CLI in the WinBox terminal, IP / MAC | 8291 / 20561 | 77 |
| `winboxnative` / `winboxnativemac` | structured M2, IP / MAC | 8291 / 20561 | 221 |

Skip counts track capability breadth: the WinBox native transports have the narrowest capability set,
so they skip most. **The authoritative capability matrix is in [README.md](../../../README.md#connection-types)** —
it is kept in sync with `TikConnectionCapability`. Do not maintain a second copy here.

`EnsureCapability(cap)` reports **Inconclusive** (a skip), not a failure, when the active transport
lacks the capability. A skipped test is not a broken test — check the capability before "fixing" it.

**CLI transports** = Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac. They share one command builder
and one output parser, so a CLI symptom almost always affects all five.

## Running

Use the harness script rather than reconstructing `dotnet test` invocations. It resolves the repo root
from its own location, always writes TRX, and takes no router coordinates:

```bash
Tools/probes/run-integration-tests.ps1 -Transport api
```

```bash
Tools/probes/run-integration-tests.ps1 -Smoke
```

| Intent | Invocation |
|---|---|
| One transport, full suite | `-Transport api` |
| Smoke subset, all transports | `-Smoke` |
| Smoke subset, some transports | `-Smoke -Transport rest,telnet,winboxnative` |
| Full matrix | no arguments |
| Hunting an intermittent failure | `-Transport telnet -WireTrace auto` |
| Explicit selection | `-Filter "ClassName=tik4net.integrationtests.IpRouteTest"` |

**How much to run for a given change** — a full API pass plus the smoke subset over the other
transports is the right default for any non-trivial change. Reserve the full matrix for changes to a
specific transport (`Crypto/`, `WinboxNative*/`, `MacTelnet/`, `ApiConnection` reader/tag multiplexing,
the CLI parsers) or for release preparation.

**Order matters in a matrix run.** The script's default order runs the API-based transports before the
CLI ones, and `winboxcli` before `winboxclimac`, because CLI transports are the ones that leave orphans
and an orphan changes the error a later transport sees.

## Reading results

```bash
Tools/probes/parse-trx.ps1 -ShowFailures -ShowSkips
```

**Always read the skips, not just the failure count.** Two runs that both report zero failures can
still differ — a test can skip on a data-dependent Inconclusive instead of failing, and for an
intermittent bug a changed skip count is often the only observation available.

**Do not count skips from the TRX summary.** In `ResultSummary/Counters` a skipped test shows up only
as the gap between `total` and `executed`; the `inconclusive` and `notExecuted` attributes are both
left at 0, so reading either reports zero skips on a run that skipped dozens. The individual
`<UnitTestResult>` elements do carry `outcome="NotExecuted"` — `parse-trx.ps1` counts those.

`-FailedTestFilter` emits a ready-made `--filter` expression to re-run just the failures.

## Hunting an intermittent failure

A bug that appears once every N full runs cannot be reproduced by blind repetition — each attempt costs
the router tens of minutes. The goal is not to hit it again, but to make the next occurrence sufficient
on its own for a diagnosis.

1. **Always keep the TRX, with a per-run filename.** See above: a clean summary does not mean the run
   was identical to the previous one.
2. **Enable the byte-level trace for the whole run** (`-WireTrace auto`, which sets `TIK4NET_WIRETRACE`).
   `WireTraceCapture` writes test boundaries into the trace as `--- TEST <name>` / `--- END <outcome>
   <name>`, so a failure can be located without correlating timestamps.
3. **Do not delete green-run artifacts** until runs have been compared against each other. A run with a
   different skip count is as interesting as a red one, and once deleted the observation is gone.
4. **Rule out login artifacts before believing you reproduced something.** The string `login failure` in
   a trace is usually the router's login banner replaying recent logs, not a live event; tell them apart
   by whether it sits near the IAC negotiation `<FF><FD>`.
5. **Residue first, code second.** A failure on an unexpected `.id`, a name collision, or a row count is
   usually a reaction to an orphan from a previous run.

## Cross-run contamination — orphans

The core mechanism: an `add` over a CLI transport can return an empty string instead of the new `.id`.
The library has no id to track, so cleanup cannot run — but **the object exists on the router**. The
next transport then hits a different error entirely:

```
winboxcli:    AddEoipWillNotFail  → add returns ""  → fail → test-eoip left on the router
winboxclimac: AddEoipWillNotFail  → "already have interface with name test-eoip"  ← different error
```

Objects most prone to this: IPsec peers and identities, EoIP interfaces, L2TP clients, WiFi
channel/security, bridge filter rules, hotspot profiles and users, firewall filter/raw rules.

**When an orphan is the cause, fix the test that leaves the residue** — register the entity with
`TestBase.SaveTracked`/`TrackForCleanup` so teardown removes it. Deleting orphans by hand only resets
the clock until the next run.

Check the router after a run using the `mikrotik` skill:

```
/ip/ipsec/peer/print          /interface/eoip/print         /interface/l2tp-client/print
/ip/hotspot/profile/print     /interface/bridge/filter/print
/interface/wifi/channel/print /interface/wifi/security/print
```

Test-created objects are prefixed `t4n` plus a GUID fragment, which is what to look for.

### The suite must not leave error-severity log lines

A red `/log` after a run alarms whoever opens the router next, so every marker the tests write is
`info`. The single deliberate exception is `LogWriteTest.LogErrorWritesALineReadableBack`, which exists
to cover `LogError` itself — one line per run per transport.

Check the log for the run's date range via the `mikrotik` skill (`/log/print` with a `?>time=` filter;
the `mikrotik` skill documents the operator syntax). Repeated identical error lines mean an orphan is
still on the router — most often an IPsec peer, which produces a continuous
`ipsec,error initiator can't find identity` flood.

## Current transport limitations

These are live constraints, not open defects. Anything historical — symptoms that no longer reproduce,
diagnoses that turned out wrong — is in [`Docs/HISTORY.md`](../../../Docs/HISTORY.md).

### CLI family

- **`add` can return an empty string** instead of the new `.id`, mainly on `winboxcli`/`winboxclimac`,
  occasionally on the others. The terminal's latency puts the router's response outside the read
  window. Consequence is an orphan, as above.
- **`print stats` is not reachable** from the CLI layer, so live counter fields (firewall
  `bytes`/`packets` and similar) come back empty over CLI transports.
- **`/system/script/run` yields no per-line output** over a terminal — it is fire-and-forget there,
  unlike the binary API.
- **Never poll a large list without a filter.** Pulling `/log/print` unfiltered inside a poll loop can
  mean tens of thousands of characters per iteration, which cannot fit the receive budget over a MAC
  transport, and the test then fails according to how chatty the log happens to be. Filter on the
  router instead (`CreateCommandAndParameters("/log/print", "message", marker)`), which is honored by
  the API, REST, CLI and WinBox native alike.

### WinBox native

- **Native CRUD only reaches paths present in the version-matched `.jg` catalog.** A path in no WinBox
  window fails with `no M2 handler mapping for path '…'`. Guard with
  `SkipOnWinboxNativeUnmappedPath(path)`; work around with `connection.PathOverride(path, new[]{maj,min})`
  or another transport. Verify a path really is absent before adding the guard — see the feature-parity
  rule in [AGENTS.md](../../../AGENTS.md).
- **Bridge creation over native** (`add type=bridge`) is not supported and answers `0xFE0006`.
- **Interface-reference list fields** (`tagged`/`untagged`, i.e. `multinumber`) are not yet encoded; the
  resolver throws `WinboxFieldResolutionException` rather than dropping them silently.
- **A `bool` entity `DefaultValue` must be the wire form `"no"`/`"yes"`**, never `"false"`/`"true"`.
  The mapper emits `yes`/`no` and compares that against `DefaultValue`, so a wrong default never
  matches, the field is sent on every add, and native then has no M2 key for it. This surfaces as a
  native-only failure while looking like a missing catalog entry.

## Skip guards

Prefer a guard bound to a **capability or an observed runtime refusal** over one bound to a transport
name. A feature-bound guard cannot mask an unrelated bug and disappears by itself once the transport
gains the feature.

```csharp
protected void EnsureCapability(TikConnectionCapability cap, string feature = null)
protected void EnsureCommandAvailable(string commandPath)      // path/package absent on this router
protected void EnsureMinRouterOsVersion(int minimumMajor, string featureDescription = null)
protected void EnsureMaxRouterOsVersion(int removedInMajor, string featureDescription = null)

protected static bool IsWinboxNativeUnsupported(Exception ex)  // catch-when: a SPECIFIC M2 error
protected void SkipOnWinboxNativeUnmappedPath(string feature)  // verify the path really is absent
protected void SkipOnSingleCommandTransport()                  // see below
protected bool IsNonApiTransport()                             // branching assertions ONLY, not a gate
```

**Before setting a gate, verify live that it is not a false assumption.** A guard placed on a
plausible-sounding capability can silently disable a test everywhere it mattered.

`SkipOnSingleCommandTransport()` is the deliberate exception that *is* transport-name based, used by
`ConcurrentCommandsTest`: it enumerates the CLI family because **that is the assertion itself** — only
those transports have a reason to serialize, and everything else must manage concurrent commands on one
connection. A feature-bound variant would sweep away exactly the regression the test exists to catch.

## Other TestBase members

```csharp
public TestContext TestContext { get; set; }   // injected by MSTest
protected ITikConnection Connection { get; }   // created in [TestInitialize]

protected TikConnectionType ResolveConnectionType()   // runsettings > App.config > Api
protected void RecreateConnection(int retryTimeoutSeconds = 20)
protected Version GetMikrotikVersion()
```

`TestBase` caches one connection per run and self-heals it on failure; only lifecycle-sensitive classes
(`SafeModeTest`) opt out.

## Writing a new test

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
        try
        {
            var loaded = Connection.LoadById<DhcpServer>(entry.Id);
            Assert.IsNotNull(loaded);
        }
        finally
        {
            if (entry.Id != null) Connection.Delete(entry);   // always clean up
        }
    }
}
```

- Always `try/finally` cleanup — a failing test must still delete what it created.
- Guards at the top of the test, not scattered through it.
- Prefix created objects `t4n` + a GUID fragment so orphans are identifiable.
- `Console.WriteLine` is captured by MSTest and is the cheapest debugging channel.
- Test files mirror the entity's folder: `tik4net.objects/Ip/Firewall/` → `tik4net.integrationtests/Ip/Firewall/`.
  One test class per domain folder is fine. Core/infra tests stay at the project root.
- The project is SDK-style: new `.cs` files are picked up automatically, no `.csproj` edit.

**Run a new test against the unfixed code first.** A test that passes before the fix cannot be cited as
evidence the fix works.

### Protocol PoC tests

Tests under `Protocols/` manage their own connection and do **not** derive from `TestBase`, so they run
regardless of which runsettings file is active. A protocol test failing during another transport's run
is a resource or timing collision, not a transport bug.

## Layout

```
tik4net.integrationtests/
├── TestBase.cs                     — base class, guards, connection reuse
├── App.config                      — router coordinates and topology (source of truth)
├── *.runsettings                   — one per transport, sets tik.connectionType
├── Protocols/
│   ├── _Shared/                    — EcSrp5, WinboxStreamCrypto, M2Message, VT100
│   ├── Transport/                  — TCP and MAC-layer helpers
│   ├── Clients/                    — WinboxM2Client, MacTelnetClient, …
│   └── Tests/                      — ApiProtocolTest, WinboxTcpProtocolTest, …
└── <domain>/                       — Interface/, Ip/, Routing/, System/, Tool/, …
```

TRX output goes to the git-ignored `TestResults/` at the repository root.

## Related

- `mikrotik` — query the router directly, and the wire-tracing reference
- `mikrotik-cli-probe` — what the router emits over the CLI/PTY layer, independently of the library
- `winbox-native-dev` — the structured-M2 layer behind the WinBox native limitations above
- `chr-test-router-init` — re-provision the test router when the suite fails wholesale
