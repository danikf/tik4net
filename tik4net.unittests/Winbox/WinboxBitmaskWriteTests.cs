// WinboxBitmaskWriteTests.cs — router-free tests for the two bitmask field shapes.
//
// `multibits` is a plain bit set, `multitristate` the same set with the negated members in a SECOND key
// (webfig: types.multitristate.put ORs the plain members into `id` and the '!' ones into `maskid`). Neither
// could be written — both fell into the resolver's list/array refusal, though each is one u32 — and the
// tri-state could not be read either, because its members live on the ELEMENT type and the catalog stopped
// short of it, leaving the field with no map.
//
// The .jg fragment is the real /ip/firewall/filter declaration from RouterOS 7.24, cut to these fields; the
// API column is what the router prints for the same rule.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxBitmaskWriteTests
    {
        private static readonly int[] FilterHandler = { 12, 1 };

        // roteros.jg, Filter Rule [12,1]: TCP Flags is a multitristate inside the usual opt/not wrappers,
        // and Src. Address Type a multibits whose members are a static map.
        private const string FilterWindow =
            "[{name:'Firewall',title:'Firewall',group:'IP',c:[" +
            "{name:'Filter Rule',title:'Filter Rules',type:'map',path:[ 12,1 ],c:[" +
              "{name:'Chain',type:'string',id:'s2'}," +
              "{name:'TCP Flags',type:'opt',id:'b197',on:'tcp',c:[{type:'not',id:'bd3',c:[" +
                 "{name:'TCP Flags',type:'multitristate',id:'u56',maskid:'u57',max:8,c:[{type:'tristate'," +
                 "values:{type:'static',map:['fin','syn','rst','psh','ack','urg','ece','cwr']}}]}]}]}," +
              "{name:'Src. Address Type',type:'opt',id:'b1aa',c:[{type:'not',id:'bcc',c:[" +
                 "{name:'Src. Address Type',type:'multibits',id:'u49',c:[{type:'enm',values:{type:'static'," +
                 "map:{1:'unicast',2:'local',3:'broadcast',5:'multicast'}}}]}]}]}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog)
            => new WinboxFieldResolver("/ip/firewall/filter", FilterHandler, catalog,
                                       new Dictionary<string, int>());

        // key → value of every encoded field, read back through the real M2 parser. A small number rides
        // in the u8 short form, so the numeric values are compared as longs rather than by their CLR type.
        private static Dictionary<int, object> Decoded(IList<byte[]> encoded)
        {
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded.SelectMany(f => f)).ToArray();
            return M2Message.ParseAllFields(msg).ToDictionary(kv => kv.Key, kv => kv.Value.Item2);
        }

        private static long Num(object wireValue) => Convert.ToInt64(wireValue);

        private static Dictionary<string, string> Decode(WinboxJgCatalog catalog,
            Dictionary<int, Tuple<string, object>> rec)
        {
            var resolver = new WinboxFieldResolver(null, FilterHandler, catalog, new Dictionary<string, int>());
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        // ── the members of a tri-state live on its element type ────────────────

        [TestMethod]
        public void ATriStateFieldFindsItsMembersOnTheElementType()
        {
            var fields = Parse(FilterWindow).GetHandlerFields(FilterHandler);

            Assert.IsNotNull(fields["tcp-flags"].EnumMap, "the map is on the 'tristate' child, not the field");
            Assert.AreEqual("syn", fields["tcp-flags"].EnumMap[1]);
            Assert.AreEqual(0x57, fields["tcp-flags"].MaskKey, "the negated members' key");
        }

        // ── decode ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ATriStateReadsItsNegatedMembersFromTheMaskKey()
        {
            // The live rule the API prints as tcp-flags="syn,!ack": bit 1 in the field, bit 4 in the mask.
            var fields = Decode(Parse(FilterWindow), new Dictionary<int, Tuple<string, object>>
            {
                [0x56] = Tuple.Create("u32", (object)2u),
                [0x57] = Tuple.Create("u32", (object)16u),
            });

            Assert.AreEqual("syn,!ack", fields["tcp-flags"]);
            Assert.IsFalse(fields.ContainsKey("u57"), "the mask is half of one value, not a field of its own");
        }

        [TestMethod]
        public void ATriStateWithNoNegatedMemberReadsAsAPlainList()
        {
            var fields = Decode(Parse(FilterWindow), new Dictionary<int, Tuple<string, object>>
            {
                [0x56] = Tuple.Create("u32", (object)6u),   // syn|rst
                [0x57] = Tuple.Create("u32", (object)0u),
            });

            Assert.AreEqual("syn,rst", fields["tcp-flags"]);
        }

        // ── encode ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ATriStateSplitsItsMembersAcrossBothKeys()
        {
            var encoded = Decoded(Resolver(Parse(FilterWindow)).EncodeField("tcp-flags", "syn,!ack"));

            Assert.AreEqual(2L, Num(encoded[0x56]), "the plain members");
            Assert.AreEqual(16L, Num(encoded[0x57]), "the negated ones");
        }

        [TestMethod]
        public void ATriStateAlwaysWritesBothKeys()
        {
            // The router keeps what it already has for a key it is not told about, so a write that dropped
            // the empty half would leave a stale '!' member behind and report success.
            var encoded = Decoded(Resolver(Parse(FilterWindow)).EncodeField("tcp-flags", "syn"));

            Assert.AreEqual(2L, Num(encoded[0x56]));
            Assert.AreEqual(0L, Num(encoded[0x57]), "the negated half has to be cleared explicitly");
        }

        [TestMethod]
        public void ALeadingBangOnATriStateNegatesTheMemberNotTheField()
        {
            // The field has a `not` wrapper (bd3), so a leading '!' on a SCALAR would set that flag. Here it
            // belongs to the member that follows it — "!ack,syn" is two members with opposite senses.
            var encoded = Decoded(Resolver(Parse(FilterWindow)).EncodeField("tcp-flags", "!ack,syn"));

            Assert.AreEqual(2L, Num(encoded[0x56]));
            Assert.AreEqual(16L, Num(encoded[0x57]));
            Assert.IsFalse(encoded.ContainsKey(0xD3), "the field-wide 'not' flag must stay untouched");
        }

        [TestMethod]
        public void AMultibitsIsEncodedAsTheOneU32ItIs()
        {
            var encoded = Decoded(Resolver(Parse(FilterWindow)).EncodeField("src-address-type", "local"));

            Assert.AreEqual(4L, Num(encoded[0x49]), "bit 2 — the map key is the bit index");
            Assert.AreEqual(true, encoded[0x1AA], "and the opt flag that makes the router read it");
        }

        // ── a bit set whose members are a TABLE ────────────────────────────────

        // roteros.jg, Group [13,2]: 'Policies' takes its members from the policy table [13,3] — no static
        // map — and keeps the denied ones in its maskid sibling. The referenced window names its rows by
        // 'Alias' (nameval), not by its 'Name' field, which holds a whole sentence.
        private const string GroupWindow =
            "[{name:'Users',title:'Users',group:'System',c:[" +
            "{name:'',type:'map',path:[ 13,3 ],nameval:'Alias',c:[" +
              "{name:'Name',type:'string',id:'s1'},{name:'Alias',type:'string',id:'s2'}]}," +
            "{name:'Group',title:'Groups',type:'map',path:[ 13,2 ],nameval:'Name',c:[" +
              "{name:'Name',type:'string',id:'s1'}," +
              "{name:'Policies',type:'set',id:'u2',maskid:'u3',values:{type:'dynamic',path:[ 13,3 ]}}]}" +
            "]}]";

        private static readonly int[] GroupHandler = { 13, 2 };

        // The policy table as the router serves it: the bit index is the row's id.
        private static readonly Dictionary<int, string> PolicyMembers = new Dictionary<int, string>
        {
            [4] = "ftp", [6] = "read", [7] = "write", [10] = "winbox", [19] = "rest-api",
        };

        private static WinboxFieldResolver GroupResolver()
            => new WinboxFieldResolver("/user/group", GroupHandler, Parse(GroupWindow),
                                       new Dictionary<string, int>());

        [TestMethod]
        public void ATableBackedSetIsRecognizedAndItsTableNamed()
        {
            CollectionAssert.AreEqual(new[] { 13, 3 }, GroupResolver().BitSetMemberTable("policy"),
                "the caller has to know which table to read before encoding");
        }

        [TestMethod]
        public void ATableBackedSetEncodesMemberNamesToTheirRowIds()
        {
            var encoded = Decoded(GroupResolver().EncodeField("policy", "read,write,!ftp", null, false,
                                                              h => PolicyMembers));

            Assert.AreEqual((1L << 6) | (1L << 7), Num(encoded[0x2]), "the granted members");
            Assert.AreEqual(1L << 4, Num(encoded[0x3]), "and only the ones the value denies");
        }

        [TestMethod]
        public void ATableBackedSetLeavesUnnamedMembersAlone()
        {
            // The router settles the members the request mentions and keeps the rest of the row — the same
            // thing the binary API does for this command. Sending "everything not named" as denied would
            // turn a two-member write into a rewrite of the whole permission list.
            var encoded = Decoded(GroupResolver().EncodeField("policy", "read,write", null, false,
                                                              h => PolicyMembers));

            Assert.AreEqual(0L, Num(encoded[0x3]));
        }

        [TestMethod]
        public void ATableBackedSetRefusesToEncodeWithoutItsMembers()
        {
            // With no member map every token misses, and the field would go out as a clean, well-formed
            // ZERO — a write that reports success and leaves the group allowed nothing.
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => GroupResolver().EncodeField("policy", "read,write"));
        }

        [TestMethod]
        public void AMemberNobodyKnowsIsAnErrorRatherThanASkippedBit()
        {
            Assert.ThrowsException<WinboxFieldValueException>(
                () => GroupResolver().EncodeField("policy", "read,tpyo", null, false, h => PolicyMembers));
        }

        [TestMethod]
        public void ABitSetsMembersAreNamedByTheWindowsNameval()
        {
            // The policy table's rows are 'read'/'write' (Alias), not "read router configuration" (Name).
            var rows = new List<Dictionary<int, Tuple<string, object>>>
            {
                new Dictionary<int, Tuple<string, object>>
                {
                    [0xFE0001] = Tuple.Create("u32", (object)6u),
                    [0x1] = Tuple.Create("str", (object)"read router configuration"),
                    [0x2] = Tuple.Create("str", (object)"read"),
                },
            };

            var map = WinboxRecordCodec.BuildMemberMap(Parse(GroupWindow), new[] { 13, 3 }, rows);

            Assert.AreEqual("read", map[6]);
        }

        [TestMethod]
        public void AMultibitsTakesSeveralMembersAtOnce()
        {
            var encoded = Decoded(Resolver(Parse(FilterWindow)).EncodeField("src-address-type",
                                                                            "unicast,broadcast"));

            Assert.AreEqual(0xAL, Num(encoded[0x49]));
        }
    }
}
