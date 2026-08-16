tik4net
====

tik4net is a .NET `netstandard2.0` library for communicating with MikroTik routers — enabling use in .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5/6/7/8/9, Xamarin, and Unity. It offers a clean, easy-to-use interface that scales from low-level raw API access all the way up to a fully typed O/R mapper. Tested and debugged against **RouterOS 7.23.2** (latest stable).

> **🆕 Many new connection types!** Beyond the classic API, tik4net now drives the router over REST, Telnet, SSH, MAC-Telnet, and WinBox (terminal + native-M2, over IP or MAC layer). See [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities). tik4net is the **only .NET library** that speaks **MAC-Telnet** and the **WinBox** protocols.

| Package | NuGet | Description |
|---|---|---|
| **tik4net** | [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net) | Everything you normally need: the [low-level ADO.NET-like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) (sync and async R/W access) **and** the [high-level O/R mapper](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) (strongly typed entities, full CRUD) |
| **tik4net.testing** | [![NuGet](https://img.shields.io/nuget/v/tik4net.testing.svg)](https://www.nuget.org/packages/tik4net.testing) | Unit-testing support — `TikFakeConnection` lets you write tests without a live router |
| **tik4net.ssh** | [![NuGet](https://img.shields.io/nuget/v/tik4net.ssh.svg)](https://www.nuget.org/packages/tik4net.ssh) | SSH (TCP 22) transport — drives the RouterOS CLI over an SSH shell (`Crud`, `Listen`, `SafeMode`, `RawCommand`, like the other CLI transports). A separate package because of its `Renci.SshNet` dependency. |

> **⚠️ Upgrading from 3.x?** The O/R mapper used to be a separate `tik4net.objects` package.
> Since 4.0 it is part of `tik4net` itself — **remove any `PackageReference` to `tik4net.objects`**
> or you will get an assembly conflict. Your source code does not change.
> See [Upgrading from 3.x to 4.0](https://github.com/danikf/tik4net/wiki/Upgrading-from-3.x-to-4.0).

[Tools](https://github.com/danikf/tik4net/wiki/High-level-API-tools) — how to scaffold a custom entity from a live router. The repo also ships an [MCP server](https://github.com/danikf/tik4net/wiki/MCP-server) that lets an AI assistant run a command against a live router over any tik4net transport (`mikrotik_call`), enumerate a menu's writable fields via Tab-completion (`mikrotik_cli_complete`), and find routers on the local segment via MNDP (`mikrotik_discover`).

# Features
* Easy to use with [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper)
* Low level access supported by [low level API](https://github.com/danikf/tik4net/wiki/Low-level-API) 
* Stable interface and backward compatibility
* Broad range of .NET runtimes supported (including .NET core 2 and Xamarin)
* New mikrotik [v.6.43 login process](https://github.com/danikf/tik4net/wiki/login-versions) supported
* Includes [MNDP](https://github.com/danikf/tik4net/wiki/MNDP) discovery helper 
* 🆕 4.0 [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode) — `SafeModeTake()` / `SafeModeRelease()` / `SafeModeUnroll()` with automatic rollback-on-disconnect (lockout protection)
* 🆕 4.0 [Change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking) — `Save` sends only the fields you changed; no-op saves skip the API call
* 🆕 4.0 [Connection capability model](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) — `connection.Supports(TikConnectionCapability.Listen)`; unsupported features fail closed
* [Unit testing without a router](https://github.com/danikf/tik4net/wiki/Communication-debugging-&-testing) via `tik4net.testing` (`TikFakeConnection`)
* Uniform [exception tree](https://github.com/danikf/tik4net/wiki/Exception-handling) across all transports
* Easy to understand and well documented code

## Connection types

All transports share the same `ITikConnection` API and O/R mapper — pick one via `TikConnectionType`. See [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities).

| Transport | Port | What it is | Capabilities |
|---|---|---|---|
| **Api** / **ApiSsl** | TCP 8728 / 8729 | native MikroTik API protocol — the default and fastest; TLS variant needs a certificate on the router | **all of them**: `Crud`, `Listen`, `Streaming`, `RawSentences`, `Tagging`, `SafeMode`, `RawCommand`, `AsyncCommands`‡, `CancelInFlight`‡ |
| **Rest** / **RestSsl** | TCP 80 / 443 | REST API, RouterOS 7.1+ | `Crud`, `Listen`\*†, `AsyncCommands`‡, `CancelInFlight`‡ — stateless HTTP, so no streaming and no Safe Mode |
| **Telnet** | TCP 23 | RouterOS CLI over plain-text Telnet | `Crud`, `Listen`\*, `SafeMode`, `RawCommand`, `AsyncCommands`‡ |
| **Ssh** | TCP 22 | RouterOS CLI over an SSH shell (separate `tik4net.ssh` package) | `Crud`, `Listen`\*, `SafeMode`, `RawCommand`, `AsyncCommands`‡ |
| **MacTelnet** | UDP 20561 | CLI over MAC-Telnet — reaches the router with **no IP route** | `Crud`, `Listen`\*, `SafeMode`, `RawCommand`, `AsyncCommands`‡ |
| **WinboxCli** / **WinboxCliMac** | TCP 8291 / UDP 20561 | CLI over the encrypted WinBox channel (EC-SRP5 + AES, no certificates) | `Crud`, `Listen`\*, `SafeMode`, `RawCommand`, `AsyncCommands`‡ |
| **WinboxNative** / **WinboxNativeMac** | TCP 8291 / UDP 20561 | structured WinBox M2 CRUD, no terminal | `Crud`, `Listen`\*, `SafeMode`, `AsyncCommands`‡, `CancelInFlight`‡§ |

\* **`Listen` outside the API is emulated by polling** (re-issuing a snapshot on a background worker), not
server push. **`Streaming`** (`ExecuteListWithDuration`) is binary-API only — no other transport holds a
command exchange open for a blocking multi-row read.

† On REST an async monitor's rows arrive **when the command ends**, not as the router produces them: RouterOS
buffers the whole HTTP response. Prefer an explicit bound (`count`/`duration`) on a REST monitor — and note
that closing the connection does not stop a command already running on the router.

‡ **`Execute*Async` — the Task-based command surface** (`ExecuteListAsync`, `ExecuteScalarAsync`, … with a
`CancellationToken`) was rolled out per transport: REST first, then the whole CLI family, then the binary API,
and finally WinBox native — each over its own awaited I/O. A transport that cannot await its I/O would not
declare the flag at all and its async methods would throw, rather than block a thread pretending to be
asynchronous; every shipped transport now awaits. `CancelInFlight`
means a token cancelled *after* dispatch really stops the wait and leaves the connection usable. **On the binary
API it is the protocol's own operation**: the client sends `/cancel tag=N`, the router answers the cancelled
command with `!trap interrupted` + `!done`, and the connection carries on — nothing is abandoned mid-stream. **On the CLI
transports it never will**: a terminal answers with an unframed byte stream, so abandoning a read would leave
output for the next command to misparse. There a mid-command cancel is reported once the response has been
drained — correct, but no faster than the command itself. A caller who would rather lose the session than wait
opts in with `TikConnectionSetup.CancellationMode = TikCancellationMode.AbandonAndClose`, which closes the
connection instead of silently desynchronizing it.

§ **On WinBox native `CancelInFlight` means two different things**, and the stronger one is why it is
declared. A **streaming window** — torch, ping, scan, traceroute, bandwidth-test — is closed by sending the
window's own `cancelcmd`, exactly what WinBox does when you close that window; the router stops. Every
streaming window in the router's `.jg` catalog declares one (68 of them on RouterOS 7.23.2, one per
`startcmd`), so this is as real a stop as the binary API's `/cancel`. An **ordinary round trip**
(getall/set/add) has no cancel verb: cancelling frees the caller and drops the request's registration while
the router finishes the work — safe because replies are dispatched by request id, so the late reply is
identified and discarded rather than handed to the next command. That weaker half is the same guarantee REST
gives.

Every transport can have its connection **reused**. Concurrent commands on one connection work on
`Api`/`ApiSsl` (set `SendTagWithSyncCommand = true` first), `Rest`/`RestSsl` and both WinBox-native
transports; the CLI family drives a single request/reply terminal and serializes by design.

# Binaries

Install via NuGet — see the package table above, or:

```
dotnet add package tik4net           # low-level API + O/R mapper — start here
dotnet add package tik4net.testing   # unit-testing support
dotnet add package tik4net.ssh       # SSH (TCP 22) transport
```

See [release notes / version history](https://github.com/danikf/tik4net/wiki/History) for what's new.

# Getting started and documentation
Mikrotik API wiki:
* [Mikrotik API wiki](https://wiki.mikrotik.com/wiki/Manual:API)
* [Mikrotik API notes](https://wiki.mikrotik.com/wiki/API_command_notes)

Project wiki:
* [**Getting started**](https://github.com/danikf/tik4net/wiki/Getting-started) — step-by-step first project (NuGet → connect → CRUD)
* [wiki root](https://github.com/danikf/tik4net/wiki) 
* [CRUD examples for all APIs](https://github.com/danikf/tik4net/wiki/CRUD-examples-for-all-APIs)
* [how to use](https://github.com/danikf/tik4net/wiki/How-to-use-tik4net-library)
* [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities) — pick a transport and see what it supports
* [Exception handling](https://github.com/danikf/tik4net/wiki/Exception-handling) — the full exception tree
* [Safe Mode](https://github.com/danikf/tik4net/wiki/Safe-Mode) · [Change tracking](https://github.com/danikf/tik4net/wiki/Change-tracking) — the flagship 4.0 features
* [Communication debugging & testing](https://github.com/danikf/tik4net/wiki/Communication-debugging-&-testing) — protocol tracing and unit tests without a router
* [History](https://github.com/danikf/tik4net/wiki/History)

Examples:
* [example project](https://github.com/danikf/tik4net/blob/master/tik4net.examples/ProgramExamples.cs)
* [support forum](http://forum.mikrotik.com/viewtopic.php?t=99954)
* For VisualBasic trivial example see [VB example](https://github.com/danikf/tik4net/wiki/VB-trivial-example)

```cs
   using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api)) // TikConnectionType.Api works for both old and new (v6.43+) login
   {
      connection.Open(HOST, USER, PASS);
```
```cs
   ITikCommand cmd = connection.CreateCommand("/system/identity/print");
   var identity = cmd.ExecuteScalar(); 
   Console.WriteLine("Identity: {0}", identity);
```
```cs
   var logs = connection.LoadList<Log>();
   foreach (Log log in logs)
   {
       Console.WriteLine("{0}[{1}]: {2}", log.Time, log.Topics, log.Message);
   }
```
```cs
   var firewallFilter = new FirewallFilter()
   {
      Chain = FirewallFilter.ChainType.Forward,
      Action = FirewallFilter.ActionType.Accept,
   };
   connection.Save(firewallFilter);
```
```cs
   ITikCommand torchCmd = connection.CreateCommand("/tool/torch", 
      connection.CreateParameter("interface", "ether1"), 
      connection.CreateParameter("port", "any"),
      connection.CreateParameter("src-address", "0.0.0.0/0"),
      connection.CreateParameter("dst-address", "0.0.0.0/0"));

  torchCmd.ExecuteAsync(response =>
      {
         Console.WriteLine("Row: " + response.GetResponseField("tx"));
      });
  Console.WriteLine("Press ENTER");
  Console.ReadLine();
  torchCmd.Cancel();
```
  
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
