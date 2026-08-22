// WinboxNegatedElementAndPairListTests.cs — router-free tests for the last two list shapes: an element the
// GUI wraps in a negation flag, and a list whose element is an address PAIR.
//
// A `not` wrapper is not a value. {type:'not',id:'b1',c:[…]} carries an id of its own, so the flag rides
// INSIDE the element's submessage beside the value, and types.not.tostr renders the pair as
// (flag ? '!' : '') + the inner type's own text. Before this change the wrapper had no addressable parts at
// all: /tool/sniffer's filter-ip-address read as "true,false" — the two flags where the API prints the two
// addresses — and no write was possible.
//
// A `multinetwork` is a list of (address, sibling) pairs. Where the pairs live is the FIELD's business (two
// parallel arrays when it has a maskid, one flattened array when it has not) and what the second half means
// is the ELEMENT's (a range end under range:1, a netmask otherwise).
//
// The .jg fragments are the real declarations from RouterOS 7.24, cut to the fields under test.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxNegatedElementAndPairListTests
    {
        private static readonly int[] SnifferHandler = { 45, 1 };
        private static readonly int[] PoolHandler = { 84, 2 };
        private static readonly int[] TemplateHandler = { 119, 9 };

        // roteros.jg, Packet Sniffer Settings [45,1]. Two fields are labelled 'Port': the streaming port on
        // the Streaming tab and the filter list on the Filter tab. Every filter element is `not`-wrapped.
        private const string SnifferWindow =
            "[{name:'Packet Sniffer',title:'Packet Sniffer',group:'Tools',c:[" +
            "{title:'Packet Sniffer Settings',type:'item',path:[ 45,1 ],c:[" +
              "{name:'Streaming',type:'tab'}," +
              "{name:'Server',type:'ipaddr',id:'u8'}," +
              "{name:'Port',type:'number',id:'u9',max:65535,min:1}," +
              "{name:'Filter',type:'tab'}," +
              "{name:'MAC Address',type:'multi',id:'Mc9',max:16,c:[{type:'not',id:'b1',c:[" +
                 "{type:'macnetwork',id:'r2',maskid:'r3'}]}]}," +
              "{name:'IP Address',type:'multi',id:'Mcc',max:16,c:[{type:'not',id:'b1',c:[" +
                 "{type:'network',id:'u2',maskid:'u3'}]}]}," +
              "{name:'Port',type:'multi',id:'Mcd',max:16,c:[{type:'not',id:'b1',c:[" +
                 "{type:'number',id:'u2',values:{type:'static',map:{80:'http',443:'https'}}}]}]}]}" +
            "]}]";

        // roteros.jg, IP Pool [84,2]: a multinetwork with NO maskid whose element declares range:1 — the
        // pairs are flattened into one array and the second half of each is the range END.
        private const string PoolWindow =
            "[{name:'Pools',title:'Pools',group:'IP',c:[" +
            "{name:'IP Pool',title:'Pools',type:'map',path:[ 84,2 ],nameval:'Name',c:[" +
              "{name:'Name',type:'string',id:'sa',min:1}," +
              "{name:'Addresses',type:'multinetwork',id:'Ub',min:1,c:[{type:'network',range:1}]}]}" +
            "]}]";

        // roteros.jg, Packet Template [119,9]: the same list WITH a maskid — the two halves ride in two
        // parallel arrays — plus the MAC flavour, whose halves are six bytes each.
        private const string TemplateWindow =
            "[{name:'Traffic Generator',title:'Traffic Generator',group:'Tools',c:[" +
            "{name:'Packet Template',title:'Packet Templates',type:'map',path:[ 119,9 ],c:[" +
              "{name:'Src.',type:'multinetwork',id:'Ud3',maskid:'Ude',max:16,optid:'b137'," +
                 "c:[{type:'network',range:1}]}," +
              "{name:'Dst.',type:'multimacnetwork',id:'Rc8',maskid:'Rdc',max:16,optid:'b12c'," +
                 "c:[{type:'macnetwork'}]}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(string body, string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, Parse(body), new Dictionary<string, int>());

        private static Dictionary<int, object> Fields(IList<byte[]> encoded)
            => M2Message.ParseAllFields(M2Message.BuildM2(encoded.ToArray()))
                        .ToDictionary(kv => kv.Key, kv => kv.Value.Item2);

        private static List<Dictionary<int, object>> Elements(IList<byte[]> encoded, int expectedKey)
        {
            var fields = M2Message.ParseAllFields(M2Message.BuildM2(encoded.ToArray()));
            Assert.IsTrue(fields.ContainsKey(expectedKey), "the list field itself");
            Assert.AreEqual("msg[]", fields[expectedKey].Item1);
            return ((List<Dictionary<int, Tuple<string, object>>>)fields[expectedKey].Item2)
                .Select(e => e.ToDictionary(kv => kv.Key, kv => kv.Value.Item2))
                .ToList();
        }

        private static long Num(object wireValue) => Convert.ToInt64(wireValue);

        private static Dictionary<string, string> Decode(
            WinboxJgCatalog catalog, string apiPath, int[] handler,
            params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static Dictionary<int, Tuple<string, object>> Sub(
            params (int key, string type, object val)[] fields)
        {
            var sub = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) sub[f.key] = Tuple.Create(f.type, f.val);
            return sub;
        }

        // ── a `not`-wrapped element, on the way out ────────────────────────────

        [TestMethod]
        public void APlainElementCarriesItsFlagSetToFalse()
        {
            // Never omitted: the router keeps whatever it holds for a key it is not told about, so leaving
            // the flag out would leave a stale '!' on an element being rewritten as plain.
            var elements = Elements(
                Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler)
                    .EncodeField("filter-ip-address", "192.168.251.0/24"), 0xCC);

            Assert.AreEqual(1, elements.Count);
            Assert.AreEqual(false, elements[0][0x1]);
            Assert.AreEqual(WinboxFieldResolver.PackIpV4("192.168.251.0"), (uint)Num(elements[0][0x2]));
            Assert.AreEqual(0x00FFFFFFL, Num(elements[0][0x3]), "the netmask, packed octet-LSB like the address");
        }

        [TestMethod]
        public void ANegatedElementSetsTheFlagAndStillCarriesTheValue()
        {
            var elements = Elements(
                Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler)
                    .EncodeField("filter-ip-address", "!192.168.251.0/24,10.0.0.1/32"), 0xCC);

            Assert.AreEqual(2, elements.Count);
            Assert.AreEqual(true, elements[0][0x1], "the '!' belongs to THIS element");
            Assert.AreEqual(WinboxFieldResolver.PackIpV4("192.168.251.0"), (uint)Num(elements[0][0x2]),
                "and the value is still the address, not the flag");
            Assert.AreEqual(false, elements[1][0x1]);
            Assert.AreEqual(WinboxFieldResolver.PackIpV4("10.0.0.1"), (uint)Num(elements[1][0x2]));
        }

        [TestMethod]
        public void AWrappedElementStillTakesItsStaticMap()
        {
            var elements = Elements(
                Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler)
                    .EncodeField("filter-port", "80,!https"), 0xCD);

            Assert.AreEqual(80L, Num(elements[0][0x2]));
            Assert.AreEqual(true, elements[1][0x1]);
            Assert.AreEqual(443L, Num(elements[1][0x2]), "the word resolved through the element's own map");
        }

        [TestMethod]
        public void AMacNetworkElementDefaultsToTheAllOnesMask()
        {
            var elements = Elements(
                Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler)
                    .EncodeField("filter-mac-address", "!AA:BB:CC:DD:EE:FF"), 0xC9);

            Assert.AreEqual(true, elements[0][0x1]);
            Assert.AreEqual("AABBCCDDEEFF", elements[0][0x2].ToString());
            Assert.AreEqual("FFFFFFFFFFFF", elements[0][0x3].ToString(), "a bare MAC is the exact address");
        }

        [TestMethod]
        public void AMacNetworkElementTakesAnExplicitMask()
        {
            var elements = Elements(
                Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler)
                    .EncodeField("filter-mac-address", "AA:BB:CC:00:00:00/FF:FF:FF:00:00:00"), 0xC9);

            Assert.AreEqual("AABBCC000000", elements[0][0x2].ToString());
            Assert.AreEqual("FFFFFF000000", elements[0][0x3].ToString());
        }

        // ── a `not`-wrapped element, on the way back ───────────────────────────

        [TestMethod]
        public void ANegatedElementReadsBackWithItsPrefixAndItsValue()
        {
            var decoded = Decode(Parse(SnifferWindow), "/tool/sniffer", SnifferHandler,
                (0xCC, "msg[]", new List<Dictionary<int, Tuple<string, object>>>
                {
                    Sub((0x1, "bool", true), (0x2, "u32", 16492736u), (0x3, "u32", 16777215u)),
                    Sub((0x1, "bool", false), (0x2, "u32", 16777226u), (0x3, "u32", 4294967295u)),
                }));

            Assert.AreEqual("!192.168.251.0/24,10.0.0.1/32", decoded["filter-ip-address"]);
        }

        [TestMethod]
        public void AMacNetworkElementReadsBackAsTheApiSpellsIt()
        {
            // RouterOS prints the mask even when it is the all-ones one, where webfig's own tostr hides it.
            var decoded = Decode(Parse(SnifferWindow), "/tool/sniffer", SnifferHandler,
                (0xC9, "msg[]", new List<Dictionary<int, Tuple<string, object>>>
                {
                    Sub((0x1, "bool", true), (0x2, "raw", "AABBCCDDEEFF"), (0x3, "raw", "FFFFFFFFFFFF")),
                }));

            Assert.AreEqual("!AA:BB:CC:DD:EE:FF/FF:FF:FF:FF:FF:FF", decoded["filter-mac-address"]);
        }

        // ── two fields, one label ──────────────────────────────────────────────

        [TestMethod]
        public void TheSecondFieldOfALabelCollisionIsReachableUnderItsTab()
        {
            var resolver = Resolver(SnifferWindow, "/tool/sniffer", SnifferHandler);

            // The plain name still means what it always meant — the streaming port.
            Assert.AreEqual(0x9L, Num(Fields(resolver.EncodeField("port", "37008")).Keys.Single()));
            // And the filter list, which had no name at all, is the tab-qualified one the API uses.
            Assert.IsTrue(Fields(resolver.EncodeField("filter-port", "80")).ContainsKey(0xCD));
        }

        // ── a list of address pairs ────────────────────────────────────────────

        [TestMethod]
        public void PairsWithNoMaskKeyAreFlattenedIntoOneArray()
        {
            var fields = Fields(Resolver(PoolWindow, "/ip/pool", PoolHandler)
                .EncodeField("ranges", "192.168.251.10-192.168.251.20,192.168.252.5"));

            CollectionAssert.AreEqual(
                new object[]
                {
                    (int)WinboxFieldResolver.PackIpV4("192.168.251.10"),
                    (int)WinboxFieldResolver.PackIpV4("192.168.251.20"),
                    (int)WinboxFieldResolver.PackIpV4("192.168.252.5"),
                    (int)WinboxFieldResolver.PackIpV4("192.168.252.5"),
                },
                ((IEnumerable<int>)ParseU32Array(fields[0xB])).Cast<object>().ToArray(),
                "lo,hi per element — a bare address is the range that starts and ends on it");
        }

        [TestMethod]
        public void APrefixIsTheRangeItSpans()
        {
            var fields = Fields(Resolver(PoolWindow, "/ip/pool", PoolHandler)
                .EncodeField("ranges", "192.168.253.0/24"));
            var v = ParseU32Array(fields[0xB]);

            Assert.AreEqual((int)WinboxFieldResolver.PackIpV4("192.168.253.0"), v[0]);
            Assert.AreEqual((int)WinboxFieldResolver.PackIpV4("192.168.253.255"), v[1]);
        }

        [TestMethod]
        public void PairsReadBackThroughTheElementsOwnRangeRule()
        {
            var decoded = Decode(Parse(PoolWindow), "/ip/pool", PoolHandler,
                (0xA, "string", "tik4net-test-pool"),
                (0xB, "u32[]", "[184264896,352037056,100444352,100444352,16623808,4294813888]"));

            Assert.AreEqual("192.168.251.10-192.168.251.20,192.168.252.5,192.168.253.0/24",
                decoded["ranges"], "and the API's own name for the field");
        }

        [TestMethod]
        public void PairsWithAMaskKeyRideInTwoParallelArrays()
        {
            var fields = Fields(Resolver(TemplateWindow, "/tool/traffic-generator/packet-template",
                                         TemplateHandler)
                .EncodeField("src", "192.168.251.10-192.168.251.20,192.168.252.5"));

            CollectionAssert.AreEqual(
                new[]
                {
                    (int)WinboxFieldResolver.PackIpV4("192.168.251.10"),
                    (int)WinboxFieldResolver.PackIpV4("192.168.252.5"),
                },
                ParseU32Array(fields[0xD3]), "the starts");
            CollectionAssert.AreEqual(
                new[]
                {
                    (int)WinboxFieldResolver.PackIpV4("192.168.251.20"),
                    (int)WinboxFieldResolver.PackIpV4("192.168.252.5"),
                },
                ParseU32Array(fields[0xDE]), "the ends, one per element rather than interleaved");
        }

        [TestMethod]
        public void TheMacFlavourPairsSixBytesWithSixMaskBytes()
        {
            var fields = Fields(Resolver(TemplateWindow, "/tool/traffic-generator/packet-template",
                                         TemplateHandler)
                .EncodeField("dst", "AA:BB:CC:DD:EE:FF"));

            Assert.AreEqual("[AABBCCDDEEFF]", fields[0xC8].ToString());
            Assert.AreEqual("[FFFFFFFFFFFF]", fields[0xDC].ToString());
        }

        [TestMethod]
        public void TheParallelMaskArrayIsNotAFieldOfItsOwn()
        {
            // It holds the second half of every element of the ONE list the API prints.
            var decoded = Decode(Parse(TemplateWindow), "/tool/traffic-generator/packet-template",
                TemplateHandler,
                (0xD3, "u32[]", "[184264896]"), (0xDE, "u32[]", "[352037056]"));

            Assert.AreEqual("192.168.251.10-192.168.251.20", decoded["src"]);
            Assert.IsFalse(decoded.Keys.Any(k => k != "src"), "no second field for the ends");
        }

        // The u32[] the M2 parser renders as the text "[a,b,…]".
        private static int[] ParseU32Array(object value)
        {
            string s = value.ToString().Trim('[', ']');
            return s.Length == 0
                ? new int[0]
                : s.Split(',').Select(p => unchecked((int)uint.Parse(p.Trim()))).ToArray();
        }
    }
}
