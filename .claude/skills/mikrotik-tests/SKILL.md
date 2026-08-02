---
name: mikrotik-tests
description: >
  Skill for working with tik4net integration tests against a live MikroTik router.
  Use this skill whenever the user wants to: run tests (single transport or all 11 in series),
  create a new test class/method, inspect or clean router state, understand why a test was
  skipped (Inconclusive), debug failures, analyze orphan objects, or check router logs after
  a test run. Also trigger when the user mentions TestBase, EnsureCapability, runsettings,
  connectionType, or any test file in tik4net.integrationtests/. Includes known baseline failure catalog
  from a full 11-transport run (2026-06-20) with expected durations and CLI gotchas.
---

# MikroTik Tests Skill

## Project context

- Test project: `tik4net.integrationtests/` — MSTest, .NET 4.8
- Tests hit a **real MikroTik router** in HyperV. There are no mocks.
- **This skill covers the integration suite only.** Router-free tests live in a separate
  `tik4net.unittests/` (net8.0) project and run in CI — if a new test does not actually need
  hardware, write it there instead (`dotnet test tik4net.unittests/tik4net.unittests.csproj`).
- Router connection settings in `tik4net.integrationtests/App.config`:
  ```xml
  <add key="host"            value="<router IP>"/>
  <add key="user"            value="<user>"/>
  <add key="pass"            value="<password>"/>
  <add key="routerMac"       value="<router MAC>"/>
  <add key="routerIdentity"  value="MikroTik"/>
  <add key="connectionType"  value="Api"/>
  <add key="restPort"        value="80"/>
  <add key="restSslPort"     value="443"/>
  <add key="restAllowInvalidCert" value="true"/>
  ```

---

## Connection types — all 11

| Enum / runsettings | Protocol | Port | Capability set | Approx. skip count |
|--------------------|----------|------|----------------|--------------------|
| `Api` / `api` | MikroTik API plain | 8728 | Crud+Listen+Streaming+RawSentences+Tagging | 60 |
| `ApiSsl` / `apissl` | MikroTik API TLS | 8729 | Crud+Listen+Streaming+RawSentences+Tagging | 60 |
| `Rest` / `rest` | REST HTTP | 80 | **Crud only** | 90 |
| `RestSsl` / `restssl` | REST HTTPS | 443 | **Crud only** | 90 |
| `Telnet` / `telnet` | CLI plain | 23 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `MacTelnet` / `mactelnet` | CLI over MAC UDP | 20561 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `Ssh` / `ssh` | CLI over SSH | 22 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxCli` / `winboxcli` | CLI in the Winbox terminal | 8291 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxCliMac` / `winboxclimac` | CLI in the Winbox terminal over MAC | 20561 | Crud+Listen\*+SafeMode+RawCommand | 77 |
| `WinboxNative` / `winboxnative` | M2 native protocol | 8291 | **Crud+Listen\*+SafeMode** | 221 |
| `WinboxNativeMac` / `winboxnativemac` | M2 over MAC | 20561 | **Crud+Listen\*+SafeMode** | 221 |

\* **Listen** is emulated on CLI/native via polling (background re-issue snapshot), not push. **Streaming** is API only.

**CLI transports** = Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac — all share the behavior described in the *CLI transport gotchas* section.

**WinboxNative/WinboxNativeMac** have the highest skip count (221 of 415): their capability set is the narrowest (no Listen, no Streaming, no CLI-specifics). Tests that depend on CLI syntax are skipped.

### Capabilities (verified from code)

```
Crud         — basic CRUD (all transports)
Listen       — async watch (Api/ApiSsl push; CLI + WinboxNative via polling)
Streaming    — ExecuteListWithDuration (Api/ApiSsl only)
RawSentences — raw sentence access (Api/ApiSsl only)
Tagging      — tag multiplexing (Api/ApiSsl only)
SafeMode     — Api/ApiSsl + CLI family + WinboxNative (NOT Rest)
RawCommand   — Api/ApiSsl + CLI family (NOT Rest, NOT WinboxNative)
```

- **Api/ApiSsl**: everything. **Rest/RestSsl**: Crud only (stateless HTTP). **CLI family** (Telnet/MacTelnet/
  Ssh/WinboxCli/WinboxCliMac): Crud+Listen\*+SafeMode+RawCommand. **WinboxNative/Mac**: Crud+Listen\*+SafeMode.
- `EnsureCapability(cap)` → `Inconclusive` (skip) if the transport doesn't support the capability.

---

## Running the tests

### Single transport

```powershell
# With TRX results (recommended):
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj \
  --settings tik4net.integrationtests/winboxcli.runsettings \
  --logger "trx;LogFileName=results_winboxcli.trx" \
  --results-directory TestResults \
  --verbosity normal

# Without TRX (console output only):
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj --settings tik4net.integrationtests/api.runsettings
```

### All transports in series — recommended strategy

```powershell
# Ordered from fastest (API) to slowest (WinboxCliMac).
# WinboxNative/WinboxNativeMac are fast thanks to their large skip count.
$transports = @("api","apissl","rest","restssl","telnet","ssh","mactelnet",
                "winboxnative","winboxnativemac","winboxcli","winboxclimac")

foreach ($t in $transports) {
    Write-Host "=== $t ===" -ForegroundColor Cyan
    dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
        --settings tik4net.integrationtests/$t.runsettings `
        --logger "trx;LogFileName=results_$t.trx" `
        --results-directory TestResults `
        --verbosity normal
}
```

**Order matters:** CLI transports leave orphans behind (see the Orphans section). Run the API-based
transports first, then CLI. WinboxCli before WinboxCliMac — otherwise an orphan left by WinboxCli
causes a different error in WinboxCliMac.

### Re-running only the failed tests

After a series run, parse the TRX and re-run only the failures:

```powershell
# Get the list of failed tests from the TRX:
[xml]$trx = Get-Content "TestResults\results_winboxcli.trx"
$failed = $trx.TestRun.Results.UnitTestResult |
    Where-Object { $_.outcome -eq 'Failed' } |
    Select-Object -ExpandProperty testName

# Run just those (--filter accepts | as OR):
$filter = ($failed | ForEach-Object { "Name=$_" }) -join "|"
dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
    --settings tik4net.integrationtests/winboxcli.runsettings `
    --filter $filter
```

### Smoke subset for larger changes (quick cross-transport check)

The full 11-transport matrix is for a full code review / release. For **regular larger changes**
(beyond unit tests), this is enough:

1. **Full run over API** (`api.runsettings`) — fastest (~5 min) and widest capability set,
   catches most logical regressions.
2. **Light smoke subset over the remaining transports** — just a few fast, self-contained tests
   that don't leave orphans and cover basic CRUD + singleton load + connection handshake:

   ```powershell
   $smokeFilter = "FullyQualifiedName~ConnectionTest|FullyQualifiedName~SystemClockTest|FullyQualifiedName~InterfaceListTest|FullyQualifiedName~IpRouteTest"
   $transports = @("rest","restssl","telnet","ssh","mactelnet","winboxcli","winboxclimac","winboxnative","winboxnativemac")

   foreach ($t in $transports) {
       dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
           --settings tik4net.integrationtests/$t.runsettings `
           --filter $smokeFilter `
           --logger "trx;LogFileName=smoke_$t.trx" `
           --results-directory TestResults `
           --verbosity normal
   }
   ```

   `ConnectionTest` always exercises Api/ApiSsl directly (it doesn't read `tik.connectionType`),
   so it's really a fixed sanity check; the other three classes do respect the runsettings
   transport and give basic load/CRUD coverage per protocol without the ~50+ min cost of the
   full CLI suites.

3. Only run the **full 11-transport matrix** (see below) when the change touches a
   transport-specific area (`Crypto/`, `WinboxNative*/`, `MacTelnet/`, `ApiConnection`
   reader/tag multiplexing, CLI parsers) or before a release.

### Approximate duration of a full run

| Transport | Duration |
|-----------|------|
| Api, ApiSsl | ~5 min |
| Rest, RestSsl | ~3 min |
| Telnet, Ssh | ~7 min |
| MacTelnet | ~13 min |
| WinboxNative, WinboxNativeMac | ~5-8 min (lots of skips) |
| WinboxCli | ~7 min |
| WinboxCliMac | **~1 h 20 min** |

Re-measured 2026-07-26 (full run on 7.23.2, 390 tests). **WinboxCli is NO LONGER a slow transport** —
it used to take ~52 min, but that was caused by the mepty wedge, which P2.13c (byte-ACK) fixed; today
it runs as fast as Telnet/SSH. Only **WinboxCliMac** remains slow, and that's due to the MAC layer, not
the Winbox terminal: MAC transports pay ~5 s per command versus ~200 ms over TCP, and they also get stuck
during a full run (P2.19) — each wedge costs a 30 s receive-timeout. Test timeouts (SafeMode: 1 min,
traceroute: skip) don't shrink.

### Results from all 11 transports

> The old pass/fail counts (2026-06-20) were **stale** — most of the A/B failures didn't reproduce
> after fixes + live verification. For the disposition of all categories (A–K) and the current limits
> table, see [`TestResults/test-failures-report.md`](../../../TestResults/test-failures-report.md).
> After a clean full run, regenerate the counts from the TRX files (see *Parsing TRX results*).

---

## Parsing TRX results

```powershell
# Summary of all TRX files at once.
# ALWAYS read the skips (Inconclusive) — two "green" runs can differ only by their count,
# and for an intermittent bug that is the only observation you have (see Hunting an intermittent bug).
foreach ($trx in (Get-ChildItem TestResults\results_*.trx | Sort-Object Name)) {
    [xml]$x = Get-Content $trx.FullName
    $c = $x.TestRun.ResultSummary.Counters
    # MSTest Assert.Inconclusive is reported in the TRX as notExecuted, not as inconclusive.
    "$($trx.Name): pass=$($c.passed) fail=$($c.failed) skip=$($c.notExecuted) total=$($c.total)"
}

# List failures from a single TRX:
[xml]$x = Get-Content TestResults\results_winboxcli.trx
$x.TestRun.Results.UnitTestResult |
    Where-Object { $_.outcome -eq 'Failed' } |
    ForEach-Object {
        $msg = ($_.Output.ErrorInfo.Message -replace '\r?\n',' ').Trim()
        "$($_.testName) | $($msg.Substring(0,[Math]::Min(120,$msg.Length)))"
    }

# Named skips (neither `-v q` nor the summary line gives you this):
$x.TestRun.Results.UnitTestResult |
    Where-Object { $_.outcome -eq 'NotExecuted' } |
    Select-Object -ExpandProperty testName | Sort-Object
```

---

## Hunting an intermittent bug

A bug that fails once every N full runs (P2.19, P2.47, P2.49) **cannot be reproduced by blind
repetition** — each attempt costs the router tens of minutes. The goal isn't to hit it again, but
to make sure the next occurrence is enough by itself for a diagnosis.

1. **Always use `--logger trx`, with a per-run file name.** A summary of `Failed: 0` doesn't mean
   the run was identical to the previous one — a test can intermittently fail on a data-dependent
   `Assert.Inconclusive` instead of an assert, and then only the **skip count** changes. Without
   the TRX you won't know that happened, let alone which test it was.
2. **Turn on byte-level trace for the whole run** via `TIK4NET_WIRETRACE` (a file path, or `1` for
   the default next to the assembly). `WireTraceCapture` writes test boundaries into the trace as
   `--- TEST <name>` / `--- END <outcome> <name>`, so a failure can be located in it without
   correlating timestamps:

   ```powershell
   $stamp = Get-Date -Format yyyyMMdd-HHmmss
   $env:TIK4NET_WIRETRACE = "TestResults\wire_telnet_$stamp.txt"
   dotnet test tik4net.integrationtests/tik4net.integrationtests.csproj `
       --settings tik4net.integrationtests/telnet.runsettings `
       --logger "trx;LogFileName=results_telnet_$stamp.trx" `
       --results-directory TestResults
   Remove-Item Env:\TIK4NET_WIRETRACE
   ```

3. **Only delete green-run artifacts after comparing runs against each other**, not right after a
   green result. The trace is large, so cleanup is tempting — but a run with a different skip count
   is just as interesting as a red one, and once you've deleted it, the observation is gone for good.
4. **Before you believe you've reproduced something, rule out login artifacts.** The string
   `login failure` in the trace is usually a **login banner** (the router prints recent logs on
   login), not an async event; tell them apart by whether it's near the IAC negotiation `<FF><FD>`.
5. **Residue first, code second.** A failure on an unexpected `.id`, a name collision, or a row
   count is usually a reaction to an orphan left by a previous run (see the Orphans section), not
   a defect.

---

## CLI transport gotchas

These symptoms show up on **Telnet, MacTelnet, Ssh, WinboxCli, WinboxCliMac**.

### A — `add` returns an empty string (the object is created on the router, but without an ID)

`:put [/path add ...]` returns `""` instead of the new `.id`. The library has no ID → `TikNoSuchItemException`. Meanwhile the object **does exist on the router** → orphan (cleanup fails — see the Orphans section).

```
CLI>> :put [/interface eoip add name=test-eoip ...]
CLI<<         ← empty string
→ TikNoSuchItemException: no such item /interface/eoip/add
```

Shows up **mainly on WinboxCli/WinboxCliMac**. On Telnet/SSH only rarely (e.g. AddRadiusServerWillNotFail on MacTelnet).
Cause: the WinboxCli terminal has higher latency; the router's response arrives outside the read window.

### B — Singleton `LoadSingle` — the second `print` returns empty

`LoadSingle<T>` calls `print as-value` twice (the first to detect an empty result, the second for data). On WinboxCli/WinboxCliMac the second call returns `""`.

```
CLI>> :put [/ip settings print as-value]
CLI<< ip-forward=yes;...
CLI>> :put [/ip settings print as-value]
CLI<<         ← empty
→ TikNoSuchItemException: no such item /ip/settings/print
```

Affected tests: `LoadIpSettingsWillNotFail`, `LoadIpTrafficFlowWillNotFail`, `LoadPppAaaWilNotFail`, `LoadSnmpWillNotFail`, `LoadMacServerWillNotFail`, `ExecuteSingleRow_With_Tag_Parameter`.

### C — ~~Terminal truncation~~ multi-value field: `Missing field 'name'` ✅ FIXED

**The original diagnosis (truncation) was WRONG.** The real cause: RouterOS renders a
multi-value (list) field in `as-value` output with a `;` separator — the SAME character used
between fields: `key-usage=key-cert-sign;crl-sign;name=mikrotik-CA`. The parser split on `;`,
so `name` ended up merged into the key `crl-sign;name` → `GetResponseField("name")` failed.

Fix: `CliOutputParser.ParseOrderedFields` — a `;`-token without `=` is a continuation (element)
of the previous multi-value field (joined by comma, like the API). Applies to ALL CLI transports
(shared parser), there is no "different terminal width". Verified: Certificate/HotspotProfile/File/Pptp
pass over both Telnet and WinboxCli.

### D — `fib=yes` rejected (presence-flag) ✅ FIXED

The RouterOS CLI rejects `fib=yes` — `fib` is a presence-flag: it's set with a bare name (`… fib`),
`=value` returns `expected end of command`. The binary API/REST accept `fib=yes` (which is why only
CLI failed). Verified live: `/routing/table/add` tab-completion lists `fib`; `fib=yes` → error at the
`=` column.

Fix: `CliCommandBuilder` — `CliPresenceFlagFields = { "fib" }`; truthy → bare name,
falsy → omit. Extensible to further presence-flags. Verified: AddRoutingTable over Telnet.

### E — SafeMode rollback after disconnect

`SafeMode_DisconnectWithoutRelease_RollsBack` expects a rollback after a disconnect without release.
**The assumption "CLI doesn't roll back" was never verified** → `SkipOnNonApi` removed. The test now
**observes** the behavior (30 s poll): it passes if the transport rolls back (including CLI/native),
otherwise `Inconclusive` (not a failure). `[Timeout(90000)]` is just a safety net against a stuck router.

### G — RunScript log race condition

The script runs, but `:put [/log print as-value]` doesn't catch it at query time. Shows up on MacTelnet, WinboxCli, WinboxCliMac.

> **2026-07-30:** a second, worse problem was added on top — polling without a filter. The test pulled
> the **entire** 1000-line memory log, up to 10 times in a row (**85,593 characters** on mactelnet, 73,710
> on winboxclimac), and a single such dump over the MAC terminal can't fit the 30 s budget. So the test
> failed depending on how chatty the log happened to be. Fixed with a filter **on the router**:
> `CreateCommandAndParameters("/log/print", "message", logMarker)` — the marker is written via
> `:log info ("RUN53_<guid>")`, so it matches exactly and returns one line or none. The filter is honored
> by Api (`?message=`), Rest, CLI (`where message=…`), and WinboxNative — verified live. After the fix it
> passes on **all 11 transports** (mactelnet 31 s timeout → 2 s, winboxclimac 1 m 16 s fail → 32 s pass).
> **Lesson for new tests:** never pull `/log/print` (or any other large list) without a filter inside a
> poll loop — on CLI/MAC transports that's a direct path to `TikConnectionReceiveTimeoutException`.

---

## WinboxNative / WinboxNativeMac gotchas

### Unmapped paths (.jg catalog)

Native CRUD only works for paths present in the version-matched `.jg` catalog. A path absent from
every WinBox window → `WinBox native: no M2 handler mapping for path '…'`. Verified unmapped:
`/tool/netwatch`, `/routing/bgp/advertisements`. Workaround: `connection.PathOverride(path, new[]{maj,min})`
or CLI/API. Test guard: `SkipOnWinboxNativeUnmappedPath(path)`.

### I — bool DefaultValue (NOT a missing mapping)

`AddSystemScriptWillNotFail` failed because `bool` serializes to `"no"/"yes"`, but the entity had
`DefaultValue="false"` → `HasDefaultValue` never matched → the field was always sent → native had no
M2 key for it. Fixed (`"no"`) + a blanket audit of all `bool` entities. **It was not a missing catalog
entry.** Watch for this pattern in new entities: a `bool` default is always the wire form `"no"/"yes"`,
not `"false"/"true"`.

### J — `/system/health` native ✅ FIXED (board-gated singleton)

Root cause: health is board-gated. The alias pointed at the `map` window `[24,29]` → `getall` =
`0xFE0002 NotImplemented` on x86/CHR. The correct window on x86 is the singleton `item` `[24,14]`
read via **get-singleton** (`0xFE000D`, verified live). Fix: `WinboxNativeConnection.PreferSingletonHealthHandler`
→ `WinboxJgCatalog.FindSingletonHandlerByLeaf("health")` (handler resolved live from `.jg`, not
hardcoded). `LoadSingle<SystemHealth>` over native now **passes**. Note: `state`/`state-after-reboot`
are API/CLI-only — the WinBox health window is a read-only HW-sensor display (`on:'lm87'`), empty on
CHR → a genuine WinBox limit. The `catch when (IsWinboxNativeUnsupported)` guard remains as a safety net.

### K — bridge-vlan `vlan-ids` native ✅ FIXED (multinumberrange)

`vlan-ids` = `multinumberrange` (`[16,13]` id `U1`, u32[]). The webfig `types.multinumberrange.put`
(without id2) flattens ranges to a u32[] `[lo0,hi0,…]` (`"3999"` → `[3999,3999]`). Fix:
`WinboxFieldResolver.EncodeField` encodes it (`U32ArraySys`), `WinboxRecordCodec` decodes it back;
the round-trip was verified live. **In addition:** the resolver now throws loud
(`WinboxFieldResolutionException`) for unsupported list/array fields (wireType `…[]` or uiType
`multi…`) instead of silently dropping them. Remaining TODO: `tagged`/`untagged` (multinumber
interface lists) and native **bridge creation** (`add type=bridge` → `0xFE0006`, a separate gap —
the test's safety net skips it when there's no existing bridge).

---

## Cross-run contamination — orphans

**Core issue:** a CLI add (Cat. A) leaves an object on the router without a tracked ID. Cleanup fails.
The next transport then gets a different error (`already have interface with name X` instead of
`no such item`).

```
WinboxCli:
  AddEoipWillNotFail → add returns "" → fail → orphan test-eoip left on the router

WinboxCliMac (after):
  AddEoipWillNotFail → already have interface with name test-eoip → a different error!
```

**Objects prone to the orphan problem** (CLI transports):
- IPsec peers (`AddIpsecIdentityWillNotFail`, `AddIpsecPolicyWillNotFail`) → causes an `ipsec,error` flood in the log
- Eoip interfaces (`AddEoipWillNotFail`)
- L2TP clients (`AddL2tpClientWillNotFail`)
- WiFi channel/security
- Bridge filter rules
- Hotspot profiles, hotspot users
- Firewall filter/raw rules

---

## Checking for orphans and the log after each run

After each transport run, verify the router's state:

```python
# Via MCP:
/ip/ipsec/peer/print                        # IPsec peers (cause an error flood)
/interface/eoip/print                       # Eoip orphans
/interface/l2tp-client/print                # L2TP orphans
/ip/hotspot/profile/print  ?name~TEST_      # Hotspot profiles
/interface/bridge/filter/print              # Bridge filter rules
/interface/wifi/channel/print               # WiFi channels
/interface/wifi/security/print              # WiFi securities
```

**Last 100 log lines** (error-flood detection):

```python
# Via MCP — filter for today's date:
command: /log/print
parameters: ["?>time=2026-06-20 00:00:00"]
# (adjust the date)
```

Look for:
- `ipsec,error initiator can't find identity` — orphan IPsec peer, remove it via `/ip/ipsec/peer/remove`
- `dhcp,error bonding1: DHCP offer rejected` — router configuration, unrelated to the tests
- Repeated entries of the same error = something is wrong

**The suite must not leave error-severity lines behind.** A red `/log` after a run scares whoever opens
the router next, so every marker the tests write is `info`. The single deliberate exception is
`LogWriteTest.LogErrorWritesALineReadableBack`, which covers `LogError` itself — one line per run per
transport. Two historical sources of noise, both fixed **2026-08-02**:

- `dhcp,error events on master port will be handled by slave ether1, update your config!!! (IPv4)` —
  `InterfaceBondingTest` enslaved `TestConstants.Interface` (the management port carrying the DHCP
  client) because a bond needs at least one slave. It now bonds a disposable `test-bond-slave` veth
  (`container` package), which logs nothing; `/interface/veth` is in `RouterOrphanCleaner` after
  `/interface/bonding`.
- `RunScript_Issue53` and `LogTopicsTest` wrote their markers with `:log error` / `LogError` — both now
  log at `info`, and `LogTopicsTest` asserts the `info` topic instead.

**Manual orphan cleanup:**
```python
/ip/ipsec/peer/remove  params: ["=.id=*X"]       # specific ID
/interface/eoip/remove params: ["=name=test-eoip"]
```

---

## Known baseline failures (state as of 2026-06-20)

> **NOTE — the `TestResults/test-failures-report.md` report (2026-06-20) is largely OUTDATED.**
> When verified against the current source, most A/B "failures" didn't reproduce at all (add
> returns an id, singleton load works). Many entries were orphan contamination or flaky timing,
> not bugs. Below is the state AFTER the fixes made in this session. When in doubt, always run
> the specific test live — don't trust the old report.

### ✅ Fixed in the library (pass on all transports)

| Cat. | What | Fix |
|------|----|--------|
| C | `Certificate`/`HotspotProfile`/`File`/`Pptp` — `Missing field 'name'` | `CliOutputParser`: multi-value `;`-elements = continuation of a field (not a new field) |
| D | `AddRoutingTableWillNotFail` — `fib=yes` | `CliCommandBuilder.CliPresenceFlagFields` — bare `fib` |
| H | `GenerateAndDeleteIpsecKeyWillNotFail` (REST) | `RestRequestBuilder._writeVerbs` += `generate-key`/`export-pub-key`/`import` — without them `/print` was appended |
| I | `AddSystemScriptWillNotFail` (WinboxNative) | `bool` DefaultValue `"false"/"true"`→`"no"/"yes"` — **fixed across all `bool` entities** (17 files); `YesNoOptions` enum left as-is (`[TikEnum("false")]`) |
| A/B | add/singleton flaky timeout on WinboxCli | `WinboxCliClient`: pre-send `DrainSync` when residual data is present (against desync) |
| G | `RunScript_Issue53_WillNotFail` — log race + **dumping the entire log** | test polls the log for ~5 s instead of a single check; **2026-07-30** additionally filters `?message=<marker>` on the router instead of pulling 1000 lines (see section G) |
| J | `/system/health` native (board-gated) | `PreferSingletonHealthHandler` → singleton `[24,14]` get-singleton (handler resolved live from `.jg`) |
| K | bridge-vlan `vlan-ids` native (silent drop) | `multinumberrange` encoding/decoding (u32[]) + loud-throw for unsupported list types |
| a | `/tool/netwatch` native unmapped path | shipped alias `/tool/netwatch` → `[51,1]` in `WinboxHandlerMap` |

> **NOTE (H):** REST action verbs DO WORK (`POST /rest/<path>/<verb>`, verified `…/generate-key`→200).
> The bug was in the library (the verb wasn't recognized → `/print` got appended). NOT a skip — fixed in the builder.

### ✅ Skip guards — tied to a specific limit (`Inconclusive`, not a failure)

| Cat. | Test | Guard | Note |
|------|------|-------|-------|
| J | `LoadSystemHealthWillNotFail` | ✅ FIXED — `catch when (IsWinboxNativeUnsupported)` remains only as a safety net | native now reads health via get-singleton `[24,14]`; LoadSingle passes |
| K | `AddBridgeVlanWillNotFail` | ✅ FIXED — `vlan-ids` round-trip asserted for all transports; `catch when (IsWinboxNativeUnsupported)` is just a safety net | the safety net now catches the native bridge-**creation** gap (`0xFE0006`) when there's no existing bridge |
| E | `SafeMode_DisconnectWithoutRelease_RollsBack` | no skip — runtime poll → pass/`Inconclusive` | the assumption couldn't be verified over the stateless MCP → the test observes instead |
| — | bgp/advertisements (and other uncovered paths) | `SkipOnWinboxNativeUnmappedPath` | path is absent from the `.jg` / handler map. **netwatch already FIXED** (alias `[51,1]`) |

> **Principle:** prefer a feature/runtime-bound skip (`IsWinboxNativeUnsupported` — catches a
> SPECIFIC M2 error, doesn't mask other bugs, and disappears by itself once the transport supports
> the feature) over a blanket transport-name skip. **Before setting a gate, verify live that it's
> not a false assumption** (like the old `SkipOnRest`/`SkipOnNonApi`/old Cat. K were). `IsNonApiTransport`
> remains only for branching **assertions** (not as a skip gate).

> **The exception that's deliberately transport-name based:** `SkipOnSingleCommandTransport()` (P2.42,
> used by `ConcurrentCommandsTest`). It enumerates the CLI family (Telnet/SSH/MacTelnet/WinboxCli/WinboxCliMac),
> because **that's the assertion itself**: only they have a reason to serialize, everything else (API,
> API-SSL, REST, both native WinBoxes) must be able to run concurrently on a single connection. A
> feature-bound variant ("skip if the transport can't handle it") would sweep under the rug exactly the
> regression this test exists to catch. Don't confuse this with the removed blind gates — this one
> skips **expected** behavior, not an unverified assumption.

### ⚠️ Orphan contamination (NOT a bug — clean up the router before/between runs)

`AddEoipWillNotFail` and similar tests fail with `already have interface with name test-eoip` when a
previous (old) run left an orphan behind. Delete the orphans via the API (see the *Checking for orphans*
section). On a clean router, add passes.

### ⚠️ Flaky (intermittent, not deterministic) — retry on failure

`LoadIpTrafficFlowWillNotFail`, `LoadListenAsync_*`, `*Async*`, `ParallelSniff*`,
`PingLocalhostAsyncWillNotFail` — polling/async over the slower CLI transports. The pre-send drain
(Cat. A/B fix) reduced the flakiness; still, on an isolated failure, retry just that test.

---

## TestBase — key methods

```csharp
public TestContext TestContext { get; set; }   // injected by MSTest
protected ITikConnection Connection { get; }   // created in [TestInitialize]

protected TikConnectionType ResolveConnectionType()
// priority: runsettings "tik.connectionType" > App.config > "Api"

protected void RecreateConnection(int retryTimeoutSeconds = 20)
protected void EnsureCapability(TikConnectionCapability cap, string feature = null)
protected void EnsureMinRouterOsVersion(int minimumMajor, string featureDescription = null)
protected void EnsureMaxRouterOsVersion(int removedInMajor, string featureDescription = null)
protected void EnsureCommandAvailable(string commandPath)
protected Version GetMikrotikVersion()

// Skip helpers (Assert.Inconclusive). PREFER runtime/feature-bound skips over transport-name skips:
protected static bool IsWinboxNativeUnsupported(Exception ex) // catch-when: specific M2 error/field-resolve → Inconclusive
protected void SkipOnWinboxNativeUnmappedPath(string feature) // path absent from .jg catalog (verify it's actually missing)
protected void SkipOnSingleCommandTransport()                 // CLI family serializes ON PURPOSE — see the exception above
protected bool IsNonApiTransport()                            // ONLY for branching assertions, NOT as a skip gate
// (SkipOnNonApi and SkipOnRest/SkipOnWinboxNative were removed — blind/unverified transport-name gates)
```

---

## Creating a new O/R mapper test

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
        try {
            var loaded = Connection.LoadById<DhcpServer>(entry.Id);
            Assert.IsNotNull(loaded);
        } finally {
            if (entry.Id != null) Connection.Delete(entry);  // always clean up!
        }
    }
}
```

**Patterns:**
- Always `try/finally` cleanup — even on failure the test must delete what it created.
- `EnsureCapability`, `EnsureMinRouterOsVersion`, `EnsureCommandAvailable` at the start.
- Prefix `t4n` + a GUID suffix for test objects (easy to find on the router).
- `Console.WriteLine(...)` for debugging — MSTest captures stdout.

---

## Creating a protocol PoC test

Protocol tests (Winbox, MacTelnet, raw API) **don't use TestBase** — they manage their own connection.

```csharp
[TestClass]
public class MyProtocolTest
{
    [TestMethod]
    public void Protocol_DoSomething_Works()
    {
        var host = ConfigurationManager.AppSettings["host"];
        var user = ConfigurationManager.AppSettings["user"];
        var pass = ConfigurationManager.AppSettings["pass"] ?? "";
        // raw TCP/UDP, custom client, assertions
    }
}
```

**Note:** Protocol tests don't skip under other transports! If you run `winboxnative.runsettings`, WinboxTcpProtocolTest still runs (it manages its own connection). A protocol test failing during a different transport run = a resource/timing collision, not a transport bug.

---

## Inspecting the router via MCP

```python
# Version and identity
/system/resource/print
/system/identity/print

# State after tests — look for orphans
/ip/ipsec/peer/print                    # → delete anything with name~t4n
/interface/eoip/print                   # → delete name=test-eoip
/interface/l2tp-client/print            # → delete name~t4ntest-l2tp
/ip/hotspot/profile/print               # → delete name~TEST_
/interface/bridge/filter/print          # → delete entries with GUID comments
/interface/wifi/channel/print           # → delete name~test-
/interface/wifi/security/print          # → delete name~test-

# Log after tests (last ~100 entries = today)
/log/print  params: ["?>time=2026-06-20 00:00:00"]
# Look for: ipsec,error + a repeated identical message = orphan flood
```

---

## File structure

```
tik4net.integrationtests/
├── TestBase.cs                            — base class
├── App.config                             — router connection settings
├── api.runsettings ... winboxnativemac.runsettings  — 11 transport settings
├── TestResults/
│   ├── results_api.trx ... results_winboxnativemac.trx
│   └── test-failures-report.md           — disposition of categories A–K + transport limits matrix
├── Protocols/
│   ├── _Shared/                           — EcSrp5, WinboxStreamCrypto, M2Message, VT100
│   ├── Transport/                         — TCP, MAC layer helpers
│   ├── Clients/                           — WinboxM2Client, MacTelnetClient, ...
│   └── Tests/
│       ├── ApiProtocolTest.cs
│       ├── WinboxTcpProtocolTest.cs
│       ├── WinboxMacProtocolTest.cs
│       ├── MacTelnetProtocolTest.cs
│       └── WinboxDumpCatalogTest.cs
└── [domain tests]/
    ├── Interface/, Ip/, Routing/, System/, Tool/, ...
```
