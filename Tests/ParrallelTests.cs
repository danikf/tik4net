using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Tests {
    public class ParrallelTests {
        [Fact]
        public void Basic() {
            var rnd = new Random();
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var arps = link.Ip.Arp.Query(nameof(IpArp.Id), nameof(IpArp.MacAddress), nameof(IpArp.Dynamic));

                Parallel.ForEach(arps, arp => {
                    if (!arp.Dynamic) {
                        arp.Comment = arp.MacAddress + " " + rnd.Next().ToString();
                        link.Ip.Arp.Update(arp);
                    }
                });
            }
        }
    }
}
