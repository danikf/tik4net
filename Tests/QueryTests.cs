using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Tests {
    public class QueryTests {
        [Fact]
        public void Query_Basic() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var eth1 = link.Interface.Query().Single(a => a.DefaultName == "ether1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }

        [Fact]
        public void Query_LimitedProperties() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Ip.Arp.Query(nameof(IpArp.MacAddress));
                Assert.True(result.Count >= 1);
                foreach (var item in result) {
                    Assert.NotNull(item.MacAddress);
                    Assert.Null(item.Id);
                    Assert.Null(item.Address);
                    Assert.Null(item.Interface);
                }
            }
        }

        [Fact]
        public void Query_WithFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var eth1 = link.Interface.Query(new QueryFilter(nameof(IpArp.Id), QueryOperationType.Equal, "*1")).Single();
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }

        [Fact]
        public void Query_LimitedProperties_WithFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var eth1 = link.Interface.Query(
                    new string[] { nameof(IpArp.Id) }, 
                    new QueryFilter[] { new QueryFilter(nameof(IpArp.Id), QueryOperationType.Equal, "*1") }
                ).Single();
                Assert.Equal("*1", eth1.Id);
                Assert.Null(eth1.Type);
            }
        }
    }
}
