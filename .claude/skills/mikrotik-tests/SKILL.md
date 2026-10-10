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
truth — read it, never restate its values. The `romonTarget*` keys name the **second** lab router, CHR2: the
RoMON target `RomonRelayTest` reaches through the first (and writes to), kept on **RouterOS 6.49.13** as the
older router — no REST, no `proplist=`, flags read as id lists (`Docs/findings-routeros-6.md`, which also
holds the numbered list of what is still open on 6.x). Run `CliFlagFieldsTest` against it after any change to
the CLI read path; not the full suite, which assumes the first router's topology. Empty `romonTargetId` = the
RoMON tests are Inconclusive. The table is in `tik4net.integrationtests/README.md`, *The lab routers*.

**Switch routers with `-Router`, never by editing `App.config`.** A router profile is a set of `<name>.<key>`
entries in `App.config` (`chr2.host`, `chr2.routerMac`, …) that override the plain keys; `-Router chr2` sets
`TIK4NET_ROUTER` for the run, writes to `TestResults/chr2`, and the `chr2` profile switches the RoMON target off
(CHR2 would relay to itself). Every test reads its settings through `LabConfig.Get`, and
`IntegrationTestConfigReadTests` in the unit suite fails on a direct `AppSettings` read, which would reach the
default router whatever the run selected. Without `-Router` the script clears an inherited `TIK4NET_ROUTER`, so
a leftover variable cannot redirect a run.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -Command "& 'Tools/probes/run-integration-tests.ps1' -Router chr2 -Transport @('api') -Filter 'FullyQualifiedName~CliFlagFieldsTest'"
```

`TestBase.LabAddress` picks which of them a run uses, and the choice is not cosmetic: the **MAC-layer
transports run with no host at all**, addressed by `routerMac` alone. That is the case those transports
exist for (a router with no IP address), so a mac* run that passed the host as well would leave it
untested — and `routerMac` is therefore mandatory in `App.config` for those three runsettings, not an
optimization that skips MNDP.

## Transports

One `*.runsettings` file per transport sets `tik.connectionType`; the suite is run once per transport.

| Runsettings | Protocol | Port |
|---|---|---|
| `api` / `apissl` | binary API, plain / TLS | 8728 / 8729 |
| `rest` / `restssl` | REST HTTP / HTTPS | 80 / 443 |
| `telnet` | CLI | 23 |
| `ssh` | CLI over SSH | 22 |
| `mactelnet` | CLI over MAC layer | 20561 |
| `winboxcli` / `winboxclimac` | CLI in the WinBox terminal, IP / MAC | 8291 / 20561 |
| `winboxnative` / `winboxnativemac` | structured M2, IP / MAC | 8291 / 20561 |

**The authoritative capability matrix is in [README.md](../../../README.md#connection-types)** — it is kept
in sync with `TikConnectionCapability`. Do not maintain a second copy here.

**Most of a run's skips are not capability skips.** About 76 of them are `[Ignore]`d manual probes and RE
harnesses that skip on *every* transport — measured 2026-08-29 on RouterOS 7.24, a 541-test run skips 92
on `api` and 108 on `winboxnative`, so what actually separates the two transports is around a dozen tests,
not the raw count. Read the reasons (`parse-trx.ps1 -ShowSkips`) rather than the number; a count that
looks plausible is how a bogus skip survives. The per-transport figures for that run are pinned in
[Docs/HISTORY.md](../../../Docs/HISTORY.md#measurements-pinned-to-a-moment).

`EnsureCapability(cap)` reports **Inconclusive** (a skip), not a failure, when the active transport
lacks the capability. A skipped test is not a broken test — check the capability before "fixing" it.

**CLI transports** = Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac. They share one command builder
and one output parser, so a CLI symptom almost always affects all five.

## Running

Use the harness script rather than reconstructing `dotnet test` invocations. It resolves the repo root
from its own location, always writes TRX, and takes no router coordinates. **With no arguments it runs
everything**: every transport on the default router, the `AnyRouter` tests on every other router App.config
defines, each leg its own `dotnet test` process, four legs at a time per the default `-MaxParallel 4`.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/probes/run-integration-tests.ps1
```

```bash
powershell -NoProfile -ExecutionPolicy Bypass -Command "& 'Tools/probes/run-integration-tests.ps1' -Router default -Transport @('api') -Sequential"
```

**Naming more than one transport or router needs `-Command`, not `-File`.** `-File` passes every argument as a
string, so `-Transport api,rest` arrives as one transport named `api,rest`. Pass an actual array:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -Command "& 'Tools/probes/run-integration-tests.ps1' -Smoke -Router default -Transport @('rest','telnet','winboxnative')"
```

| Intent | Invocation |
|---|---|
| Everything, all routers, four legs at a time | no arguments |
| One transport, full suite, in this console | `-Router default -Transport api -Sequential` |
| Smoke subset, all transports | `-Smoke -Router default` |
| Smoke subset, some transports | `-Smoke -Router default -Transport @('rest','telnet','winboxnative')` (needs `-Command`) |
| The older-RouterOS tests | `-Router chr2` (only category `AnyRouter` runs there) |
| Hunting an intermittent failure | `-Router default -Transport telnet -Sequential -WireTrace auto` |
| The lightest parallel load: two lanes per router | `-Pairs` |
| Is a failure a collision between parallel tests? | the same selection with `-LockAll` |
| Explicit selection | `-Filter "ClassName=tik4net.integrationtests.IpRouteTest"` |

**The legs.** One leg per router and transport, plus one leg named `independent` per router: the classes in
category `LegIndependent` never read `tik.connectionType` (RomonRelayTest, the protocol PoC tests,
`RouterPrerequisiteTest`, …), so they measured the same thing in all eleven legs; they run once, over the
binary API, and every transport leg excludes them. `-NoIndependentLeg` drops it.
A secondary router (CHR2, CHR3) carries none of the default router's topology, so it gets only category
`AnyRouter`; its results go to `<ResultsDirectory>/<router>`. In parallel mode each leg's console output is a
`.log` beside its TRX, and the script builds once up front and starts every leg with `--no-build`.
`-MaxParallel n` caps how many legs run at once (0 = all). `-Pairs` runs just two lanes per router.

**How many legs at once** — measured on the lab CHR (2 vCPU), default router, 2026-09-30: 2 legs at once 53 min,
**4 legs 34 min** (the tests themselves only ~12 % slower than alone; the rest of the cost is waiting for test locks),
12 legs no faster than 4, with every leg slowed 4–10× and CLI reads cut short by the loaded router. Sequentially the
same suite is about 68 min (124 min before the `independent` leg). Hence the default of 4.

**The MAC-layer legs never overlap.** mactelnet, winboxclimac and winboxnativemac always share one lane: with two MAC
sessions to one router, the router drops datagrams of its own output — MAC-Telnet refuses the answer with *"the router
ran a backlog past this client"*, WinBox-MAC logins fail — which no sequential run since September has shown. The
lane keeps the MAC legs apart; the `independent` leg's MAC tests are kept off them by a lock: every test of a MAC leg
holds the `mac-layer` resource, and a test elsewhere that opens a MAC connection names it
(`[TestLock(TestLockAttribute.MacLayer)]`).

**The summary splits each leg's time**: wall clock, the sum of its test durations, how much of that was waiting for
test locks, and the rest (running). A TRX duration includes TestInitialize, where the wait happens; every test writes
its wait as `[lock-wait-ms] n` into its output, and the script adds them up.

**Two things the script guarantees, and one it cannot.** It **exits non-zero when any leg failed**, naming
them, so a matrix is safe to judge by its exit code and no wrapper will call it green. It **refuses an
unrecognised transport or router name up front** rather than skipping past it, because a run that matched
nothing otherwise looks exactly like a run that passed. What it cannot tell you is whether the tests that *ran*
were the ones you meant — `--filter` matching nothing is still a green run of zero tests, so check the
test count (the summary prints it per leg, from the TRX) against what you expected.

**A leg that hangs on its way out does not hold the run.** `dotnet test` has been seen to print its run summary and
then never write the TRX nor exit (three legs at once, 2026-10-01, an hour lost). A leg still alive `-HangMinutes`
(default 3) after its summary gets a dump `hang_<leg>_<pid>.dmp` beside its log (`dotnet-dump collect`, when
`dotnet tool install -g dotnet-dump` has been run), is killed with its process tree, and is reported with the counts
from its log — the legs run with an English console for that — and named under *HUNG* at the end. Its tests count as
they ran; the hang itself is a tooling finding, so read the dump (`dotnet-dump analyze <dmp>` → `clrstack -all`).

**A re-run does not destroy the previous TRX.** Results keep the stable `results_<transport>.trx` name
that `parse-trx.ps1` and this document use, and an existing one (and its `.log`) is moved aside as
`results_<transport>_<timestamp>.trx` before the leg starts. This matters exactly when re-running one leg
to see whether its failure reproduces: the file recording the failure is the evidence, and the re-run is
what would otherwise overwrite it. `-ResultsDirectory` keeps a whole investigation separate.

**How much to run for a given change** — a full API pass plus the smoke subset over the other
transports is the right default for any non-trivial change. Reserve the full matrix for changes to a
specific transport (`Crypto/`, `WinboxNative*/`, `MacTelnet/`, `ApiConnection` reader/tag multiplexing,
the CLI parsers) or for release preparation.

## Parallel legs: test locks and run leases

The legs run against one router at the same time. What keeps them apart is in the suite, not in the script:

- **Every test holds cross-process locks** from TestInitialize to TestCleanup (`LockedTestBase`, the base of
  `TestBase` and of every other test class; `TestLocks.cs`). By default it holds the run, the router and its leg
  **shared**, and its **own name exclusive** — the same test never runs in two legs at once, because it creates
  the same fixed-name rows in both. That default makes the legs a pipeline: a leg starts a test only when the leg
  ahead of it has finished it.
- **`[TestLock(...)]`** on a class or method adds to that. Choose the least that is enough:
  - `[TestLock("resource")]` — exclusive on a named router resource (`"testAddress"`, `"testInterface-comment"`,
    `"/ip/settings"`); it excludes only tests naming the same resource. For a singleton written and restored, or a
    lab resource several tests use.
  - `[TestLock(TestLockScope.Router)]` — waits for every test on the router to finish and holds them all off. For
    state the whole router shares (the identity — it is in every CLI prompt —, safe mode, services, reboot), for a
    test comparing a whole table between two reads, and for timing probes. Expensive: every one drains the router.
  - `RouterHostKey = "romonTargetHost"` locks another lab router as well (RomonRelayTest writes to CHR2).
  - `TestLockScope.Global` — every run on the machine; `TestLockScope.Leg` — only this process's other tests.
  - In a test body, `using (LockResource("x"))` / `LockRouter()` escalate for part of the test. The set is
    released and taken again in order, so the escalation is not atomic.
- **`[SafeInParallelLegs]`** drops the own-name lock for a test that creates nothing under a fixed name and changes
  nothing another test reads (read-only tests, tests working in a private chain). Put it on slow tests that only
  wait: under the own-name lock a 15 s timeout test costs eleven times 15 s.
- **A name several tests of one class share** is made run-unique: `TestNames.Unique("tik4net-test-pool")` appends
  the run tag (`…-u3k9xa`). A GUID-stamped name needs nothing.
- **The orphan sweep knows whose rows it sees.** Each leg holds a lease (`RunLease.cs`) for as long as it lives. A
  row carrying a run tag is removed only when its run has ended; an untagged test row only when no other run
  against the router is alive (the first leg to start sweeps them). At the end each leg removes what it left under
  its own tag and **names it** in the output — the fix belongs in that test.
- Lock and lease files live in `%TEMP%\tik4net-testlocks`. The OS releases them when a process dies, so a killed
  leg never wedges the others. A lock waited on for more than a second is logged in the test's output
  (`[lock] waited …`), and a wait longer than `TIK4NET_LOCK_TIMEOUT_MINUTES` (default 20) fails naming the holder.

**A failure seen only in a parallel run** is a collision until shown otherwise: re-run the same selection with
`-LockAll` (every test takes the global lock exclusive — the processes stay parallel, the tests do not). Green
there means two tests share something neither of them locks: lock it, or make the name unique.

**A `[Timeout]` includes the lock wait.** MSTest times TestInitialize with the test, so a test with a short
`[Timeout]` can run out while it still waits for its locks, and under parallel legs that is the common case. MSTest
then abandons the thread, which goes on waiting; the set is closed by then (`TestLockSet.Close`, from the cleanup or
the next test's TestInitialize), so the lock it gets later is given back at once instead of being held for the rest of
the process. Such a failure reads "exceeded execution timeout period" with a `[lock] waited …` line in its output —
a collision, not a slow router; give the test a timeout that covers the wait, or none.

**Timing assertions stretch under load.** The router's throughput is shared by every connection
(`Docs/findings-router-throughput-ceiling.md`), so a test that asserts how long something takes belongs under
`TestLockScope.Router`, or waits with `WaitUntil` and a budget rather than a fixed sleep.

**`--filter` cannot run an `[Ignore]`d test.** MSTest applies `[Ignore]` before the filter, so naming the
test still reports it `Přeskočeno`/skipped and the run passes — a green result that measured nothing.
Comment the attribute out, run, then put it back. The audit and dump tests are the ones this bites:

| Test | What it does |
|---|---|
| `TransportPathMapAuditTest.AuditPathMapAgainstApi` | per API path, one transport against the binary API: row count, field-name set, the VALUES of shared fields on rows paired by `.id` (case-sensitive), whether each shared field is printed on each paired row at all (a word on one side and none on the other — native's `passthrough` on an action-less mangle rule), and all six write verbs. A VALUE-DIFF line names the row (`field [*id]`). The transport is `TIK4NET_AUDIT_TRANSPORT` (default `WinboxNative`) and the report is named after it. Run it after touching the alias tables, the `.jg` harvest, a codec or a CLI parser, or on a new RouterOS version — a normal suite run cannot catch a path answering with the wrong window or the wrong value. Reports `OK=… KNOWN-GAP=… MISMATCH=… VALUE-DIFF=… UNMAPPED=…`, the API field names the transport does not report, and the names it reports that the API does NOT print (`<transport>-only:` per path, tallied). A non-native transport that adds a name fails unless `KnownAddedFields` names it with a reason; WinBox native adds hundreds of GUI-window fields by design, so there the tally is only compared. Compare the tallies against the previous commit's **for the same transport**. A known gap belongs in `KnownFieldGaps`/`KnownValueGaps` **with the reason named**, so a new disagreement on the same path still fails. All ten non-API transports pass as of 7.24; the full sweep is ten runs of 45 s to 3 minutes each, and the MAC variants are NOT the hours their per-command latency suggests. |
| `WinboxDumpCatalogTest` | dumps the live `.jg` catalog next to the other catalog dumps |

**The CLI menu grammar is cached on disk** (`CatalogCachePath`, default `%TEMP%\tik4net\cli-schema\v2\<version>\`), so
the terminal legs ask `/console/inspect` or Tab once per RouterOS build, not once per connection; the first leg
warms it for the rest. A test about the router's grammar itself must not read it: `MenuSchemaTest` describes over a
connection of its own with `CatalogCachePath = null`, and `MenuSchemaCompletenessAuditTest` likewise — do the same in
a new one. For a cold run of the whole suite, delete `%TEMP%\tik4net\cli-schema` first (deleting it is always safe).

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

1. **Keep every TRX and compare them.** The script preserves the previous one for you (see Running
   above), but that only helps if you read them: a clean summary does not mean the run was identical to
   the one before it. Skip counts and durations are the comparison worth making.
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

### Create traffic-path rows disabled

A test row that sits in a path the router actually enforces — a firewall or bridge rule, a routing rule,
a walled-garden or access-list entry, a queue — is **created with `disabled=yes`**. The suite runs over
the very connection those rows govern, so an enabled probe row is a rule being applied to the test
that made it, and the blast radius is the whole lab rather than one red test.

The failure this prevents does not look like a test failure. It looks like the router dying: an
unconditional `/routing/rule` whose `action` a write probe flipped to `drop` takes every IP transport
offline at once, so the run appears to hang at whatever line came next and the obvious reading is "the
router wedged, reboot it". It has not wedged — **the MAC-layer transports still answer**, because they
are L2 and no routing rule touches them.

So when every IP transport goes silent mid-run, **try `MacTelnet` before reaching for a reboot**:

```
transport: MacTelnet, routerMac from App.config →  /routing/rule/print
```

If that answers, the router is healthy and something the run created is dropping or diverting traffic;
remove it over the same L2 path and IP returns immediately, with the uptime unbroken. A wedge and a
self-inflicted blackout are indistinguishable over IP alone, and only one of them needs a reboot.

Belt-and-braces for a probe that has to enable such a row: park a scheduler on the router first, so the
router undoes it without you.

```
/system/scheduler/add name=probe-rescue interval=2m on-event="/routing/rule remove [find comment~\"probe-\"]"
```

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

- **`add` can answer without the new `.id`**, mainly on `winboxcli`/`winboxclimac`, occasionally on the
  others. The terminal's latency puts the router's response outside the read window, so the reply is lost
  rather than late — the row itself is created. This now raises **`TikAddIdNotReadException`** instead of
  handing back an empty id, so the test that hits it fails *at the add* and names the cause. The
  consequence is still an orphan (there is no id to clean up with), so a test that creates rows over a CLI
  transport should be able to find its own row by the marker it chose — see the orphan section above.
- **`print stats` is not reachable** from the CLI layer, so live counter fields (firewall
  `bytes`/`packets` and similar) come back empty over CLI transports.
- **Reordering a `/routing/rule` in the same second its rows were added wedges RouterOS 7.24's routing
  MANAGEMENT interface.** The move returns and the router logs it as applied; from then on every
  `/routing/*` menu and `/ip/route` times out — on every transport and in the router's own shell, which
  never returns a prompt. Nothing is logged, not even under `error`, CPU stays idle, and forwarding and
  every other menu are unaffected, so **the API still answers everything except routing** — this is not
  the router falling off the network (that one is a probe row dropping traffic, see above; MacTelnet
  fixes it). Only a reboot clears this one. A settle before the move avoids it: the boundary
  measured between 100 ms (wedges) and 200 ms (clean), and the audit waits 1 s.
  `RoutingRuleMoveWedgeRepro` reproduces it in 90 s, and its `TIK4NET_WEDGE_SETTLE_MS` walks the
  boundary again on a new RouterOS version.
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
  `SkipIfWinboxNativeCannot(path, body)`, which waits for that refusal instead of naming the transport;
  work around with `connection.PathOverride(path, new[]{maj,min})` or another transport. A guard that
  skips native by name is how `/tool/netwatch` stayed untested on the one transport whose field mapping
  can be wrong — WinBox exposes it perfectly well. See the feature-parity rule in
  [AGENTS.md](../../../AGENTS.md).
- **A subtype interface is created by sending its type discriminator.** `/interface/bridge`, `/vlan`,
  `/eoip` and the rest share the generic `[20,0]` handler, and the field that filters a *read* is the
  same field that tells `add` what to *create* — without it the router answers `unsupported device
  type`. Native sends it from the handler map, so a subtype the map does not know still cannot be added.
- **The remaining array shapes** (`multitristatearray`, `string[]`, …) are not yet encoded; the resolver
  throws `WinboxFieldResolutionException` rather than dropping them silently. `multinumber` (`tagged`/
  `untagged`, `topics`) and `multinumberrange` (`vlan-ids`, `dst-port`) do encode.
- **A field no WinBox window exposes has no M2 key** and is refused by name — `/routing/ospf/instance`
  `use-dn` is the worked example. Check the `.jg` before assuming it is ours: `ft-preserve-vlanid` and
  `radius-accounting` looked API-only and were label gaps.
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

protected void SkipIfWinboxNativeCannot(string feature, Action body)  // skip on the REFUSAL, not the name
protected static bool IsWinboxNativeUnsupported(Exception ex)  // catch-when: a SPECIFIC M2 error
protected void SkipOnSingleCommandTransport()                  // see below
protected bool IsSingleCommandTransport()                      // the same list, for the inverse test
protected bool IsNonApiTransport()                             // branching assertions ONLY, not a gate
```

**Before setting a gate, verify live that it is not a false assumption.** A guard placed on a
plausible-sounding capability can silently disable a test everywhere it mattered.

`SkipOnSingleCommandTransport()` is the deliberate exception that *is* transport-name based, used by
`ConcurrentCommandsTest`: it enumerates the CLI family because **that is the assertion itself** — only
those transports have a reason to serialize, and everything else must manage concurrent commands on one
connection. A feature-bound variant would sweep away exactly the regression the test exists to catch.

The CLI family is not simply skipped there, though: `ConcurrentCommandsTest` has a second method gated on
`IsSingleCommandTransport()` — the same list, inverted — asserting that a transport allowed to *queue*
commands is still safe to call from several threads. Between them, every transport runs exactly one of the
two, so a transport that runs neither (or both) means the two lists have drifted apart.

## Other TestBase members

```csharp
public TestContext TestContext { get; set; }   // injected by MSTest (declared on LockedTestBase)
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

**A new O/R-mapper entity gets exactly two tests**, named after it: `List<Entity>sWillNotFail` (guard,
then `LoadAll<T>()` is non-null) and `Add<Entity>WillNotFail` (create with a marker, reload by id,
assert, delete in a `finally`). A **read-only or singleton** entity gets only the List test —
`LoadSingle<T>()` for a singleton, `LoadAll<T>()` otherwise. The `entity-generator` skill scaffolds
the entity itself; the harness is here.

- Always `try/finally` cleanup — a failing test must still delete what it created.
- Guards at the top of the test, not scattered through it.
- Prefix created objects `t4n` + a GUID fragment so orphans are identifiable. A fixed name that more than
  one test of the class uses goes through `TestNames.Unique`, or the parallel legs collide on it.
- Decide the test's lock (see *Parallel legs* above): nothing for a test on its own rows, a resource lock for a
  shared singleton or lab resource, `TestLockScope.Router` for router-wide state or a whole-table comparison,
  `[SafeInParallelLegs]` for a slow read-only test.
- `Console.WriteLine` is captured by MSTest and is the cheapest debugging channel.
- Test files mirror the entity's folder: `tik4net.objects/Ip/Firewall/` → `tik4net.integrationtests/Ip/Firewall/`.
  One test class per domain folder is fine. Core/infra tests stay at the project root.
- The project is SDK-style: new `.cs` files are picked up automatically, no `.csproj` edit.

**Run a new test against the unfixed code first.** A test that passes before the fix cannot be cited as
evidence the fix works.

### Protocol PoC tests

Tests under `Protocols/` manage their own connection and do **not** derive from `TestBase` (they derive from
`LockedTestBase`, for the locks). They do not read the leg's transport, so they carry
`[TestCategory(TestCategories.LegIndependent)]` and run once, in the `independent` leg. A new class that picks its
transports itself belongs in that category too; one that reads `tik.connectionType` must not be in it.

## Layout

```
tik4net.integrationtests/
├── LockedTestBase.cs               — base of every test class: the per-test locks, test categories
├── TestLocks.cs                    — cross-process reader/writer locks, [TestLock], [SafeInParallelLegs]
├── RunLease.cs                     — run tag + lease, TestNames.Unique
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
