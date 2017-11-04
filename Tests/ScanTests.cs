using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Tests {
    public class ScanTests {
        [Fact]
        public void Scan_Basic1() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interfaces.Scan();
                Assert.True(result.Count > 1);

                var eth1 = result.Single(a => a.DefaultName == "ether1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }


        [Fact]
        public void Scan_Basic2() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Ip.Arp.Scan();
                Assert.True(result.Count >= 1);
            }
        }

        [Fact]
        public void Scan_Properties() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Ip.Arp.Scan(new string[] { nameof(IpArp.MacAddress) });
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
        public void Scan_Query() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interfaces.Scan(null, new Dictionary<string, string>() { { nameof(IpArp.Id), "*1" } });
                Assert.Equal(1, result.Count);
                var eth1 = result.Single(a => a.Id == "*1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }

        [Fact]
        public void Scan_Properties_Query() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var result = link.Interfaces.Scan(new string[] { nameof(IpArp.Id) }, new Dictionary<string, string>() { { nameof(IpArp.Id), "*1" } });
                Assert.Equal(1, result.Count);
                var eth1 = result.Single(a => a.Id == "*1");
                Assert.Equal("*1", eth1.Id);
                Assert.Null(eth1.Type);
            }
        }
    }
}
