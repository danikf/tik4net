#nullable enable

using System;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// X9 of the WinBox + MAC layer review: where a MAC-layer session opened with a host but no NIC in the router's
    /// subnet sends its broadcast. Real adapters report an address with no mask (some VPN and virtual ones), and the
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
            var addresses = new UnicastIPAddressInformation[]
            {
                new FakeAddress("10.8.0.2", null),
                new FakeAddress("192.168.4.31", "255.255.255.0"),
            };

            Assert.AreEqual("192.168.4.255",
                MacLayerTransport.BroadcastFor(IPAddress.Parse("192.168.4.236"), addresses).ToString());
        }

        [TestMethod]
        public void NoAddressInTheSubnetFallsBackToTheSlash24()
        {
            var addresses = new UnicastIPAddressInformation[] { new FakeAddress("10.8.0.2", "255.255.255.0") };

            Assert.AreEqual("192.168.4.255",
                MacLayerTransport.BroadcastFor(IPAddress.Parse("192.168.4.236"), addresses).ToString());
        }
    }
}
