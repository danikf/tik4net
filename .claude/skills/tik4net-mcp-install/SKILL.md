---
name: tik4net-mcp-install
description: >
  Install, update or repair the tik4net.mcp MCP server (the `mikrotik_call` / `mikrotik_cli_complete`
  tools) — either through the in-repo dev launcher or as a .NET global tool. Use when changes to
  Tools/tik4net.mcp/ or to tik4net itself are not showing up in the MCP tools, when the MCP server is
  missing/unavailable/failing to start, when a build fails with a locked file under
  Tools/tik4net.mcp/bin or in .dotnet\tools, when setting the server up on a fresh machine, or when
  asked to "reinstall / refresh / rebuild the MCP server", "update tik4net.mcp", "clean install of
  the MCP server". Not for using the server — that is the `mikrotik` skill.
---

# Install / update tik4net.mcp

The MCP server is a **compiled binary**, not the source tree: it never picks up a source change on
its own. After touching anything under `Tools/tik4net.mcp/` or in `tik4net/` (the server links the
library), it has to be rebuilt and the client reconnected.

There are two setups, and which one is in play decides what you have to run.

## Working in this repository — the dev launcher (default)

The checked-in `.mcp.json` starts the server through `Tools/tik4net.mcp/run-dev.ps1`, which stages
`bin/Debug/net8.0` into a throw-away directory under `%TEMP%` and runs it from there. No running
server ever holds a handle inside the repository or in `.dotnet\tools`.

The whole refresh is:

```bash
dotnet build tik4net.sln
```

then reconnect the `tik4net-mcp` server in the client. Nothing to uninstall, no cache to purge, and
no need to stop the servers other sessions are using — each session keeps its own staged copy until
it reconnects.

The launcher **does not build** (a client cannot report a build failure — it would just get a server
that never starts), so it runs whatever the last build produced. It logs the staged build's
timestamp and path to stderr, which is the quickest way to confirm the client really picked up the
new bits. `-Configuration Release` or `$env:TIK4NET_MCP_CONFIGURATION` selects a different build;
stagings older than two days are pruned on the next launch, skipping any still in use.

PowerShell only — on Linux/macOS use the global tool below.

## Installing the global tool

For a fresh machine, or to use the server outside this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1
```

Add `-Force` to skip the "stop the running server?" prompt (needed when running non-interactively —
the prompt cannot be answered and the script aborts). Add `-SkipPack` to install the `.nupkg`
already in `Build/` without rebuilding.

The script stops running servers, packs Release, drops the stale NuGet cache entry, uninstalls,
reinstalls, and verifies the installed binary starts. **Restart the MCP client afterwards** (or
reconnect its servers) — the old process is gone and the client will not reconnect on its own.

### Why not just `dotnet tool update`

Three traps the script exists to handle. Hitting any of them looks like "my change had no effect":

1. **Every running server locks the installed binary.** The install fails until *all* of them are
   stopped — including servers belonging to other client sessions, which is why this is the wrong
   loop to be in while developing. Two process names matter: `tik4net-mcp` (the installed tool) and
   `tik4net.mcp` (a staged or `dotnet run` server).
2. **`dotnet tool update` is a no-op at an unchanged version.** The project pins `VersionPrefix`
   4.0.0, so a rebuilt 4.0.0 is "already up to date" and the OLD binary stays installed. The script
   always uninstalls first.
3. **NuGet caches by id+version.** Reinstalling 4.0.0 can restore the *cached* 4.0.0 rather than the
   one just packed. The script removes `<global-packages>/tik4net.mcp/<version>` first.

A same-version reinstall through the script does deliver new code (see
[`Docs/HISTORY.md`](../../../Docs/HISTORY.md) for the verification).

## Never run the server straight from bin

`dotnet run --project Tools/tik4net.mcp/…` in `.mcp.json` locks the build output of a project that is
part of the solution, so `dotnet build tik4net.sln` fails. Use the launcher (staged copy) or the
installed tool (outside the repository).

## Checking what is actually running

```powershell
Get-Process tik4net-mcp, tik4net.mcp -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, Path
```

`Path` tells you the setup: under `Temp\tik4net.mcp-dev\…` is the dev launcher, under
`.dotnet\tools` the global tool, under the repo's `bin\` the broken `dotnet run` configuration.
Several servers at once is normal — one per open client session, each with a live parent:

```powershell
Get-CimInstance Win32_Process -Filter "Name='tik4net-mcp.exe'" |
  Select-Object ProcessId, CreationDate, ParentProcessId
```

A stdio server exits on its own when the client closes stdin (verified: exit 0, under a second), so
a server outliving its client is a real anomaly — check the parent still exists before assuming a leak.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Change not visible, dev launcher | Not rebuilt, or the server was not reconnected — check the launcher's stderr line for the staged build's timestamp |
| `No Debug build found at …bin\Debug\net8.0` | `dotnet build tik4net.sln` first; the launcher never builds |
| Build fails on a locked file under `Tools\tik4net.mcp\bin` | A client is running the old `dotnet run` configuration; fix `.mcp.json` and restart it |
| `install-tool.ps1` aborts at the confirmation prompt | Non-interactive shell; pass `-Force` |
| `Server processes are still running` | Another client session holds the installed tool — close it, or use the dev launcher instead |
| MCP tools missing after a successful install | The client was not restarted |
| Server exits immediately outside a client | Expected — a stdio server with no client on stdin exits 0. That is the install script's own verification |

## Related

- `mikrotik` — using the server (`mikrotik_call`, transports, wire tracing)
- [`Tools/tik4net.mcp/README.md`](../../../Tools/tik4net.mcp/README.md) — tool parameters, the dev
  launcher, and the published-package install (`dotnet tool install -g tik4net.mcp`)
