tik4net
====

**tik4net is the most complete .NET library for MikroTik RouterOS.** One connection contract covers every way into a
router — the binary API, REST, Telnet, SSH, MAC-Telnet and the WinBox channel, over IP or straight over the MAC
layer — so you use whatever the router already has enabled, and switching transport is one enum value. On top of it
you work at the level that suits the task — raw sentences, an ADO.NET-like command API, or a typed O/R mapper over
170+ RouterOS menus — on the same connection. The first working program is five lines.

Tested on RouterOS **7.24.5** on every transport, and on 7.21.5 and 6.49.13
([RouterOS versions](https://github.com/danikf/tik4net/wiki/RouterOS-versions)).

## Install

| Package | NuGet | Contains |
|---|---|---|
| **tik4net** | [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net) | everything you normally need: every transport but SSH, the [low-level](https://github.com/danikf/tik4net/wiki/Low-level-API) and [ADO.NET-like](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) APIs, and the [O/R mapper](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) |
| **tik4net.ssh** | [![NuGet](https://img.shields.io/nuget/v/tik4net.ssh.svg)](https://www.nuget.org/packages/tik4net.ssh) | the [SSH transport](https://github.com/danikf/tik4net/wiki/SSH-connection) — separate because of its SSH.NET dependency |
| **tik4net.testing** | [![NuGet](https://img.shields.io/nuget/v/tik4net.testing.svg)](https://www.nuget.org/packages/tik4net.testing) | `TikFakeConnection` — [unit tests without a router](https://github.com/danikf/tik4net/wiki/Unit-testing-without-a-router) |

```
dotnet add package tik4net
```

The packages target `netstandard2.0` and `net8.0`: .NET Framework 4.6.1+, .NET Core / .NET 5+, Xamarin and Unity.

> **Upgrading from 3.x?** Remove any `tik4net.objects` package reference — the O/R mapper is part of `tik4net`. See
> [Upgrading from 3.x to 4.0](https://github.com/danikf/tik4net/wiki/Upgrading-from-3.x-to-4.0).

## Quick start

```cs
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

var setup = new TikConnectionSetup("192.168.88.1", "admin", "");
using (ITikConnection connection = setup.Create(TikConnectionType.Api))   // or Rest, Ssh, MacTelnet, WinboxCli, …
{
    ITikCommand cmd = connection.CreateCommand("/system/identity/print");
    Console.WriteLine("Identity: " + cmd.ExecuteScalar());

    foreach (Log log in connection.LoadList<Log>())
        Console.WriteLine("{0}[{1}]: {2}", log.Time, log.Topics, log.Message);

    connection.Save(new FirewallFilter()
    {
        Chain = FirewallFilter.ChainType.Forward,
        Action = FirewallFilter.ActionType.Accept,
    });
}
```

Step by step: [Getting started](https://github.com/danikf/tik4net/wiki/Getting-started). The whole documentation: [the wiki](https://github.com/danikf/tik4net/wiki/).

## What's new

**5.0** — prerelease, `5.0.0-alpha` on NuGet ([Upgrading from 4.x](https://github.com/danikf/tik4net/wiki/Upgrading-from-4.x-to-5.0)):
* [RoMON](https://github.com/danikf/tik4net/wiki/RoMON-connection) — reach a router through a neighbouring one, over any CLI or WinBox transport
* One codebase from RouterOS 6.49 to 7.24 — [RouterOS versions](https://github.com/danikf/tik4net/wiki/RouterOS-versions)
* [TikField](https://github.com/danikf/tik4net/wiki/TikField) — a property knows whether the router printed the field, and never invents a default
* [Describing menus](https://github.com/danikf/tik4net/wiki/Describing-menus) — `DescribeMenu` asks the router which commands, arguments and values a menu takes
* [Connection string](https://github.com/danikf/tik4net/wiki/Connecting-to-a-router#from-a-connection-string) — router, credentials and transport from configuration
* [Negated matchers](https://github.com/danikf/tik4net/wiki/TikField#negated-matchers) and [value lists](https://github.com/danikf/tik4net/wiki/TikField#value-lists-tikvaluelistt) — `src-address=!10.0.0.0/8`, `dst-port=22,8291` as typed values
* [Compile-time checks](https://github.com/danikf/tik4net/wiki/TikField#pitfalls) — analyzers TIK001–TIK003 catch `TikField` comparisons that can never match
* [SSH private-key login](https://github.com/danikf/tik4net/wiki/SSH-connection#authentication)
* [List sync with the fewest moves](https://github.com/danikf/tik4net/wiki/TikListMerge) — ordered menus reordered with minimal moves
* [Filtered CLI reads run on the router](https://github.com/danikf/tik4net/wiki/CLI-transports#same-code-as-over-the-api)
* [Upgrade with an AI agent](https://github.com/danikf/tik4net/wiki/Upgrading-from-4.x-to-5.0-with-an-AI-agent) — a ready-to-paste migration prompt

**4.0** — the current release:
* [Eleven transports, one contract](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities), including the MAC-layer ones found by [MNDP](https://github.com/danikf/tik4net/wiki/MNDP)
* [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode) with automatic rollback · [Change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking) — `Save` sends only what changed
* [Typed per-transport interfaces and fail-closed capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities#the-typed-connection-interfaces--ask-the-compiler-first)
* [Async and cancellation](https://github.com/danikf/tik4net/wiki/Async-and-cancellation) · [Monitors and streaming](https://github.com/danikf/tik4net/wiki/Monitors-and-streaming), `IAsyncEnumerable` reads on `net8.0`
* [MCP server](https://github.com/danikf/tik4net/wiki/MCP-server) · [Upgrade with an AI agent](https://github.com/danikf/tik4net/wiki/Upgrading-from-3.x-to-4.0-with-an-AI-agent)

Every change, version by version: [History](https://github.com/danikf/tik4net/wiki/History).

## Features

* [Three API levels](https://github.com/danikf/tik4net/wiki/How-to-use-tik4net-library) on one connection: [low-level](https://github.com/danikf/tik4net/wiki/Low-level-API), [ADO.NET-like](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API), [O/R mapper](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) with [170+ entities](https://github.com/danikf/tik4net/wiki/Entity-reference)
* [Eleven transports](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities), including the [MAC-layer ones](https://github.com/danikf/tik4net/wiki/MAC-layer-transports) that reach a router with no IP address, and [RoMON](https://github.com/danikf/tik4net/wiki/RoMON-connection)
* [Capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities#capabilities) answered by the compiler or `Supports(...)` — never silently wrong
* [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode), [change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking), [list merge](https://github.com/danikf/tik4net/wiki/TikListMerge)
* [Async with cancellation](https://github.com/danikf/tik4net/wiki/Async-and-cancellation), [monitors and streaming](https://github.com/danikf/tik4net/wiki/Monitors-and-streaming)
* One [exception tree](https://github.com/danikf/tik4net/wiki/Exception-handling) on every transport
* [Unit testing without a router](https://github.com/danikf/tik4net/wiki/Unit-testing-without-a-router), [wire tracing](https://github.com/danikf/tik4net/wiki/Communication-debugging), [MNDP discovery](https://github.com/danikf/tik4net/wiki/MNDP)
* [MCP server](https://github.com/danikf/tik4net/wiki/MCP-server) for AI assistants; **[tik4mcp](https://github.com/danikf/tik4mcp)** — a production MikroTik MCP server built on tik4net

## Connection types

All transports share the same `ITikConnection` API and O/R mapper — pick one via `TikConnectionType`. See
[Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities)
for what each capability means in practice, and for the per-transport detail behind this table.

| Transport | Port | What it is | Capabilities |
|---|---|---|---|
| **Api** / **ApiSsl** | TCP 8728 / 8729 | native MikroTik API protocol — the default and fastest; TLS variant needs a certificate on the router | every one but the two WinBox-native ones: `Crud`, `Listen`, `Streaming`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`, `CancelInFlight`, `MenuSchema` |
| **Rest** / **RestSsl** | TCP 80 / 443 | REST API, RouterOS 7.1+ | `Crud`, `Listen`, `AsyncCommands`, `CancelInFlight`, `MenuSchema` — stateless HTTP, so no streaming and no Safe Mode |
| **Telnet** | TCP 23 | RouterOS CLI over plain-text Telnet | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`, `MenuSchema` |
| **Ssh** | TCP 22 | RouterOS CLI over an SSH shell (separate `tik4net.ssh` package) | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`, `MenuSchema` |
| **MacTelnet** | UDP 20561 | CLI over MAC-Telnet — reaches a router with **no IP route, or no IP address at all** | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`, `MenuSchema` |
| **WinboxCli** / **WinboxCliMac** | TCP 8291 / UDP 20561 | CLI over the encrypted WinBox channel (EC-SRP5 + AES, no certificates) | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands`, `MenuSchema` |
| **WinboxNative** / **WinboxNativeMac** | TCP 8291 / UDP 20561 | structured WinBox M2 CRUD, no terminal — **experimental**: fields are addressed by number, and the API-name ↔ M2 mapping is reconstructed rather than published | `Crud`, `Listen`, `SafeMode`, `AsyncCommands`, `CancelInFlight`, `FieldLabels`, `MenuSchema`, `StructuredWrites` |

What the table does not say, one line each — the
[capabilities page](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) has the rest:

* **`Listen`** is pushed by the router on the binary API and polled everywhere else; **`Streaming`** is binary-API only.
* **`RawCommand`** sends the transport's own language unchanged — API words, or console text on the CLI transports.
* **`CancelInFlight`**: a cancel after sending ends the wait and leaves the connection usable; on the CLI it waits for the reply.
* **`StructuredWrites`**: WinBox native writes `tcp-flags` exactly; a text `set` elsewhere replaces only one half.
* **Several threads**: commands run concurrently on the API, REST and WinBox native; the CLI transports queue them.

## Help and examples

* [Example project](https://github.com/danikf/tik4net/blob/master/tik4net.examples/ProgramExamples.cs), including monitors such as `/tool/torch`
* [Support forum](http://forum.mikrotik.com/viewtopic.php?t=99954) · [issues](https://github.com/danikf/tik4net/issues)
* MikroTik's own [API manual](https://help.mikrotik.com/docs/display/ROS/API)
* Related: **[tiktop](https://github.com/danikf/tiktop)** — a MikroTik traffic monitor inspired by `iftop`

## Contributing

* [ARCHITECTURE.md](ARCHITECTURE.md) — how the codebase is laid out: the transport family, the capability model, the O/R mapper internals, and where the risky code lives. **Read this before any non-trivial change.**
* [AGENTS.md](AGENTS.md) — working rules and the documentation map. Written for AI coding agents, but it is the shortest accurate description of how this project is worked on, so it is worth reading either way.
* [Docs/](Docs/README.md) — protocol ground truth: what the router actually does on the wire, established by live probing. [Docs/HISTORY.md](Docs/HISTORY.md) holds the project's superseded diagnoses and dated incidents.
* Each project directory has its own `README.md` describing what it is and what belongs in it.
* Tests: [tik4net.unittests](tik4net.unittests/README.md) runs in CI on every pull request; [tik4net.integrationtests](tik4net.integrationtests/README.md) needs a live router. If a test does not need hardware, it belongs in the former.
* **Collaborators are welcome.** If you are interested in helping maintain this project, open an issue or get in touch.

## License

Apache 2.0.
