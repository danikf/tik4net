<#
.SYNOPSIS
    Runs the tik4net integration suite against the lab routers: every transport, every router, in parallel
    by default; or one transport, a smoke subset, one router, sequentially.

.DESCRIPTION
    The integration suite is run once per transport (a "leg"). Which transport a leg uses comes from the
    `tik.connectionType` run parameter, supplied by one *.runsettings file per transport in
    tik4net.integrationtests/.

    By default every leg is its own `dotnet test` process and they all run at once. What keeps them from
    colliding on the router is in the suite, not here: every test holds cross-process locks from start to
    end (tik4net.integrationtests/TestLocks.cs), and the orphan sweep only removes rows whose run has ended
    (RunLease.cs). -Sequential runs the legs one after another, as before.

    Classes that never read the leg's transport (category LegIndependent - RomonRelayTest and the other
    protocol probes) measure the same thing in every leg, so they run once, in a leg of their own named
    "independent" (over the binary API), and every transport leg excludes them.

    Router coordinates are NOT arguments to this script: they live in
    tik4net.integrationtests/App.config, which is the single source of truth. -Router picks the router
    profiles that file defines; nothing tracked is edited to switch routers. The secondary routers carry
    none of the default router's topology, so they get only the tests in category AnyRouter.

    Results are always written as TRX so that skips remain inspectable after the run - a summary line
    of "Failed: 0" does not prove two runs were identical. Use parse-trx.ps1 to read them. In parallel
    mode each leg's console output goes to a .log file beside its TRX.

.PARAMETER Transport
    One or more transport names matching the *.runsettings files (api, apissl, rest, restssl, telnet,
    ssh, mactelnet, winboxcli, winboxclimac, winboxnative, winboxnativemac).
    Defaults to every transport, ordered API-before-CLI (see the mikrotik-tests skill).

.PARAMETER Smoke
    Run only the fast, self-contained smoke classes instead of the full suite. This is the subset
    intended for the non-API transports when validating an ordinary larger change.

.PARAMETER Filter
    An explicit --filter expression, overriding -Smoke. It is combined with the leg's own category filter.

.PARAMETER Router
    The router profiles to run against: 'default' (the unprefixed App.config keys) and/or the names of
    profiles App.config defines ('<name>.host'). Default: every router App.config defines. A secondary
    router runs only category AnyRouter. The script sets TIK4NET_ROUTER per leg, so a variable left
    over in the shell cannot redirect a run.

.PARAMETER Sequential
    Run the legs one after another in this console (the behaviour before parallel runs). Default: parallel.

.PARAMETER MaxParallel
    At most this many legs at once; 0 = no limit. Default 4, measured on the lab CHR (2 vCPU) on 2026-09-30:
    2 legs at once took 53 min, 4 took 34 min with the tests running only ~12 % slower than alone, and 12 at
    once slowed every leg 4-10x (Docs/findings-router-throughput-ceiling.md) for no gain in wall time.

.PARAMETER Pairs
    Two lanes per router instead of one per leg: the MAC-layer legs with the API and independent legs in one,
    the IP terminal, WinBox-IP and REST legs in the other, each lane running its legs one after another. The
    lightest load that still runs two legs at once.

.PARAMETER LockAll
    Serialise every test on the machine through the test locks (TIK4NET_LOCK_ALL=1) while keeping the
    parallel processes - the way to tell whether a failure is a collision between parallel tests.

.PARAMETER NoIndependentLeg
    Skip the leg that runs the LegIndependent classes.

.PARAMETER ResultsDirectory
    Where TRX files are written. Defaults to ./TestResults; a secondary router's go to <ResultsDirectory>/<router>,
    so a run against another router never moves the default router's results aside (git-ignored).

.PARAMETER NoBuild
    Run the already-built test assembly (dotnet test --no-build). A parallel run always builds once up
    front and starts every leg with --no-build; this skips that build too.

.PARAMETER WireTrace
    Enable byte-level wire tracing for the run by setting TIK4NET_WIRETRACE. Pass a file path, or
    'auto' to name the file after the leg and timestamp. Test boundaries are written into the trace, so a
    failure can be located without correlating timestamps. With more than one leg, use 'auto'.

.EXAMPLE
    ./run-integration-tests.ps1
    Everything: every transport on the default router, the AnyRouter tests on the others, all at once.

.EXAMPLE
    ./run-integration-tests.ps1 -Router default -Transport api -Sequential
    Full suite over the binary API on the default router, in this console.

.EXAMPLE
    ./run-integration-tests.ps1 -Smoke -Router default
    Smoke subset over every transport, in parallel.

.EXAMPLE
    ./run-integration-tests.ps1 -Router chr2 -Transport api -Filter 'FullyQualifiedName~CliFlagFieldsTest'
    The flag test against the second lab router (RouterOS 6), results in ./TestResults/chr2.
#>
[CmdletBinding()]
param(
    [string[]] $Transport = @('api', 'apissl', 'rest', 'restssl', 'telnet', 'ssh', 'mactelnet',
                              'winboxnative', 'winboxnativemac', 'winboxcli', 'winboxclimac'),
    [switch]   $Smoke,
    [string]   $Filter,
    [string[]] $Router,
    [switch]   $Sequential,
    [int]      $MaxParallel = 4,
    [switch]   $Pairs,
    [switch]   $LockAll,
    [switch]   $NoIndependentLeg,
    [switch]   $NoBuild,
    [string]   $ResultsDirectory = 'TestResults',
    [string]   $WireTrace
)

$ErrorActionPreference = 'Stop'

# Resolve the repository root from this script's location, so the script works from any working
# directory and carries no machine-specific path.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project  = Join-Path $repoRoot 'tik4net.integrationtests\tik4net.integrationtests.csproj'

if (-not (Test-Path $project)) {
    throw "Integration test project not found at $project"
}

# The smoke subset: fast, self-contained classes that do not leave orphans, covering the connection
# handshake, a singleton load, and basic list/CRUD.
$smokeClasses = @('ConnectionTest', 'SystemClockTest', 'InterfaceListTest', 'IpRouteTest')
$smokeFilter  = ($smokeClasses | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'

$baseFilter = $Filter
if (-not $baseFilter -and $Smoke) { $baseFilter = $smokeFilter }
$isSmoke = ($baseFilter -eq $smokeFilter)

# Validate the whole selection BEFORE running anything. This used to warn per leg and carry on, so
# `-Transport api,apissl` through `powershell -File` - which arrives as ONE string rather than an
# array - matched no runsettings, ran no test, and still exited 0 with an empty summary table. A
# run that measured nothing must not be indistinguishable from a run that passed.
$unknown = @($Transport | Where-Object {
    -not (Test-Path (Join-Path $repoRoot "tik4net.integrationtests\$_.runsettings")) })
if ($unknown) {
    $available = (Get-ChildItem (Join-Path $repoRoot 'tik4net.integrationtests') -Filter '*.runsettings' |
                  ForEach-Object { $_.BaseName }) -join ', '
    throw ("No runsettings for transport(s): {0}. Available: {1}. " -f ($unknown -join ', '), $available) +
          '(Passing a comma-separated list to -Transport via "powershell -File" makes it a single ' +
          'string - use "powershell -Command" with -Transport @(''api'',''rest'') instead.)'
}

# The router profiles. Checked against App.config before anything runs: a profile the file does not define
# would otherwise fail every test at its first connection (LabConfig throws), a matrix-long way to learn of a typo.
$appConfig = [xml](Get-Content (Join-Path $repoRoot 'tik4net.integrationtests\App.config'))
function Get-LabSetting([string] $key) {
    ($appConfig.configuration.appSettings.add | Where-Object { $_.key -eq $key } | Select-Object -First 1).value
}
$definedProfiles = @($appConfig.configuration.appSettings.add | Where-Object { $_.key -like '*.host' } |
                     ForEach-Object { $_.key -replace '\.host$', '' })
if (-not $Router) { $Router = @('default') + $definedProfiles }
foreach ($r in $Router) {
    if ($r -ne 'default' -and $definedProfiles -notcontains $r) {
        throw "App.config defines no router profile '$r' (no '$r.host'). Defined: default, $($definedProfiles -join ', ')."
    }
}

function Join-Filter([string[]] $parts) {
    $parts = @($parts | Where-Object { $_ })
    if (-not $parts) { return $null }
    if ($parts.Count -eq 1) { return $parts[0] }
    ($parts | ForEach-Object { "($_)" }) -join '&'
}

# The legs: one per router and transport, plus one independent leg per router. A lane is a sequence of legs run one
# after another; lanes run side by side.
#
# The MAC-layer legs always share one lane, so two of them never run at once. With two MAC sessions to one router
# the router drops datagrams of its own output - MAC-Telnet reports "the router ran a backlog past this client",
# WinBox-MAC logins fail - which a sequential run never shows (measured 2026-09-30: every MAC-Telnet failure of
# the parallel runs, none in the sequential ones since September).
$macTransports = @('mactelnet', 'winboxclimac', 'winboxnativemac')
function Get-Lane([string] $router, [string] $leg) {
    if ($Pairs) {
        if ($macTransports -contains $leg -or @('api', 'apissl', 'independent') -contains $leg) { return "$router/mac" }
        return "$router/ip"
    }
    if ($macTransports -contains $leg) { return "$router/mac" }
    "$router/$leg"
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$legs = @()
foreach ($r in $Router) {
    $secondary   = ($r -ne 'default')
    $routerFilter = if ($secondary) { 'TestCategory=AnyRouter' } else { $null }
    # A secondary router always gets a folder of its own: its legs have the same TRX names as the default's.
    $resultsRel  = if ($secondary) { Join-Path $ResultsDirectory $r } else { $ResultsDirectory }
    $resultsPath = Join-Path $repoRoot $resultsRel
    if (-not (Test-Path $resultsPath)) { New-Item -ItemType Directory -Path $resultsPath | Out-Null }
    $prefix = if ($isSmoke) { 'smoke' } else { 'results' }

    foreach ($t in $Transport) {
        $legs += [pscustomobject]@{
            Router = $r; Name = $t; Lane = (Get-Lane $r $t); Runsettings = "$t.runsettings"; ResultsPath = $resultsPath
            Filter = Join-Filter @($baseFilter, $routerFilter, 'TestCategory!=LegIndependent')
            Trx    = "${prefix}_$t.trx"
        }
    }
    if (-not $NoIndependentLeg) {
        $legs += [pscustomobject]@{
            Router = $r; Name = 'independent'; Lane = (Get-Lane $r 'independent')
            Runsettings = 'api.runsettings'; ResultsPath = $resultsPath
            Filter = Join-Filter @($baseFilter, $routerFilter, 'TestCategory=LegIndependent')
            Trx    = "${prefix}_independent.trx"
        }
    }
}

# What a finished leg did, from its TRX: the console summary line is localised and moves around. A TRX duration
# includes TestInitialize, where a test waits for its locks, and each test writes that wait as '[lock-wait-ms] n'
# (LockedTestBase) - so the sum of durations splits into waiting for locks and running.
function Get-LegStats($leg) {
    $trx = Join-Path $leg.ResultsPath $leg.Trx
    if (-not (Test-Path $trx)) { return [pscustomobject]@{ Counts = 'no TRX'; TestSec = 0; LockSec = 0 } }
    $run = ([xml](Get-Content $trx -Raw)).TestRun
    $c = $run.ResultSummary.Counters
    # Inconclusive and [Ignore]d tests are counted under different attributes by different MSTest versions;
    # what is neither passed nor failed is a skip.
    $failed = [int]$c.failed + [int]$c.error + [int]$c.timeout + [int]$c.aborted
    $testSec = 0.0; $lockMs = 0
    foreach ($r in @($run.Results.UnitTestResult)) {
        if ($r.duration) { $testSec += [TimeSpan]::Parse($r.duration, [Globalization.CultureInfo]::InvariantCulture).TotalSeconds }
        $out = $r.Output.StdOut
        if ($out) { foreach ($m in [regex]::Matches($out, '\[lock-wait-ms\] (\d+)')) { $lockMs += [long]$m.Groups[1].Value } }
    }
    [pscustomobject]@{
        Counts  = 'total {0}, passed {1}, failed {2}, skipped {3}' -f $c.total, $c.passed, $failed, ([int]$c.total - [int]$c.passed - $failed)
        TestSec = $testSec
        LockSec = $lockMs / 1000.0
    }
}

function New-SummaryRow($leg, [TimeSpan] $wall, [int] $exitCode) {
    $st = Get-LegStats $leg
    [pscustomobject]@{
        Router = $leg.Router; Leg = $leg.Name; Wall = '{0:hh\:mm\:ss}' -f $wall
        Tests = '{0:hh\:mm\:ss}' -f [TimeSpan]::FromSeconds($st.TestSec)
        LockWait = '{0:hh\:mm\:ss}' -f [TimeSpan]::FromSeconds($st.LockSec)
        Running = '{0:hh\:mm\:ss}' -f [TimeSpan]::FromSeconds([Math]::Max(0, $st.TestSec - $st.LockSec))
        ExitCode = $exitCode; Counts = $st.Counts; Trx = Join-Path $leg.ResultsPath $leg.Trx
        TestSec = $st.TestSec; LockSec = $st.LockSec
    }
}

function Get-DotnetArgs($leg, [bool] $noBuild) {
    $a = @(
        'test', $project,
        '--settings', (Join-Path $repoRoot "tik4net.integrationtests\$($leg.Runsettings)"),
        '--logger', "trx;LogFileName=$($leg.Trx)",
        '--results-directory', $leg.ResultsPath,
        '--verbosity', 'normal'
    )
    if ($leg.Filter) { $a += @('--filter', $leg.Filter) }
    if ($noBuild) { $a += '--no-build' }
    $a
}

# Keep the stable name (parse-trx.ps1 and the docs refer to results_<transport>.trx) but move an
# existing one aside first. A re-run of a failing leg otherwise destroys the very TRX that recorded
# the failure - which is exactly what the mikrotik-tests skill says never to do.
function Move-PreviousResult($leg) {
    foreach ($ext in @('.trx', '.log')) {
        $path = Join-Path $leg.ResultsPath ([IO.Path]::ChangeExtension($leg.Trx, $ext))
        if (Test-Path $path) {
            $keep = Join-Path $leg.ResultsPath ("{0}_{1:yyyyMMdd-HHmmss}{2}" -f [IO.Path]::GetFileNameWithoutExtension($leg.Trx), (Get-Item $path).LastWriteTime, $ext)
            Move-Item -LiteralPath $path -Destination $keep -Force
        }
    }
}

# The environment a leg's process starts with. Set in this process just before the leg starts, and put back
# afterwards: a child inherits the environment at the moment it is created.
function Set-LegEnvironment($leg) {
    if ($leg.Router -eq 'default') { Remove-Item Env:\TIK4NET_ROUTER -ErrorAction SilentlyContinue }
    else { $env:TIK4NET_ROUTER = $leg.Router }
    if ($LockAll) { $env:TIK4NET_LOCK_ALL = '1' }
    if ($WireTrace) {
        $env:TIK4NET_WIRETRACE = if ($WireTrace -eq 'auto') {
            Join-Path $leg.ResultsPath "wire_$($leg.Name)_$stamp.txt"
        } else { $WireTrace }
    }
}
$savedEnv = @{}
foreach ($name in @('TIK4NET_ROUTER', 'TIK4NET_LOCK_ALL', 'TIK4NET_WIRETRACE')) {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name)
}
function Restore-Environment {
    foreach ($name in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name]) }
}

$lanes = [ordered]@{}
foreach ($leg in $legs) {
    if (-not $lanes.Contains($leg.Lane)) { $lanes[$leg.Lane] = [System.Collections.Queue]::new() }
    $lanes[$leg.Lane].Enqueue($leg)
}
Write-Host ("routers: {0}; legs: {1}; mode: {2}" -f ($Router -join ', '), $legs.Count,
            $(if ($Sequential) { 'sequential' } else { "parallel, $($lanes.Count) lane(s), max $(if ($MaxParallel -gt 0) { $MaxParallel } else { 'all' }) at once" })) -ForegroundColor Cyan

$summary = @()
try {
    if ($Sequential) {
        foreach ($leg in $legs) {
            Write-Host "=== $($leg.Router) / $($leg.Name) ===" -ForegroundColor Cyan
            Move-PreviousResult $leg
            Set-LegEnvironment $leg
            $started = Get-Date
            $dotnetArgs = Get-DotnetArgs $leg ([bool]$NoBuild)
            & dotnet @dotnetArgs
            $exitCode = $LASTEXITCODE
            Restore-Environment
            $summary += New-SummaryRow $leg ((Get-Date) - $started) $exitCode
        }
    }
    else {
        # One build, then every leg with --no-build: legs building the same project at once race on its
        # output folder (a failure that reports "build failed" with 0 errors).
        if (-not $NoBuild) {
            Write-Host 'building once for all legs...' -ForegroundColor DarkGray
            & dotnet build $project --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        }

        $running = @()
        $runStarted = Get-Date
        while (@($lanes.Values | Where-Object { $_.Count -gt 0 }).Count -gt 0 -or $running.Count -gt 0) {
            # Start the next leg of every idle lane, up to -MaxParallel legs at once.
            foreach ($laneName in @($lanes.Keys)) {
                if ($MaxParallel -gt 0 -and $running.Count -ge $MaxParallel) { break }
                if ($lanes[$laneName].Count -eq 0) { continue }
                if (@($running | Where-Object { $_.Leg.Lane -eq $laneName }).Count -gt 0) { continue }
                $leg = $lanes[$laneName].Dequeue()
                Move-PreviousResult $leg
                Set-LegEnvironment $leg
                $log = Join-Path $leg.ResultsPath ([IO.Path]::ChangeExtension($leg.Trx, '.log'))
                $quoted = (Get-DotnetArgs $leg $true) | ForEach-Object { if ($_ -match '[\s&|()!]') { '"' + $_ + '"' } else { $_ } }
                $p = Start-Process -FilePath 'dotnet' -ArgumentList $quoted -NoNewWindow -PassThru `
                                   -RedirectStandardOutput $log -RedirectStandardError "$log.err"
                $null = $p.Handle   # without touching Handle, Windows PowerShell never learns the exit code
                Restore-Environment
                $running += [pscustomobject]@{ Leg = $leg; Process = $p; Started = Get-Date; Log = $log }
                Write-Host ("  started {0} / {1} (pid {2})" -f $leg.Router, $leg.Name, $p.Id) -ForegroundColor DarkGray
            }

            Start-Sleep -Milliseconds 500
            $still = @()
            foreach ($job in $running) {
                if (-not $job.Process.HasExited) { $still += $job; continue }
                $job.Process.WaitForExit()
                $err = "$($job.Log).err"
                if ((Test-Path $err) -and (Get-Item $err).Length -gt 0) { Get-Content $err | Add-Content $job.Log }
                Remove-Item $err -ErrorAction SilentlyContinue
                $row = New-SummaryRow $job.Leg ((Get-Date) - $job.Started) $job.Process.ExitCode
                $summary += $row
                $color = if ($job.Process.ExitCode -eq 0) { 'Green' } else { 'Red' }
                Write-Host ("  done    {0} / {1}: exit {2}, {3}; wall {4}, lock wait {5}" -f $job.Leg.Router, $job.Leg.Name,
                            $job.Process.ExitCode, $row.Counts, $row.Wall, $row.LockWait) -ForegroundColor $color
            }
            $running = $still
        }
        Write-Host ("wall time {0:hh\:mm\:ss}" -f ((Get-Date) - $runStarted)) -ForegroundColor Cyan
    }
}
finally {
    Restore-Environment
}

Write-Host ''
Write-Host '=== run summary ===' -ForegroundColor Cyan
$summary | Format-Table Router, Leg, Wall, Tests, LockWait, Running, ExitCode, Counts -AutoSize
$totalTests = ($summary | Measure-Object TestSec -Sum).Sum
$totalLock  = ($summary | Measure-Object LockSec -Sum).Sum
Write-Host ("all legs: tests {0:hh\:mm\:ss}, of which waiting for locks {1:hh\:mm\:ss} and running {2:hh\:mm\:ss}" -f
            [TimeSpan]::FromSeconds($totalTests), [TimeSpan]::FromSeconds($totalLock),
            [TimeSpan]::FromSeconds([Math]::Max(0, $totalTests - $totalLock))) -ForegroundColor Cyan

Write-Host "Read the results (including named skips) with:" -ForegroundColor DarkGray
Write-Host "  $PSScriptRoot\parse-trx.ps1 -ResultsDirectory $ResultsDirectory" -ForegroundColor DarkGray

# A matrix in which three legs exited 1 must not exit 0: anything reading the exit code - a wrapper
# script, a person - would be told the run passed.
$failedLegs = @($summary | Where-Object { $_.ExitCode -ne 0 })
if ($failedLegs) {
    Write-Host ''
    Write-Host ("FAILED on {0} of {1} leg(s): {2}" -f $failedLegs.Count, $summary.Count,
                (($failedLegs | ForEach-Object { "$($_.Router)/$($_.Leg)" }) -join ', ')) -ForegroundColor Red
    exit 1
}
if (-not $summary) { Write-Warning 'No leg ran.'; exit 1 }
exit 0
