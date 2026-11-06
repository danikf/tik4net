#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// What <see cref="TikMacText"/> and <see cref="TikHexText"/> make of each spelling, through the places that use
    /// them: the address parser (strict), WinBox native's MAC parser and its raw/macaddr encoder (lenient), the WinBox MAC
    /// renderer, the hex reader and the schema cache's file-name hash. The M2 trace hex and the MNDP MAC are pinned by
    /// <c>M2MessageGoldenTests</c> and <c>MndpDiscoverTests</c>.
    /// </summary>
    [TestClass]
    public class MacAndHexTextTests
    {
        private static string Hex(byte[]? bytes) => bytes == null ? "<null>" : BitConverter.ToString(bytes).Replace("-", "");

        // ── The address parser: strict ────────────────────────────────────────

        [DataTestMethod]
        [DataRow("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
        [DataRow("aa-bb-cc-dd-ee-0f", "AA:BB:CC:DD:EE:0F")]
        public void TheAddressParser_ReadsSixPairsAndWritesUpperCaseColons(string text, string expected)
        {
            var address = TikRouterAddress.Parse(text);
            Assert.IsTrue(address.HasMac);
            Assert.AreEqual(expected, address.Mac);
        }

        [DataTestMethod]
        [DataRow("1:2:3:4:5:6")]
        [DataRow("AABBCCDDEEFF")]
        [DataRow("AA:BB-CC:DD:EE:FF")]
        [DataRow("AA:BB:CC:DD:EE:GG")]
        [DataRow("AA:BB:CC:DD:EE:FF:")]
        public void TheAddressParser_TakesAnythingElseForAHost(string text)
        {
            Assert.IsFalse(TikRouterAddress.Parse(text).HasMac);
            Assert.ThrowsException<ArgumentException>(() => TikRouterAddress.FromMac(text));
        }

        // ── WinBox native's MAC parser: lenient ───────────────────────────────

        [DataTestMethod]
        [DataRow("AA:BB:CC:DD:EE:FF", "AABBCCDDEEFF")]
        [DataRow("aa:bb:cc:dd:ee:ff", "AABBCCDDEEFF")]
        [DataRow("1:2:3:4:5:6", "010203040506")]
        [DataRow("AA-BB-CC-DD-EE-FF", "AABBCCDDEEFF")]
        [DataRow("1-2-3-4-5-6", "010203040506")]
        [DataRow("aabbccddeeff", "AABBCCDDEEFF")]
        public void TheWinboxParser_ReadsWhatTheRouterAccepts(string text, string expected)
        {
            Assert.IsTrue(TikMacText.TryParse(text, lenient: true, out byte[]? mac));
            Assert.AreEqual(expected, Hex(mac));
        }

        [DataTestMethod]
        [DataRow("AA:BB-CC:DD:EE:FF")]
        [DataRow("AABBCCDDEEF")]
        [DataRow("AABBCCDDEEFF00")]
        [DataRow("1:2:3:4:5")]
        [DataRow("1:2:3:4:5:6:7")]
        [DataRow("100:2:3:4:5:6")]
        [DataRow(" 1:2:3:4:5:6")]
        [DataRow("001:2:3:4:5:6")]
        [DataRow("G:2:3:4:5:6")]
        [DataRow("::::5:6")]
        [DataRow("")]
        public void TheWinboxParser_RefusesAnythingElse(string text)
        {
            Assert.IsFalse(TikMacText.TryParse(text, lenient: true, out byte[]? mac));
            Assert.IsNull(mac);
        }

        // ── WinBox native's macaddr encoder ───────────────────────────────────

        private const string MacWindow =
            "[{name:'DHCP Server',title:'DHCP Server',group:'IP',c:[" +
            "{name:'DHCP Network',title:'Networks',type:'map',path:[ 20,6 ],c:[" +
              "{name:'Admin MAC',type:'macaddr',id:'R7'}," +
              "{name:'Valid Servers',type:'multiraw',id:'R2',c:[{type:'macaddr'}]}]}" +
            "]}]";

        private static string EncodedMac(string value, string apiName = "admin-mac")
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(MacWindow), "the trimmed window must parse");
            var resolver = new WinboxFieldResolver("/ip/dhcp-server/network", new[] { 20, 6 }, catalog, new Dictionary<string, int>());
            var encoded = resolver.EncodeField(apiName, value);
            Assert.AreEqual(1, encoded.Count, "expected exactly one encoded field");
            var field = M2Message.ParseAllFields(M2Message.BuildM2(encoded.ToArray())).Single();
            return field.Value.TraceName + " " + Convert.ToString(field.Value.Value);
        }

        [DataTestMethod]
        [DataRow("AA:BB:CC:DD:EE:FF", "AABBCCDDEEFF")]
        [DataRow("aa-bb-cc-dd-ee-ff", "AABBCCDDEEFF")]
        [DataRow("AABBCCDDEEFF", "AABBCCDDEEFF")]
        [DataRow("AA BB CC DD EE FF", "AABBCCDDEEFF")]
        [DataRow("1:2:3:4:5:6", "010203040506")]
        [DataRow("1-2-3-4-5-6", "010203040506")]
        [DataRow("xyz", "78797A")]
        [DataRow("ABC", "414243")]
        public void TheMacaddrEncoder_ReadsAMacAndFallsBackToTheText(string value, string expected)
        {
            Assert.AreEqual("raw " + expected, EncodedMac(value));
        }

        [TestMethod]
        public void AMacListReadsEachElementAsAMac()
        {
            Assert.AreEqual("raw[] [010203040506,AABBCCDDEEFF]", EncodedMac("1:2:3:4:5:6,AA:BB:CC:DD:EE:FF", "valid-servers"));
        }

        // ── WinBox native's MAC renderer ──────────────────────────────────────

        [TestMethod]
        public void TheWinboxRenderer_WritesBytesAsUpperCaseColons()
        {
            Assert.AreEqual("AA:0B:CC:DD:EE:FF", WinboxValueFormat.MacFromBytes(new byte[] { 0xAA, 0x0B, 0xCC, 0xDD, 0xEE, 0xFF }));
            Assert.AreEqual("01:02", WinboxValueFormat.MacFromBytes(new byte[] { 1, 2 }));
        }

        [DataTestMethod]
        [DataRow("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
        [DataRow("AABB", "AA:BB")]
        [DataRow("AA:BB", "AA:BB")]
        [DataRow("ABC", "ABC")]
        [DataRow("xyz", "xyz")]
        [DataRow("", "")]
        public void TheWinboxRenderer_SplitsUnseparatedHexAndLeavesAnythingElse(string value, string expected)
        {
            Assert.AreEqual(expected, WinboxValueFormat.MacFromBytes(value));
        }

        [TestMethod]
        public void TheWinboxRenderer_WritesAnEmptyByteArrayAsEmpty()
        {
            // An empty raw is no address at all; it must not reach the caller as object.ToString()'s "System.Byte[]".
            Assert.AreEqual("", WinboxValueFormat.MacFromBytes(new byte[0]));
        }

        // ── Hex ───────────────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("0A0b", "0A0B")]
        [DataRow("", "<null>")]
        [DataRow("ABC", "<null>")]
        [DataRow("GG", "<null>")]
        [DataRow(" A", "<null>")]
        public void TheHexDecoder_ReadsPairs(string text, string expected)
        {
            TikHexText.TryParse(text, out byte[]? bytes);
            Assert.AreEqual(expected, Hex(bytes));
        }

        [DataTestMethod]
        [DataRow("", true)]
        [DataRow("09afAF", true)]
        [DataRow("0x", false)]
        [DataRow(" A", false)]
        public void TheHexPredicate_AcceptsOnlyHexDigits(string text, bool expected)
        {
            Assert.AreEqual(expected, TikHexText.IsHex(text));
        }

        [TestMethod]
        public void TheSchemaCacheName_IsTheFirstFourHashBytesInLowerCase()
        {
            string path = CliSchemaStore.PathFor("cache", "7.24.4|x86_64|routeros");
            Assert.AreEqual("0462044f.json", System.IO.Path.GetFileName(path));
        }
    }
}
