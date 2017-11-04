using InvertedTomato.TikLink;
using System.Linq;
using Xunit;

namespace Tests {
    public class ScanTests {
        [Fact]
        public void Scan_Interfaces() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var interfaces = link.Interfaces.Scan();
                Assert.True(interfaces.Count > 1);

                var eth1 = interfaces.Single(a => a.DefaultName == "ether1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }


        [Fact]
        public void Scan_IpArp() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var ipArt = link.Ip.Arp.Scan();
                Assert.True(ipArt.Count >= 1);
            }
        }
    }
}
