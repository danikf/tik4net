// WinboxUnionAndTupleFieldTests.cs — a declaration whose label is on the parent and whose ids are on the
// children.
//
// A field's key is taken from its own `id`. A `union` and a `tuple` have none: the NAME is on the node and
// the ids are on the children. The union case was half-solved — the first child was registered, so the field
// existed but only answered when the router used that address family — and the tuple case not at all.
//
//   {name:'Src. Address',type:'union',single:1,
//    c:[{type:'network',id:'u1',maskid:'u2'},{type:'network6',id:'a15',maskid:'u16'}]}
//   {name:'Remote',type:'tuple',sep:':',c:[{type:'ip6addr',id:'ad'},{type:'number',id:'ue'}]}
//
// Live on RouterOS 7.24: /ip/ipsec/policy's template row carries the IPv6 family (0x15 with its length at
// 0x16) and the API prints '::/0'; /ip/service's connection rows carry 0xD and 0xE and the API prints
// '192.168.4.31:65504'.
//
// The parse is the whole of this file: whether the router's bytes then decode is measured against the router
// itself in WinboxNativeFieldNameTest, which is where a value can be compared with the API's own text.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxUnionAndTupleFieldTests
    {
        private static readonly int[] Handler = { 85, 1 };

        // One window carrying all four shapes: the two that must be registered and the two that must not.
        private const string Window =
            "[{name:'IPsec',title:'IPsec',group:'IP',c:[" +
            "{name:'IPsec Policy',title:'Policies',type:'map',path:[ 85,1 ],c:[" +
              "{name:'Src. Address',type:'union',single:1,c:[" +
                 "{type:'network',id:'u1',maskid:'u2'},{type:'network6',id:'a15',deflen:0,maskid:'u16'}]}," +
              "{name:'Remote',type:'tuple',sep:':',ro:1,c:[" +
                 "{type:'ip6addr',id:'ad',allowipv4:1},{type:'number',id:'ue'}]}," +
              // separate:1 — webfig draws the parts as boxes of their own, and they carry their own names.
              "{name:'Max Limit',type:'tuple',sep:' ',separate:1,c:[" +
                 "{name:'Upload Max Limit',type:'number',id:'u20'}," +
                 "{name:'Download Max Limit',type:'number',id:'u21'}]}," +
              // No separate:1, but the children are named all the same, so they are fields in their own right.
              "{name:'Priority',type:'tuple',sep:'/',c:[" +
                 "{name:'Upload Priority',type:'number',id:'u30'}," +
                 "{name:'Download Priority',type:'number',id:'u31'}]}," +
              // A tuple that is NOT read-only — oflow.jg's 'Datapath ID' is one, so the write side has to
              // have an answer of its own rather than leaning on the read-only rule.
              "{name:'Datapath ID',type:'tuple',sep:'/',c:[" +
                 "{type:'number',id:'u40'},{type:'number',id:'u41'}]}" +
            "]}]}]";

        private static WinboxFieldResolver Resolver()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window), "the trimmed window must parse");
            return new WinboxFieldResolver("/ip/ipsec/policy", Handler, catalog,
                                           new Dictionary<string, int>());
        }

        private static Dictionary<string, string> Decode(params (int key, string type, object val)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window));
            var resolver = new WinboxFieldResolver("/ip/ipsec/policy", Handler, catalog,
                                                   new Dictionary<string, int>());
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static byte[] V6(params byte[] leading)
        {
            var b = new byte[16];
            Array.Copy(leading, b, leading.Length);
            return b;
        }

        // ── union ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void TheFirstFamilyOfAUnionStillAnswers()
        {
            // Unchanged behaviour, and the reason the write side is unchanged too: the NAME still resolves
            // to the first member, so an IPv6 value still fails to encode with the codec's own error rather
            // than being sent on the wrong key.
            Assert.AreEqual(0x1, Resolver().ResolveKey("src-address"));

            var fields = Decode((0x1, "u32", 0x0100000AU), (0x2, "u32", 0xFFFFFF00U));
            Assert.AreEqual("10.0.0.1/24", fields["src-address"]);
        }

        [TestMethod]
        public void TheOtherFamilyAnswersToTheSameNameAtItsOwnKey()
        {
            var fields = Decode((0x15, "ip6", V6(0x20, 0x01, 0x0d, 0xb8)), (0x16, "u32", 32U));

            Assert.IsTrue(fields.ContainsKey("src-address"),
                "a row in the second address family must report the field at all");
            Assert.AreEqual("2001:db8::/32", fields["src-address"]);
        }

        [TestMethod]
        public void AnAlternativeIsTypedAsItselfAndNotAsTheFirstFamily()
        {
            // The sibling of a network6 holds the PREFIX LENGTH, that of a network a NETMASK. Typing the v6
            // family by the v4 member's rules would read the 0 as a mask and print /0 for every length —
            // right by accident on ::/0, wrong on everything else, which is why the length here is not 0.
            var fields = Decode((0x15, "ip6", V6(0xfe, 0x80)), (0x16, "u32", 64U));

            Assert.AreEqual("fe80::/64", fields["src-address"]);
        }

        [TestMethod]
        public void AFamilyWithNoLengthIsTheBareAddress()
        {
            var fields = Decode((0x15, "ip6", V6(0xfe, 0x80)));

            Assert.AreEqual("fe80::", fields["src-address"]);
        }

        // ── tuple ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ATupleIsJoinedByItsOwnSeparator()
        {
            var fields = Decode((0xd, "ip6", V6(0x20, 0x01)), (0xe, "u32", 65504U));

            Assert.AreEqual("2001::" + ":" + "65504", fields["remote"]);
        }

        [TestMethod]
        public void EitherPartOfATupleReachesTheWholeValue()
        {
            // The decode walks the record's keys in whatever order they arrived, so both part keys have to
            // name the same field and render the same text — otherwise the value would depend on the order
            // the router happened to put its fields in.
            var forward = Decode((0xd, "ip6", V6(0x20, 0x01)), (0xe, "u32", 1U));
            var reverse = Decode((0xe, "u32", 1U), (0xd, "ip6", V6(0x20, 0x01)));

            Assert.AreEqual(forward["remote"], reverse["remote"]);
            Assert.AreEqual(1, CountKeysStartingWith(forward, "remote"),
                "the parts must not also surface as fields of their own");
        }

        [TestMethod]
        public void AWritableTupleIsRefusedRatherThanHalfWritten()
        {
            // Splitting the joined text back onto the parts is not the inverse of joining it — an IPv6
            // 'Remote' puts its own colons in the way of the tuple's — so writing the first part and
            // dropping the rest would be a request the router accepts and half-obeys.
            //
            // A read-only tuple never reaches this: EncodeField drops a ro field before any typed encoder
            // runs. Every tuple the 7.24 catalog puts on a mapped path IS read-only, which is exactly why
            // this test declares one that is not — otherwise the rule would be untested and would read as
            // covered.
            var ex = Assert.ThrowsException<WinboxFieldResolutionException>(
                () => Resolver().EncodeField("datapath-id", "7/9"));
            StringAssert.Contains(ex.Message, "datapath-id");
        }

        [TestMethod]
        public void AReadOnlyTupleIsStillNotWritten()
        {
            // The same outcome by the older rule, kept explicit so a change to either one cannot leave a
            // tuple silently writing its first part.
            Assert.AreEqual(0, Resolver().EncodeField("remote", "1.2.3.4:80").Count);
        }

        // ── the tuples that are NOT one field ─────────────────────────────────

        [TestMethod]
        public void ASeparateTupleLeavesItsChildrenAlone()
        {
            var r = Resolver();

            Assert.AreEqual(0x20, r.ResolveKey("upload-max-limit"));
            Assert.AreEqual(0x21, r.ResolveKey("download-max-limit"));

            var fields = Decode((0x20, "u32", 1000000U), (0x21, "u32", 2000000U));
            Assert.IsFalse(fields.ContainsKey("max-limit"),
                "'separate:1' is webfig saying the parts are boxes of their own");
            Assert.AreEqual("1000000", fields["upload-max-limit"]);
            Assert.AreEqual("2000000", fields["download-max-limit"]);
        }

        [TestMethod]
        public void ATupleWhoseChildrenAreNamedLeavesThemAloneToo()
        {
            var fields = Decode((0x30, "u32", 8U), (0x31, "u32", 8U));

            Assert.IsFalse(fields.ContainsKey("priority"),
                "a child with a label of its own is a field in its own right whatever the tuple says");
            Assert.AreEqual("8", fields["upload-priority"]);
            Assert.AreEqual("8", fields["download-priority"]);
        }

        // ── an alternative is a fallback, never an owner ──────────────────────

        // The Ping window, cut to the collision: the reply's 'Seq #' is `uf` and the request's
        // 'Src. Address' union has `af` for its IPv6 family. ONE key, 0xF, told apart by nothing but the
        // ftype letter — and both are fields of the same window.
        private const string PingWindow =
            "[{name:'Ping',title:'Ping',group:'Tools',c:[" +
            "{title:'Ping',type:'query',path:[ 22 ],request:[" +
              "{name:'Src. Address',type:'union',opt:1,c:[{type:'ipaddr',id:'u9'},{type:'ip6addr',id:'af'}]}]," +
            "c:[{name:'Seq #',type:'number',id:'uf',width:45}," +
               "{name:'Status',type:'string',id:'se',width:100}]}" +
            "]}]";

        [TestMethod]
        public void AnAlternativeDoesNotTakeAKeyAFieldOwnsOutright()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(PingWindow), "the trimmed ping window must parse");
            var resolver = new WinboxFieldResolver("/ping", new[] { 22 }, catalog,
                                                   new Dictionary<string, int>());

            var byKey = resolver.BuildKeyToApiName();

            Assert.IsTrue(byKey.TryGetValue(0xf, out string atF), "0xF must name something");
            // 'seq', not 'seq-#': /ping ships an alias for the label, which is beside the point here — what
            // matters is that 0xF still names the sequence number and not the union's other family.
            Assert.AreEqual("seq", atF,
                "0xF belongs to the field whose own id it is; the union's v6 family only borrows it");
            Assert.AreEqual(0x9, resolver.ResolveKey("src-address"),
                "and the union itself still resolves to its first family");
        }

        private static int CountKeysStartingWith(Dictionary<string, string> fields, string prefix)
        {
            int n = 0;
            foreach (var k in fields.Keys)
                if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }
    }
}
