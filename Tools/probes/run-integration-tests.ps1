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

    $trxName = "${prefix}_$t.trx"
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
