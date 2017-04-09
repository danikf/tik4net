tik4net
====

Unique complex mikrotik API communication solution.

The tik4net project provides easy to use API to connect and manage mikrotik routers via mikrotik API protocol.
It has 3 parts:
* [Basic ADO.NET like API](https://github.com/danikf/tik4net/wiki/ADO.NET-like-API) - to perform R/W access to mikrotik in both sync and async code (tik4net.dll).
* [O/R mapper like highlevel API](https://github.com/danikf/tik4net/wiki/High-level-API-with-O-R-mapper) with imported mikrotik strong-typed entities. (tik4net.objects.dll) 
* Tools - semi-automatic generators of custom entity C# code (for usage with tik4net.objects.dll)

# Binaries
* [nuget](https://www.nuget.org/packages/tik4net/)
* [dlls download](http://forum.mikrotik.com/viewtopic.php?t=99954)

# Getting started and documentation
Project wiki:
* [wiki root](https://github.com/danikf/tik4net/wiki) 
* [CRUD examples for all APIs](https://github.com/danikf/tik4net/wiki/CRUD-examples-for-all-APIs)
* [how to use](https://github.com/danikf/tik4net/wiki/How-to-use-tik4net-library)

Examples:
* [example project](https://github.com/danikf/tik4net/blob/master/tik4net.examples/ProgramExamples.cs)
* [support forum](http://forum.mikrotik.com/viewtopic.php?t=99954)
* For VisualBasic trivial example see [VB example](https://github.com/danikf/tik4net/wiki/VB-trivial-example)

```cs
   using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
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

Builded binaries (dlls) could be downloaded from [mikrotik official forum](http://forum.mikrotik.com/viewtopic.php?t=99954).

  
# Looking for help
* Importing other classes
* Looking for betatesters

# Roadmap
* .NET core support & build
* creating highlevel classes for all mikrotik entities (you can still generate your own classes)
* convert examples to separate unittests (in progress)

Future:
* create and contribute to tiktop (see linux iftop) project 

REMARKS: This project is rewritten version of deprecated tik4net at googlecode (last version was 0.9.7.)

# Licenses
* Apache 2.0.
