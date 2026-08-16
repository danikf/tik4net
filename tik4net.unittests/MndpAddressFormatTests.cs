using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Mndp;

namespace tik4net.unittests
{
    /// <summary>
    /// The MNDP address TLV carries address BYTES, not text.
    /// </summary>
    /// <remarks>
    /// It sat next to a run of genuinely textual fields (identity, version, board name) and was decoded the
    /// same way, so <c>fe80::215:5dff:fe04:1f03</c> arrived as sixteen latin-1 characters. Nothing displayed
    /// the field until MNDP got an MCP tool of its own, and the garbage appeared in the first live call —
    /// which is also why it is worth a test rather than a quiet fix: the same mistake reads as plausible
    /// every time it is made.
    /// </remarks>
    [TestClass]
    public class MndpAddressFormatTests
    {
        [TestMethod]
        public void SixteenBytesAreAnIpv6Address()
        {
            // The live CHR's link-local address, exactly as it arrives on the wire.
            byte[] raw = { 0xFE, 0x80, 0, 0, 0, 0, 0, 0, 0x02, 0x15, 0x5D, 0xFF, 0xFE, 0x04, 0x1F, 0x03 };

            Assert.AreEqual("fe80::215:5dff:fe04:1f03", MndpHelper.FormatIpAddress(raw));
        }

        [TestMethod]
        public void FourBytesAreAnIpv4Address()
        {
            Assert.AreEqual("192.168.4.236", MndpHelper.FormatIpAddress(new byte[] { 192, 168, 4, 236 }));
        }

        [TestMethod]
        public void AnythingElseIsEmptyRatherThanGarbage()
        {
            // A length no address has means the field was not understood. Empty says that; a latin-1
            // rendering of the bytes claims to be an address and is not.
            Assert.AreEqual("", MndpHelper.FormatIpAddress(new byte[] { 1, 2, 3 }));
            Assert.AreEqual("", MndpHelper.FormatIpAddress(new byte[0]));
            Assert.AreEqual("", MndpHelper.FormatIpAddress(null));
        }

        [TestMethod]
        public void TheFormattedAddressParsesBack()
        {
            byte[] raw = { 0xFE, 0x80, 0, 0, 0, 0, 0, 0, 0x02, 0x15, 0x5D, 0xFF, 0xFE, 0x04, 0x1F, 0x03 };

            IPAddress parsed = IPAddress.Parse(MndpHelper.FormatIpAddress(raw));

            CollectionAssert.AreEqual(raw, parsed.GetAddressBytes());
        }
    }
}
