---
name: mikrotik
description: >
  Connect to a MikroTik router via the tik4net MCP server and query or modify its configuration over
  any supported transport (API, REST, Telnet, SSH, MAC-Telnet, WinBox CLI/native, and their MAC-layer
  variants). Use when the user wants to inspect router settings, list resources (addresses, routes,
  interfaces, firewall rules, etc.),
  change configuration, add/remove entries, run any MikroTik command, or debug/compare a transport
  protocol. Also covers finding routers on the local network by MNDP broadcast when their IP or MAC
  is unknown ("which MikroTiks are on this segment", "what is the router's MAC").
---

# MikroTik Router Skill

Use the `mikrotik_call` MCP tool (provided by the `tik4net-mcp` server) to interact with MikroTik
routers via the tik4net low-level API. The same word/sentence call runs over **every** transport, so
the `command` / `parameters` format is identical no matter which transport you pick — pick the
transport, keep the command the same.

## Default test router

Unless the user specifies otherwise, read the router coordinates from
`tik4net.integrationtests/App.config` (`host`, `user`, `pass`) and connect with:
- transport: `Api`
- port: `0` (auto / transport default)

If that file is missing or the user works against a different router, ask for host and credentials
rather than guessing.

## Tool parameters

| Parameter        | Type     | Description |
|------------------|----------|-------------|
| `host`           | string   | IP or hostname |
| `username`       | string   | Username |
| `password`       | string   | Password (may be empty) |
| `command`        | string   | API command path |
| `transport`      | string   | Transport name, default `Api` (see table below) |
| `port`           | int      | Port, 0 = transport default |
| `routerMac`      | string   | Router MAC `AA:BB:CC:DD:EE:FF` — only MacTelnet / WinboxCliMac (else MNDP discovery, ~5 s) |
| `traceLevel`     | string   | `off` (default), `words` (raw words/CLI lines), or `bytes` (words **plus** a byte/frame wire trace) — see [Wire tracing & router-log](#wire-tracing--router-log-debugging) |
| `traceChannels`  | string[] | `bytes` only: keep just these emit-site channels (`wbxcli.mepty`, `wbxtcp.frame`, `telnet.sock`, `mactelnet.udp`, `api.word`); omit = all |
| `includeRawTrace`| bool     | Back-compat alias for `traceLevel='words'`, default false |
| `includeRouterLog`| bool    | Also append the router's own `/log` lines emitted during the command (`--- ROUTER LOG ---`), read over a separate API connection; default false |
| `routerLogTail`  | int      | `includeRouterLog` only: cap on kept log lines, default 200 |
| `executeMode`    | string   | `auto` (default) = `CallCommandSync`; `nonquery` = `ExecuteNonQuery()` — needed for action verbs (see below) |
| `parameters`     | string[] | Extra API words (see below) |

## Transports

| `transport`    | Wire                         | Default port | Router prerequisite | Notes |
|----------------|------------------------------|--------------|---------------------|-------|
| `Api`          | plain API                    | 8728         | api service (default) | default |
| `ApiSsl`       | TLS API                      | 8729         | api-ssl service + cert | |
| `Rest`         | HTTP REST                    | 80           | `www` service, RouterOS 7.1+ | |
| `RestSsl`      | HTTPS REST                   | 443          | `www-ssl` service + cert, 7.1+ | |
| `Telnet`       | plain CLI                    | 23           | `/ip/service set telnet disabled=no` | CRUD via CLI |
| `Ssh`          | CLI over an SSH PTY shell    | 22           | `/ip/service set ssh disabled=no` | satellite package `tik4net.ssh` |
| `MacTelnet`    | CLI over MAC layer (UDP)     | 20561        | `/tool/mac-server set allowed-interface-list=all` | no IP route needed; `routerMac` or MNDP |
| `WinboxCli`    | encrypted terminal CLI       | 8291         | winbox service (default) | EC-SRP5 + AES |
| `WinboxCliMac` | encrypted CLI over MAC (UDP) | 20561        | `/tool/mac-server/mac-winbox set allowed-interface-list=all` | `routerMac` or MNDP |
| `WinboxNative` | structured M2 (no terminal)  | 8291         | winbox service (default) | maps API fields ↔ WinBox keys via `.jg` |
| `WinboxNativeMac` | structured M2 over MAC (UDP) | 20561     | `/tool/mac-server/mac-winbox set allowed-interface-list=all` | as `WinboxNative`; `routerMac` or MNDP |

> Only `Api` / `ApiSsl` support Listen/Streaming (async `torch`-style). All CLI/REST/Winbox transports
> are request/response only.

### Parameter format

MikroTik API sentence words for the `parameters` array:
- **Filter** — `?name=value` (e.g. `?disabled=yes`, `?.id=*3`)
- **NameValue** — `=name=value` (e.g. `=address=10.0.0.1/24`, `=.id=*1`)

## Wire tracing & router-log debugging

When a call misbehaves — empty/garbled output, a hang, a "success" that didn't actually happen, or a
transport that disagrees with the API baseline — turn on tracing instead of guessing.

**`traceLevel`** (appended as extra sections after the JSON result):

- `words` — the raw words/CLI lines exchanged **for the command** (rides `OnReadRow`/`OnWriteRow`). Per
  transport: API words, REST HTTP, the synthesized CLI text, or the WinBox M2 message. Best for
  comparing a transport against the `Api` baseline to spot a mis-mapped/dropped field. (`includeRawTrace:true`
  is the old name for this.)
- `bytes` — everything `words` shows **plus** a `--- WIRE TRACE (bytes) ---` section: the layer *below*
  the cleaned word — exact bytes, WinBox-CLI `mepty` pull cadence, VT100 negotiation, M2 frame chunking,
  and the prompt/settle/timeout **decisions**. This is the layer a terminal hang/desync actually lives in
  (it's what pinned the WinBox-CLI large-output stall). Filter noise with `traceChannels`, e.g.
  `["wbxcli.mepty"]`. Channels: `wbxcli.mepty`, `wbxtcp.frame`, `telnet.sock`, `mactelnet.udp`, `api.word`.
  Events read `[+<ms>] <channel> >>/<</-- <escaped-bytes>  (<note>)`. Run **one** `bytes` call at a time
  (process-wide sink).

**`includeRouterLog: true`** appends a `--- ROUTER LOG ---` section: the router's own `/log` lines emitted
**during** the command, captured over a *separate* API connection (never the transport under test). It is
the device-side half of the story — pair it with `traceLevel:'bytes'` to answer "was the router blocking or
were we not pulling?" or "did a CLI action the client called success actually log an error?". Note many
actions log nothing by default (a good `/tool/wol` is silent; a bad MAC / ipsec error is logged).

```jsonc
// byte trace of a WinBox-CLI terminal command, mepty channel only
{ "command": "/interface/print", "parameters": ["detail"], "transport": "WinboxCli",
  "traceLevel": "bytes", "traceChannels": ["wbxcli.mepty"] }
// our bytes + the device log for a CLI action reported as success
{ "command": "/tool/wol", "parameters": ["=mac=00:11:22:33:44:55", "=interface=badiface"],
  "transport": "Telnet", "traceLevel": "bytes", "includeRouterLog": true }
```

> **Note:** `traceLevel`/`includeRouterLog` require the **rebuilt** MCP server — the running server keeps
> `tik4net.dll` loaded, so rebuild + restart the MCP client after a library/tool change (see the MCP-server
> wiki page), and check the build stamp below to confirm it took. The public sink behind `bytes` is
> `tik4net.Diagnostics.TikWireTrace`, usable from any tik4net program, not just the MCP.

## Which server answered — the build stamp

Every answer carries the version, **build timestamp** and path of the assembly that produced it:

- `mikrotik_call` — a trailing line `--- MCP SERVER --- tik4net.mcp 4.0.0 built 2026-08-23 10:15:42 (…)`
- `mikrotik_cli_complete`, `mikrotik_discover` — a `serverBuild` property on the returned JSON object

This exists because the dev launcher (`Tools/tik4net.mcp/run-dev.ps1`) starts each session from a
throw-away **copy** of the build output under `%TEMP%`, so the server can be rebuilt and replaced while
clients are connected — which also means the running process may be any staging, from any build, and the
repository cannot tell you which.

**After changing anything in `tik4net/` or the MCP tool, read the stamp before you read the answer.** If
its timestamp predates your build, the client is still on the old server: the answer describes the *previous*
code, and no amount of re-running will change that. Reconnect the `tik4net-mcp` server and check again.
A stamp that moved is the only positive confirmation the change is live.

## Common commands

### Read / inspect

```
/system/resource/print         — CPU, memory, uptime
/system/identity/print         — router name
/interface/print               — all interfaces
/ip/address/print              — IP addresses
/ip/route/print                — routing table
/ip/firewall/filter/print      — firewall filter rules
/ip/firewall/nat/print         — NAT rules
/ip/dhcp-server/lease/print    — DHCP leases
/ip/dns/print                  — DNS settings
/system/clock/print            — system time
/log/print                     — system log (all entries)
```

### Log — filtering by time

The MikroTik API supports comparison operators in filters — the `?>` prefix means "greater than", `?<` means "less than":

| Intent | Parameter |
|---|---|
| entries after a date/time | `"?>time=2026-05-31 00:00:00"` |
| entries before a date/time | `"?<time=2026-05-31 00:00:00"` |
| today's log only | `"?>time=2026-05-31 00:00:00"` |

Value format: `YYYY-MM-DD HH:MM:SS` (exactly as returned by `/log/print`).

Example — today's log:
```
command: /log/print
parameters: ["?>time=2026-05-31 00:00:00"]
```

> **Note:** If you need `>=` (from a given moment inclusive), use a value one second lower,
> e.g. `?>time=2026-05-30 23:59:59`, because the API's `?>` operator is strictly "greater than".

### Modify

```
/ip/address/add        params: ["=address=10.0.0.1/24", "=interface=ether1"]
/ip/address/set        params: ["=.id=*1", "=disabled=yes"]
/ip/address/remove     params: ["=.id=*1"]
/interface/set         params: ["=.id=*0", "=disabled=yes"]
/system/identity/set   params: ["=name=MyRouter"]
```

### Action verbs (`executeMode: "nonquery"`)

Some commands are **actions** — they *do* something and return **no result set** (no `!re` rows):

```
/system/script/run         params: ["=.id=*1"]   (or ["=number=myscript"])
/system/reboot
/system/reset-configuration
/system/shutdown
/interface/reset-counters  params: ["=.id=*0"]
```

These map to `ExecuteNonQuery()`, **not** the print/read path. Over the **command transports**
(`Telnet`, `MacTelnet`, `WinboxCli`, `WinboxCliMac`, `WinboxNative`) the default `auto` path
(`CallCommandSync`) dispatches by verb and treats an unknown verb like `run` as a *read*, so it throws
`NotSupportedException` ("…is an action command and returns no result set… Invoke it with
ExecuteNonQuery()"). Pass **`executeMode: "nonquery"`** to invoke the action instead:

```
command: /system/script/run
parameters: ["=.id=*1"]
executeMode: nonquery
transport: WinboxNative
→ "OK (action executed, no data returned)"
```

On success the tool returns `OK (action executed, no data returned)`; failures still surface as
`ERROR (...)` / `TRAP [...]`. On `Api`/`ApiSsl`/`Rest`/`RestSsl` action verbs already work in `auto`
mode (those transports send the sentence verbatim rather than dispatching by verb), but `nonquery` is
accepted there too and is the safe, explicit choice for any no-result action regardless of transport.

> WinboxNative dispatches the action as the handler's `.jg` *doit*/SYS_CMD; CLI transports type the
> command line fire-and-forget. The optional target row is named via `=.id=*N` (or the verb's own
> selector, e.g. `=number=` for `/system/script/run`).

## Workflow

1. If the user does not specify connection details, use the test router defaults above.
2. Call `mikrotik_call` with the appropriate `command` and `parameters` (and `transport` if not `Api`).
3. The tool returns either:
   - A JSON array of records (for `print` commands)
   - `OK (no data returned)` for write commands that succeeded
   - `OK (action executed, no data returned)` for `executeMode: "nonquery"` action verbs that succeeded
   - `ERROR (...)` or `TRAP [code]: message` on failure
   - …followed by a `--- RAW TRACE … ---` block when `includeRawTrace=true`
4. Present the result in a readable way — format tables for `print` results, confirm changes for
   write operations.
5. For complex tasks (e.g. "show all firewall rules that drop traffic"), chain multiple calls.

## Debugging a transport protocol

The point of multi-transport support is to **debug a transport by comparing it against the API baseline**.

- Run the **same** `command` over two transports and diff the JSON. The API result is the source of
  truth; a CLI/Winbox transport should round-trip to the same records.
  ```
  A) transport: Api          command: /system/resource/print
  B) transport: WinboxNative command: /system/resource/print   ← compare to A
  ```
- Add `includeRawTrace: true` to see the raw words for the command exchange in that transport's own
  wire/CLI form (API words, REST HTTP, synthesized CLI text + raw response, or M2). This is the fastest
  way to spot a mis-mapped field or a parsing gap. The trace covers the **command exchange**, not the
  login handshake (for handshake-level work use the protocol tests in `tik4net.integrationtests/Protocols/`).
- Per-transport gotchas to keep in mind when a result looks off:
  - **WinboxNative** — `.id`/tag handling differs; singletons (e.g. `/system/resource`) and ordered
    lists are the usual suspects. Field names are mapped via a version-matched `.jg` catalog.
  - **MacTelnet / WinboxCliMac** — need `routerMac` or MNDP discovery (~5 s); won't work if
    `mac-server` allowed-interface-list isn't set.
  - **CLI transports (Telnet/MacTelnet/WinboxCli)** — `print stats` and other interactive/streaming
    sub-commands are limited; prefer plain `print` / `print detail`.

### WinboxNative RAW TRACE format

For `WinboxNative` the trace is **not** API words — it is the raw M2 wire message rendered by
`M2Message.Describe`. One `>>` line per request, one `<<` line per reply. Format:

```
>> M2[<len>B] 0x<fullKey>=<wireType>:<value> 0x<fullKey>=<wireType>:<value> …
<< M2[<len>B] … 0xFE0002=msg[]:[{0x<key>=<val>,…},{…}] …
```

- **`0x<fullKey>`** — the 24-bit M2 field key `(namespace<<16)|(keyHi<<8)|keyLo`. Namespace `0xFF…` =
  system/control fields, `0xFE…` = session/record-frame fields, low keys (`0x1`, `0xD`, …) = the
  handler's user fields (the ones mapped to API names via `.jg`).
- **`<wireType>`** — `bool`/`u8`/`u32`/`u64`/`str`/`raw`/`u32[]`/`str[]`/`msg`/`msg[]`.
- Key system fields you'll see every time: `0xFF0001` = **to-handler** (`u32[]`, e.g. `[20,1]` =
  the handler array — the table being addressed), `0xFF0002` = from, `0xFF0006` = **command/SYS_CMD**
  (getall/get/set/add/remove/move — small ints), `0xFF0008` = **status** on replies (`0` = OK),
  `0xFF001C` = `msg-proxy-<ver>` banner, `0xFE0002` = **records array** (`msg[]`, one submessage per row).
- Records arrive under `0xFE0002` as `msg[]`, expanded inline as `[{…},{…}]`. Each `{…}` is one row's
  **raw numeric M2 keys** (pre-mapping) — this is what to diff against the final JSON to find a
  mis-mapped or dropped field.
- Reading payoff: a single `/path/print` may emit **several** `>>`/`<<` pairs — the data handler plus
  extra `getall`s on reference tables to resolve dynamic enums (e.g. `/ip/address` also reads the
  interface `[20,0]` and vrf `[20,101]` handlers to turn numeric ids into `ether1` / `main`). Seeing N
  round-trips for one command is expected, not a bug.

## Tab-completion / router introspection — `mikrotik_cli_complete`

The `tik4net-mcp` server also exposes **`mikrotik_cli_complete`**, which drives RouterOS terminal
Tab-completion to enumerate the menu tree and an object's settable parameters — the scriptable way to "map"
a router or resolve an entity's writable fields. CLI terminal transports only (default `Telnet`).

| Parameter   | Description |
|-------------|-------------|
| `host` / `username` / `password` | as above |
| `input`     | partial CLI line, **exactly as typed before Tab** — include the trailing space to list the next word |
| `transport` | `Telnet` (default), `Ssh`, `WinboxCli`, `MacTelnet`, `WinboxCliMac` (not `Api`/`Rest`/`WinboxNative*`) |
| `port` / `routerMac` | as above (MAC only for MacTelnet/WinboxCliMac) |

Returns `{ input, transport, tokens[], raw }`:
- after a **menu path** (`input: "/interface "`) — child menus + command verbs;
- after **`add `/`set `** (`input: "/interface/vlan add "`) — the **settable parameter names** (the writable
  field set, the gold source for generating a tik4net entity — see the `entity-generator` skill).

Notes: include the trailing space; long names may be column-truncated by RouterOS (cross-check full names
via `/path/print` `=detail=`); empty `tokens` means the input completed to a single unique token.

## Finding a router — `mikrotik_discover`

**`mikrotik_discover`** listens for the MNDP broadcast (UDP 5678) every RouterOS device sends. It takes
**no `host` and no credentials**, so it is what to use before you have any: after a VM rebuild that may
have moved the IP/MAC/identity, or when several MikroTiks share the segment.

| Parameter | Description |
|-----------|-------------|
| `timeoutSeconds` | how long to listen; default `6`, clamped 1–60 (the library's own default is 60 s — too long here) |
| `stopWhenFirstFound` | return on the first answer; **leave it off when choosing between routers**, since it returns whichever broadcast first |

Returns `{ timeoutSeconds, count, routers[] }` with `identity`, `ipv4`, `ipv6`, `mac`, `version`,
`platform`, `boardName`, `uptime`, `softwareId`, `interfaceName`. The `mac` is what the MAC-layer
transports need.

**Zero rows is ambiguous.** It usually means the *host* firewall is dropping the inbound broadcast, not
that the segment is empty — the failure is silent and looks identical. MNDP also does not cross a router,
so a device on another subnet never appears. Don't read an empty result as "the router is down"; confirm
with a direct `mikrotik_call` if you have an address to try.

## Notes

- Changing the tool surface (new transports/params/tools) requires the `tik4net-mcp` server to be rebuilt
  and reloaded — if `transport` is rejected as unknown or `mikrotik_cli_complete` / `mikrotik_discover` is
  missing, the running server is stale.
