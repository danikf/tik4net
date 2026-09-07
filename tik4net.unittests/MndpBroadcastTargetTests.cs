using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Mndp;

namespace tik4net.unittests
{
    /// <summary>
    /// Discovery solicits each local interface at its own SUBNET broadcast address, and this is the
    /// arithmetic that produces it.
    /// </summary>
    /// <remarks>
    /// The solicitation used to go to <see cref="IPAddress.Broadcast"/> on an unbound socket, so the host's
    /// routing table chose one interface and every other segment was invisible until a router there
    /// happened to broadcast on its own cycle — on a machine with a VM switch or a VPN, discovery returned a
    /// short list and the tooling blamed the host firewall. Sending from a bound socket needs a directed
    /// address per interface, so a wrong mask here does not fail loudly: it sends to the wrong segment, and
    /// the symptom is again "no routers found".
    /// </remarks>
    [TestClass]
    public class MndpBroadcastTargetTests
    {
        [TestMethod]
        public void AByteAlignedMaskSetsTheHostBits()
        {
            Assert.AreEqual("192.168.4.255",
                MndpHelper.SubnetBroadcast(IPAddress.Parse("192.168.4.31"),
                                           IPAddress.Parse("255.255.255.0"))?.ToString());
        }

        [TestMethod]
        public void AMaskInsideAByteIsNotRoundedToTheByte()
        {
            // /20, the width Hyper-V's default switch hands out. Rounding this to /16 or /24 addresses a
            // segment the interface is not on, which is silent: nothing answers and nothing complains.
            Assert.AreEqual("172.24.79.255",
                MndpHelper.SubnetBroadcast(IPAddress.Parse("172.24.64.1"),
                                           IPAddress.Parse("255.255.240.0"))?.ToString());
        }

        [TestMethod]
        public void APointToPointMaskBroadcastsToTheAddressItself()
        {
            // /32 has no host bits, so the "broadcast" is the address. Worth pinning because it is the case
            // an implementation is most likely to special-case into something else.
            Assert.AreEqual("10.0.0.7",
                MndpHelper.SubnetBroadcast(IPAddress.Parse("10.0.0.7"),
                                           IPAddress.Parse("255.255.255.255"))?.ToString());
        }

        [TestMethod]
        public void AnEmptyMaskIsNotAnInterfaceToSolicit()
        {
            // 0.0.0.0 would yield 255.255.255.255 — the limited broadcast, whose routing is the very thing
            // binding per interface exists to stop depending on. Refusing the interface is the honest answer.
            Assert.IsNull(MndpHelper.SubnetBroadcast(IPAddress.Parse("192.168.4.31"),
                                                     IPAddress.Parse("0.0.0.0")));
        }

        [TestMethod]
        public void AnIpv6AddressHasNoIpv4Broadcast()
        {
            Assert.IsNull(MndpHelper.SubnetBroadcast(IPAddress.Parse("fe80::215:5dff:fe04:1f03"),
                                                     IPAddress.Parse("255.255.255.0")));
        }

        [TestMethod]
        public void AMissingAddressOrMaskIsRefusedRatherThanThrowing()
        {
            // GetIPProperties() reports interfaces whose IPv4Mask is null; that is a NIC to skip, not a crash
            // in the middle of enumerating the others.
            Assert.IsNull(MndpHelper.SubnetBroadcast(IPAddress.Parse("192.168.4.31"), null));
            Assert.IsNull(MndpHelper.SubnetBroadcast(null, IPAddress.Parse("255.255.255.0")));
        }
    }
}
