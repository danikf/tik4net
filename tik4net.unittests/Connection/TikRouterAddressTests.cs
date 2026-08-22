using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikRouterAddress"/> exists to tell two strings apart that a constructor overload cannot,
    /// so what is tested here is the telling apart: a MAC is recognised, and nothing that is not a MAC is
    /// mistaken for one.
    /// </summary>
    [TestClass]
    public class TikRouterAddressTests
    {
        [TestMethod]
        public void AMacShapedStringIsReadAsAMac()
        {
            foreach (var text in new[] { "AA:BB:CC:DD:EE:FF", "aa:bb:cc:dd:ee:ff", "00-11-22-33-44-55" })
            {
                var address = TikRouterAddress.Parse(text);
                Assert.IsTrue(address.HasMac, text);
                Assert.IsFalse(address.HasHost, text);
            }
        }

        [TestMethod]
        public void AMacIsNormalizedToUpperCaseColonForm()
        {
            Assert.AreEqual("00:11:22:33:44:55", TikRouterAddress.Parse("00-11-22-33-44-55").Mac);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", TikRouterAddress.FromMac("aa:bb:cc:dd:ee:ff").Mac);
        }

        [TestMethod]
        public void NothingThatIsNotAMacIsMistakenForOne()
        {
            // The IPv6 entries are the reason the MAC pattern is strict rather than "hex and colons":
            // an address that is read as a MAC would be sent to a MAC transport and never reach the router.
            string[] hosts =
            {
                "192.168.88.1",
                "router.example.com",
                "localhost",
                "fe80::215:5dff:fe04:1f03",
                "::1",
                "2001:db8:0:0:0:0:2:1",
                "AA:BB:CC:DD:EE",          // five groups
                "AA:BB:CC:DD:EE:FF:00",    // seven groups
                "AA:BB:CC:DD:EE:GG",       // not hex
                "AA:BB-CC:DD:EE:FF",       // mixed separators
            };

            foreach (var host in hosts)
            {
                var address = TikRouterAddress.Parse(host);
                Assert.IsTrue(address.HasHost, host + " should have been read as a host");
                Assert.IsFalse(address.HasMac, host + " should NOT have been read as a MAC");
                Assert.AreEqual(host, address.Host, host);
            }
        }

        [TestMethod]
        public void TheNamedFactoriesSayOutrightWhichCoordinateIsMeant()
        {
            Assert.AreEqual("192.168.88.1", TikRouterAddress.FromHost("192.168.88.1").Host);
            Assert.IsFalse(TikRouterAddress.FromHost("192.168.88.1").HasMac);

            var both = TikRouterAddress.FromHostAndMac("192.168.88.1", "AA:BB:CC:DD:EE:FF");
            Assert.IsTrue(both.HasHost);
            Assert.IsTrue(both.HasMac);
        }

        [TestMethod]
        public void FromMacRefusesAValueThatIsNotAMac()
        {
            // Silently taking it as a host is what the whole type exists to prevent: the caller said MAC.
            Assert.ThrowsException<ArgumentException>(() => TikRouterAddress.FromMac("192.168.88.1"));
        }

        [TestMethod]
        public void ABareStringConvertsImplicitly()
        {
            TikRouterAddress host = "192.168.88.1";
            TikRouterAddress mac = "AA:BB:CC:DD:EE:FF";
            Assert.IsTrue(host.HasHost);
            Assert.IsTrue(mac.HasMac);
        }

        [TestMethod]
        public void TryParseRejectsOnlyTheEmptyValue()
        {
            Assert.IsFalse(TikRouterAddress.TryParse(null, out _));
            Assert.IsFalse(TikRouterAddress.TryParse("", out _));
            Assert.IsTrue(TikRouterAddress.TryParse("anything-else", out var parsed));
            Assert.AreEqual("anything-else", parsed.Host);
        }

        [TestMethod]
        public void EqualityIgnoresCaseAndSeparator()
        {
            Assert.AreEqual(TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF"),
                            TikRouterAddress.FromMac("aa-bb-cc-dd-ee-ff"));
            Assert.AreNotEqual(TikRouterAddress.FromHost("192.168.88.1"),
                               TikRouterAddress.FromHost("192.168.88.2"));
        }
    }
}
