// WinboxEnumAndUnsetDecodeTests.cs — router-free tests for A2: the decode steps where the RAW WIRE FORM was
// reaching the caller instead of the value RouterOS's own API reports for the same record.
//
// All four rules were established against the live router (7.23.2) rather than guessed — the API column in
// each test is what /ip/proxy, /ip/ssh, /ip/ipsec/proposal, /system/logging/action and /ip/proxy/access
// actually print. The .jg fragments are the real declarations, cut down to what the decode reads.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxEnumAndUnsetDecodeTests
    {
        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        // Decode one record the way the connection does: resolver → key maps → codec. `ops` is null because
        // none of these fields is a dynamic reference, so nothing is ever read from a router.
        private static Dictionary<string, string> Decode(
            WinboxJgCatalog catalog, int[] handler, Dictionary<int, Tuple<string, object>> rec)
        {
            var resolver = new WinboxFieldResolver(null, handler, catalog, new Dictionary<string, int>());
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static Dictionary<int, Tuple<string, object>> Rec(params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return rec;
        }

        // ── nested `values:` wrappers ──────────────────────────────────────────

        // secure.jg, IPsec Proposal [85,7]: the map sits under enumfilter → defenum → static, and the labels
        // spell the numeric value out ('modp1024 (2)') where the API says 'modp1024'.
        private const string IpsecProposalWindow =
            "[{name:'IPsec Proposal',type:'map',path:[ 85,7 ],c:[" +
            "{name:'Name',type:'string',id:'sfe0010'}," +
            "{name:'Lifetime',type:'interval',id:'u2',def:1800,opt:1}," +
            "{name:'PFS Group',type:'enm',id:'u4',values:{type:'enumfilter',filters:[{id:0},{id:2},{id:14}]," +
              "values:{type:'defenum',defid:0,defname:'none',values:{type:'static',map:{" +
              "1:'modp768 (1)',2:'modp1024 (2)',14:'modp2048 (14)'}}}}}]}]";

        [TestMethod]
        public void AnEnumMapNestedUnderEnumfilterAndDefenumIsFound()
        {
            var fields = Parse(IpsecProposalWindow).GetHandlerFields(new[] { 85, 7 });

            Assert.IsTrue(fields.TryGetValue("pfs-group", out var pfs));
            Assert.IsNotNull(pfs.EnumMap, "reading only the top level of `values` left this field with no map");
            Assert.AreEqual("modp1024", pfs.EnumMap[2], "the ' (2)' in the WinBox label is display decoration");
            Assert.AreEqual("none", pfs.EnumMap[0], "the defenum's own id/name belongs in the map too");
        }

        [TestMethod]
        public void TheParenthesisedValueIsStrippedOnlyWhenItIsTheKey()
        {
            // The strip is keyed on the number MATCHING, so a label that genuinely ends in a parenthesised
            // number keeps it — otherwise this would quietly rewrite unrelated labels.
            var catalog = Parse("[{name:'W',type:'map',path:[ 9,9 ],c:[{name:'F',type:'enm',id:'u1'," +
                "values:{type:'static',map:{2:'modp1024 (2)',3:'weird (7)'}}}]}]");
            var map = catalog.GetHandlerFields(new[] { 9, 9 })["f"].EnumMap;

            Assert.AreEqual("modp1024", map[2]);
            Assert.AreEqual("weird-(7)", map[3], "(7) is not key 3, so it is part of the name");
        }

        [TestMethod]
        public void PfsGroupDecodesToTheApiValue()
        {
            var decoded = Decode(Parse(IpsecProposalWindow), new[] { 85, 7 },
                Rec((0x4, "u32", (uint)2), (0xFE0010, "str", "default")));

            Assert.AreEqual("modp1024", decoded["pfs-group"], "the API prints pfs-group=modp1024 for this row");
        }

        [TestMethod]
        public void AnOrdinaryDefaultIsStillAValueThatGetsReported()
        {
            // Only the u32 unset MARKER is treated as "not set". A proposal's lifetime declares def:1800 and
            // the API prints lifetime=30m for a row carrying exactly that — dropping every value that equals
            // its default would empty half of every record.
            var decoded = Decode(Parse(IpsecProposalWindow), new[] { 85, 7 },
                Rec((0x2, "u32", (uint)1800)));

            Assert.IsTrue(decoded.ContainsKey("lifetime"));
            Assert.AreEqual("1800", decoded["lifetime"]);
        }

        // ── list fields whose elements are literals ────────────────────────────

        // roteros.jg Web Proxy Settings [96,1] 'Port' (a multinumber of plain numbers) and secure.jg SSH
        // Settings [8,2] 'Ciphers' (a multinumber whose element is a static enum).
        private const string PortAndCiphers =
            "[{name:'Web Proxy Settings',type:'item',path:[ 96,1 ],c:[" +
            "{name:'Port',type:'multinumber',id:'U2',min:1,c:[{type:'number',max:65535,min:1}]}]}," +
            "{name:'SSH Settings',type:'item',path:[ 8,2 ],c:[" +
            "{name:'Ciphers',type:'multinumber',id:'Ub',c:[{type:'enm',values:{type:'static'," +
              "map:{0:'Auto',1:'AES GCM',2:'AES CTR'}}}]}]}]";

        [TestMethod]
        public void AListOfPlainNumbersDecodesToTheNumbers()
        {
            var catalog = Parse(PortAndCiphers);

            Assert.AreEqual("8080",
                Decode(catalog, new[] { 96, 1 }, Rec((0x2, "u32[]", "[8080]")))["port"],
                "the API prints port=8080, not the wire form [8080]");
            Assert.AreEqual("8080,3128",
                Decode(catalog, new[] { 96, 1 }, Rec((0x2, "u32[]", "[8080,3128]")))["port"]);
        }

        [TestMethod]
        public void AListOfStaticEnumElementsDecodesToTheLabels()
        {
            var catalog = Parse(PortAndCiphers);

            Assert.AreEqual("auto",
                Decode(catalog, new[] { 8, 2 }, Rec((0xB, "u32[]", "[0]")))["ciphers"],
                "the API prints ciphers=auto for this row");
            Assert.AreEqual("aes-gcm,aes-ctr",
                Decode(catalog, new[] { 8, 2 }, Rec((0xB, "u32[]", "[1,2]")))["ciphers"]);
        }

        [TestMethod]
        public void AnElementTheMapDoesNotNameStaysNumericRatherThanVanishing()
        {
            // A shorter list would read as "the router has fewer of these" — the value must stay visible.
            Assert.AreEqual("auto,9",
                Decode(Parse(PortAndCiphers), new[] { 8, 2 }, Rec((0xB, "u32[]", "[0,9]")))["ciphers"]);
        }

        // ── "not set" is not a value ───────────────────────────────────────────

        // roteros.jg Web Proxy Rule [96,3]: every matcher is opt-wrapped, and the router sends the key with
        // the flag down for a rule that does not use it.
        private const string ProxyRuleWindow =
            "[{name:'Web Proxy Rule',type:'map',path:[ 96,3 ],c:[" +
            "{name:'Method',type:'opt',id:'bc86',c:[{type:'not',id:'bc22',c:[{type:'string',id:'sbc1'," +
              "values:{type:'static',map:['GET','HEAD','POST']}}]}]}," +
            "{name:'Action',type:'enm',id:'ubc6',values:{type:'static',map:[ 'allow','deny' ]}}]}]";

        [TestMethod]
        public void AnOptWrappedFieldWhoseFlagIsDownIsNotReportedAtAll()
        {
            var catalog = Parse(ProxyRuleWindow);

            var unset = Decode(catalog, new[] { 96, 3 },
                Rec((0xBC1, "str", ""), (0xC86, "bool", false), (0xBC6, "u32", (uint)1)));
            Assert.IsFalse(unset.ContainsKey("method"),
                "the API leaves 'method' out of a rule that does not match on it — an empty string is not the same answer");
            Assert.AreEqual("deny", unset["action"], "the rest of the row is unaffected");

            var set = Decode(catalog, new[] { 96, 3 },
                Rec((0xBC1, "str", "GET"), (0xC86, "bool", true), (0xBC6, "u32", (uint)1)));
            Assert.AreEqual("GET", set["method"], "with the flag up it is a value like any other");
        }

        // roteros.jg Log Action [3,1] 'Syslog Severity': a number whose declared default IS the u32 unset
        // marker (its real domain is 0-7), versus 'Max. Cache Size', where 4294967295 is a NAMED value.
        private const string SentinelWindow =
            "[{name:'Log Action',type:'map',path:[ 3,1 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Syslog Severity',type:'number',id:'ue',def:4294967295,max:7,opt:1," +
              "values:{type:'static',map:['emergency','alert','critical','error']}}," +
            "{name:'Max Cache Size',type:'enm',id:'ub',values:{type:'static'," +
              "map:{0:'none',4294967295:'unlimited'}}}]}]";

        [TestMethod]
        public void TheU32UnsetMarkerIsNotReportedAsANumber()
        {
            var decoded = Decode(Parse(SentinelWindow), new[] { 3, 1 },
                Rec((0xE, "u32", 4294967295u), (0x1, "str", "remote")));

            Assert.IsFalse(decoded.ContainsKey("syslog-severity"),
                "the API prints no syslog-severity for this row; 4294967295 is the 'not set' marker");
            Assert.AreEqual("remote", decoded["name"]);
        }

        [TestMethod]
        public void ASetSeverityIsReportedNormally()
        {
            var decoded = Decode(Parse(SentinelWindow), new[] { 3, 1 }, Rec((0xE, "u32", (uint)3)));

            Assert.AreEqual("error", decoded["syslog-severity"]);
        }

        [TestMethod]
        public void TheSameNumberIsKeptWhereTheCatalogGivesItAName()
        {
            // /ip/proxy max-cache-size=unlimited IS 4294967295 on the wire, and the API prints it. The
            // sentinel rule must not swallow a value the catalog names.
            var decoded = Decode(Parse(SentinelWindow), new[] { 3, 1 }, Rec((0xB, "u32", 4294967295u)));

            Assert.AreEqual("unlimited", decoded["max-cache-size"]);
        }

        // ── the fourth way of saying "not set": an opt enum outside its own map ─

        // roteros.jg Certificate [19,1]. 'Digest Algorithm' carries the opt:1 ATTRIBUTE (not an opt WRAPPER —
        // there is no flag key here) and a static map that has no member 0. 'Key Type' is the control case:
        // opt:1 as well, but its value IS in its map. Live on 7.23.2 an UNSIGNED certificate's record carries
        // u85=0 and u87=1, and the API prints key-type=rsa with no digest-algorithm at all.
        private const string CertificateWindow =
            "[{name:'Certificate',type:'map',path:[ 19,1 ],c:[" +
            "{name:'Name',type:'string',id:'sfe0010'}," +
            "{name:'Digest Algorithm',type:'enm',id:'u85',opt:1,values:{type:'static'," +
              "map:{4:'md5',64:'sha1',672:'sha256',673:'sha384',674:'sha512'}}}," +
            "{name:'Key Type',type:'enm',id:'u87',opt:1,values:{type:'static',map:{1:'RSA',2:'DSA',3:'EC'}}}," +
            "{name:'Trust Store',type:'enm',id:'u25',values:{type:'static',map:{1:'none',2:'all'}}}]}]";

        [TestMethod]
        public void AnOptEnumOutsideItsOwnMapIsNotSet()
        {
            // webfig's types.enm.tostr: enum2string misses, then `if(attrs.opt) return ''`. RouterOS's API
            // says the same thing by leaving the field out of the row — so must we, or the O/R mapper is
            // handed a '0' it cannot convert (which is how this surfaced: a certificate created without a
            // digest-algorithm failed to load back over native).
            var decoded = Decode(Parse(CertificateWindow), new[] { 19, 1 },
                Rec((0x85, "u8", (byte)0), (0x87, "u8", (byte)1), (0xFE0010, "str", "unsigned-cert")));

            Assert.IsFalse(decoded.ContainsKey("digest-algorithm"),
                "0 is not a member of this enum, and the field is opt — the API prints no digest-algorithm");
            Assert.AreEqual("rsa", decoded["key-type"], "an opt enum whose value IS mapped is a normal value");
        }

        [TestMethod]
        public void AMappedValueOnTheSameFieldIsStillReported()
        {
            // The signed certificates on the same table carry u85=672, and the API prints sha256. The rule
            // must key on the value being outside the map, not on the field being optional.
            var decoded = Decode(Parse(CertificateWindow), new[] { 19, 1 },
                Rec((0x85, "u32", 672u), (0xFE0010, "str", "ca")));

            Assert.AreEqual("sha256", decoded["digest-algorithm"]);
        }

        [TestMethod]
        public void ANonOptEnumOutsideItsMapKeepsTheRawValue()
        {
            // Without opt, webfig renders the literal 'unknown' — it is not saying "not set", it is saying
            // the catalog and the router disagree. Dropping the field here would hide a stale .jg after a
            // RouterOS upgrade, so the raw text survives (and the codec traces it).
            var decoded = Decode(Parse(CertificateWindow), new[] { 19, 1 }, Rec((0x25, "u32", 9u)));

            Assert.AreEqual("9", decoded["trust-store"]);
        }

        // ── the rest of the list family renders like the list family ──────────

        // ppp.jg PPP Profile 'Address List' (multistring, its child a string with a dropdown source),
        // roteros.jg RoMON 'Secrets' (multistring of secrets) and DNS 'Dynamic Servers' (a plain `multi`
        // whose elements are addr compounds). None of the three had a case before: an empty one reached the
        // caller as the literal "[]" and dynamic-servers as the raw u32 behind each addr.
        private const string ListFamilyWindow =
            "[{name:'PPP Profile',type:'map',path:[ 70,1 ],c:[" +
            "{name:'Address List',type:'multistring',id:'S1e',c:[{type:'string',sorted:1," +
              "values:{type:'dynamic',path:[ 20,34 ]}}]}," +
            "{name:'Broadcast Addresses',type:'multiipaddr',id:'Ua',c:[{type:'ipaddr'}]}," +
            "{name:'Dynamic Servers',type:'multi',id:'Mb',ro:1,c:[{type:'addr',allow:'46v'}]}]}]";

        [TestMethod]
        public void AnEmptyListIsEmpty()
        {
            var decoded = Decode(Parse(ListFamilyWindow), new[] { 70, 1 },
                Rec((0x1E, "str[]", "[]"), (0xA, "u32[]", "[]")));

            Assert.AreEqual("", decoded["address-list"], "the API prints address-list= for a profile with none");
            Assert.AreEqual("", decoded["broadcast-addresses"]);
        }

        [TestMethod]
        public void AMultistringKeepsItsElementsAsText()
        {
            // The element's `values:{type:'dynamic'}` is the dropdown's SOURCE, not the wire form — a
            // multistring already carries text, so it must not be run through reference resolution the way
            // the log's topics (a multinumber of ids) is.
            var decoded = Decode(Parse(ListFamilyWindow), new[] { 70, 1 },
                Rec((0x1E, "str[]", "[allowed,blocked]")));

            Assert.AreEqual("allowed,blocked", decoded["address-list"]);
        }

        [TestMethod]
        public void AMultiipaddrElementIsUnpackedLikeAScalarIp()
        {
            // 17082560 == 0x0104A8C0, whose bytes ARE the quad in order — the same wire form a scalar ipaddr
            // uses, and the number the live audit captured for /ip/dns's first dynamic server.
            var decoded = Decode(Parse(ListFamilyWindow), new[] { 70, 1 },
                Rec((0xA, "u32[]", "[17082560]")));

            Assert.AreEqual("192.168.4.1", decoded["broadcast-addresses"]);
        }

        [TestMethod]
        public void AMultiOfAddrCompoundsRendersEachAddress()
        {
            // types.multi.tostr hands each element to the CHILD's tostr — here types.addr.tostr, which reads
            // the compound by sub-key. Rendering the submessage generically gave the raw u32 instead
            // (/ip/dns dynamic-servers read '17082560,3445500682' where the API says the addresses).
            var elements = new List<Dictionary<int, Tuple<string, object>>>
            {
                new Dictionary<int, Tuple<string, object>> { [0xFEFF20] = Tuple.Create("u32", (object)17082560u) },
                new Dictionary<int, Tuple<string, object>> { [0xFEFF20] = Tuple.Create("u32", (object)33859776u) },
            };
            var decoded = Decode(Parse(ListFamilyWindow), new[] { 70, 1 },
                Rec((0xB, "msg[]", elements)));

            Assert.AreEqual("192.168.4.1,192.168.4.2", decoded["dynamic-servers"]);
        }

        // ── a macaddr arrives as hex text, not as bytes ────────────────────────

        private const string MacWindow =
            "[{name:'ARP',type:'map',path:[ 20,5 ],c:[" +
            "{name:'MAC Address',type:'macaddr',id:'r2',opt:1}," +
            "{name:'Name',type:'string',id:'sfe0010'}]}]";

        [TestMethod]
        public void AMacAddressIsGroupedIntoOctets()
        {
            // M2Message renders an FT_RAW value as unseparated uppercase hex, never as a byte[] — so the
            // macaddr case had a decoder that no live value ever reached, and /interface/ethernet, /ip/arp,
            // /ip/neighbor and /tool/romon all reported one 12-digit run.
            var decoded = Decode(Parse(MacWindow), new[] { 20, 5 },
                Rec((0x2, "raw", "48EA62D0AD17")));

            Assert.AreEqual("48:EA:62:D0:AD:17", decoded["mac-address"]);
        }

        [TestMethod]
        public void AnAlreadySeparatedMacIsLeftAlone()
        {
            var decoded = Decode(Parse(MacWindow), new[] { 20, 5 },
                Rec((0x2, "raw", "48:EA:62:D0:AD:17")));

            Assert.AreEqual("48:EA:62:D0:AD:17", decoded["mac-address"]);
        }

        [TestMethod]
        public void ANonHexMacValueIsNotRegrouped()
        {
            // The regrouping keys on the value being hex, so a field that answers with something else is
            // reported as it came rather than being sliced into pairs.
            var decoded = Decode(Parse(MacWindow), new[] { 20, 5 },
                Rec((0x2, "raw", "unknown")));

            Assert.AreEqual("unknown", decoded["mac-address"]);
        }
    }
}
