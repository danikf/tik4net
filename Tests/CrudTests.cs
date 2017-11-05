using InvertedTomato.TikLink;
using InvertedTomato.TikLink.RosRecords;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using System.Linq;

namespace Tests {
    public class CrudTests {
        [Fact]
        public void Crud_IpArp() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var obj1 = new IpArp() {
                    MacAddress = RandomAddress.GenerateMacAddress(),
                    Address = RandomAddress.GenerateIpAddress(),
                    Interface = "eth01",
                    Comment = "<test>"
                };
                link.Ip.Arp.Put(obj1);

                // Find record
                var objs1 = link.Ip.Arp.List(null, new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj1.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj1.Address}" }
                });
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.MacAddress, obj2.MacAddress);
                Assert.Equal(obj1.Address, obj2.Address);
                Assert.StartsWith(obj1.Interface, obj2.Interface);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.Address = RandomAddress.GenerateIpAddress();
                link.Ip.Arp.Put(obj2);

                // Find record again
                var objs2 = link.Ip.Arp.List(null, new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj2.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj2.Address}" }
                });
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.MacAddress, obj3.MacAddress);
                Assert.Equal(obj2.Address, obj3.Address);
                Assert.StartsWith(obj2.Interface, obj3.Interface);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Ip.Arp.Delete(obj3.Id);

                // Make sure we can't find the record any more
                var objs3 = link.Ip.Arp.List(null, new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj1.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj1.Address}" }
                });
                Assert.Equal(0, objs3.Count);
            }
        }

        [Fact]
        public void Crud_FirewallFilter() {
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
                var obj2 = objs1.Single();
                Assert.Equal(obj1.SrcAddress, obj2.SrcAddress);
                Assert.Equal(obj1.DstAddress, obj2.DstAddress);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.SrcAddress = "2.2.2.2";
                link.Firewall.Filter.Put(obj2);

                // Find record again
                var objs2 = link.Firewall.Filter.List(null, new Dictionary<string, string>() {
                    {nameof(FirewallFilter.SrcAddress), $"={obj2.SrcAddress}" },
                    {nameof(FirewallFilter.DstAddress), $"={obj2.DstAddress}" }
                });
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.SrcAddress, obj3.SrcAddress);
                Assert.Equal(obj2.DstAddress, obj3.DstAddress);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Firewall.Filter.Delete(obj3.Id);

                // Make sure we can't find the record any more
                var objs3 = link.Firewall.Filter.List(null, new Dictionary<string, string>() {
                    {nameof(FirewallFilter.SrcAddress), $"={obj3.SrcAddress}" },
                    {nameof(FirewallFilter.DstAddress), $"={obj3.DstAddress}" }
                });
                Assert.Equal(0, objs3.Count);
            }
        }
    }
}
