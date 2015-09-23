tik4net
====

Unique complex mikrotik API communication solution.

The tik4net project provides easy to use API to connect and manage mikrotik routers via mikrotik API protocol.
It has 3 parts:
* Basic ADO.NET like API - to perform R/W access to mikrotik in both sync and async code (tik4net.dll).
* O/R mapper like highlevel API with imported mikrotik strong-typed entities. (tik4net.objects.dll) 
* Tools - semi-automatic generators of custom entity C# code (for usage with tik4net.objects.dll)

# Getting started and documentation
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

See [project wiki](https://github.com/danikf/tik4net/wiki) or [example project](https://github.com/danikf/tik4net/blob/master/tik4net.examples/ProgramExamples.cs) for examples.

  
# Looking for help
* Importing of ip/hotspot menu into classes (and betatest it) - it used to be frequent question about previous version but unfortunattely I am not using this feature.
* Importing other classes
* Looking for betatesters !!!

# Roadmap
* creating highlevel classes for all mikrotik entities (you can also generate your own classes)
* add SSL support
* add examples and documentation
* convert examples to separate unittests

Future:
* create and contribute to tiktop (see linux iftop) project 

REMARKS: This project is rewritten version of deprecated tik4net at googlecode (last version was 0.9.7.)

# Licenses
* Apache 2.0.
