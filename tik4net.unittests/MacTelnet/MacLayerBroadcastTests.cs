#nullable enable

using System;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// X9 of the WinBox + MAC layer review: where a MAC-layer session opened with a host sends its broadcast. Real adapters report an address with no mask (some VPN and virtual ones), and the
    /// choice must step over it rather than fail the open.
    /// </summary>
    [TestClass]
    public class MacLayerBroadcastTests
    {
        private sealed class FakeAddress : UnicastIPAddressInformation
        {
            private readonly IPAddress _address;
            private readonly IPAddress? _mask;
            internal FakeAddress(string address, string? mask)
            {
                _address = IPAddress.Parse(address);
                _mask = mask == null ? null : IPAddress.Parse(mask);
            }

            public override IPAddress Address => _address;
            public override IPAddress IPv4Mask => _mask!;   // null, as real adapters report it
            public override bool IsDnsEligible => false;
            public override bool IsTransient => false;
            public override long AddressPreferredLifetime => 0;
            public override long AddressValidLifetime => 0;
            public override long DhcpLeaseLifetime => 0;
            public override DuplicateAddressDetectionState DuplicateAddressDetectionState => DuplicateAddressDetectionState.Preferred;
            public override PrefixOrigin PrefixOrigin => PrefixOrigin.Manual;
            public override SuffixOrigin SuffixOrigin => SuffixOrigin.Manual;
        }

        [TestMethod]
        public void AnAddressWithNoMaskIsSteppedOver()
        {
            var target = IPAddress.Parse("192.168.4.236");

            Assert.IsFalse(TikLocalNics.InSubnet(new FakeAddress("10.8.0.2", null), target));
            Assert.IsFalse(TikLocalNics.InSubnet(new FakeAddress("192.168.4.31", null), target));
            Assert.IsTrue(TikLocalNics.InSubnet(new FakeAddress("192.168.4.31", "255.255.255.0"), target));
        }

        [TestMethod]
        public void TheMatchingAddressGivesItsSubnetBroadcast()
        {
            Assert.AreEqual("192.168.4.255",
                MacLayerTransport.BroadcastFor(IPAddress.Parse("192.168.4.236"),
                    new FakeAddress("192.168.4.31", "255.255.255.0")).ToString());
            Assert.AreEqual("192.168.7.255",
                MacLayerTransport.BroadcastFor(IPAddress.Parse("192.168.4.236"),
                    new FakeAddress("192.168.4.31", "255.255.252.0")).ToString());
        }

        [TestMethod]
        public void NoAddressInTheSubnetFallsBackToTheSlash24()
        {
            Assert.AreEqual("192.168.4.255",
                MacLayerTransport.BroadcastFor(IPAddress.Parse("192.168.4.236"), null).ToString());
        }

        /// <summary>
        /// H6 of the shared-helpers review: <see cref="TikLocalNics.FindFor"/> on this machine's own interfaces.
        /// Each interface with a MAC and a masked IPv4 address is found for that address, with an address whose
        /// subnet holds it; the loopback, which no MAC-layer session can use, is never found.
        /// </summary>
        [TestMethod]
        public void FindForFindsAnInterfaceThatHoldsTheTarget()
        {
            Assert.IsNull(TikLocalNics.FindFor(IPAddress.Loopback));

            int checkedCount = 0;
            foreach (var ni in TikLocalNics.Eligible())
            {
                if (TikLocalNics.MacOf(ni) == null) continue;
                foreach (var ua in TikLocalNics.IPv4Addresses(ni))
                {
                    if (ua.IPv4Mask == null) continue;
                    var found = TikLocalNics.FindFor(ua.Address);
                    Assert.IsNotNull(found, ua.Address.ToString());
                    Assert.IsTrue(TikLocalNics.InSubnet(found!.Address, ua.Address));
                    Assert.AreEqual(6, found.Mac.Length);
                    checkedCount++;
                }
            }

            if (checkedCount == 0)
                Assert.Inconclusive("This machine has no up interface with a MAC and a masked IPv4 address.");
        }
    }
}
