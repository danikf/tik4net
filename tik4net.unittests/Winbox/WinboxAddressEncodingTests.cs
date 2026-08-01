using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The webfig <c>addr</c> compound and the <c>FT_ADDR6</c> field type (P2.52/P2.53).
    /// </summary>
    /// <remarks>
    /// These are pure codec rules taken from <c>master*.js</c> (<c>types.addr.fromstr</c>/<c>tostr</c>,
    /// <c>string2ip6addr</c>, and the <c>msg2buffer</c> field-type table), so they belong here rather than in
    /// the router-bound suite. What they pin down is the thing that was silently wrong: an address that is not
    /// a bare IPv4 literal must ride at its OWN sub-key, and an IPv6 value is its own field type — sending
    /// either as a plain string or as <c>raw</c> makes the router ignore the field and answer as though it had
    /// never been sent.
    /// </remarks>
    [TestClass]
    public class WinboxAddressEncodingTests
    {
        // ── FT_ADDR6 on the wire ───────────────────────────────────────────────

        [TestMethod]
        public void Addr6FieldIsSixteenBytesWithNoLengthPrefix()
        {
            byte[] f = M2Message.Addr6Sys(0xFEFF21, WinboxFieldResolver.PackIpV6("::1"));

            Assert.AreEqual(4 + 16, f.Length, "key(3) + type(1) + 16 address bytes, no length byte");
            Assert.AreEqual(0x21, f[0]); Assert.AreEqual(0xFF, f[1]); Assert.AreEqual(0xFE, f[2]);
            Assert.AreEqual(0x18, f[3], "FT_ADDR6 = ftype 3 << 3");
            Assert.AreEqual(1, f[f.Length - 1]);
            Assert.IsTrue(f.Skip(4).Take(15).All(b => b == 0));
        }

        [TestMethod]
        public void AnAddr6FieldDoesNotDesyncTheFieldsAfterIt()
        {
            // The regression this guards: an unknown type byte made SkipTypeBytes return 0, so the parser read
            // the ADDRESS bytes as the next key+type and every later field decoded into garbage. A traceroute
            // hop came back as an empty submessage because of exactly this.
            var m2 = new[] { (byte)'M', (byte)'2' }
                .Concat(M2Message.U32Sys(0x10, 4242))
                .Concat(M2Message.Addr6Sys(0x11, WinboxFieldResolver.PackIpV6("2001:db8::1")))
                .Concat(M2Message.StringSys(0x12, "after"))
                .ToArray();

            var fields = M2Message.ParseAllFields(m2);

            Assert.AreEqual(3, fields.Count, "the field after the address must still parse");
            Assert.AreEqual((uint)4242, fields[0x10].Item2);
            Assert.AreEqual("ip6", fields[0x11].Item1);
            Assert.AreEqual("2001:db8::1", WinboxFieldResolver.IpV6FromBytes((byte[])fields[0x11].Item2));
            Assert.AreEqual("after", fields[0x12].Item2);
        }

        [TestMethod]
        public void EveryFieldTypeHasASkipRule()
        {
            // ftype << 3 | size flags, for the ftype table in master.js. A missing case returns 0 and desyncs
            // the rest of the message, which is silent — so the guard is that NO type byte returns 0 for a
            // payload that plainly has bytes.
            var counted = new byte[] { 0x80, 0x90, 0x98, 0x88 };   // bool[] u64[] addr6[] u32[]
            var data = new byte[] { 0x01, 0x00 }                    // count = 1
                .Concat(Enumerable.Repeat((byte)0xAB, 16)).ToArray();
            foreach (byte t in counted)
                Assert.IsTrue(M2Message.SkipTypeBytes(t, data, 0) > 2, $"type 0x{t:X2} must skip its element");

            Assert.AreEqual(16, M2Message.SkipTypeBytes(0x18, data, 0), "addr6 is a fixed 16 bytes");
        }

        // ── IPv6 text ↔ bytes ──────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("::1")]
        [DataRow("2001:db8::1")]
        [DataRow("fe80::1")]
        [DataRow("2001:db8:85a3::8a2e:370:7334")]
        public void IpV6RoundTripsThroughBytes(string text)
        {
            byte[] bytes = WinboxFieldResolver.PackIpV6(text);
            Assert.IsNotNull(bytes, text);
            Assert.AreEqual(16, bytes.Length);
            Assert.AreEqual(text, WinboxFieldResolver.IpV6FromBytes(bytes));
        }

        [TestMethod]
        public void AnIpV4MappedAddressReadsBackAsTheDottedQuad()
        {
            // What a traceroute hop arrives as. The binary API calls this hop 127.0.0.1, so the native
            // transport must not call it ::ffff:127.0.0.1 — same record, same name, per the parity rule.
            byte[] mapped = WinboxFieldResolver.PackIpV6("::ffff:127.0.0.1");

            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 127, 0, 0, 1 }, mapped);
            Assert.AreEqual("127.0.0.1", WinboxFieldResolver.IpV6FromBytes(mapped));
        }

        [DataTestMethod]
        [DataRow("192.168.1.1")]
        [DataRow("not-an-address")]
        [DataRow("")]
        [DataRow("1:2:3:4:5:6:7:8:9")]
        [DataRow("::1::2")]
        public void PackIpV6RejectsWhatIsNotAnIpV6Address(string text)
        {
            Assert.IsNull(WinboxFieldResolver.PackIpV6(text));
        }

        // ── the addr compound: one sub-key per address form ────────────────────

        private static System.Collections.Generic.Dictionary<int, Tuple<string, object>> EncodeAddrField(
            string value, string allow)
        {
            byte[] body = new[] { (byte)'M', (byte)'2' }
                .Concat(WinboxFieldResolver.EncodeAddr(value, allow, "address", "/ping").SelectMany(f => f))
                .ToArray();
            return M2Message.ParseAllFields(body);
        }

        private static int FirstSubKey(string value, string allow)
            => EncodeAddrField(value, allow).Keys.First();

        [DataTestMethod]
        [DataRow("127.0.0.1", "46v%Dm", 0xFEFF20)]      // IPv4 → ufeff20
        [DataRow("::1", "46v%Dm", 0xFEFF21)]            // IPv6 → afeff21
        [DataRow("example.com", "46v%Dm", 0xFEFF26)]    // DNS name → sfeff26
        [DataRow("AA:BB:CC:DD:EE:FF", "46m", 0xFEFF2F)] // MAC → rfeff2f
        public void EachAddressFormRidesAtItsOwnSubKey(string value, string allow, int expectedSubKey)
        {
            Assert.AreEqual(expectedSubKey, FirstSubKey(value, allow));
        }

        [TestMethod]
        public void AHostNameIsRefusedByAFieldThatDoesNotAcceptOne()
        {
            // allow:'46' is address-only. Before P2.53 an unencodable value fell back to a bare string at the
            // field key, which the router silently ignores — the caller was told the command had succeeded.
            var ex = Assert.ThrowsException<WinboxFieldValueException>(
                () => EncodeAddrField("example.com", "46"));
            Assert.IsTrue(ex.Message.Contains("address"), ex.Message);
        }

        [TestMethod]
        public void APrefixLengthRidesAlongsideTheAddress()
        {
            var sub = EncodeAddrField("10.0.0.0/8", "46/");

            Assert.IsTrue(sub.ContainsKey(0xFEFF20), "the address");
            Assert.AreEqual((byte)8, Convert.ToByte(sub[0xFEFF25].Item2), "the /len at ufeff25");
        }

        [TestMethod]
        public void AnInterfaceQualifierIsRefusedRatherThanDropped()
        {
            // "fe80::1%ether1" without its zone is a DIFFERENT destination, so dropping the qualifier would
            // quietly address something else.
            var ex = Assert.ThrowsException<WinboxFieldResolutionException>(
                () => EncodeAddrField("fe80::1%ether1", "46i"));
            Assert.IsTrue(ex.Message.Contains("%"), ex.Message);
        }
    }
}
