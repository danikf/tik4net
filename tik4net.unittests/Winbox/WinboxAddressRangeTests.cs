using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The <c>network</c> + <c>range:1</c> pair (start address + range-END sibling) that every firewall address
    /// field is built from (P2.33).
    /// </summary>
    /// <remarks>
    /// The rule pinned here is the ROUTER's, measured live on 7.23.2 and recorded in
    /// <c>Docs/winbox-native-m2-protocol.md</c> §24: RouterOS renders the stored pair by its SPAN, not by how
    /// the value was entered — a single host is bare (never <c>/32</c>), an aligned block is CIDR (even when it
    /// was entered as <c>a-b</c>), everything else is <c>a-b</c>. Rendering it any other way makes the same
    /// record read differently over WinBox native than over every other transport, which is the whole defect.
    /// </remarks>
    [TestClass]
    public class WinboxAddressRangeTests
    {
        // Addresses ride octet-LSB (192.0.2.74 → 0x4A0200C0), so these are the wire values, not host order.
        private static uint Ip(string s) => WinboxFieldResolver.PackIpV4(s).Value;

        private static string Format(string start, string end)
            => WinboxFieldResolver.FormatV4Range(Ip(start), Ip(end));

        // ── decode: pair → RouterOS text ──────────────────────────────────────

        [TestMethod]
        public void ASingleHostRendersBare()
        {
            // The filed P2.33 symptom was "192.0.2.74/32"; what it actually produced was "/6", because the end
            // address was being bit-counted as if it were a netmask. Both are wrong: the API says "192.0.2.74".
            Assert.AreEqual("192.0.2.74", Format("192.0.2.74", "192.0.2.74"));
        }

        [TestMethod]
        public void AnAlignedBlockRendersAsCidr()
        {
            Assert.AreEqual("192.0.2.0/24", Format("192.0.2.0", "192.0.2.255"));
            Assert.AreEqual("192.0.2.0/30", Format("192.0.2.0", "192.0.2.3"));
            Assert.AreEqual("10.0.0.0/8", Format("10.0.0.0", "10.255.255.255"));
            Assert.AreEqual("0.0.0.0/0", Format("0.0.0.0", "255.255.255.255"));
        }

        [TestMethod]
        public void AnUnalignedSpanRendersAsStartDashEnd()
        {
            Assert.AreEqual("192.0.2.10-192.0.2.20", Format("192.0.2.10", "192.0.2.20"));
            // Right size, wrong alignment: 4 addresses, but not on a /30 boundary.
            Assert.AreEqual("192.0.2.1-192.0.2.4", Format("192.0.2.1", "192.0.2.4"));
        }

        [TestMethod]
        public void SpanArithmeticIsOctetOrderAware()
        {
            // The guard for the packing trap: compared in raw octet-LSB order, 192.0.2.255 (0xFF0200C0) is
            // LARGER than 192.0.3.0 (0x000300C0) only by accident, and 192.0.2.0's span comes out negative.
            // A /24 whose last octet spans 0..255 is the case that exposes it.
            Assert.AreEqual("192.0.2.0/24", Format("192.0.2.0", "192.0.2.255"));
            Assert.AreEqual("192.0.2.255-192.0.3.0", Format("192.0.2.255", "192.0.3.0"));
        }

        [TestMethod]
        public void AnInvertedPairIsNotInventedIntoABlock()
        {
            // end < start is nonsense the router should never send; render it literally rather than computing
            // a prefix from an underflowed span.
            Assert.AreEqual("192.0.2.20-192.0.2.10", Format("192.0.2.20", "192.0.2.10"));
        }

        // ── encode: RouterOS text → pair ──────────────────────────────────────

        [TestMethod]
        public void AllThreeFormsParseIntoTheStartEndPair()
        {
            Assert.IsTrue(WinboxFieldResolver.TryParseV4Range("192.0.2.74", out uint s1, out uint e1));
            Assert.AreEqual(Ip("192.0.2.74"), s1);
            Assert.AreEqual(Ip("192.0.2.74"), e1, "a host must send end=start, not an open-ended range");

            Assert.IsTrue(WinboxFieldResolver.TryParseV4Range("192.0.2.10-192.0.2.20", out uint s2, out uint e2));
            Assert.AreEqual(Ip("192.0.2.10"), s2);
            Assert.AreEqual(Ip("192.0.2.20"), e2);

            Assert.IsTrue(WinboxFieldResolver.TryParseV4Range("192.0.2.0/24", out uint s3, out uint e3));
            Assert.AreEqual(Ip("192.0.2.0"), s3);
            Assert.AreEqual(Ip("192.0.2.255"), e3, "a CIDR block sends its LAST address as the end");
        }

        [TestMethod]
        public void ACidrValueIsMaskedDownToItsBlock()
        {
            // "192.0.2.74/24" names the block, not the host — the same normalisation the router does.
            Assert.IsTrue(WinboxFieldResolver.TryParseV4Range("192.0.2.74/24", out uint s, out uint e));
            Assert.AreEqual(Ip("192.0.2.0"), s);
            Assert.AreEqual(Ip("192.0.2.255"), e);
        }

        [TestMethod]
        public void ASlashThirtyTwoCollapsesToAHost()
        {
            Assert.IsTrue(WinboxFieldResolver.TryParseV4Range("192.0.2.74/32", out uint s, out uint e));
            Assert.AreEqual(s, e);
            Assert.AreEqual("192.0.2.74", WinboxFieldResolver.FormatV4Range(s, e));
        }

        [TestMethod]
        public void EveryFormSurvivesARoundTrip()
        {
            foreach (string text in new[]
                     { "192.0.2.74", "192.0.2.0/24", "192.0.2.0/30", "10.0.0.0/8", "192.0.2.10-192.0.2.20" })
            {
                Assert.IsTrue(WinboxFieldResolver.TryParseV4Range(text, out uint s, out uint e), text);
                Assert.AreEqual(text, WinboxFieldResolver.FormatV4Range(s, e), $"round trip of '{text}'");
            }
        }

        [TestMethod]
        public void ANonIpValueIsRefusedSoTheCallerCanFallThrough()
        {
            Assert.IsFalse(WinboxFieldResolver.TryParseV4Range("example.com", out _, out _));
            Assert.IsFalse(WinboxFieldResolver.TryParseV4Range("2001:db8::1", out _, out _));
            Assert.IsFalse(WinboxFieldResolver.TryParseV4Range("", out _, out _));
        }
    }
}
