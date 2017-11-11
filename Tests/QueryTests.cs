using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Tests {
    public class QueryTests {
        [Fact]
        public void Query_Basic1() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interface.Query(QueryModeType.Quick);
                Assert.True(result.Count > 1);

                var eth1 = result.Single(a => a.DefaultName == "ether1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }


        [Fact]
        public void Query_Basic2() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Ip.Arp.Query(QueryModeType.Quick);
                Assert.True(result.Count >= 1);
            }
        }

        [Fact]
        public void Query_LimitedProperties() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Ip.Arp.Query(QueryModeType.Quick, new string[] { nameof(IpArp.MacAddress) });
                foreach (var item in result) {
                    Assert.NotNull(item.MacAddress);
                    Assert.Null(item.Id);
                    Assert.Null(item.Address);
                    Assert.Null(item.Interface);
                }
                Assert.True(result.Count >= 1);
            }
        }

        [Fact]
        public void List_WithFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interface.Query(QueryModeType.Quick, new QueryFilter(nameof(IpArp.Id), QueryOperationType.Equal, "*1"));
                Assert.Equal(1, result.Count);
                var eth1 = result.Single(a => a.Id == "*1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }

        [Fact]
        public void List_LimitedProperties_WithFilter() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interface.Query(QueryModeType.Quick, new string[] { nameof(IpArp.Id) }, new QueryFilter(nameof(IpArp.Id), QueryOperationType.Equal, "*1"));
                Assert.Equal(1, result.Count);
                var eth1 = result.Single(a => a.Id == "*1");
                Assert.Equal("*1", eth1.Id);
                Assert.Null(eth1.Type);
            }
        }
    }
}
