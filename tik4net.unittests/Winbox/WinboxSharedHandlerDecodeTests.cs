// WinboxSharedHandlerDecodeTests.cs — fields lost where several windows share one handler.
//
// Measured on RouterOS 7.24.2 over WinBox native against the binary API, each on a seeded row:
//   /ip/upnp          show-dummy-rule   b3 on the settings singleton; the interface list's 'Forced External IP'
//                                       {opt b3, ipaddr u4} consumed the key as its present-flag.
//   /interface/ipip   local-address     u3e9 in the 'IP Tunnel' window; the base /interface synthetic
//                                       mac-address took the key.
//   /interface/bonding arp-interval,    on the arp pane of the 'Link Monitoring' deck; the API prints them on a
//                     arp-ip-targets    mii bond too, and the codec dropped them as another kind's fields.
// Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxSharedHandlerDecodeTests
    {
        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler,
            string windowKey, params (int key, string wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                windowKey: windowKey);
            var rec = fields.ToDictionary(f => f.key, f => Tuple.Create(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField(), resolver.DerivedBoolFields);
        }

        [TestMethod]
        public void AFlagKeyAnotherWindowOwnsIsNotConsumed()
        {
            const string upnp =
                "[{name:'IP',c:[{name:'UPnP',title:'UPnP Settings',type:'item',path:[ 28,0 ],c:[" +
                "{name:'Enabled',type:'bool',id:'b1'},{name:'Show Dummy Rule',type:'bool',id:'b3'}]}," +
                "{name:'UPnP',title:'Interfaces',type:'map',path:[ 28,0 ],c:[" +
                "{name:'Interface',type:'enm',id:'u1',values:{type:'dynamic',path:[ 20,0 ]}}," +
                "{name:'Forced External IP',type:'opt',id:'b3',c:[{type:'ipaddr',id:'u4'}]}]}]}]";

            var decoded = Decode(upnp, "/ip/upnp", new[] { 28, 0 }, null,
                (0x1, "bool", (object)true), (0x3, "bool", (object)true));

            Assert.AreEqual("true", decoded["show-dummy-rule"],
                "the interface list's opt flag is not a reason to drop the settings' own field on that key");
        }

        [TestMethod]
        public void ASubtypeWindowBeatsTheBaseSetsSyntheticOnTheSameKey()
        {
            // An 'IP Tunnel' subtype window on the generic interface handler, declaring Local Address at u3e9 —
            // the key the base /interface set's synthetic calls mac-address.
            const string ipip =
                "[{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],generic:'iface'," +
                "c:[{name:'Name',type:'string',id:'s10006'},{name:'Type',type:'enm',id:'u10001',values:{type:'static',map:{21:'ipip'}}}]}," +
                "{name:'IP Tunnel',title:'IP Tunnel',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Local Address',type:'ipaddr',id:'u3e9',opt:1}]}]}]";

            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(ipip));
            string windowKey = catalog.GetDerivedPaths().Keys.FirstOrDefault(k => k.EndsWith("ip-tunnel", StringComparison.Ordinal));
            Assert.IsNotNull(windowKey, "the subtype window must be harvested under a derived path");

            var decoded = Decode(ipip, "/interface/ipip", new[] { 20, 0 }, windowKey,
                (0x3E9, "u32", (object)687891210u));

            Assert.AreEqual("10.99.0.41", decoded["local-address"]);
            Assert.IsFalse(decoded.ContainsKey("mac-address"), "the key is not a MAC on an IPIP tunnel");
        }

        [TestMethod]
        public void AnInterfaceSubtypeWithASetOfItsOwnKeepsTheBaseCounterNames()
        {
            // /interface/ethernet ships a set of its own; the /interface set's by-key counter names (0x100FC is
            // rx-byte) still apply to its rows, which are rows of the generic interface table.
            var decoded = Decode("[]", "/interface/ethernet", new[] { 20, 0 }, null,
                (0x100FC, "u64", (object)1234ul), (0x1000D, "bool", (object)false));

            Assert.AreEqual("1234", decoded["rx-byte"]);
            Assert.AreEqual("false", decoded["disable-running-check"], "and its own synthetic still applies");
        }

        [TestMethod]
        public void TheStateFlagIsNamedAsTheApiNamesItPerPath()
        {
            // 0xFE0008: `inactive` on the routing tables, `invalid` on a VRRP interface (7.24.2, seeded rows).
            var rule = Decode("[]", "/routing/rule", new[] { 44, 18 }, null, (0xFE0008, "bool", (object)true));
            Assert.AreEqual("true", rule["inactive"]);
            Assert.IsFalse(rule.ContainsKey("invalid"));

            var vrrp = Decode("[]", "/interface/vrrp", new[] { 20, 0 }, null,
                (0xFE0008, "bool", (object)true), (0x13, "string", (object)"none"));
            Assert.AreEqual("true", vrrp["invalid"]);
            Assert.AreEqual("none", vrrp["on-fail"], "a key no window declares, moved on a seeded row");
        }

        [TestMethod]
        public void AHotspotRowIsTheDefaultExactlyWhenItIsTheShippedRow()
        {
            var shipped = Decode("[]", "/ip/hotspot/user/profile", new[] { 63, 3 }, null, (0xFE0001, "u32", (object)0u));
            var added = Decode("[]", "/ip/hotspot/user/profile", new[] { 63, 3 }, null, (0xFE0001, "u32", (object)5u));

            Assert.AreEqual("true", shipped["default"]);
            Assert.AreEqual("false", added["default"]);
        }

        [TestMethod]
        public void KeysNoWindowDeclaresAreReadUnderTheApiNames()
        {
            // Each measured on 7.24.2: the value moved on a seeded row, or the one key holding the API's value.
            var resource = Decode("[]", "/system/resource", new[] { 24, 2 }, null, (0x1B, "string", (object)"MikroTik"));
            Assert.AreEqual("MikroTik", resource["platform"]);

            var ether = Decode("[]", "/interface/ethernet", new[] { 20, 0 }, null,
                (0x404, "raw", (object)new byte[] { 0x02, 0x00, 0x00, 0xAA, 0xBB, 0xCC }));
            Assert.AreEqual("02:00:00:AA:BB:CC", ether["orig-mac-address"]);

            var server = Decode("[]", "/ip/dhcp-server", new[] { 23, 0 }, null,
                (0x1E, "str[]", (object)"[t4n-dsal]"));
            Assert.AreEqual("t4n-dsal", server["address-lists"]);
        }

        [TestMethod]
        public void ALeaseIsBlockedExactlyWhenItsAccessIsBlocked()
        {
            const string leases =
                "[{name:'DHCP',c:[{title:'Leases',type:'map',path:[ 23,1 ],c:[" +
                "{name:'Block Access',type:'bool',id:'b7e'}]}]}]";

            var blocked = Decode(leases, "/ip/dhcp-server/lease", new[] { 23, 1 }, null, (0x7E, "bool", (object)true));
            var open = Decode(leases, "/ip/dhcp-server/lease", new[] { 23, 1 }, null, (0x7E, "bool", (object)false));

            Assert.AreEqual("true", blocked["blocked"]);
            Assert.AreEqual("false", open["blocked"]);
        }

        [TestMethod]
        public void ATunnelsKeepaliveIsItsOptionalTupleJoined()
        {
            // EoIP Tunnel, 7.24.2: {name:'Keepalive',type:'opt',id:'b7d4',c:[{type:'tuple',sep:',',separate:1,
            // c:[{interval u7d5},{number u7d9}]}]}. The API prints keepalive=10s,10, and nothing while the option
            // is off; keepalive=7s,3 moved u7d5 to 7 and u7d9 to 3.
            const string eoip =
                "[{name:'Interfaces',c:[{name:'EoIP Tunnel',title:'EoIP Tunnel',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Keepalive',type:'opt',id:'b7d4',def:1,c:[{type:'tuple',sep:',',separate:1,c:[" +
                "{type:'interval',id:'u7d5',def:10,min:1},{type:'number',id:'u7d9',def:10,min:1}]}]}]}]}]";

            var on = Decode(eoip, "/interface/eoip", new[] { 20, 0 }, null,
                (0x7D4, "bool", (object)true), (0x7D5, "u32", (object)7u), (0x7D9, "u32", (object)3u));
            Assert.AreEqual("7s,3", on["keepalive"]);

            var off = Decode(eoip, "/interface/eoip", new[] { 20, 0 }, null,
                (0x7D4, "bool", (object)false), (0x7D5, "u32", (object)10u), (0x7D9, "u32", (object)10u));
            Assert.IsFalse(off.ContainsKey("keepalive"), "the option is off, so the API prints no keepalive");
        }

        [TestMethod]
        public void ATunnelsKeepaliveIsWrittenAsItsFlagAndBothParts()
        {
            const string eoip =
                "[{name:'Interfaces',c:[{name:'EoIP Tunnel',title:'EoIP Tunnel',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Keepalive',type:'opt',id:'b7d4',def:1,c:[{type:'tuple',sep:',',separate:1,c:[" +
                "{type:'interval',id:'u7d5',def:10,min:1},{type:'number',id:'u7d9',def:10,min:1}]}]}]}]}]";
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(eoip));
            var resolver = new WinboxFieldResolver("/interface/eoip", new[] { 20, 0 }, catalog,
                new Dictionary<string, int>());

            var back = new Dictionary<int, Tuple<string, object>>();
            foreach (var piece in resolver.EncodeField("keepalive", "7s,3"))
                foreach (var kv in M2Message.ParseAllFields(M2Message.BuildM2(M2Message.SysFrom(), piece)))
                    back[kv.Key] = kv.Value;

            Assert.AreEqual(true, back[0x7D4].Item2, "the option has to be switched on or the router ignores the parts");
            Assert.AreEqual("7", back[0x7D5].Item2.ToString());
            Assert.AreEqual("3", back[0x7D9].Item2.ToString());
        }

        [TestMethod]
        public void ABondKeepsTheFieldsOfTheOtherLinkMonitoringPane()
        {
            const string bonding =
                "[{name:'Interfaces',c:[{name:'Bonding',title:'Bonding',type:'map',path:[ 20,0 ],c:[" +
                "{name:'Link Monitoring',type:'enm',id:'u7d4',def:2,values:{type:'static',map:[ 'none','arp','mii' ]}}," +
                "{type:'deck',panes:[{vals:[ 1 ],c:[{name:'ARP Interval',type:'number',id:'u7d5',def:100,postfix:'ms'}]}," +
                "{vals:[ 2 ],c:[{name:'MII Interval',type:'number',id:'u7d7',def:100,postfix:'ms'}]}],selon:'Link Monitoring'}]}]}]";

            var decoded = Decode(bonding, "/interface/bonding", new[] { 20, 0 }, null,
                (0x7D4, "u32", (object)2u), (0x7D5, "u32", (object)100u), (0x7D7, "u32", (object)100u));

            Assert.AreEqual("100ms", decoded["arp-interval"], "the API prints it on a mii bond");
            Assert.AreEqual("100ms", decoded["mii-interval"]);
        }
    }
}
