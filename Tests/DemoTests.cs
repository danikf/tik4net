using InvertedTomato.TikLink;
using InvertedTomato.TikLink.RosRecords;
using System;
using System.Linq;
using Xunit;

namespace Tests {
    public class DemoTests {
        [Fact]
        public void DemoTest() {
            // Connect to the router
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                // Get list of interfaces
                var ifaces = link.Interfaces.List();
                foreach (var iface in ifaces) {
                    Console.WriteLine(iface.Name);
                }

                // Get ether1's received byte counter
                var ether1 = ifaces.Single(a => a.DefaultName == "ether1");
                Console.WriteLine(ether1.RxByte);
            }
        }
    }
}
