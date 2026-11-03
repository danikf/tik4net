using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Mndp;

namespace tik4net.unittests
{
    /// <summary>
    /// <see cref="TikInstanceDescriptor.IpDescription"/> reads an address by its value, and a default descriptor can
    /// still describe itself.
    /// </summary>
    [TestClass]
    public class MndpInstanceDescriptorTests
    {
        private static TikInstanceDescriptor Router(IPAddress ipv4, string ipv6 = "")
            => new TikInstanceDescriptor("r", "7.24", "MikroTik", TimeSpan.Zero, "", "CHR", "", "AA:BB:CC:DD:EE:FF", ipv6, "ether1", ipv4);

        [TestMethod]
        public void AnAnnouncedZeroAddressIsNoAddress()
        {
            // The parser builds a new IPAddress from the four bytes, so it is equal to IPAddress.Any, not the same object.
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", Router(new IPAddress(new byte[4])).IpDescription);
            Assert.AreEqual("fe80::1", Router(new IPAddress(new byte[4]), "fe80::1").IpDescription);
        }

        [TestMethod]
        public void AnIpv4AddressDescribesTheRouter()
        {
            Assert.AreEqual("192.0.2.7", Router(IPAddress.Parse("192.0.2.7")).IpDescription);
        }

        [TestMethod]
        public void ADefaultDescriptorDescribesItselfWithoutThrowing()
        {
            Assert.IsNotNull(default(TikInstanceDescriptor).ToString());
        }
    }
}
