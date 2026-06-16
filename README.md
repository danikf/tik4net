tik4net
====

tik4net is a .NET `netstandard2.0` library for communicating with MikroTik routers — enabling use in .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5/6/7/8/9, Xamarin, and Unity. It offers a clean, easy-to-use interface that scales from low-level raw API access all the way up to a fully typed O/R mapper. Tested and debugged against **RouterOS 7.21.4** (latest stable).

> **🆕 Many new connection types!** Beyond the classic API, tik4net now drives the router over REST, Telnet, SSH, MAC-Telnet, and WinBox (terminal + native-M2, over IP or MAC layer). See [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities). tik4net is the **only .NET library** that speaks **MAC-Telnet** and the **WinBox** protocols.

| Package | NuGet | Description |
|---|---|---|
| **tik4net** | [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net) | [Low-level ADO.NET-like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) — synchronous and async R/W access |
| **tik4net.entities** | [![NuGet](https://img.shields.io/nuget/v/tik4net.entities.svg)](https://www.nuget.org/packages/tik4net.entities) | [High-level O/R mapper](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) — strongly typed entities, full CRUD. Pulls in `tik4net` automatically. |
| **tik4net.testing** | [![NuGet](https://img.shields.io/nuget/v/tik4net.testing.svg)](https://www.nuget.org/packages/tik4net.testing) | Unit-testing support — `TikFakeConnection` lets you write tests without a live router |
| **tik4net.ssh** | _will be published in the 4.x release_ | SSH (TCP 22) transport — drives the RouterOS CLI over an SSH shell (full CRUD, Listen, Safe Mode). A separate package because of its `Renci.SshNet` dependency. |

[Tools](https://github.com/danikf/tik4net/wiki/High-level-API-tools) — semi-automatic C# code generators for custom entities (used with tik4net.entities). The repo also ships an [MCP server](https://github.com/danikf/tik4net/wiki/MCP-server) exposing a `mikrotik_call` tool that lets an AI assistant run a command against a live router over any tik4net transport.

# Features
* Easy to use with [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper)
* Low level access supported by [low level API](https://github.com/danikf/tik4net/wiki/Low-level-API) 
* Stable interface and backward compatibility
* Broad range of .NET runtimes supported (including .NET core 2 and Xamarin)
* New mikrotik [v.6.43 login process](https://github.com/danikf/tik4net/wiki/login-versions) supported
* Includes [MNDP](https://github.com/danikf/tik4net/wiki/MNDP) discovery helper 
* Easy to understand and well documented code

## Connection types

All transports share the same `ITikConnection` API and O/R mapper — pick one via `TikConnectionType`. See [Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities).

* **Api** — native MikroTik API protocol (TCP 8728); the default, fastest transport with full Listen/Streaming support.
* **ApiSsl** — the API protocol over TLS (TCP 8729), using a certificate on the router.
* **Rest** / **RestSsl** — REST API over HTTP (80) / HTTPS (443); requires RouterOS 7.1+.
* **Ssh** — drives the RouterOS CLI over an SSH shell (TCP 22); full CRUD plus Listen and Safe Mode.
* **Telnet** — drives the RouterOS CLI over plain-text Telnet (TCP 23); full CRUD.
* **MacTelnet** — drives the CLI over MAC-Telnet (UDP 20561), reaching the router with no IP route.
* **WinboxCli** / **WinboxCliMac** — drives the CLI over the encrypted WinBox channel (TCP 8291 / MAC layer).
* **WinboxNative** / **WinboxNativeMac** — structured WinBox M2 CRUD with no terminal (TCP 8291 / MAC layer).

# Binaries

Install via NuGet — see the package table above, or:

```
dotnet add package tik4net.entities  # high-level API (pulls in tik4net)
dotnet add package tik4net           # low-level API only
dotnet add package tik4net.testing   # unit-testing support
# dotnet add package tik4net.ssh     # SSH (TCP 22) transport — will be published in the 4.x release
```

See [release notes / version history](https://github.com/danikf/tik4net/wiki/History) for what's new.

# Getting started and documentation
Mikrotik API wiki:
* [Mikrotik API wiki](https://wiki.mikrotik.com/wiki/Manual:API)
* [Mikrotik API notes](https://wiki.mikrotik.com/wiki/API_command_notes)

Project wiki:
* [wiki root](https://github.com/danikf/tik4net/wiki) 
* [CRUD examples for all APIs](https://github.com/danikf/tik4net/wiki/CRUD-examples-for-all-APIs)
* [how to use](https://github.com/danikf/tik4net/wiki/How-to-use-tik4net-library)
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
  
# Looking for help
* **I am looking for collaborators.** If you are interested in helping maintain this project, please reach out — open an issue or contact me directly.
* Looking for betatesters

# Roadmap & future
* create highlevel classes for all mikrotik entities (you can still generate your own classes)
* create tiklink project - easy use-to wrapper over mikrotik router with fluent API 
* convert examples to separate unittests (in progress)
* **[tiktop](https://github.com/danikf/tiktop)** — a MikroTik traffic monitor inspired by Linux `iftop` (currently in alpha, available on NuGet/GitHub)

REMARKS: This project is rewritten version of deprecated tik4net at googlecode (last version was 0.9.7.)

# Licenses
* Apache 2.0.
