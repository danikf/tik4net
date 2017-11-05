using InvertedTomato.TikLink;
using InvertedTomato.TikLink.RosRecords;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using System.Linq;

namespace Tests {
    public class PutListDeleteTests {
        [Fact]
        public void PutListDelete_IpArp() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var arp1 = new IpArp() {
                    MacAddress = RandomAddress.GenerateMacAddress(),
                    Address = RandomAddress.GenerateIpAddress(),
                    Interface = "eth01",
                    Comment = "<test>"
                };
                link.Ip.Arp.Put(arp1);

                // Find record
                var arps1 = link.Ip.Arp.List(null, new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={arp1.MacAddress}" },
                    {nameof(IpArp.Address), $"={arp1.Address}" }
                });
                Assert.Equal(1, arps1.Count);
                var arp2 = arps1.Single();

                // Delete record
                link.Ip.Arp.Delete(arp2.Id);

                // Make sure we can't find the record any more
                var arps2 = link.Ip.Arp.List(null, new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={arp1.MacAddress}" },
                    {nameof(IpArp.Address), $"={arp1.Address}" }
                });
                Assert.Equal(0, arps2.Count);
            }
        }



        [Fact]
        public void PutListDelete_FirewallFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var obj1 = new FirewallFilter() {
                    Action = FirewallFilter.ActionType.Passthrough,
                    SrcAddress = "1.1.1.1",
                    DstAddress = "1.1.1.1",
                    Comment = "<test>"
                };
                link.Firewall.Filter.Put(obj1);

                // Find record
                var objs1 = link.Firewall.Filter.List(null, new Dictionary<string, string>() {
                    {nameof(FirewallFilter.SrcAddress), $"={obj1.SrcAddress}" },
                    {nameof(FirewallFilter.DstAddress), $"={obj1.DstAddress}" }
                });
                Assert.Equal(1, objs1.Count);
                var arp2 = objs1.Single();

                // Delete record
                link.Firewall.Filter.Delete(arp2.Id);

                // Make sure we can't find the record any more
                var objs2 = link.Firewall.Filter.List(null, new Dictionary<string, string>() {
                    {nameof(FirewallFilter.SrcAddress), $"={obj1.SrcAddress}" },
                    {nameof(FirewallFilter.DstAddress), $"={obj1.DstAddress}" }
                });
                Assert.Equal(0, objs2.Count);
            }
        }
    }
}
