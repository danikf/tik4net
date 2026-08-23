# tik4net.mcp

An [MCP](https://modelcontextprotocol.io) server that exposes MikroTik routers to an MCP client
(Claude Code, Claude Desktop, …) through [tik4net](https://github.com/danikf/tik4net). It provides three
tools:
- **`mikrotik_call`** — runs any RouterOS command over **every** tik4net transport, so you can inspect and
  modify a router, or debug/compare the wire protocol across transports, from an AI assistant.
- **`mikrotik_cli_complete`** — drives RouterOS terminal **Tab-completion** to enumerate the menu tree and
  an object's settable parameters (the scriptable way to "map" a router / resolve an entity's writable
  fields). CLI terminal transports only.
- **`mikrotik_discover`** — finds MikroTik routers on the local segment via MNDP broadcast. No host, no
  credentials — the tool to reach for when you do not yet know the router's address or MAC.

## Install

```
dotnet tool install -g tik4net.mcp
```

This puts `tik4net-mcp` on your PATH (requires the .NET 8 runtime). Point your MCP client at that
command over stdio:

```jsonc
{
  "mcpServers": {
    "tik4net-mcp": {
      "command": "tik4net-mcp",
      "type": "stdio"
    }
  }
}
```

That is the right setup for *using* the server. Developing it is a different story, below.

## Working on the server (or on tik4net itself)

A server process holds a lock on the binary it runs. Run it straight from `bin/` and
`dotnet build tik4net.sln` fails; run it from the installed global tool and reinstalling fails
instead — including when the lock belongs to a *different* client session you did not want to
disturb. Neither is a good edit loop.

So in this repository the checked-in [`.mcp.json`](../../.mcp.json) starts the server through
[`run-dev.ps1`](run-dev.ps1), which copies `bin/<Configuration>/net8.0` into a throw-away directory
under `%TEMP%` and runs it from there. Nothing inside the repository or the tool store is ever
locked, and the loop becomes:

```
dotnet build tik4net.sln          # never blocked, however many clients are connected
```

then reconnect that one MCP server in your client. No uninstall, no NuGet cache purge, no stopping
anyone else's server; each session keeps running its own staged copy until it reconnects.

The script does not build — an MCP client cannot report a build failure, it would just get a server
that never starts — so it always runs the output of your last build, and says which one on stderr.
It stages `Debug` by default (what a plain `dotnet build` produces); override with
`-Configuration Release` or `$env:TIK4NET_MCP_CONFIGURATION`. Staged copies older than two days are
deleted on the next launch, skipping any that a server is still using.

It is a PowerShell script, so on Linux/macOS use the installed-tool configuration shown above and
reinstall after each change.

### Installing from source

For a fresh machine, or to use this build outside the repository:

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1
```

`install-tool.ps1` stops any running server (which would otherwise hold a lock on its own binary),
packs Release, and reinstalls the global tool. Use it for every refresh, not just the first install
— `dotnet tool update` is a no-op while the version is unchanged, so a rebuilt 4.0.0 would silently
leave the old binary in place. Pass `-Force` to skip the confirmation prompt when running
non-interactively.

Restart your MCP client afterwards; it will not reconnect to the replaced server on its own.

> **Do not point your MCP client at `dotnet run --project Tools/tik4net.mcp/…`.** That server holds
> a lock on its own `bin/` output, and `dotnet build tik4net.sln` — which includes this project —
> then fails. Use `run-dev.ps1` (staged copy) or the installed tool (outside the repository).

## The `mikrotik_call` tool

| Parameter         | Type     | Description |
|-------------------|----------|-------------|
| `host`            | string   | Router IP or hostname |
| `username`        | string   | Login user |
| `password`        | string   | Login password (may be empty) |
| `command`         | string   | API command path, e.g. `/ip/address/print`, `/system/resource/print` |
| `transport`       | string   | Transport (default `Api`): `Api`, `ApiSsl`, `Rest`, `RestSsl`, `Telnet`, `Ssh`, `MacTelnet`, `WinboxCli`, `WinboxCliMac`, `WinboxNative`, `WinboxNativeMac` |
| `port`            | int      | TCP/UDP port; `0` = transport default |
| `routerMac`       | string   | Router MAC — only the MAC-layer transports `MacTelnet` / `WinboxCliMac` / `WinboxNativeMac` (else MNDP discovery) |
| `traceLevel`      | string   | `off` (default), `words` (raw words/CLI lines), or `bytes` (words **plus** a byte/frame-level wire trace: pre-ANSI terminal bytes, mepty `PULL`/prompt/settle notes, M2 frame chunks, socket I/O) |
| `traceChannels`   | string[] | `bytes` only: keep just these channels (`wbxcli.mepty`, `wbxtcp.frame`, `telnet.sock`, `mactelnet.udp`, `api.word`); omit = all |
| `includeRawTrace` | bool     | Back-compat alias for `traceLevel='words'` |
| `includeRouterLog`| bool     | Also append the router's own `/log` lines emitted **during** the command, as a `--- ROUTER LOG ---` section — captured over a **separate** API connection (TCP 8728) so it never perturbs the transport under test |
| `routerLogTail`   | int      | Max router-log lines to keep (`includeRouterLog` only), default `200` |
| `executeMode`     | string   | `auto` (default) or `nonquery` (force `ExecuteNonQuery()` for action verbs like `/system/script/run`) |
| `parameters`      | string[] | Extra API words — filter `?name=value`, name-value `=name=value` |

All transports accept the same `command` / `parameters` format. Only `Api` / `ApiSsl` support
Listen/Streaming.

### The build stamp

Every tool response names the assembly that produced it — version, **build timestamp** and path:
`mikrotik_call` appends a trailing `--- MCP SERVER --- tik4net.mcp 4.0.0 built 2026-08-23 10:15:42 (…)`
line, and `mikrotik_cli_complete` / `mikrotik_discover` carry the same text in a `serverBuild` property.

The dev launcher runs each session from a throw-away copy of the build output, so the server can be
replaced while clients are connected — which also means the repository cannot tell you which build is
answering. After a rebuild, compare the stamp against your build before reading the answer: an older
timestamp means the client is still on the previous server.

### Examples

```jsonc
// read
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/system/resource/print" }

// filtered print
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/ip/firewall/filter/print", "parameters": ["?action=drop"] }

// compare a transport against the API baseline, with raw word trace
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/ip/address/print", "transport": "WinboxNative", "traceLevel": "words" }

// byte/frame-level wire trace of a WinBox CLI command (diagnose a terminal hang/desync)
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/interface/print", "parameters": ["detail"], "transport": "WinboxCli",
  "traceLevel": "bytes", "traceChannels": ["wbxcli.mepty"] }

// wire trace + the router's own log lines for the same command (device-side view alongside ours)
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/tool/wol", "parameters": ["=mac=00:11:22:33:44:55", "=interface=badiface"],
  "transport": "Telnet", "traceLevel": "bytes", "includeRouterLog": true }
```

## The `mikrotik_cli_complete` tool

Enumerates what RouterOS would **Tab-complete** for a partial CLI command — the scriptable way to walk the
menu tree or resolve an object's writable fields from a live router.

| Parameter   | Type   | Description |
|-------------|--------|-------------|
| `host`      | string | Router IP or hostname |
| `username`  | string | Login user |
| `password`  | string | Login password (may be empty) |
| `input`     | string | Partial CLI line, **exactly as typed before Tab** — include the trailing space to list the next word |
| `transport` | string | CLI terminal transport (default `Telnet`): `Telnet`, `Ssh`, `WinboxCli`, `MacTelnet`, `WinboxCliMac`. `Api`/`Rest`/`WinboxNative*` are rejected |
| `port`      | int    | TCP/UDP port; `0` = transport default |
| `routerMac` | string | Router MAC — only `MacTelnet` / `WinboxCliMac` |

Returns `{ input, transport, tokens[], raw }`. After a **menu path** (`/interface `) `tokens` are child
menus + verbs; after **`add `/`set `** (`/interface/vlan add `) they are the **settable parameter names** —
the writable field set for that object. `tokens` is empty when the input completes to a single unique token.
Long names may be column-truncated by RouterOS — use `mikrotik_call` `… /print` with `=detail=` for full
names. Supported on all CLI terminal transports (Telnet, Ssh, WinboxCli, MacTelnet, WinboxCliMac).

```jsonc
// settable parameters of /interface/vlan (the entity's writable fields)
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "input": "/interface/vlan add " }

// child menus + verbs under /ip
{ "host": "192.168.88.1", "username": "admin", "password": "", "input": "/ip " }
```

## The `mikrotik_discover` tool

Finds MikroTik routers on the local network segment by listening for the **MNDP** broadcast (UDP 5678)
that every RouterOS device sends. Takes **no host and no credentials** — this is the tool for when you do
not yet know the router's address, when a rebuilt VM may have changed its IP/MAC/identity, or when several
MikroTiks share the segment and picking the wrong one would be a coin flip.

| Parameter            | Type | Description |
|----------------------|------|-------------|
| `timeoutSeconds`     | int  | How long to listen; default `6`, clamped to 1–60. 5–8 is plenty — devices re-broadcast every few seconds |
| `stopWhenFirstFound` | bool | Return on the first answer. Faster, but you get whichever device broadcast first — do **not** use it when choosing between routers |

Returns `{ timeoutSeconds, count, routers[] }`, each router carrying `identity`, `ipv4`, `ipv6`, `mac`,
`version`, `platform`, `boardName`, `uptime`, `softwareId` and `interfaceName`. The `mac` is what the
MAC-layer transports (`MacTelnet`, `WinboxCliMac`, `WinboxNativeMac`) need.

> **Zero rows is ambiguous, and usually not an empty segment.** The common cause is the *host* firewall
> dropping the inbound UDP 5678 broadcast — it fails silently and looks identical to "no routers here".
> A router on a different subnet is invisible too: MNDP does not cross a router.

```jsonc
{ "timeoutSeconds": 6 }
```

## Documentation

Full docs, the per-transport RAW trace format, and prerequisites for each transport are on the
**[MCP server wiki page](https://github.com/danikf/tik4net/wiki/MCP-server)**.

Licensed under the same terms as tik4net (see LICENSE).
