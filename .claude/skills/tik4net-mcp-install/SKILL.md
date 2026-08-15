---
name: tik4net-mcp-install
description: >
  Install, update or repair the tik4net.mcp MCP server (the `mikrotik_call` / `mikrotik_cli_complete`
  tools) as a .NET global tool. Use when changes to Tools/tik4net.mcp/ or to tik4net itself are not
  showing up in the MCP tools, when the MCP server is missing/unavailable/failing to start, when
  `dotnet build tik4net.sln` fails with a locked file under Tools/tik4net.mcp/bin, when setting the
  server up on a fresh machine, or when asked to "reinstall / refresh / rebuild the MCP server",
  "update tik4net.mcp", "clean install of the MCP server". Not for using the server — that is the
  `mikrotik` skill.
---

# Install / update tik4net.mcp

The MCP server is a **compiled binary in `%USERPROFILE%\.dotnet\tools`**, not the source tree. It
does not pick up source changes on its own: after touching anything under `Tools/tik4net.mcp/` or in
`tik4net/` (the server links the library), it must be repacked and reinstalled.

## The one command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1
```

Add `-Force` to skip the "stop the running server?" prompt (needed when running non-interactively —
the prompt cannot be answered and the script aborts). Add `-SkipPack` to install the `.nupkg`
already in `Build/` without rebuilding.

The script stops running servers, packs Release, drops the stale NuGet cache entry, uninstalls,
reinstalls, and verifies the installed binary starts. **Restart the MCP client afterwards** (or
reconnect its servers) — the old process is gone and the client will not reconnect on its own.

## Why not just `dotnet tool update`

Three traps the script exists to handle. Hitting any of them looks like "my change had no effect":

1. **The running server locks its own binary.** Install fails, or `dotnet build tik4net.sln` fails
   with `MSB3027 … tik4net.mcp.exe … used by another process`. Two process names matter:
   `tik4net-mcp` (the installed tool) and `tik4net.mcp` (a `dotnet run` server).
2. **`dotnet tool update` is a no-op at an unchanged version.** The project pins `VersionPrefix`
   4.0.0, so a rebuilt 4.0.0 is "already up to date" and the OLD binary stays installed. The script
   always uninstalls first.
3. **NuGet caches by id+version.** Reinstalling 4.0.0 can restore the *cached* 4.0.0 rather than the
   one just packed. The script removes `<global-packages>/tik4net.mcp/<version>` first.

A same-version reinstall through the script does deliver new code (see
[`Docs/HISTORY.md`](../../../Docs/HISTORY.md) for the verification).

## Never run the server from bin

`.mcp.json` must invoke the installed command:

```jsonc
{ "mcpServers": { "tik4net-mcp": { "command": "tik4net-mcp", "type": "stdio" } } }
```

**Not** `dotnet run --project Tools/tik4net.mcp/…`. A server started that way locks the build output,
because the project is part of the solution — so `dotnet build tik4net.sln` fails. The installed tool
lives outside the repo and cannot collide with a build.

## Checking what is actually running

```powershell
Get-Process tik4net-mcp, tik4net.mcp -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, Path
dotnet tool list -g
```

`Path` must be under `.dotnet\tools`. If it points into the repo's `bin\`, the client is still on the
old `dotnet run` configuration — fix `.mcp.json` and restart it.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Script aborts at the confirmation prompt | Non-interactive shell; pass `-Force` |
| `Server processes are still running` | A handle survived the kill — check for a second client holding the server, then re-run |
| `dotnet build tik4net.sln` still fails on a locked file | An MCP client is still running the old bin-based server; restart it |
| MCP tools missing after a successful install | The client was not restarted |
| Install succeeds, behaviour unchanged | Confirm the client's `.mcp.json` uses `tik4net-mcp`, not `dotnet run` |
| Server exits immediately outside a client | Expected — stdio server with no client on stdin exits 0. That is the script's own verification |

## Related

- `mikrotik` — using the server (`mikrotik_call`, transports, wire tracing)
- [`Tools/tik4net.mcp/README.md`](../../../Tools/tik4net.mcp/README.md) — tool parameters and the
  published-package install (`dotnet tool install -g tik4net.mcp`)
