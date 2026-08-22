// WinboxBigNumberListTests.cs — router-free tests for FT_U64_ARRAY (.jg prefix 'Q', webfig multibignumber).
//
// Two independent holes met on this one shape, which is why it read as nothing at all:
//
//   * the harvest gave the field the wire type "?" — 'Q' was missing from WinboxJgCatalog.Prefix, whose
//     lookup falls back to "?" rather than failing. The field WAS registered under its own name and key, so
//     nothing looked wrong; it simply had no type any encoder or decoder could act on.
//   * M2Message.ParseAllFields had no case for type byte 0x90, so the value never survived the parse. It was
//     skipped cleanly — SkipTypeBytes has always known the layout — leaving the frame in sync and the field
//     absent.
//
// Ground truth for both is webfig's own `id2int` literal, which maps every .jg prefix letter to an ftype:
// `{b:0<<27,u:1<<27,q:2<<27,a:3<<27,s:4<<27,m:5<<27,r:6<<27,B:16<<27,U:17<<27,Q:18<<27,A:19<<27,S:20<<27,
// M:21<<27,R:22<<27}` — Q is FT_U64_ARRAY (18), type byte 18<<3 = 0x90 — plus
// `types.multibignumber = inherit(types.multinumber)`, which makes the list itself the shape we already read.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxBigNumberListTests
    {
        private static readonly int[] Handler = { 20, 55 };

        // A switch-port window cut to one multibignumber (roteros.jg 7.24 declares forty of them on [20,55])
        // plus the plain u32 list beside it, so the two array widths are told apart by nothing but the type.
        private const string Window =
            "[{name:'Switch',title:'Switch',group:'Switch',c:[" +
            "{name:'Port',title:'Port',type:'map',path:[ 20,55 ],c:[" +
              "{name:'Tx Packet',type:'multibignumber',id:'Q480',ro:1,c:[{type:'bignumber'}]}," +
              "{name:'Layer Size',type:'multibignumber',id:'Q36',c:[{type:'bignumber'}]}," +
              "{name:'Forward To',type:'multinumber',id:'U13',c:[{type:'number'}]}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog)
            => new WinboxFieldResolver("/interface/ethernet/switch/port", Handler, catalog,
                                       new Dictionary<string, int>());

        /// <summary>
        /// Builds an FT_U64_ARRAY field by hand — 16-bit count, then eight bytes per element, no per-element
        /// length. Deliberately NOT <c>M2Message.U64ArraySys</c>: a parser test whose input comes from our own
        /// writer agrees with itself whatever both of them do.
        /// </summary>
        private static byte[] U64Array(int fullKey, params ulong[] values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF), (byte)((fullKey >> 16) & 0xFF), 0x90
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Length));
            foreach (ulong v in values) b.AddRange(BitConverter.GetBytes(v));
            return b.ToArray();
        }

        // ── the wire layer ────────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AU64ArrayFieldSurvivesTheParseInsteadOfBeingSkipped()
        {
            byte[] m2 = M2Message.BuildM2(
                M2Message.SysFrom(),
                U64Array(0x480, 1, 2, 3),
                M2Message.U32User(0x12, 7));

            var fields = M2Message.ParseAllFields(m2);

            Assert.IsTrue(fields.ContainsKey(0x480), "the field must reach the caller at all");
            Assert.AreEqual("u64[]", fields[0x480].Item1);
            Assert.AreEqual("[1,2,3]", fields[0x480].Item2.ToString());
            Assert.AreEqual(7u, fields[0x12].Item2,
                "and the field after it still reads — the frame stayed in sync either way");
        }

        [TestMethod]
        public void AnElementWiderThanAU32KeepsAllSixtyFourBits()
        {
            // The point of the type. A packet counter past 4294967295 is exactly why the field is not a u32[],
            // so reading eight bytes and not four is the whole correctness claim.
            byte[] m2 = M2Message.BuildM2(
                M2Message.SysFrom(),
                U64Array(0x480, 4294967296UL, ulong.MaxValue));

            var fields = M2Message.ParseAllFields(m2);
            Assert.AreEqual("[4294967296,18446744073709551615]", fields[0x480].Item2.ToString());
        }

        [TestMethod]
        public void AnEmptyU64ArrayIsAValueRatherThanAMissingField()
        {
            var fields = M2Message.ParseAllFields(
                M2Message.BuildM2(M2Message.SysFrom(), U64Array(0x480)));

            Assert.IsTrue(fields.ContainsKey(0x480));
            Assert.AreEqual("[]", fields[0x480].Item2.ToString());
        }

        [TestMethod]
        public void AU64ArrayCutShortByTheFrameEndStopsAtTheBoundary()
        {
            byte[] m2 = M2Message.BuildM2(
                M2Message.SysFrom(), U64Array(0x480, 1, 2, 3));

            // The declared count still says three, but only two elements' bytes are there. The parse must
            // take what is present and stop rather than walk off the end — and one byte less than that leaves
            // the second element incomplete, so it stops one element earlier again.
            var fields = M2Message.ParseAllFields(m2.Take(m2.Length - 8).ToArray());
            Assert.AreEqual("[1,2]", fields[0x480].Item2.ToString());

            var shorter = M2Message.ParseAllFields(m2.Take(m2.Length - 9).ToArray());
            Assert.AreEqual("[1]", shorter[0x480].Item2.ToString());
        }

        // ── the catalog ───────────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AQPrefixedIdIsTypedRatherThanLeftUnknown()
        {
            var fields = Parse(Window).GetHandlerFields(Handler);
            Assert.IsNotNull(fields);

            Assert.AreEqual("u64[]", fields!["tx-packet"].WireType);
            Assert.AreEqual("multibignumber", fields["tx-packet"].UiType);
            Assert.AreEqual(0x480, fields["tx-packet"].Key);
            Assert.AreEqual("u32[]", fields["forward-to"].WireType,
                "and the u32 list beside it is unchanged");
        }

        // ── decode ────────────────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AMultiBigNumberReadsAsItsCommaJoinedElements()
        {
            var catalog = Parse(Window);
            var resolver = Resolver(catalog);
            var rec = new Dictionary<int, Tuple<string, object>>
            {
                [0xFE0001] = Tuple.Create("u32", (object)1u),
                [0x480] = Tuple.Create("u64[]", (object)"[10,4294967296,0]"),
            };

            var decoded = new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());

            Assert.AreEqual("10,4294967296,0", decoded["tx-packet"],
                "webfig types.multi.tostr comma-joins the elements, each through the element's own type — " +
                "and bignumber is types.number, so an element is the number itself");
        }

        // ── encode ────────────────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AMultiBigNumberIsWrittenAsOneU64Array()
        {
            var resolver = Resolver(Parse(Window));

            var written = resolver.EncodeField("layer-size", "10,4294967296");
            Assert.AreEqual(1, written.Count, "one field, one array — types.multinumber stores it flat");

            var back = M2Message.ParseAllFields(M2Message.BuildM2(M2Message.SysFrom(), written[0]));
            Assert.AreEqual("u64[]", back[0x36].Item1, "and at the u64 width, not the u32 one");
            Assert.AreEqual("[10,4294967296]", back[0x36].Item2.ToString());
        }

        [TestMethod]
        public void AnEmptyMultiBigNumberClearsTheFieldRatherThanLeavingItAlone()
        {
            // The same rule the rest of the list family follows: a key the router is not told about keeps
            // whatever it already holds, so clearing a list has to be said out loud as the empty array.
            var written = Resolver(Parse(Window)).EncodeField("layer-size", "");
            Assert.AreEqual(1, written.Count);

            var back = M2Message.ParseAllFields(M2Message.BuildM2(M2Message.SysFrom(), written[0]));
            Assert.AreEqual("[]", back[0x36].Item2.ToString());
        }

        [TestMethod]
        public void AnElementThatIsNotANumberFailsLoudlyInsteadOfShorteningTheList()
        {
            // A dropped element is a request the router accepts without complaint, which reads back as "the
            // router has fewer of these" — the failure mode this whole family is written to avoid.
            var resolver = Resolver(Parse(Window));
            try
            {
                resolver.EncodeField("layer-size", "10,not-a-number");
                Assert.Fail("a non-numeric element must be refused, not skipped");
            }
            catch (WinboxFieldValueException)
            {
            }
        }
    }
}
