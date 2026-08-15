<#
.SYNOPSIS
    Summarises TRX result files from an integration run: counts, failures, and named skips.

.DESCRIPTION
    Neither the console summary nor `-v q` tells you WHICH tests were skipped. That matters: two runs
    that both report zero failures can still differ, because a test can skip on a data-dependent
    Assert.Inconclusive instead of failing. For an intermittent bug, a changed skip count is often the
    only observation available — so compare skip sets between runs rather than trusting the summary.

    Counting skips from the TRX has a trap. A skipped test appears in ResultSummary/Counters only as
    the gap between `total` and `executed`: the `inconclusive` and `notExecuted` counter attributes
    are both left at 0, so reading either of them reports zero skips on a run that skipped dozens.
    The individual <UnitTestResult> elements DO carry outcome="NotExecuted", so this script counts the
    results themselves rather than trusting the summary counters.

.PARAMETER ResultsDirectory
    Directory holding the TRX files. Defaults to ./TestResults relative to the repository root.

.PARAMETER Pattern
    Filename pattern to match. Defaults to *.trx.

.PARAMETER ShowFailures
    List each failed test with the head of its error message.

.PARAMETER ShowSkips
    List the names of skipped (Inconclusive) tests.

.PARAMETER FailedTestFilter
    Emit a ready-to-use `dotnet test --filter` expression selecting only the failed tests, so a run
    can be repeated for just those.

.EXAMPLE
    ./parse-trx.ps1
    Counts for every TRX in TestResults.

.EXAMPLE
    ./parse-trx.ps1 -Pattern 'results_winboxcli.trx' -ShowFailures -FailedTestFilter
#>
[CmdletBinding()]
param(
    [string] $ResultsDirectory = 'TestResults',
    [string] $Pattern = '*.trx',
    [switch] $ShowFailures,
    [switch] $ShowSkips,
    [switch] $FailedTestFilter
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$resultsPath = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory
} else {
    Join-Path $repoRoot $ResultsDirectory
}

if (-not (Test-Path $resultsPath)) { throw "Results directory not found: $resultsPath" }

$files = Get-ChildItem -Path $resultsPath -Filter $Pattern -File | Sort-Object Name
if (-not $files) { Write-Warning "No TRX files matching '$Pattern' in $resultsPath"; return }

$rows = foreach ($file in $files) {
    [xml]$trx = Get-Content $file.FullName
    $results = @($trx.TestRun.Results.UnitTestResult)

    # Counted from the results, not from ResultSummary/Counters: the summary leaves both the
    # `inconclusive` and `notExecuted` attributes at 0 even when tests were skipped.
    $byOutcome = $results | Group-Object outcome -AsHashTable -AsString
    $count = { param($name) if ($byOutcome -and $byOutcome[$name]) { @($byOutcome[$name]).Count } else { 0 } }

    [pscustomobject]@{
        File    = $file.Name
        Total   = $results.Count
        Passed  = & $count 'Passed'
        Failed  = & $count 'Failed'
        Skipped = & $count 'NotExecuted'
    }
}

$rows | Format-Table -AutoSize

foreach ($file in $files) {
    [xml]$trx = Get-Content $file.FullName
    $results = $trx.TestRun.Results.UnitTestResult

    if ($ShowFailures) {
        $failed = $results | Where-Object { $_.outcome -eq 'Failed' }
        if ($failed) {
            Write-Host "`n--- failures in $($file.Name) ---" -ForegroundColor Red
            foreach ($f in $failed) {
                $msg = ($f.Output.ErrorInfo.Message -replace '\r?\n', ' ').Trim()
                $head = $msg.Substring(0, [Math]::Min(140, $msg.Length))
                Write-Host ("  {0} | {1}" -f $f.testName, $head)
            }
        }
    }

    if ($ShowSkips) {
        $skipped = $results | Where-Object { $_.outcome -eq 'NotExecuted' } |
                   Select-Object -ExpandProperty testName | Sort-Object
        if ($skipped) {
            Write-Host "`n--- skips in $($file.Name) ($($skipped.Count)) ---" -ForegroundColor Yellow
            $skipped | ForEach-Object { Write-Host "  $_" }
        }
    }

    if ($FailedTestFilter) {
        $names = $results | Where-Object { $_.outcome -eq 'Failed' } |
                 Select-Object -ExpandProperty testName
        if ($names) {
            $filter = ($names | ForEach-Object { "Name=$_" }) -join '|'
            Write-Host "`n--- re-run filter for $($file.Name) ---" -ForegroundColor Cyan
            Write-Host "  --filter `"$filter`""
        }
    }
}
