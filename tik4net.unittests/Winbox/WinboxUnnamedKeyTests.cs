// WinboxUnnamedKeyTests.cs — fields the router sends that no .jg window names, or names differently.
//
// Each pairing was measured on RouterOS 7.24.2 by setting the value over the API and naming the one M2 key that
// moved, or by the one key holding the API's value on every row. Router-free: the catalog, the resolver and the
// codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxUnnamedKeyTests
    {
        private static WinboxFieldResolver Resolver(string catalogText, string apiPath, int[] handler)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            return new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
        }

        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler,
            params (int key, string wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = fields.ToDictionary(f => f.key, f => Tuple.Create(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.BuildKeyToApiName(),
                resolver.BuildKeyToField(), resolver.DerivedBoolFields);
        }

        private static Dictionary<int, Tuple<string, object>> Written(WinboxFieldResolver resolver, string name, string value)
        {
            var back = new Dictionary<int, Tuple<string, object>>();
            foreach (var piece in resolver.EncodeField(name, value))
                foreach (var kv in M2Message.ParseAllFields(M2Message.BuildM2(M2Message.SysFrom(), piece)))
                    back[kv.Key] = kv.Value;
            return back;
        }

        [TestMethod]
        public void KeysNoWindowDeclaresReadAsTheApiNamesThem()
        {
            Assert.AreEqual("short", Decode("[]", "/interface/ethernet", new[] { 20, 0 }, (0x3F5, "u32", (object)0u))["cable-settings"]);

            var l2tp = Decode("[]", "/interface/l2tp-server/server", new[] { 27, 31 },
                (0xDF, "u32", (object)2u), (0xDE, "u32", (object)5u));
            Assert.AreEqual("l2tpv2", l2tp["accept-proto-version"]);
            Assert.AreEqual("ether", l2tp["accept-pseudowire-type"]);

            Assert.AreEqual("false", Decode("[]", "/interface/bridge", new[] { 20, 0 }, (0x65, "bool", (object)false))["auto-mac"]);
            Assert.AreEqual("0x1818", Decode("[]", "/ip/settings", new[] { 20, 17 }, (0x1C, "u32", (object)6168u))["icmp-rate-mask"]);
            Assert.AreEqual("true", Decode("[]", "/ip/firewall/connection/tracking", new[] { 20, 17 }, (0x2F, "bool", (object)true))["active-ipv6"]);
            Assert.AreEqual("true", Decode("[]", "/ip/ipsec/peer", new[] { 85, 4 }, (0xE, "bool", (object)true))["responder"]);

            var peer = Decode("[]", "/interface/wireguard/peers", new[] { 20, 110 },
                (0x3F3, "u64", (object)1234ul), (0x3F4, "u64", (object)5678ul));
            Assert.AreEqual("1234", peer["rx"]);
            Assert.AreEqual("5678", peer["tx"]);

            Assert.AreEqual("actual-interface",
                Resolver("[]", "/ip/address", new[] { 20, 1 }).BuildKeyToApiName()[0x5]);
        }

        [TestMethod]
        public void TheKeysThatAreWritableWriteBackTheValueTheyRead()
        {
            var ether = Written(Resolver("[]", "/interface/ethernet", new[] { 20, 0 }), "cable-settings", "standard");
            Assert.AreEqual("1", ether[0x3F5].Item2.ToString());

            var mask = Written(Resolver("[]", "/ip/settings", new[] { 20, 17 }), "icmp-rate-mask", "0x1819");
            Assert.AreEqual("6169", mask[0x1C].Item2.ToString(), "the API's own hex spelling is what a caller hands back");
        }

        [TestMethod]
        public void LabelsTheWindowSpellsDifferentlyAnswerToTheApiName()
        {
            const string peers =
                "[{name:'WireGuard',c:[{name:'Peers',title:'Peers',type:'map',path:[ 20,110 ],c:[" +
                "{name:'Endpoint',type:'addr',id:'m3ee',allow:'46D',opt:1}]}]}]";
            Assert.AreEqual(0x3EE, Resolver(peers, "/interface/wireguard/peers", new[] { 20, 110 }).ResolveKey("endpoint-address"));

            const string lease =
                "[{name:'DHCP',c:[{title:'Leases',type:'map',path:[ 23,1 ],c:[" +
                "{name:'DHCP Options',type:'multinumber',id:'U11',c:[{type:'enm',values:{type:'dynamic',path:[ 23,6 ]}}]}]}]}]";
            Assert.IsTrue(Resolver(lease, "/ip/dhcp-server/lease", new[] { 23, 1 }).BuildKeyToApiName().Values.Contains("dhcp-option"));

            const string provisioning =
                "[{name:'CAPsMAN',c:[{title:'Provisioning',type:'map',path:[ 88,154 ],c:[" +
                "{name:'Slave Configuration',type:'multinumber',id:'U500c',c:[{type:'enm',values:{type:'dynamic',path:[ 88,152 ]}}]}]}]}]";
            Assert.IsTrue(Resolver(provisioning, "/caps-man/provisioning", new[] { 88, 154 }).BuildKeyToApiName().Values.Contains("slave-configurations"));

            const string tree =
                "[{name:'Queues',c:[{name:'Queue Tree',title:'Queue Tree',type:'map',path:[ 20,12 ],c:[" +
                "{name:'Avg. Rate',type:'bitrate',id:'ucc',ro:1},{name:'Avg. Packet Rate',type:'decimal',id:'ucd',ro:1}]}]}]";
            var names = Resolver(tree, "/queue/tree", new[] { 20, 12 }).BuildKeyToApiName();
            Assert.AreEqual("rate", names[0xCC]);
            Assert.AreEqual("packet-rate", names[0xCD]);
        }

        [TestMethod]
        public void AnActivePeerNamesItsSpisAndSaysWhetherItResponds()
        {
            // 7.24.2, an initiating peer: the API printed side=initiator responder=false spii=ef035cfdda65233b
            // spir=0000000000000000, and the record carried b5=false with the two SPIs as strings at 0x15/0x16.
            const string peers =
                "[{name:'IPsec',c:[{name:'IPsec Remote Peer',title:'Active Peers',type:'map',path:[ 85,9 ],ro:1,c:[" +
                "{name:'Side',type:'enm',id:'b5',opt:1,ro:1,values:{type:'static',map:[ 'initiator','responder' ]}}," +
                "{name:'Responder',type:'flag',id:'b5',hint:'R'}]}]}]";

            var initiator = Decode(peers, "/ip/ipsec/active-peers", new[] { 85, 9 },
                (0x5, "bool", (object)false), (0x15, "string", (object)"ef035cfdda65233b"), (0x16, "string", (object)"0000000000000000"));
            Assert.AreEqual("false", initiator["responder"]);
            Assert.AreEqual("ef035cfdda65233b", initiator["spii"]);
            Assert.AreEqual("0000000000000000", initiator["spir"]);

            var responder = Decode(peers, "/ip/ipsec/active-peers", new[] { 85, 9 }, (0x5, "bool", (object)true));
            Assert.AreEqual("true", responder["responder"]);
        }

        [TestMethod]
        public void APackageIsAvailableExactlyWhenItIsNotInstalled()
        {
            const string package =
                "[{name:'System',c:[{name:'Package',title:'Packages',type:'map',path:[ 24,23 ],c:[" +
                "{name:'Installed',type:'bool',id:'bb'},{name:'available',type:'flag',id:'bb',hint:'A',inv:1}]}]}]";
            Assert.AreEqual("false", Decode(package, "/system/package", new[] { 24, 23 }, (0xB, "bool", (object)true))["available"]);
            Assert.AreEqual("true", Decode(package, "/system/package", new[] { 24, 23 }, (0xB, "bool", (object)false))["available"]);
        }

        [TestMethod]
        public void TheRowsNoteIsItsAbout()
        {
            var noted = Decode("[]", "/ip/dhcp-server", new[] { 23, 0 }, (0xFE001C, "str[]", (object)"[No IP address on interface]"));
            Assert.AreEqual("No IP address on interface", noted[".about"]);

            var silent = Decode("[]", "/ip/dhcp-server", new[] { 23, 0 }, (0xFE001C, "str[]", (object)"[]"));
            Assert.IsFalse(silent.ContainsKey(".about"), "an empty note is no .about at all");
        }

        [TestMethod]
        public void TheUnsetMarkerIsTheWordTheApiPrintsForIt()
        {
            const string manager =
                "[{name:'CAPsMAN',c:[{name:'Manager',title:'Manager',type:'item',path:[ 88,155 ],c:[" +
                "{name:'Certificate',type:'enm',id:'u2',def:4294967295,opt:1,values:{type:'defenum',defid:0,defname:'auto',values:{type:'dynamic',path:[ 19,1 ]}}}]}]}]";
            Assert.AreEqual("none", Decode(manager, "/caps-man/manager", new[] { 88, 155 }, (0x2, "u32", (object)4294967295u))["certificate"]);
            var written = Written(Resolver(manager, "/caps-man/manager", new[] { 88, 155 }), "certificate", "none");
            Assert.AreEqual("4294967295", written[0x2].Item2.ToString());

            const string certificates =
                "[{name:'System',c:[{name:'Certificates',title:'Certificates',type:'map',path:[ 19,1 ],c:[" +
                "{name:'Trust Store',type:'set',id:'u2d',def:4294967295,values:{type:'static',map:{1:'ipsec',2:'wpa-eap'}}}]}]}]";
            Assert.AreEqual("all", Decode(certificates, "/certificate", new[] { 19, 1 }, (0x2D, "u32", (object)4294967295u))["trust-store"]);
        }
    }
}
