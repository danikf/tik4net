# tik4net.mcp

An [MCP](https://modelcontextprotocol.io) server that exposes MikroTik routers to an MCP client
(Claude Code, Claude Desktop, …) through [tik4net](https://github.com/danikf/tik4net). It provides two
tools:
- **`mikrotik_call`** — runs any RouterOS command over **every** tik4net transport, so you can inspect and
  modify a router, or debug/compare the wire protocol across transports, from an AI assistant.
- **`mikrotik_cli_complete`** — drives RouterOS terminal **Tab-completion** to enumerate the menu tree and
  an object's settable parameters (the scriptable way to "map" a router / resolve an entity's writable
  fields). CLI terminal transports only.

## Install

```
dotnet tool install -g tik4net.mcp
```

This puts `tik4net-mcp` on your PATH (requires the .NET 8 runtime).

### Install from source

Working in this repository, or want a build that is newer than the published package:

```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/tik4net.mcp/install-tool.ps1
```

`install-tool.ps1` stops any running server (which would otherwise hold a lock on its own binary),
packs Release, and reinstalls the global tool. Use it for every refresh, not just the first install
— `dotnet tool update` is a no-op while the version is unchanged, so a rebuilt 4.0.0 would silently
leave the old binary in place. Pass `-Force` to skip the confirmation prompt when running
non-interactively.

Restart your MCP client afterwards; it will not reconnect to the replaced server on its own.

> **Do not point your MCP client at `dotnet run --project Tools/tik4net.mcp/…`.** The running server
> holds a lock on its own `bin/` output, and `dotnet build tik4net.sln` — which includes this project
> — then fails. Installing as a tool puts the binary outside the repository, where it can't collide
> with a build.

## Configure your MCP client

Point the client at the installed command over stdio. For example, an `.mcp.json`:

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

## The `mikrotik_call` tool

| Parameter         | Type     | Description |
|-------------------|----------|-------------|
| `host`            | string   | Router IP or hostname |
| `username`        | string   | Login user |
| `password`        | string   | Login password (may be empty) |
| `command`         | string   | API command path, e.g. `/ip/address/print`, `/system/resource/print` |
| `transport`       | string   | Transport (default `Api`): `Api`, `ApiSsl`, `Rest`, `RestSsl`, `Telnet`, `MacTelnet`, `WinboxCli`, `WinboxCliMac`, `WinboxNative` |
| `port`            | int      | TCP/UDP port; `0` = transport default |
| `routerMac`       | string   | Router MAC — only `MacTelnet` / `WinboxCliMac` (else MNDP discovery) |
| `traceLevel`      | string   | `off` (default), `words` (raw words/CLI lines), or `bytes` (words **plus** a byte/frame-level wire trace: pre-ANSI terminal bytes, mepty `PULL`/prompt/settle notes, M2 frame chunks, socket I/O) |
| `traceChannels`   | string[] | `bytes` only: keep just these channels (`wbxcli.mepty`, `wbxtcp.frame`, `telnet.sock`, `mactelnet.udp`, `api.word`); omit = all |
| `includeRawTrace` | bool     | Back-compat alias for `traceLevel='words'` |
| `includeRouterLog`| bool     | Also append the router's own `/log` lines emitted **during** the command, as a `--- ROUTER LOG ---` section — captured over a **separate** API connection (TCP 8728) so it never perturbs the transport under test |
| `routerLogTail`   | int      | Max router-log lines to keep (`includeRouterLog` only), default `200` |
| `executeMode`     | string   | `auto` (default) or `nonquery` (force `ExecuteNonQuery()` for action verbs like `/system/script/run`) |
| `parameters`      | string[] | Extra API words — filter `?name=value`, name-value `=name=value` |

All transports accept the same `command` / `parameters` format. Only `Api` / `ApiSsl` support
Listen/Streaming.

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
| `transport` | string | CLI terminal transport (default `Telnet`): `Telnet`, `WinboxCli`, `MacTelnet`, `WinboxCliMac`. `Api`/`Rest`/`WinboxNative` are rejected |
| `port`      | int    | TCP/UDP port; `0` = transport default |
| `routerMac` | string | Router MAC — only `MacTelnet` / `WinboxCliMac` |

Returns `{ input, transport, tokens[], raw }`. After a **menu path** (`/interface `) `tokens` are child
menus + verbs; after **`add `/`set `** (`/interface/vlan add `) they are the **settable parameter names** —
the writable field set for that object. `tokens` is empty when the input completes to a single unique token.
Long names may be column-truncated by RouterOS — use `mikrotik_call` `… /print` with `=detail=` for full
names. Supported on all CLI terminal transports (Telnet, WinboxCli, MacTelnet, WinboxCliMac).

```jsonc
// settable parameters of /interface/vlan (the entity's writable fields)
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "input": "/interface/vlan add " }

// child menus + verbs under /ip
{ "host": "192.168.88.1", "username": "admin", "password": "", "input": "/ip " }
```

## Documentation

Full docs, the per-transport RAW trace format, and prerequisites for each transport are on the
**[MCP server wiki page](https://github.com/danikf/tik4net/wiki/MCP-server)**.

Licensed under the same terms as tik4net (see LICENSE).
