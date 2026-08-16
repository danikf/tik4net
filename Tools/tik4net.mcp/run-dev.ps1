<#
.SYNOPSIS
  Runs the MCP server from this working copy, out of a throw-away copy of the build output.

.DESCRIPTION
  Launch wrapper for MCP clients working *in this repository*. It copies
  Tools/tik4net.mcp/bin/<Configuration>/net8.0 into a fresh directory under %TEMP% and starts the
  server from there, so the running process never holds a handle on anything inside the repository
  or in %USERPROFILE%\.dotnet\tools.

  That is the whole point: `dotnet build tik4net.sln` keeps working no matter how many clients are
  connected, and picking up a change in tik4net or in the server is just

      dotnet build tik4net.sln     +     reconnect this one MCP server

  with no uninstall, no NuGet cache purge, and without stopping the servers other sessions are using.

  This script does NOT build — an MCP client would have no way to report a build failure, and would
  get a server that never starts. Build first; the server always runs the output of the last build.

  For installing the real global tool (fresh machine, or using the server outside this repo), use
  install-tool.ps1 instead.

.PARAMETER Configuration
  Build configuration to stage. Defaults to $env:TIK4NET_MCP_CONFIGURATION, else Debug — which is
  what a plain `dotnet build tik4net.sln` produces.

.PARAMETER KeepDays
  Age above which previously staged directories are deleted. Directories still in use are skipped.

.EXAMPLE
  # .mcp.json
  # "command": "powershell",
  # "args": ["-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass",
  #          "-File","Tools/tik4net.mcp/run-dev.ps1"]
#>
param(
    [string] $Configuration = $(if ($env:TIK4NET_MCP_CONFIGURATION) { $env:TIK4NET_MCP_CONFIGURATION } else { 'Debug' }),
    [int]    $KeepDays = 2
)

$ErrorActionPreference = 'Stop'

# stdout belongs to the MCP protocol — every diagnostic goes to stderr, where the client logs it.
function Write-Note { param([string] $Text) [Console]::Error.WriteLine("[tik4net.mcp/run-dev] $Text") }

$sourceDir = Join-Path $PSScriptRoot "bin\$Configuration\net8.0"
$sourceDll = Join-Path $sourceDir 'tik4net.mcp.dll'

if (-not (Test-Path $sourceDll)) {
    Write-Note "No $Configuration build found at $sourceDir."
    Write-Note 'Build it first:  dotnet build tik4net.sln'
    exit 1
}

$stageRoot = Join-Path $env:TEMP 'tik4net.mcp-dev'
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

# ── Prune old stagings ─────────────────────────────────────────────────────────
# A directory whose tik4net.mcp.dll cannot be opened exclusively still has a server running out of
# it; deleting it piecemeal would leave that server without the assemblies it has not loaded yet.
$cutoff = (Get-Date).AddDays(-$KeepDays)
foreach ($old in @(Get-ChildItem -Path $stageRoot -Directory -ErrorAction SilentlyContinue |
                   Where-Object { $_.LastWriteTime -lt $cutoff })) {
    $probe = Join-Path $old.FullName 'tik4net.mcp.dll'
    if (Test-Path $probe) {
        try {
            $h = [System.IO.File]::Open($probe, 'Open', 'ReadWrite', 'None')
            $h.Dispose()
        } catch {
            continue   # in use
        }
    }
    Remove-Item -Recurse -Force $old.FullName -ErrorAction SilentlyContinue
}

# ── Stage this launch ──────────────────────────────────────────────────────────
$stageDir = Join-Path $stageRoot ('{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), $PID)
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $stageDir -Recurse -Force

$built = (Get-Item $sourceDll).LastWriteTime
Write-Note ("running $Configuration build of {0:yyyy-MM-dd HH:mm:ss} from $stageDir" -f $built)

# ── Run ────────────────────────────────────────────────────────────────────────
# The apphost when present, so the child process keeps the recognisable tik4net.mcp name; stdio is
# inherited, so the client's pipes reach the server untouched.
$exe = Join-Path $stageDir 'tik4net.mcp.exe'
if (Test-Path $exe) {
    & $exe @args
} else {
    & dotnet (Join-Path $stageDir 'tik4net.mcp.dll') @args
}

exit $LASTEXITCODE
