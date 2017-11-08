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

| Function                            | Type        | Status     |
|-------------------------------------|-------------|------------|
| /bridge/filter                      | Records     | Untested   |
| /bridge/nat                         | Records     | Untested   |
| /bridge/port                        | Records     | Untested   |
| /bridge/settings                    | Singleton   | Untested   |
| /caps-man/registration-table        | RO Records  | Untested   |
| /interface                          | Records     | Untested   |
| /interface/bridge                   | Records     | Untested   |
| /interface/ethernet                 | Records     | Untested   |
| /interface/wireless                 | Records     | Untested   |
| /interface/wireless/access-list     | Records     | Untested   |
| /interface/wireless/registration-table | RO Records     | Untested   |
| /interface/wireless/security-profiles | Records     | Untested   |
| /ip/accounting                      | Singleton   | Untested   |
| /ip/accounting/snapshot             | RO Records  | Untested   |
| /ip/accounting/uncounted            | Singleton   | Untested   |
| /ip/accounting/web-access           | Singleton   | Untested   |
| /ip/address                         | Records     | Untested   |
| /ip/arp                             | Records     | *Ready*    |
| /ip/dhcp-client                     | Records     | Untested   |
| /ip/dhcp-server                     | Records     | Untested   |
| /ip/dhcp-server/alert               | Records     | Untested   |
| /ip/dhcp-server/config              | Singleton   | Untested   |
| /ip/dhcp-server/lease               | Records     | *Ready*    |
| /ip/dhcp-server/network             | Records     | *Ready*    |
| /ip/dhcp-server/option              | Records     | Untested   |
| /ip/dns                             | Singleton   | Untested   |
| /ip/dns/cache                       | RO Records  | Untested   |
| /ip/dns/cache/all                   | RO Records  | Untested   |
| /ip/dns/static                      | Records     | Untested   |
| /ip/firewall/address-list           | Records     | Untested   |
| /ip/firewall/connection             | Records     | Untested   |
| /ip/firewall/connection/tracking    | Singleton   | Untested   |
| /ip/firewall/filter                 | Records     | *Ready*    |
| /ip/firewall/mangle                 | Records     | Untested   |
| /ip/firewall/nat                    | Records     | Untested   |
| /ip/hotspot/active                  | RO Records  | Untested   |
| /ip/hotspot/user                    | Records     | Untested   |
| /ip/hotspot/user-profile            | Records     | Untested   |
| /ip/pool                            | Records     | Untested   |
| /queue/simple                       | Records     | *Ready*    |
| /queue/tree                         | Records     | Untested   |
| /queue/type                         | Records     | Untested   |
| /system/resource                    | RO Singleton| Untested   |<
| /tool/pint                          | RO Records  | Untested   |
| /tool/torch                         | RO Records  | Untested   |

Getting Started
===
Grab it on [NuGet](https://www.nuget.org/packages/InvertedTomato.TikLink/):
```
PM> Install-Package InvertedTomato.TikLink
```

Credits
===
TikLink is a significant fork of the inspirational [Tik4Net](https://github.com/danikf/tik4net) library. Thank you Danikf and the guys for all your fantastic work!
