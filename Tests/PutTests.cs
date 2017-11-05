using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Tests {
    public class PutTests {
        [Fact]
        public void Put_Basic() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                link.Ip.Arp.Put(new IpArp() {
                    MacAddress = "00:00:00:00:01",
                    Address = "0.0.0.1"
                });
            }
        }
    }
}
