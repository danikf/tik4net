using InvertedTomato.TikLink;
using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Tests {
    public class ScanTests {
        [Fact]
        public void Scan() {
            using (var link = Link.Connect(Credentials.Current.Host)) {
                link.Login(Credentials.Current.Username, Credentials.Current.Password);

                var interfaces = link.Scan<InterfaceRecord>();

            }
        }
    }
}
