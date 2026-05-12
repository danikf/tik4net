tik4net
====

> ## 📢 A note from the maintainer
>
> **I sincerely apologize for the long period of inactivity.** Thanks to AI tooling, I finally have some bandwidth again. In the near future I intend to work through all open PRs and critical bugs and publish an updated NuGet package.
>
> To prevent this from happening again, **I am looking for collaborators**. If you are interested in helping maintain this project, please reach out — open an issue or contact me directly. Special thanks to Deantwo for helping others while I was away.
>
> **The next release will target `netstandard2.0` only.** This should be sufficient for all current use cases — it covers .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5/6/7/8, Xamarin, and Unity.
>
> The upcoming release has been tested and debugged against **RouterOS 7.21.4** (latest stable).

tik4net is a .NET library for communicating with MikroTik routers via the MikroTik API protocol. It offers a clean, easy-to-use interface that scales from low-level raw API access all the way up to a fully typed O/R mapper, and ships as two NuGet packages:

* **tik4net** — [Basic ADO.NET-like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API): R/W access to MikroTik in both synchronous and asynchronous code.
* **tik4net.objects** — [High-level O/R mapper API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper): strongly typed MikroTik entities with full CRUD support.
* [Tools](https://github.com/danikf/tik4net/wiki/High-level-API-tools) — semi-automatic C# code generators for custom entities (used with tik4net.objects).

# Features
* Easy to use with [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper)
* Low level access supported by [low level API](https://github.com/danikf/tik4net/wiki/Low-level-API) 
* Stable interface and backward compatibility
* Broad range of .NET runtimes supported (including .NET core 2 and Xamarin)
* New mikrotik [v.6.43 login process](https://github.com/danikf/tik4net/wiki/login-versions) supported
* Includes [MNDP](https://github.com/danikf/tik4net/wiki/MNDP) discovery helper 
* Easy to understand and well documented code

# Binaries
***Stable***
* [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net)
* [builded dlls download](http://forum.mikrotik.com/viewtopic.php?t=99954)

***In development (v 3.6)***
* Download as sources.

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
   using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api_v2)) // Use TikConnectionType.Api for mikrotikversion prior v6.45
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
* Importing other classes
* Looking for betatesters

# Roadmap & future
* create highlevel classes for all mikrotik entities (you can still generate your own classes)
* create tiklink project - easy use-to wrapper over mikrotik router with fluent API 
* convert examples to separate unittests (in progress)
* **tiktop** — a MikroTik traffic monitor inspired by Linux `iftop` (currently in alpha, publication coming soon)

REMARKS: This project is rewritten version of deprecated tik4net at googlecode (last version was 0.9.7.)

# Licenses
* Apache 2.0.
