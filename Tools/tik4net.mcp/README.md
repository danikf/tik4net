# tik4net.mcp

An [MCP](https://modelcontextprotocol.io) server that exposes MikroTik routers to an MCP client
(Claude Code, Claude Desktop, …) through [tik4net](https://github.com/danikf/tik4net). It provides a
single tool, **`mikrotik_call`**, that runs any RouterOS command over **every** tik4net transport — so
you can inspect and modify a router, or debug/compare the wire protocol across transports, from an AI
assistant.

## Install

```
dotnet tool install -g tik4net.mcp
```

This puts `tik4net-mcp` on your PATH (requires the .NET 8 runtime).

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
| `includeRawTrace` | bool     | Also return the raw words exchanged for the command (protocol debugging) |
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

// compare a transport against the API baseline, with raw trace
{ "host": "192.168.88.1", "username": "admin", "password": "",
  "command": "/ip/address/print", "transport": "WinboxNative", "includeRawTrace": true }
```

## Documentation

Full docs, the per-transport RAW trace format, and prerequisites for each transport are on the
**[MCP server wiki page](https://github.com/danikf/tik4net/wiki/MCP-server)**.

Licensed under the same terms as tik4net (see LICENSE).
