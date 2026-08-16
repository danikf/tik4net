// WinboxKeyCollisionAndCompoundDecodeTests.cs — router-free tests for the last A11 decode classes: a key
// two fields share, a list element that is a union or a tuple, a `multibits` bitmask, an `enm` carrying a
// unit postfix, an address the API prints joined to its port, and a name two windows on one handler claim.
//
// Every expectation below is what RouterOS's own API prints for the same record on 7.23.2, and every .jg
// fragment is the real declaration cut down to what the decode reads. Run against the code as it was before
// this change, all six fail.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxKeyCollisionAndCompoundDecodeTests
    {
        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static Dictionary<string, string> Decode(
            WinboxJgCatalog catalog, int[] handler, Dictionary<int, Tuple<string, object>> rec,
            string apiPath = null, string windowKey = null)
        {
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog,
                new Dictionary<string, int>(), false, windowKey);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static Dictionary<int, Tuple<string, object>> Rec(params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return rec;
        }

        private static Dictionary<int, Tuple<string, object>> Sub(params (int key, string type, object val)[] fields)
            => Rec(fields);

        // ── one key, two fields ────────────────────────────────────────────────

        // dhcp.jg, DHCP Client [43,1]: 'Add Default Route' is u12 on the DHCP tab and 'DHCP Options' is U12
        // on the Advanced tab — the SAME M2 key 0x12, told apart by nothing but the wire type. The router
        // sends both in one record (confirmed with a duplicate-tolerant TLV dump on 7.23.2:
        // `0x12 u32[] [4294967286,4294967285]` and `0x12 u8 1`).
        private const string DhcpClientWindow =
            "[{name:'DHCP Client',type:'map',path:[ 43,1 ],c:[" +
            "{name:'Add Default Route',type:'enm',id:'u12',def:1," +
              "values:{type:'static',map:[ 'no','yes','special classless' ]}}," +
            "{name:'DHCP Options',type:'multinumber',id:'U12',c:[{type:'number'}]}]}]";

        [TestMethod]
        public void TwoFieldsSharingOneKeyAreToldApartByArrayness()
        {
            var rec = M2Message.ParseAllFields(BuildRecord(
                U32Array(0x12, 4294967286, 4294967285),
                U8(0x12, 1)));

            var fields = Decode(Parse(DhcpClientWindow), new[] { 43, 1 }, rec);

            Assert.AreEqual("yes", fields["add-default-route"],
                "the scalar u12 was dropped at parse time and its name went to the array");
            Assert.AreEqual("4294967286,4294967285", fields["dhcp-options"]);
        }

        [TestMethod]
        public void TheOrDERTheRouterSendsThemInDoesNotDecideWhichIsWhich()
        {
            // The same record with the two fields the other way round on the wire. Whichever arrives first
            // takes the plain key and the other is filed under its qualified one, so the answer must not move.
            var rec = M2Message.ParseAllFields(BuildRecord(
                U8(0x12, 1),
                U32Array(0x12, 4294967286, 4294967285)));

            var fields = Decode(Parse(DhcpClientWindow), new[] { 43, 1 }, rec);

            Assert.AreEqual("yes", fields["add-default-route"]);
            Assert.AreEqual("4294967286,4294967285", fields["dhcp-options"]);
        }

        // ── list elements: union and tuple ─────────────────────────────────────

        // roteros.jg, SNMP Community [34,1]: a `multi` whose element is a union of an IPv4 network (u8 with
        // netmask u9) and an IPv6 one (a16 with PREFIX LENGTH u17). The router's default row is the IPv6
        // any-address, which the API prints '::/0'.
        private const string SnmpCommunityWindow =
            "[{name:'SNMP Community',type:'map',path:[ 34,1 ],c:[" +
            "{name:'Name',type:'string',id:'s5'}," +
            "{name:'Addresses',type:'multi',id:'M1b',c:[{type:'union',single:1,c:[" +
              "{type:'network',id:'u8',maskid:'u9'},{type:'network6',id:'a16',deflen:0,maskid:'u17'}]}]}]}]";

        [TestMethod]
        public void AUnionElementKeepsThePrefixLengthOfTheMemberItCarries()
        {
            var v6 = Sub((0x16, "ip6", new byte[16]), (0x17, "u32", (uint)0));
            var v4 = Sub((0x8, "u32", 0xEC04A8C0u), (0x9, "u32", 0x00FFFFFFu));   // 192.168.4.236 / 255.255.255.0
            var rec = Rec((0x1B, "msg[]", new List<Dictionary<int, Tuple<string, object>>> { v6, v4 }));

            var fields = Decode(Parse(SnmpCommunityWindow), new[] { 34, 1 }, rec);

            Assert.AreEqual("::/0,192.168.4.236/24", fields["addresses"],
                "the generic nested-message fallback returned the address and dropped the length");
        }

        // secure.jg, Certificate: a `multi` whose element is a TUPLE joined by ':' — an enm naming the family
        // and a union carrying the value. The API prints 'IP:192.168.4.236'.
        private const string CertificateWindow =
            "[{name:'Certificate',type:'map',path:[ 9,1 ],c:[" +
            "{name:'Name',type:'string',id:'sfe0010'}," +
            "{name:'Subject Alt. Name',type:'multi',id:'M84',c:[{type:'tuple',sep:':',separate:1,c:[" +
              "{name:'Subject Alt. Name Type',type:'enm',id:'u7f',values:{type:'static',map:{1:'IP',2:'DNS',3:'Email'}}}," +
              "{name:'Subject Alt. Name',type:'union',single:1,c:[" +
                "{type:'ip6addr',id:'a7e'},{type:'ipaddr',id:'u7d'},{type:'string',id:'s7c'}]}]}]}]}]";

        [TestMethod]
        public void ATupleElementJoinsItsPartsWithTheDeclaredSeparator()
        {
            var alt = Sub((0x7F, "u32", (uint)1), (0x7D, "u32", 0xEC04A8C0u));
            var rec = Rec((0x84, "msg[]", new List<Dictionary<int, Tuple<string, object>>> { alt }));

            var fields = Decode(Parse(CertificateWindow), new[] { 9, 1 }, rec);

            // The API prints 'IP:192.168.4.236'. The case difference is the label normalizer, which lowercases
            // every enum label in the catalog (it is also why an SNMP community's authentication-protocol
            // reads 'md5' where the API says 'MD5') — a separate, deliberate, catalog-wide rule, and the one
            // the path-map audit compares case-insensitively for. What this test is about is the SHAPE.
            Assert.AreEqual("ip:192.168.4.236", fields["subject-alt-name"],
                "the element reached the caller as the bare u32 of its second part");
        }

        // ── multibits ──────────────────────────────────────────────────────────

        // roteros.jg, /ip/neighbor: 'System Caps' is a `multibits` — a BITMASK over the map's indices, not a
        // member of it (types.multibits.get: `for(i=0..31) if(val&(1<<i)) push(i)`).
        private const string NeighborWindow =
            "[{name:'Neighbor',type:'map',path:[ 33,1 ],c:[" +
            "{name:'System Caps',type:'multibits',id:'u11',c:[{type:'enm',values:{type:'static'," +
              "map:{0:'other',1:'repeater',2:'bridge',3:'wlan-ap',4:'router'}}}]}]}]";

        [TestMethod]
        public void AMultibitsOfZeroIsNoFlagsAndNotTheMembersAtIndexZero()
        {
            var none = Decode(Parse(NeighborWindow), new[] { 33, 1 }, Rec((0x11, "u32", (uint)0)));
            Assert.AreEqual("", none["system-caps"], "0 is an EMPTY set; 'other' is the member at BIT 0");

            var some = Decode(Parse(NeighborWindow), new[] { 33, 1 }, Rec((0x11, "u32", (uint)0b10101)));
            Assert.AreEqual("other,bridge,router", some["system-caps"]);
        }

        // ── an enm's unit postfix ──────────────────────────────────────────────

        // secure.jg, IPsec Profile [85,6]: 'DPD Interval' is an enm whose only member is 'disable DPD' at 0,
        // with a `number` child and postfix:'s'. The API prints '8s' and 'disable-dpd'.
        private const string IpsecProfileWindow =
            "[{name:'IPsec Profile',type:'map',path:[ 85,6 ],c:[" +
            "{name:'DPD Interval',type:'enm',id:'u8',def:8,postfix:'s'," +
              "values:{type:'static',map:[ 'disable DPD' ]},c:[{type:'number',max:3600}]}]}]";

        [TestMethod]
        public void AnEnmWithASecondsPostfixRendersItsFallThroughValueAsADuration()
        {
            var catalog = Parse(IpsecProfileWindow);

            Assert.AreEqual("8s", Decode(catalog, new[] { 85, 6 }, Rec((0x8, "u32", (uint)8)))["dpd-interval"],
                "webfig paints the unit beside the box; the API puts it in the value");
            Assert.AreEqual("2m", Decode(catalog, new[] { 85, 6 }, Rec((0x8, "u32", (uint)120)))["dpd-interval"]);
            Assert.AreEqual("disable-dpd",
                Decode(catalog, new[] { 85, 6 }, Rec((0x8, "u32", (uint)0)))["dpd-interval"],
                "a value the map DOES name is not a number, so the postfix has nothing to say about it");
        }

        // ── address:port ───────────────────────────────────────────────────────

        // roteros.jg, HotSpot Server Profile: 'HTTP Proxy' (u83) sits beside 'HTTP Proxy Port' (u84) exactly
        // as 'SMTP Server' (u87) sits beside nothing — and the API prints http-proxy=0.0.0.0:0 against
        // smtp-server=0.0.0.0. The pairing is shipped per path in WinboxFieldResolver.
        private const string HotspotProfileWindow =
            "[{name:'HotSpot Server Profile',type:'map',path:[ 55,2 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'HTTP Proxy',type:'ipaddr',id:'u83',opt:1}," +
            "{name:'HTTP Proxy Port',type:'number',id:'u84',max:65535}," +
            "{name:'SMTP Server',type:'ipaddr',id:'u87',opt:1}]}]";

        [TestMethod]
        public void TheApiPrintsTheHotspotProxyAsAddressAndPortInOneField()
        {
            var rec = Rec((0x83, "u32", 0x0100007Fu), (0x84, "u32", (uint)3128), (0x87, "u32", (uint)0));

            var fields = Decode(Parse(HotspotProfileWindow), new[] { 55, 2 }, rec, "/ip/hotspot/profile");

            Assert.AreEqual("127.0.0.1:3128", fields["http-proxy"]);
            Assert.IsFalse(fields.ContainsKey("http-proxy-port"),
                "the port is half of one API field, not a field of its own");
            Assert.AreEqual("0.0.0.0", fields["smtp-server"],
                "an address with no port sibling is untouched");
        }

        // ── one name, two windows ──────────────────────────────────────────────

        // roteros.jg: 'NTP Client' and 'NTP Server' are two type:'item' windows on the SAME handler [47,1],
        // and each has an 'Enabled' — b4 and b6. The singleton record carries both, so a key→name map that
        // let both keys be called 'enabled' answered with whichever the RECORD listed first.
        private const string NtpWindows =
            "[{name:'NTP Client',group:'System',c:[{title:'NTP Client',type:'item',path:[ 47,1 ],c:[" +
              "{name:'Enabled',type:'bool',id:'b4'}," +
              "{name:'Mode',type:'enm',id:'u1',values:{type:'static',map:{0:'unicast',1:'broadcast'}}}]}]}," +
            "{name:'NTP Server',group:'System',c:[{title:'NTP Server',type:'item',path:[ 47,1 ],c:[" +
              "{name:'Enabled',type:'bool',id:'b6'}," +
              "{name:'Broadcast',type:'bool',id:'b7'}]}]}]";

        [TestMethod]
        public void TheWindowAPathResolvesToOwnsTheNamesItClaims()
        {
            var catalog = Parse(NtpWindows);
            var rec = Rec((0x4, "bool", true), (0x6, "bool", false),
                          (0x1, "u32", (uint)0), (0x7, "bool", false));

            var server = Decode(catalog, new[] { 47, 1 }, rec,
                "/system/ntp/server", "/system/ntp-server/ntp-server");
            Assert.AreEqual("false", server["enabled"],
                "b4 is the CLIENT's enabled; the record carries both and field order was deciding");

            var client = Decode(catalog, new[] { 47, 1 }, rec,
                "/system/ntp/client", "/system/ntp-client/ntp-client");
            Assert.AreEqual("true", client["enabled"]);
        }

        // ── wire helpers: build a real M2 message so ParseAllFields is exercised ──

        private static byte[] BuildRecord(params byte[][] fields) => M2Message.BuildM2(fields);

        private static byte[] U8(int key, byte value)
            => new byte[] { (byte)(key & 0xFF), (byte)((key >> 8) & 0xFF), (byte)((key >> 16) & 0xFF), 0x09, value };

        private static byte[] U32Array(int key, params uint[] values)
        {
            var b = new List<byte>
            {
                (byte)(key & 0xFF), (byte)((key >> 8) & 0xFF), (byte)((key >> 16) & 0xFF), 0x88,
                (byte)(values.Length & 0xFF), (byte)((values.Length >> 8) & 0xFF),
            };
            foreach (uint v in values) b.AddRange(BitConverter.GetBytes(v));
            return b.ToArray();
        }
    }
}
