TLDR, do I make it go?
===
```c#
// Connect to the router
using (var link = Link.Connect("{router-host}", "{router-user}", "{router-password}")) {
    // Get list of interfaces
    var ifaces = link.Interfaces.List();
    foreach (var iface in ifaces) {
        Console.WriteLine(iface.Name);
    }

    // Get ether1's received byte counter
    var ether1 = ifaces.Single(a => a.DefaultName == "ether1");
    Console.WriteLine(ether1.RxByte);

    // Create new static ARP entry
    link.Ip.Arp.Put(new IpArp() {
        MacAddress = "01:02:03:04:05:06",
        Address = "1.2.3.4",
        Interface = ether1.Name,
        Comment = "Demo ARP entry"
    });
}
```

Summary
===
TikLink allows you to control MikoTik routers (RouterOS) from .NET applications. Key features:
 - Written in .NET Standard 1.3, for maximum compadibility
 - We're a national ISP, and we use it
 - Fully thread safe
 - Easy to use
 - Ability to support multiple concurrent requests

Functionality
===
Not all router records and methods are implimented currently. Feel free to raise a Pull Request to have your changes incorporated. Or otherwise raise a [issue](https://github.com/invertedtomato/tiklink/issues) and we'll probably pop that functionality in within a week.

Getting Started
===
Grab it on [NuGet](https://www.nuget.org/packages/InvertedTomato.TikLink/):
```
PM> Install-Package InvertedTomato.TikLink
```

Credits
===
TikLink is a significant fork of the inspirational [Tik4Net](https://github.com/danikf/tik4net) library. Thank you Danikf and the guys for all your fantastic work!
