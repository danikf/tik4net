using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Diagnostics;

namespace tik4net.unittests
{
    /// <summary>
    /// An endpoint in an exception message keeps the part that finds the session and drops the part that
    /// describes the network.
    /// </summary>
    /// <remarks>
    /// The receive-timeout message names the socket's local endpoint so a stalled session can be found in
    /// the router's own <c>/ip/firewall/connection</c> table, which is keyed by <c>address:port</c>. That
    /// message ends up in user logs and pasted bug reports, and the address is the only part of it that is
    /// information about somebody's network rather than about the fault.
    /// </remarks>
    [TestClass]
    public class EndpointTextMaskingTests
    {
        [TestMethod]
        public void AnIpv4EndpointKeepsItsLastOctetAndItsPort()
        {
            // The port is the whole point: it is what identifies the row on the router.
            Assert.AreEqual("xx.xx.xx.31:56864",
                TikEndpointText.Describe(new IPEndPoint(IPAddress.Parse("192.168.4.31"), 56864)));
        }

        [TestMethod]
        public void TwoHostsOnOneSegmentStillReadDifferently()
        {
            // Masking down to nothing would make the message worse at its job than the address it replaced.
            Assert.AreNotEqual(TikEndpointText.Describe(IPAddress.Parse("192.168.4.31")),
                               TikEndpointText.Describe(IPAddress.Parse("192.168.4.32")));
        }

        [TestMethod]
        public void TheSubnetDoesNotSurviveMasking()
        {
            // Two different networks, same host part: the output must not distinguish them, or the mask is
            // not masking anything.
            Assert.AreEqual(TikEndpointText.Describe(IPAddress.Parse("192.168.4.31")),
                            TikEndpointText.Describe(IPAddress.Parse("10.20.30.31")));
        }

        [TestMethod]
        public void AnIpv6AddressKeepsOnlyItsLastGroup()
        {
            string masked = TikEndpointText.Describe(IPAddress.Parse("fe80::215:5dff:fe04:1f03"));

            StringAssert.EndsWith(masked, "1f03");
            StringAssert.StartsWith(masked, "xx:");
            Assert.IsFalse(masked.Contains("fe80"), "the prefix names the network: " + masked);
            Assert.IsFalse(masked.Contains("5dff"), "a middle group survived: " + masked);
        }

        [TestMethod]
        public void AnIpv6ScopeIdIsDroppedBecauseItNamesALocalInterface()
        {
            Assert.IsFalse(TikEndpointText.Describe(IPAddress.Parse("fe80::1%12")).Contains("%"));
        }

        [TestMethod]
        public void LoopbackIsLeftAlone()
        {
            // Masking it would cost the reader the one thing the line was telling them — that the connection
            // never left the machine — and reveals nothing about anyone's network.
            Assert.AreEqual("127.0.0.1:8728",
                TikEndpointText.Describe(new IPEndPoint(IPAddress.Loopback, 8728)));
        }

        [TestMethod]
        public void AMissingEndpointIsDescribedRatherThanThrown()
        {
            // This runs while an exception message is being built; it must not fail there.
            Assert.AreEqual("unknown", TikEndpointText.Describe((EndPoint)null));
            Assert.AreEqual("unknown", TikEndpointText.Describe((IPAddress)null));
        }

        [TestMethod]
        public void AnEndpointThatIsNotAddressAndPortIsNotPassedThrough()
        {
            // Nothing is known about what such an endpoint's text contains, so it is named by family rather
            // than rendered — the alternative is a mask that silently does not apply.
            var other = new UnknownEndPoint();

            Assert.AreEqual(AddressFamily.Unix.ToString(), TikEndpointText.Describe(other));
        }

        private sealed class UnknownEndPoint : EndPoint
        {
            public override AddressFamily AddressFamily => AddressFamily.Unix;
            public override string ToString() => "/var/run/some.sock";
        }
    }
}
