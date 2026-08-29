tik4net
====

**tik4net is the most complete .NET library for talking to MikroTik RouterOS devices.** Every way into a
router is available — the binary API, REST, Telnet, SSH, MAC-Telnet and the WinBox channel, over IP or
straight over the MAC layer — and they all sit behind one `ITikConnection` interface. You work with
whatever the router already has enabled instead of reconfiguring it first, and switching transport is one
enum value; the rest of your code does not change.

Work at whichever level suits the task — raw sentences, an ADO.NET-shaped command API, or a fully typed
O/R mapper — on the same connection, mixing them freely. Response parsing, terminal and paging quirks,
both API login handshakes and correlating replies to their caller are handled for you; what a transport
genuinely cannot do, it tells you through its capabilities instead of quietly doing the wrong thing. The
surface is large, and the first working program is five lines.

Tested and debugged against **RouterOS 7.24** (latest stable) — every transport verified against a live router.

> **🆕 Many new connection types!** Beyond the classic API, tik4net now drives the router over REST, Telnet, SSH, MAC-Telnet, and WinBox (terminal + native-M2, over IP or MAC layer). See [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities). tik4net is the **only .NET library** that speaks **MAC-Telnet** and the **WinBox** protocols.

| Package | NuGet | Description |
|---|---|---|
| **tik4net** | [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net) | Everything you normally need: the [low-level ADO.NET-like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) (sync and async R/W access) **and** the [high-level O/R mapper](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) (strongly typed entities, full CRUD) |
| **tik4net.testing** | [![NuGet](https://img.shields.io/nuget/v/tik4net.testing.svg)](https://www.nuget.org/packages/tik4net.testing) | Unit-testing support — `TikFakeConnection` lets you write tests without a live router |
| **tik4net.ssh** | [![NuGet](https://img.shields.io/nuget/v/tik4net.ssh.svg)](https://www.nuget.org/packages/tik4net.ssh) | The SSH transport (TCP 22) — a separate package because of its `Renci.SshNet` dependency |

> **⚠️ Upgrading from 3.x?** The O/R mapper is now part of `tik4net` itself — **remove any
> `PackageReference` to `tik4net.objects`** or you will get an assembly conflict. Your source code does
> not change. See [Upgrading from 3.x to 4.0](https://github.com/danikf/tik4net/wiki/Upgrading-from-3.x-to-4.0).

# Features
* Easy to use with the [O/R mapper high-level API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) — strongly typed entities, full CRUD
* [Low-level API](https://github.com/danikf/tik4net/wiki/Low-level-API) when you need to send the transport's own language unchanged
* One connection contract over [every transport](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities), including the MAC-layer ones that reach a router with no IP address
* Broad range of .NET runtimes supported (including .NET Framework, Xamarin and Unity)
* Both API login handshakes, [old and v6.43+](https://github.com/danikf/tik4net/wiki/login-versions), negotiated automatically
* [MNDP](https://github.com/danikf/tik4net/wiki/MNDP) discovery helper — find routers on the segment with no connection at all
* 🆕 4.0 [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode) — `SafeModeTake()` / `SafeModeRelease()` / `SafeModeUnroll()` with automatic rollback-on-disconnect (lockout protection)
* 🆕 4.0 [Change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking) — `Save` sends only the fields you changed; no-op saves skip the API call
* 🆕 4.0 [Connection capability model](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) — `connection.Supports(TikConnectionCapability.Listen)`; unsupported features fail closed
* [Unit testing without a router](https://github.com/danikf/tik4net/wiki/Unit-testing-without-a-router) via `tik4net.testing` (`TikFakeConnection`)
* Uniform [exception tree](https://github.com/danikf/tik4net/wiki/Exception-handling) across all transports
* [Entity scaffolding](https://github.com/danikf/tik4net/wiki/High-level-API-tools) from a live router, and an [MCP server](https://github.com/danikf/tik4net/wiki/MCP-server) that lets an AI assistant drive a router over any tik4net transport
* Easy to understand and well documented code

## Connection types

All transports share the same `ITikConnection` API and O/R mapper — pick one via `TikConnectionType`. See
[Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities)
for what each capability means in practice, and for the per-transport detail behind this table.

| Transport | Port | What it is | Capabilities |
|---|---|---|---|
| **Api** / **ApiSsl** | TCP 8728 / 8729 | native MikroTik API protocol — the default and fastest; TLS variant needs a certificate on the router | **all of them**: `Crud`, `Listen`, `Streaming`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`, `CancelInFlight` |
| **Rest** / **RestSsl** | TCP 80 / 443 | REST API, RouterOS 7.1+ | `Crud`, `Listen`, `AsyncCommands`, `CancelInFlight` — stateless HTTP, so no streaming and no Safe Mode |
| **Telnet** | TCP 23 | RouterOS CLI over plain-text Telnet | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands` |
| **Ssh** | TCP 22 | RouterOS CLI over an SSH shell (separate `tik4net.ssh` package) | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands` |
| **MacTelnet** | UDP 20561 | CLI over MAC-Telnet — reaches a router with **no IP route, or no IP address at all** | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands` |
| **WinboxCli** / **WinboxCliMac** | TCP 8291 / UDP 20561 | CLI over the encrypted WinBox channel (EC-SRP5 + AES, no certificates) | `Crud`, `Listen`, `SafeMode`, `RawCommand`, `AsyncCommands` |
| **WinboxNative** / **WinboxNativeMac** | TCP 8291 / UDP 20561 | structured WinBox M2 CRUD, no terminal | `Crud`, `Listen`, `SafeMode`, `AsyncCommands`, `CancelInFlight` |

What the table does not say, in one line each — the
[capabilities page](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) has the rest:

* **`Listen`** is server push on the binary API and emulated by polling everywhere else; **`Streaming`**
  (a blocking multi-row read) is binary-API only.
* **`RawCommand`** sends a command in the transport's *own* language, unchanged — API words on the API,
  real CLI text on the terminal transports. REST and WinBox native have a request shape rather than a
  language, so they do not offer it.
* **`AsyncCommands`** is the `Task`-based surface with a `CancellationToken`; **`CancelInFlight`** adds
  that a cancel after dispatch really stops the wait and leaves the connection usable — on the CLI
  transports a cancel is correct but no faster than the command itself.
* **Connections are reusable on every transport.** Concurrent commands on one connection work on
  `Api`/`ApiSsl`, `Rest`/`RestSsl` and both WinBox-native transports; the CLI family drives a single
  terminal and serializes by design.

# Binaries

Install via NuGet — see the package table above, or:

```
dotnet add package tik4net           # low-level API + O/R mapper — start here
dotnet add package tik4net.testing   # unit-testing support
dotnet add package tik4net.ssh       # SSH (TCP 22) transport
```

**Runtimes:** the packages target `netstandard2.0;net8.0` — usable from .NET Framework 4.6.1+, .NET Core
2.0+, .NET 5 and newer, Xamarin and Unity, with no runtime dependencies. The `net8.0` build additionally
carries the async streaming API (see
[ADO.NET-like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API)).

See [release notes / version history](https://github.com/danikf/tik4net/wiki/History) for what's new.

# Getting started and documentation

A complete first program — connect, read a typed list, create a rule:

```cs
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

// TikConnectionSetup is the entry point: it carries every option and opens the transport you name.
// TikConnectionType.Api works for both the old and the new (v6.43+) login.
var setup = new TikConnectionSetup(HOST, USER, PASS);
using (ITikConnection connection = setup.Create(TikConnectionType.Api))
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

Project wiki:
* [**Getting started**](https://github.com/danikf/tik4net/wiki/Getting-started) — step-by-step first project (NuGet → connect → CRUD)
* [wiki root](https://github.com/danikf/tik4net/wiki)
* [How to use tik4net](https://github.com/danikf/tik4net/wiki/How-to-use-tik4net-library) — picking the right API level
* [CRUD examples for all APIs](https://github.com/danikf/tik4net/wiki/CRUD-examples-for-all-APIs)
* [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) — pick a transport and see what it supports
* [Exception handling](https://github.com/danikf/tik4net/wiki/Exception-handling) — the full exception tree
* [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode) · [Change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking) — the flagship 4.0 features
* [Communication debugging](https://github.com/danikf/tik4net/wiki/Communication-debugging) — protocol words and raw wire bytes
* [Unit testing without a router](https://github.com/danikf/tik4net/wiki/Unit-testing-without-a-router) — `TikFakeConnection`, tests with no hardware
* [History](https://github.com/danikf/tik4net/wiki/History)

Examples and help:
* [example project](https://github.com/danikf/tik4net/blob/master/tik4net.examples/ProgramExamples.cs) — including asynchronous commands such as `/tool/torch`
* [VisualBasic example](https://github.com/danikf/tik4net/wiki/VB-trivial-example)
* [support forum](http://forum.mikrotik.com/viewtopic.php?t=99954)

MikroTik's own protocol documentation:
* [MikroTik API manual](https://wiki.mikrotik.com/wiki/Manual:API)
* [MikroTik API command notes](https://wiki.mikrotik.com/wiki/API_command_notes)

# Contributing

* [ARCHITECTURE.md](ARCHITECTURE.md) — how the codebase is laid out: the transport family, the capability model, the O/R mapper internals, and where the risky code lives. **Read this before any non-trivial change.**
* [AGENTS.md](AGENTS.md) — working rules and the documentation map. Written for AI coding agents, but it is the shortest accurate description of how this project is worked on, so it is worth reading either way.
* [Docs/](Docs/README.md) — protocol ground truth: what the router actually does on the wire, established by live probing. [Docs/HISTORY.md](Docs/HISTORY.md) holds the project's superseded diagnoses and dated incidents.
* Each project directory has its own `README.md` describing what it is and what belongs in it.
* Tests: [tik4net.unittests](tik4net.unittests/README.md) runs in CI on every pull request; [tik4net.integrationtests](tik4net.integrationtests/README.md) needs a live router. If a test does not need hardware, it belongs in the former.

# Looking for help
* **I am looking for collaborators.** If you are interested in helping maintain this project, please reach out — open an issue or contact me directly.
* Looking for betatesters

# Roadmap & future
See the [4.x roadmap](https://github.com/danikf/tik4net/wiki/Roadmap-4x) wiki page for details. Highlights:
* create highlevel classes for all mikrotik entities (you can still generate your own classes)
* create tiklink project - easy use-to wrapper over mikrotik router with fluent API 
* convert examples to separate unittests (in progress)
* **[tiktop](https://github.com/danikf/tiktop)** — a MikroTik traffic monitor inspired by Linux `iftop` (currently in alpha, available on NuGet/GitHub)

# Licenses
* Apache 2.0.
