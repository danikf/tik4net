<#
.SYNOPSIS
    Runs the tik4net integration suite against a live router, for one transport, a smoke subset,
    or the full transport matrix.

.DESCRIPTION
    The integration suite is meant to be run once per transport. Which transport is used comes from
    the `tik.connectionType` run parameter, supplied by one *.runsettings file per transport in
    tik4net.integrationtests/.

    Router coordinates are NOT arguments to this script: they live in
    tik4net.integrationtests/App.config, which is the single source of truth. Point that file at the
    router first.

    Results are always written as TRX so that skips remain inspectable after the run — a summary line
    of "Failed: 0" does not prove two runs were identical. Use parse-trx.ps1 to read them.

.PARAMETER Transport
    One or more transport names matching the *.runsettings files (api, apissl, rest, restssl, telnet,
    ssh, mactelnet, winboxcli, winboxclimac, winboxnative, winboxnativemac).
    Defaults to every transport, ordered fastest-first and API-before-CLI (see -Matrix notes).

.PARAMETER Smoke
    Run only the fast, self-contained smoke classes instead of the full suite. This is the subset
    intended for the non-API transports when validating an ordinary larger change.

.PARAMETER Filter
    An explicit --filter expression, overriding -Smoke.

.PARAMETER ResultsDirectory
    Where TRX files are written. Defaults to ./TestResults (git-ignored).

.PARAMETER WireTrace
    Enable byte-level wire tracing for the run by setting TIK4NET_WIRETRACE. Pass a file path, or
    'auto' to name the file after the transport and timestamp. Test boundaries are written into the
    trace, so a failure can be located without correlating timestamps.

.EXAMPLE
    ./run-integration-tests.ps1 -Transport api
    Full suite over the binary API.

.EXAMPLE
    ./run-integration-tests.ps1 -Smoke
    Smoke subset over every transport.

.EXAMPLE
    ./run-integration-tests.ps1 -Transport telnet -WireTrace auto
    Full Telnet run with a byte trace, for hunting an intermittent failure.
#>
[CmdletBinding()]
param(
    [string[]] $Transport = @('api', 'apissl', 'rest', 'restssl', 'telnet', 'ssh', 'mactelnet',
                              'winboxnative', 'winboxnativemac', 'winboxcli', 'winboxclimac'),
    [switch]   $Smoke,
    [string]   $Filter,
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

$effectiveFilter = $Filter
if (-not $effectiveFilter -and $Smoke) { $effectiveFilter = $smokeFilter }

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

$resultsPath = Join-Path $repoRoot $ResultsDirectory
if (-not (Test-Path $resultsPath)) { New-Item -ItemType Directory -Path $resultsPath | Out-Null }

$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$prefix  = if ($effectiveFilter -eq $smokeFilter) { 'smoke' } else { 'results' }
$summary = @()

foreach ($t in $Transport) {
    $runsettings = Join-Path $repoRoot "tik4net.integrationtests\$t.runsettings"
    if (-not (Test-Path $runsettings)) {
        Write-Warning "No runsettings for transport '$t' at $runsettings - skipping."
        continue
    }

    Write-Host "=== $t ===" -ForegroundColor Cyan

    if ($WireTrace) {
        $env:TIK4NET_WIRETRACE = if ($WireTrace -eq 'auto') {
            Join-Path $resultsPath "wire_${t}_$stamp.txt"
        } else { $WireTrace }
        Write-Host "    wire trace -> $env:TIK4NET_WIRETRACE" -ForegroundColor DarkGray
    }

    # Keep the stable name (parse-trx.ps1 and the docs refer to results_<transport>.trx) but move an
    # existing one aside first. A re-run of a failing leg otherwise destroys the very TRX that recorded
    # the failure - which is exactly what the mikrotik-tests skill says never to do.
    $trxName = "${prefix}_$t.trx"
    $trxPath = Join-Path $resultsPath $trxName
    if (Test-Path $trxPath) {
        $keep = Join-Path $resultsPath ("{0}_{1}_{2:yyyyMMdd-HHmmss}.trx" -f $prefix, $t, (Get-Item $trxPath).LastWriteTime)
        Move-Item -LiteralPath $trxPath -Destination $keep -Force
        Write-Host "    previous run kept as $(Split-Path $keep -Leaf)" -ForegroundColor DarkGray
    }
    $args = @(
        'test', $project,
        '--settings', $runsettings,
        '--logger', "trx;LogFileName=$trxName",
        '--results-directory', $resultsPath,
        '--verbosity', 'normal'
    )
    if ($effectiveFilter) { $args += @('--filter', $effectiveFilter) }

    $started = Get-Date
    & dotnet @args
    $exitCode = $LASTEXITCODE
    $elapsed  = (Get-Date) - $started

    if ($WireTrace) { Remove-Item Env:\TIK4NET_WIRETRACE -ErrorAction SilentlyContinue }

    $summary += [pscustomobject]@{
        Transport = $t
        Duration  = '{0:hh\:mm\:ss}' -f $elapsed
        ExitCode  = $exitCode
        Trx       = Join-Path $resultsPath $trxName
    }
}

Write-Host ''
Write-Host '=== run summary ===' -ForegroundColor Cyan
$summary | Format-Table -AutoSize

Write-Host "Read the results (including named skips) with:" -ForegroundColor DarkGray
Write-Host "  $PSScriptRoot\parse-trx.ps1 -ResultsDirectory $ResultsDirectory" -ForegroundColor DarkGray

# The per-leg ExitCode was collected into the table above and then thrown away: the script had no exit
# statement, so a matrix in which three legs exited 1 still exited 0. Anything reading the exit code -
# CI, a wrapper script, a person - was told the run passed.
$failedLegs = @($summary | Where-Object { $_.ExitCode -ne 0 })
if ($failedLegs) {
    Write-Host ''
    Write-Host ("FAILED on {0} of {1} transport(s): {2}" -f $failedLegs.Count, $summary.Count,
                (($failedLegs | ForEach-Object { $_.Transport }) -join ', ')) -ForegroundColor Red
    exit 1
}
if (-not $summary) { Write-Warning 'No transport ran.'; exit 1 }
exit 0
