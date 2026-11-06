// WinboxRouterOs6FieldScopeTests.cs — names a WinBox native print reported on RouterOS 6 that the API's print does not
// (WinboxNativeFieldScopeSweepTest on CHR2, 6.49.21), each against the API's print, print detail and get there:
//   /ip/settings, /interface/bridge/settings  max-entries   the connection list's statusbar field on [20,17]
//   /ip/arp                       dhcp                      the API spells it DHCP on 6.49.21, dhcp on 7.21.5 and 7.25
//   /routing/ospf/area            inactive                  the API's invalid on 6.49.x, inactive on 7.24
//   /ip/hotspot/user              server                    left out at all on 6.49.21, printed on 7.21.5 and 7.25
//   /routing/bgp/peer             remote-port, update-source, cisco-vpls-nlri-length-format
//                                                           left out while unset; the last is the API's cisco-vpls-nlri-len-fmt
// The catalogs are trimmed from the 6.49.21 and 7.25 .jg. Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRouterOs6FieldScopeTests
    {
        private static readonly Version Ros6 = new Version(6, 49, 21);
        private static readonly Version Ros7 = new Version(7, 25);

        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler, string windowSuffix,
            Version version, params (int key, M2Type wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            string windowKey = windowSuffix == null ? null
                : catalog.GetDerivedPaths().Keys.FirstOrDefault(k => k.EndsWith(windowSuffix, StringComparison.Ordinal));
            if (windowSuffix != null) Assert.IsNotNull(windowKey, "the window must be harvested under a derived path");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                windowKey: windowKey, routerVersion: version);
            var rec = fields.ToM2Record(f => f.key, f => new M2Value(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.DecodePlan());
        }

        // ── max-entries: a statusbar reading another window's handler ────────

        // 6.49.21: the connection list's statusbar shows [20,17]'s 'Max Entries'; Tracking does not declare it.
        private const string ConntrackWindows6 =
            "[{name:'IP',c:[{name:'Firewall',c:[" +
            "{name:'Firewall Connection',title:'Connections',type:'map',path:[ 20,12 ],c:[" +
            "{name:'Src. Address',type:'ipaddr',id:'u1'}," +
            "{type:'statusbar',path:[ 20,17 ],c:[{name:'Max Entries',type:'number',id:'ue'}]}]}," +
            "{name:'Connection Tracking',title:'Tracking',type:'item',path:[ 20,17 ],c:[" +
            "{name:'Generic Timeout',type:'interval',id:'ud',scale:100}]}]}," +
            "{name:'Settings',title:'IP Settings',type:'item',path:[ 20,17 ],c:[" +
            "{name:'IP Forward',type:'bool',id:'b12'}]}]}]";

        // 7.25: the same, with 'Max Entries' on the Tracking window too.
        private const string ConntrackWindows7 =
            "[{name:'IP',c:[{name:'Firewall',c:[" +
            "{name:'Firewall Connection',title:'Connections',type:'map',path:[ 20,12 ],c:[" +
            "{name:'Src. Address',type:'ipaddr',id:'u1'}," +
            "{type:'statusbar',path:[ 20,17 ],c:[{name:'Max Entries',type:'number',id:'ue'}]}]}," +
            "{name:'Connection Tracking',title:'Tracking',type:'item',path:[ 20,17 ],c:[" +
            "{name:'Generic Timeout',type:'interval',id:'ud',scale:100}," +
            "{name:'Max Entries',type:'number',id:'ue',ro:1}]}]}," +
            "{name:'Settings',title:'IP Settings',type:'item',path:[ 20,17 ],c:[" +
            "{name:'IP Forward',type:'bool',id:'b12'}]}]}]";

        private static readonly (int, M2Type, object)[] ConntrackRecord =
        {
            (0x12, M2Type.Bool, true), (0xD, M2Type.U32, 60000u), (0xE, M2Type.U32, 1048576u),
        };

        [TestMethod]
        public void AStatusbarsFieldIsNotTheFieldOfEveryWindowOnItsHandler()
        {
            foreach (string jg in new[] { ConntrackWindows6, ConntrackWindows7 })
            {
                var settings = Decode(jg, "/ip/settings", new[] { 20, 17 }, "/settings", Ros6, ConntrackRecord);
                Assert.IsFalse(settings.ContainsKey("max-entries"), "connection tracking's count is not an IP setting");
                Assert.AreEqual("true", settings["ip-forward"]);
            }
        }

        [TestMethod]
        public void TrackingKeepsMaxEntriesWhetherOrNotItsWindowDeclaresIt()
        {
            foreach (string jg in new[] { ConntrackWindows6, ConntrackWindows7 })
            {
                var tracking = Decode(jg, "/ip/firewall/connection/tracking", new[] { 20, 17 }, "/connection-tracking",
                    Ros6, ConntrackRecord);
                Assert.AreEqual("1048576", tracking["max-entries"], "the API's tracking print has it on 6.49.21 and 7.25");
            }
        }

        // ── /ip/arp: the flag the API spells by version ──────────────────────

        private const string Arp =
            "[{name:'IP',c:[{name:'ARP',title:'ARP List',type:'map',path:[ 20,8 ],c:[" +
            "{name:'IP Address',type:'ipaddr',id:'u1'},{name:'DHCP',type:'flag',id:'b4',hint:'H'}]}]}]";

        private static Dictionary<string, string> DecodeArp(Version version)
            => Decode(Arp, "/ip/arp", new[] { 20, 8 }, "/arp", version,
                (0x1, M2Type.U32, (object)0x0104A8C0u), (0x4, M2Type.Bool, (object)false));

        [TestMethod]
        public void TheArpDhcpFlagIsSpelledAsTheRoutersApiSpellsIt()
        {
            CollectionAssert.Contains(DecodeArp(Ros6).Keys.ToList(), "DHCP");
            CollectionAssert.Contains(DecodeArp(new Version(7, 21, 5)).Keys.ToList(), "dhcp");
            CollectionAssert.Contains(DecodeArp(null).Keys.ToList(), "dhcp", "with no version, the catalog's spelling");
            Assert.AreEqual("false", DecodeArp(Ros6)["dhcp"], "one entry, whichever case it is read by");
        }

        // ── /routing/ospf/area: the state flag's two words ──────────────────

        private const string OspfArea6 =
            "[{name:'Routing',c:[{name:'OSPF Area',title:'Areas',type:'map',path:[ 44,122 ],c:[" +
            "{name:'Area Name',type:'string',id:'s2'},{type:'invalid'}]}]}]";

        [TestMethod]
        public void TheOspfAreasStateFlagIsTheWordItsVersionPrints()
        {
            var ros6 = Decode(OspfArea6, "/routing/ospf/area", new[] { 44, 122 }, null, Ros6,
                (0x2, M2Type.String, (object)"backbone"), (0xFE0008, M2Type.Bool, (object)false));
            Assert.AreEqual("false", ros6["invalid"]);
            Assert.IsFalse(ros6.ContainsKey("inactive"), "6.49.21's API has no inactive");

            var ros7 = Decode(OspfArea6, "/routing/ospf/area", new[] { 44, 122 }, null, Ros7,
                (0x2, M2Type.String, (object)"backbone"), (0xFE0008, M2Type.Bool, (object)false));
            Assert.AreEqual("false", ros7["inactive"]);
            Assert.IsFalse(ros7.ContainsKey("invalid"));
        }

        // ── /ip/hotspot/user: server on the shipped row ─────────────────────

        private const string HotspotUser =
            "[{name:'IP',c:[{name:'Hotspot User',title:'Users',type:'map',path:[ 63,1 ],c:[" +
            "{name:'def',type:'bool',id:'bfe000d',nonpublic:1}," +
            "{name:'Server',type:'enm',id:'uc',on:'nondef',values:{type:'defenum',defid:0,defname:'all',values:{type:'dynamic',path:[ 63,11 ]}}}," +
            "{name:'Name',type:'string',id:'s1'}]}]}]";

        private static Dictionary<string, string> DecodeDefaultTrial(Version version)
            => Decode(HotspotUser, "/ip/hotspot/user", new[] { 63, 1 }, "/hotspot-user", version,
                (0xFE0001, M2Type.U32, (object)0u), (0xFE000D, M2Type.Bool, (object)true),
                (0xC, M2Type.U32, (object)0u), (0x1, M2Type.String, (object)"default-trial"));

        [TestMethod]
        public void AnUnsetHotspotUserServerIsLeftOutOnRouterOs6Only()
        {
            Assert.IsFalse(DecodeDefaultTrial(Ros6).ContainsKey("server"), "6.49.21 leaves server=all out");
            Assert.AreEqual("all", DecodeDefaultTrial(new Version(7, 21, 5))["server"], "7.21.5 prints it");
            Assert.AreEqual("all", DecodeDefaultTrial(Ros7)["server"], "7.25 prints it");
            Assert.AreEqual("all", DecodeDefaultTrial(null)["server"], "with no version the field stays");
        }

        // ── /routing/bgp/peer (RouterOS 6 only) ──────────────────────────────

        private const string BgpPeer =
            "[{name:'Routing',c:[{name:'BGP Peer',title:'Peers',type:'map',path:[ 44,102 ],c:[" +
            "{name:'Name',type:'string',id:'sfe0010'}," +
            "{name:'Remote Port',type:'number',id:'ue',def:179,opt:1}," +
            "{name:'Update Source',type:'union',opt:1,single:1,c:[{type:'ipaddr',id:'u1b'},{type:'ip6addr',id:'a1c'}," +
            "{type:'enm',id:'u1d',values:{type:'defenum',defid:4294967295,defname:'none',values:{type:'dynamic',path:[ 20,0 ]}}}]}," +
            "{name:'Cisco VPLS NLRI Length Format',type:'enm',id:'u27',def:0,values:{type:'static'," +
            "map:{0:'auto bits',1:'auto bytes',2:'bits',3:'bytes'}}}]}]}]";

        private static Dictionary<string, string> DecodePeer(uint port, uint nlri)
            => Decode(BgpPeer, "/routing/bgp/peer", new[] { 44, 102 }, "/bgp-peer", Ros6,
                (0xFE0010, M2Type.String, (object)"lab-bgp-chr"), (0xE, M2Type.U32, (object)port),
                (0x1D, M2Type.U32, (object)4294967295u), (0x27, M2Type.U32, (object)nlri));

        [TestMethod]
        public void AnUnsetBgpPeerPrintsNoRemotePortUpdateSourceOrNlriFormat()
        {
            // The lab peer's record, 6.49.21: 0xE=179, 0x1D=0xFFFFFFFF, 0x27=0; the API's print has none of the three.
            var row = DecodePeer(179u, 0u);
            foreach (string name in new[] { "remote-port", "update-source", "cisco-vpls-nlri-len-fmt", "cisco-vpls-nlri-length-format" })
                Assert.IsFalse(row.ContainsKey(name), name);
            Assert.AreEqual("lab-bgp-chr", row["name"]);
        }

        [TestMethod]
        public void TheBgpPeersNlriFormatIsTheApisLenFmt()
        {
            var row = DecodePeer(1179u, 2u);
            Assert.AreEqual("bits", row["cisco-vpls-nlri-len-fmt"]);
            Assert.IsFalse(row.ContainsKey("cisco-vpls-nlri-length-format"), "the label's spelling is not an API name");
            Assert.AreEqual("1179", row["remote-port"]);
        }
    }
}
