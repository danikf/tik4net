tik4net
====

Unique complex mikrotik API communication solution.

The tik4net project provides easy to use API to connect and manage mikrotik routers via mikrotik API protocol.
It has 3 parts:
* [Basic ADO.NET like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) - to perform R/W access to mikrotik in both sync and async code (tik4net.dll).
* [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) with imported mikrotik strong-typed entities. (tik4net.objects.dll) 
* [Tools](https://github.com/danikf/tik4net/wiki/High-level-API-tools) - semi-automatic generators of custom entity C# code (for usage with tik4net.objects.dll)

# Features
* Easy to use with [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper)
* Low level access supported by [low level API](https://github.com/danikf/tik4net/wiki/Low-level-API) 
* Stable interface and backward compatibility
* Broad range of .NET runtimes supported (including .NET core 2 and Xamarin)
* New mikrotik [v.6.43 login process](https://github.com/danikf/tik4net/wiki/login-versions) supported
* Easy to understand and well documented code

# Binaries
***Stable***
* [![NuGet](https://img.shields.io/nuget/v/tik4net.svg)](https://www.nuget.org/packages/tik4net)
* [builded dlls download](http://forum.mikrotik.com/viewtopic.php?t=99954)

***In development (v 3.6)***
* Download as sources.

# Getting started and documentation
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
* create and contribute to tiktop (see linux iftop) project 

REMARKS: This project is rewritten version of deprecated tik4net at googlecode (last version was 0.9.7.)

# Licenses
* Apache 2.0.
