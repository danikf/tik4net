using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
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
                link.Ip.Arp.Add(obj1);
                Assert.NotEmpty(obj1.Id);

                // Find record
                var objs1 = link.Ip.Arp.Query(new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj1.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj1.Address}" }
                }, null);
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.MacAddress, obj2.MacAddress);
                Assert.Equal(obj1.Address, obj2.Address);
                Assert.StartsWith(obj1.Interface, obj2.Interface);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.Address = RandomAddress.GenerateIpAddress();
                link.Ip.Arp.Update(obj2);

                // Find record again
                var objs2 = link.Ip.Arp.Query(new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj2.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj2.Address}" }
                }, null);
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.MacAddress, obj3.MacAddress);
                Assert.Equal(obj2.Address, obj3.Address);
                Assert.StartsWith(obj2.Interface, obj3.Interface);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Ip.Arp.Delete(obj3);

                // Make sure we can't find the record any more
                var objs3 = link.Ip.Arp.Query(new Dictionary<string, string>() {
                    {nameof(IpArp.MacAddress), $"={obj1.MacAddress}" },
                    {nameof(IpArp.Address), $"={obj1.Address}" }
                }, null);
                Assert.Equal(0, objs3.Count);
            }
        }

        [Fact]
        public void Crud_FirewallFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var obj1 = new IpFirewallFilter() {
                    Action = IpFirewallFilter.ActionType.Passthrough,
                    SrcAddress = "1.1.1.1",
                    DstAddress = "1.1.1.1",
                    Comment = "<test>"
                };
                link.Ip.Firewall.Filter.Add(obj1);

                // Find record
                var objs1 = link.Ip.Firewall.Filter.Query(new Dictionary<string, string>() {
                    {nameof(IpFirewallFilter.SrcAddress), $"={obj1.SrcAddress}" },
                    {nameof(IpFirewallFilter.DstAddress), $"={obj1.DstAddress}" }
                }, null);
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.Action, obj2.Action);
                Assert.Equal(obj1.SrcAddress, obj2.SrcAddress);
                Assert.Equal(obj1.DstAddress, obj2.DstAddress);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.SrcAddress = "2.2.2.2";
                link.Ip.Firewall.Filter.Update(obj2);

                // Find record again
                var objs2 = link.Ip.Firewall.Filter.Query(new Dictionary<string, string>() {
                    {nameof(IpFirewallFilter.SrcAddress), $"={obj2.SrcAddress}" },
                    {nameof(IpFirewallFilter.DstAddress), $"={obj2.DstAddress}" }
                }, null);
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.Action, obj3.Action);
                Assert.Equal(obj2.SrcAddress, obj3.SrcAddress);
                Assert.Equal(obj2.DstAddress, obj3.DstAddress);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Ip.Firewall.Filter.Delete(obj3);

                // Make sure we can't find the record any more
                var objs3 = link.Ip.Firewall.Filter.Query(new Dictionary<string, string>() {
                    {nameof(IpFirewallFilter.SrcAddress), $"={obj3.SrcAddress}" },
                    {nameof(IpFirewallFilter.DstAddress), $"={obj3.DstAddress}" }
                }, null);
                Assert.Equal(0, objs3.Count);
            }
        }


        [Fact]
        public void Crud_QueuesSimple() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                throw new NotImplementedException();
            }
        }


        [Fact]
        public void Crud_IpDhcpServerLease() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var obj1 = new IpDhcpServerLease() {
                    Address = RandomAddress.GenerateIpAddress(),
                    MacAddress = RandomAddress.GenerateMacAddress(),
                    LeaseTime = new TimeSpan(1, 2, 3, 4, 0),
                    Comment = "<test>"
                };
                link.Ip.DhcpServer.Lease.Add(obj1);

                // Find record
                var objs1 = link.Ip.DhcpServer.Lease.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerLease.Address), $"={obj1.Address}" },
                    {nameof(IpDhcpServerLease.MacAddress), $"={obj1.MacAddress}" }
                }, null);
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.Address, obj2.Address);
                Assert.Equal(obj1.MacAddress, obj2.MacAddress);
                Assert.Equal(obj1.LeaseTime, obj2.LeaseTime);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.Address = "2.2.2.2";
                link.Ip.DhcpServer.Lease.Update(obj2);

                // Find record again
                var objs2 = link.Ip.DhcpServer.Lease.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerLease.Address), $"={obj2.Address}" },
                    {nameof(IpDhcpServerLease.MacAddress), $"={obj2.MacAddress}" }
                }, null);
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.Address, obj3.Address);
                Assert.Equal(obj2.MacAddress, obj3.MacAddress);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Ip.DhcpServer.Lease.Delete(obj3);

                // Make sure we can't find the record any more
                var objs3 = link.Ip.DhcpServer.Lease.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerLease.Address), $"={obj3.Address}" },
                    {nameof(IpDhcpServerLease.MacAddress), $"={obj3.MacAddress}" }
                }, null);
                Assert.Equal(0, objs3.Count);
            }

        }

        [Fact]
        public void Crud_IpDhcpServerNetwork() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Create record
                var obj1 = new IpDhcpServerNetwork() {
                    Address = "1.1.1.0/24",
                    Comment = "<test>"
                };
                link.Ip.DhcpServer.Network.Add(obj1);

                // Find record
                var objs1 = link.Ip.DhcpServer.Network.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerNetwork.Address), $"={obj1.Address}" },
                }, null);
                Assert.Equal(1, objs1.Count);
                var obj2 = objs1.Single();
                Assert.Equal(obj1.Address, obj2.Address);
                Assert.Equal(obj1.Comment, obj2.Comment);

                // Edit record
                obj2.Address = "2.2.2.0/24";
                link.Ip.DhcpServer.Network.Update(obj2);

                // Find record again
                var objs2 = link.Ip.DhcpServer.Network.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerNetwork.Address), $"={obj2.Address}" },
                }, null);
                Assert.Equal(1, objs2.Count);
                var obj3 = objs1.Single();
                Assert.Equal(obj2.Address, obj3.Address);
                Assert.Equal(obj2.Comment, obj3.Comment);

                // Delete record
                link.Ip.DhcpServer.Network.Delete(obj3);

                // Make sure we can't find the record any more
                var objs3 = link.Ip.DhcpServer.Network.Query(new Dictionary<string, string>() {
                    {nameof(IpDhcpServerNetwork.Address), $"={obj3.Address}" }
                }, null);
                Assert.Equal(0, objs3.Count);
            }
        }
    }
}