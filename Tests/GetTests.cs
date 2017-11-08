using InvertedTomato.TikLink;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using System.Linq;
using InvertedTomato.TikLink.RosRecords;

namespace Tests {
    public class GetTests {
        [Fact]
        public void Get_Basic() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var eth1 = link.Interface.Get("*1");
                Assert.Equal("*1", eth1.Id);
                Assert.Equal("ether", eth1.Type);
            }
        }

        [Fact]
        public void Get_LimitedProperties() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                var eth1 = link.Interface.Get("*1", new string[] { nameof(Interface.Id) });
                Assert.Equal("*1", eth1.Id);
                Assert.Null(eth1.Type);
            }
        }
    }
}
