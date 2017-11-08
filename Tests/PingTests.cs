using InvertedTomato.TikLink;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Tests {
    public class PingTests {
        [Fact]
        public void IsAlive_True() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                Assert.True(link.Tool.Ping.IsAlive("8.8.8.8"));
            }
        }

        [Fact]
        public void IsAlive_False() {
            using (var link = Link.Connect(Credentials.Current.Host, Credentials.Current.Username, Credentials.Current.Password)) {
                Assert.False(link.Tool.Ping.IsAlive("1.1.1.1"));
            }
        }
    }
}
