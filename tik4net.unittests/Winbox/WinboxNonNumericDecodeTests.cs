using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// What the WinBox-native decode path does with a value it cannot read as a number (P2.25).
    /// </summary>
    /// <remarks>
    /// The old code answered every one of these with <c>0</c>, and <c>0</c> is a legal address, a legal netmask
    /// and a legal enum member — so an unreadable value reached the caller looking exactly like something the
    /// router had said. These tests pin the two honest answers instead: <b>throw</b> where the fallback would be
    /// indistinguishable from a real value (addresses, netmasks, ranges), and <b>fall back to the raw wire text</b>
    /// where it is visibly not a decoded value (enum labels, flag sets, references). Nothing here needs a router:
    /// the inputs are the CLR values <c>M2Message</c> hands the codec.
    /// </remarks>
    [TestClass]
    public class WinboxNonNumericDecodeTests
    {
        // The shapes M2Message can actually produce for a field: raw bytes (FT_ADDR6/MAC/raw), a string, a
        // nested submessage, and null. None of them is convertible to a number.
        private static IEnumerable<object> NonNumericValues()
        {
            yield return new byte[] { 1, 2, 3, 4 };
            yield return "ether1";
            yield return new Dictionary<int, Tuple<string, object>>();
            yield return null;
        }

        [TestMethod]
        public void TryToInt64_AcceptsEveryNumericWireShape()
        {
            foreach (object numeric in new object[] { (uint)3232235777, 42, (long)42, (byte)7, "123", true })
            {
                Assert.IsTrue(WinboxFieldResolver.TryToInt64(numeric, out _),
                    $"'{numeric}' ({numeric.GetType().Name}) must still read as a number");
            }
        }

        [TestMethod]
        public void TryToInt64_ReportsFailureInsteadOfSubstitutingZero()
        {
            foreach (object v in NonNumericValues())
            {
                Assert.IsFalse(WinboxFieldResolver.TryToInt64(v, out long parsed),
                    $"'{v}' must not read as a number");
                Assert.AreEqual(0L, parsed, "the out value stays 0, which is precisely why the bool must be checked");
            }
        }

        [TestMethod]
        public void AnUnreadableAddressThrowsInsteadOfRendering_0_0_0_0()
        {
            foreach (object v in NonNumericValues())
            {
                var ex = Assert.ThrowsException<WinboxFieldResolutionException>(
                    () => WinboxFieldResolver.IpFromU32(v),
                    $"'{v}' used to decode as the perfectly plausible address 0.0.0.0");
                StringAssert.Contains(ex.Message, "IPv4 address");
            }
        }

        [TestMethod]
        public void AnUnreadableNetmaskThrowsInsteadOfRenderingPrefixZero()
        {
            // /0 is not a nonsense prefix — it is the default route's — so this fallback was unfalsifiable
            // by inspection of the result.
            foreach (object v in NonNumericValues())
                Assert.ThrowsException<WinboxFieldResolutionException>(() => WinboxFieldResolver.MaskToPrefix(v));
        }

        [TestMethod]
        public void AnUnreadableRangeEndThrowsInsteadOfCollapsingToAHost()
        {
            // Both halves matter: with end silently 0 the pair rendered as a bare host or an enormous block,
            // depending on which half was unreadable.
            uint valid = WinboxFieldResolver.PackIpV4("192.0.2.10").Value;
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => WinboxFieldResolver.FormatV4Range(valid, "not-a-number"));
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => WinboxFieldResolver.FormatV4Range("not-a-number", valid));
        }

        [TestMethod]
        public void AValidAddressStillDecodes()
        {
            // The guard must not have narrowed what a good value can be: octet-LSB u32, and the boxed forms
            // M2Message may hand over.
            uint packed = WinboxFieldResolver.PackIpV4("192.168.1.1").Value;
            Assert.AreEqual("192.168.1.1", WinboxFieldResolver.IpFromU32(packed));
            Assert.AreEqual("192.168.1.1", WinboxFieldResolver.IpFromU32((long)packed));
            Assert.AreEqual(24, WinboxFieldResolver.MaskToPrefix(WinboxFieldResolver.PackIpV4("255.255.255.0").Value));
        }

        // ── the fall-back-to-raw half ─────────────────────────────────────────

        private static Dictionary<string, string> Decode(WinboxJgField field, object value)
        {
            var codec = new WinboxRecordCodec(null, null);
            var rec = new Dictionary<int, Tuple<string, object>> { [field.Key] = Tuple.Create("u32", value) };
            return codec.DecodeRecord(rec,
                new Dictionary<int, string> { [field.Key] = field.ApiName },
                new Dictionary<int, WinboxJgField> { [field.Key] = field });
        }

        [TestMethod]
        public void AnUnreadableEnumValueKeepsTheRawTextRatherThanTheZeroLabel()
        {
            // The zero label is the trap: on a typical .jg map index 0 is "none"/"off"/"disabled", so a value
            // the codec could not read used to come back as a confident "off".
            var map = new Dictionary<int, string> { [0] = "off", [1] = "on" };
            var field = new WinboxJgField("state", 0x10001, "u32", false, enumMap: map, uiType: "enm");

            Assert.AreEqual("ether1", Decode(field, "ether1")["state"]);
            Assert.AreEqual("on", Decode(field, 1)["state"], "a readable value must still map to its label");
        }

        [TestMethod]
        public void AnUnreadableFlagSetKeepsTheRawTextRatherThanAnEmptySet()
        {
            // An empty label list reads as "no flags set", which is a meaningful firewall answer.
            var map = new Dictionary<int, string> { [0] = "established", [1] = "related" };
            var field = new WinboxJgField("connection-state", 0x10002, "u32", false, enumMap: map, uiType: "set");

            Assert.AreEqual("not-a-number", Decode(field, "not-a-number")["connection-state"]);
            Assert.AreEqual("established,related", Decode(field, 3)["connection-state"]);
        }

        [TestMethod]
        public void AnUnreadableIdKeepsTheRawTextRatherThanClaimingHandleZero()
        {
            // "*0" is a real RouterOS handle; returning it for an id we could not read would point every
            // subsequent set/remove at whatever row *0 happens to be.
            var codec = new WinboxRecordCodec(null, null);
            var keyToName = new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Id] = TikSpecialProperties.Id };

            var decoded = codec.DecodeRecord(
                new Dictionary<int, Tuple<string, object>>
                { [WinboxM2Protocol.RecordKey.Id] = Tuple.Create("raw", (object)new byte[] { 9 }) },
                keyToName, null);
            Assert.AreNotEqual("*0", decoded[TikSpecialProperties.Id]);

            var ok = codec.DecodeRecord(
                new Dictionary<int, Tuple<string, object>>
                { [WinboxM2Protocol.RecordKey.Id] = Tuple.Create("u32", (object)26u) },
                keyToName, null);
            Assert.AreEqual("*1A", ok[TikSpecialProperties.Id]);
        }
    }
}
