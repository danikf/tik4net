<#
.SYNOPSIS
  (Re)installs tik4net.mcp as a .NET global tool from this working copy.

.DESCRIPTION
  Stops any running server, packs the current source, and installs the result over whatever is
  installed now. Use it after changing anything under Tools/tik4net.mcp/ or in tik4net itself —
  the MCP server is a compiled binary in %USERPROFILE%\.dotnet\tools and does not pick up source
  changes on its own.

  Three things this handles that a bare `dotnet tool update` does not:

  1. A running server holds a file lock on its own binary; the install fails until it is stopped.
  2. `dotnet tool update` is a no-op when the version has not changed — and the project's version
     is a fixed VersionPrefix, so a rebuilt 4.0.0 would silently keep the OLD binary. This script
     always uninstalls first.
  3. NuGet caches packages by id+version in the global-packages folder. Re-installing the same
     version would restore the cached (stale) copy, so the cache entry is dropped first.

  NOTE: stopping the server disconnects it from any MCP client using it (Claude Code included).
  Restart the client, or reconnect its MCP servers, afterwards.

.PARAMETER SkipPack
  Install the .nupkg already in Build/ instead of packing again.

.PARAMETER Force
  Stop running servers without asking.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch] $SkipPack,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$PackageId   = 'tik4net.mcp'
$CommandName = 'tik4net-mcp'

# Repo root = two levels up from Tools/tik4net.mcp/.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project  = Join-Path $repoRoot 'Tools\tik4net.mcp\tik4net.mcp.csproj'
$buildDir = Join-Path $repoRoot 'Build'

if (-not (Test-Path $project)) { throw "Project not found: $project" }

function Write-Step { param([string] $Text) Write-Host "==> $Text" -ForegroundColor Cyan }

# ── 1. Stop anything holding the binary ────────────────────────────────────────
# Both names matter: the installed tool runs as tik4net-mcp, a `dotnet run` server as tik4net.mcp.
$running = @(Get-Process -Name 'tik4net-mcp', 'tik4net.mcp' -ErrorAction SilentlyContinue)

if ($running.Count -gt 0) {
    Write-Step "Stopping $($running.Count) running server process(es)"
    $running | Select-Object Id, ProcessName, Path | Format-Table -AutoSize | Out-String | Write-Host

    if (-not $Force) {
        Write-Host 'This disconnects the MCP server from any client currently using it.' -ForegroundColor Yellow
        $answer = Read-Host 'Stop them and continue? [y/N]'
        if ($answer -notmatch '^(y|yes)$') { Write-Host 'Aborted.'; exit 1 }
    }

    foreach ($p in $running) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop }
        catch { Write-Warning "Could not stop PID $($p.Id): $($_.Exception.Message)" }
    }

    # Stop-Process returns before the handles are actually released.
    for ($i = 0; $i -lt 20; $i++) {
        if (-not (Get-Process -Name 'tik4net-mcp', 'tik4net.mcp' -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process -Name 'tik4net-mcp', 'tik4net.mcp' -ErrorAction SilentlyContinue) {
        throw 'Server processes are still running; cannot replace a locked binary.'
    }
} else {
    Write-Step 'No server process running'
}

# ── 2. Pack ────────────────────────────────────────────────────────────────────
if ($SkipPack) {
    Write-Step 'Skipping pack (-SkipPack)'
} else {
    Write-Step 'Packing (Release)'
    & dotnet pack $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE." }
}

# Newest package wins, so an older leftover .nupkg in Build/ cannot be picked up by accident.
$nupkg = Get-ChildItem -Path $buildDir -Filter "$PackageId.*.nupkg" -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw "No $PackageId .nupkg found in $buildDir. Run without -SkipPack." }

if ($nupkg.Name -notmatch "^$([regex]::Escape($PackageId))\.(?<v>\d+\.\d+\.\d+.*)\.nupkg$") {
    throw "Cannot read a version out of $($nupkg.Name)."
}
$version = $Matches['v']
Write-Step "Package: $($nupkg.Name)  (version $version)"

# ── 3. Drop the stale cache entry for this exact version ───────────────────────
$globalPackages = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$cacheEntry = Join-Path ($globalPackages.Trim()) "$PackageId\$version"
if (Test-Path $cacheEntry) {
    Write-Step "Removing cached $PackageId/$version"
    Remove-Item -Recurse -Force $cacheEntry
}

# ── 4. Uninstall + install ─────────────────────────────────────────────────────
$installed = (& dotnet tool list -g) | Select-String -Pattern "^\s*$([regex]::Escape($PackageId))\s"
if ($installed) {
    Write-Step "Uninstalling current $PackageId"
    & dotnet tool uninstall -g $PackageId | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool uninstall failed with exit code $LASTEXITCODE." }
}

Write-Step "Installing $PackageId $version"
& dotnet tool install -g --add-source $buildDir $PackageId --version $version | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed with exit code $LASTEXITCODE." }

# ── 5. Verify ──────────────────────────────────────────────────────────────────
$exe = Join-Path $env:USERPROFILE ".dotnet\tools\$CommandName.exe"
if (-not (Test-Path $exe)) { throw "Installed, but $exe is missing." }

Write-Step 'Verifying the installed binary starts'
# The server talks MCP over stdio: with stdin closed straight away it initialises and exits 0.
# System.Diagnostics.Process rather than Start-Process — the latter leaves ExitCode empty on
# PowerShell 5.1 when -PassThru is used without -Wait, and resolves 'NUL' as a relative path.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName               = $exe
$psi.UseShellExecute        = $false
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true

$proc = [System.Diagnostics.Process]::Start($psi)
$proc.StandardInput.Close()
if (-not $proc.WaitForExit(15000)) {
    $proc.Kill()
    throw 'Installed binary did not exit within 15 s on a closed stdin.'
}
if ($proc.ExitCode -ne 0) {
    $proc.StandardError.ReadToEnd() | Write-Host
    throw "Installed binary exited with code $($proc.ExitCode)."
}

Write-Host ''
Write-Host "$PackageId $version installed: $exe" -ForegroundColor Green
Write-Host 'Restart your MCP client (or reconnect its servers) to pick it up.' -ForegroundColor Yellow
