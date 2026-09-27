// WinboxRouterOs6LabelTests.cs — RouterOS 6 windows whose labels and captions are not the API's words.
//
// The windows below are cut from the 6.49.13 catalog (fields kept verbatim, the rest dropped). Each one read under a
// name or a value the binary API of the same router does not use, found by the path-map audit against 6.49.13.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRouterOs6LabelTests
    {
        private static Dictionary<string, string> Decode(string window, string apiPath, int[] handler,
            Dictionary<int, Tuple<string, object>> record)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(window), "the window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            return new WinboxRecordCodec(null, catalog).DecodeRecord(record, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static WinboxFieldResolver Resolver(string window, string apiPath, int[] handler)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(window), "the window must parse");
            return new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
        }

        private static string Dump(Dictionary<string, string> fields)
            => string.Join(";", fields.Select(kv => kv.Key + "=" + kv.Value));

        private const string OspfInstance =
            "[{name:'OSPF Instance',title:'Instances',type:'map',path:[ 44,120 ],nameval:'Name',prefix:'ospf',c:[" +
            "{name:'Name',type:'string',id:'sfe0010',width:100},{name:'Router ID',type:'ipaddr',id:'u1',width:120}," +
            "{name:'Redistribute Default Route',type:'enm',id:'u5',values:{type:'static',map:['never','if installed (as type 1)'," +
            "'if installed (as type 2)','always (as type 1)','always (as type 2)']}}," +
            "{name:'Redistribute Connected Routes',type:'enm',id:'u3',def:2,values:{type:'static',map:[ 'as type 1','as type 2','no' ]}}," +
            "{name:'Redistribute Static Routes',type:'enm',id:'u2',def:2,values:{type:'static',map:[ 'as type 1','as type 2','no' ]}}," +
            "{name:'Metrics',type:'tab'},{name:'Default Route Metric',type:'number',id:'uc',def:1,max:16777214}," +
            "{name:'Static Routes Metric',type:'number',id:'u9',def:20,max:16777214}," +
            "{name:'BGP Routes Metric',type:'number',id:'ud',def:4294967295,max:16777214,opt:1}," +
            "{name:'Other OSPF Routes Metric',type:'number',id:'u12',def:4294967295,max:16777214,opt:1}]}]";

        [TestMethod]
        public void OspfInstance_TheUnsetMetricsReadAuto()
        {
            // The stock 6.49.13 instance: 0xD and 0x12 carry the marker, and the API prints metric-bgp=auto.
            var fields = Decode(OspfInstance, "/routing/ospf/instance", new[] { 44, 120 },
                new Dictionary<int, Tuple<string, object>>
                {
                    [0xD] = Tuple.Create("u32", (object)4294967295u),
                    [0x12] = Tuple.Create("u32", (object)25u),
                });

            Assert.AreEqual("auto", fields["metric-bgp"], Dump(fields));
            Assert.AreEqual("25", fields["metric-other-ospf"], Dump(fields));
        }

        [TestMethod]
        public void DhcpServerConfig_ReadsTheKeysThe6xWindowDoesNotDeclare()
        {
            const string config = "[{title:'DHCP Config',type:'item',path:[ 23,5 ],c:[{name:'Store Leases On Disk'," +
                "type:'interval',id:'u1',values:{type:'static',map:{0:'immediately',4294967295:'never'}}}]}]";
            var fields = Decode(config, "/ip/dhcp-server/config", new[] { 23, 5 },
                new Dictionary<int, Tuple<string, object>>
                {
                    [0x1] = Tuple.Create("u32", (object)300u),
                    [0x2] = Tuple.Create("u32", (object)90u),
                    [0x3] = Tuple.Create("bool", (object)false),
                });

            Assert.AreEqual("5m", fields["store-leases-disk"], Dump(fields));
            Assert.AreEqual("1m30s", fields["interim-update"], Dump(fields));
            Assert.AreEqual("false", fields["accounting"], Dump(fields));
        }

        [TestMethod]
        public void OspfInstance_ReadsUnderTheApiNamesAndWords()
        {
            var fields = Decode(OspfInstance, "/routing/ospf/instance", new[] { 44, 120 },
                new Dictionary<int, Tuple<string, object>>
                {
                    [0xFE0010] = Tuple.Create("str", (object)"default"),
                    [0x5] = Tuple.Create("u32", (object)3u),
                    [0x3] = Tuple.Create("u32", (object)0u),
                    [0x2] = Tuple.Create("u32", (object)2u),
                    [0xC] = Tuple.Create("u32", (object)1u),
                    [0x9] = Tuple.Create("u32", (object)20u),
                });

            Assert.AreEqual("always-as-type-1", fields["distribute-default"], Dump(fields));
            Assert.AreEqual("as-type-1", fields["redistribute-connected"], Dump(fields));
            Assert.AreEqual("no", fields["redistribute-static"], Dump(fields));
            Assert.AreEqual("1", fields["metric-default"], Dump(fields));
            Assert.AreEqual("20", fields["metric-static"], Dump(fields));
            Assert.IsFalse(fields.ContainsKey("redistribute-default-route"), Dump(fields));
        }

        [TestMethod]
        public void OspfInstance_WritesTheApiNames()
        {
            var resolver = Resolver(OspfInstance, "/routing/ospf/instance", new[] { 44, 120 });

            Assert.AreEqual(0x5, resolver.ResolveKey("distribute-default"));
            Assert.AreEqual(0x9, resolver.ResolveKey("metric-static"));
        }

        [TestMethod]
        public void NtpClientAndAccounting_ReadUnderTheApiNames()
        {
            const string ntp = "[{title:'NTP Client',type:'item',path:[ 24,7 ],c:[{name:'Enabled',type:'bool',id:'b1'}," +
                "{name:'Primary NTP Server',type:'ipaddr',id:'u2'},{name:'Secondary NTP Server',type:'ipaddr',id:'u3'}," +
                "{name:'Last Update',type:'age',id:'u6a',opt:1,postfix:'ago',ro:1,scale:100}]}]";
            var names = Resolver(ntp, "/system/ntp/client", new[] { 24, 7 }).BuildKeyToApiName();

            Assert.AreEqual("primary-ntp", names[0x2]);
            Assert.AreEqual("secondary-ntp", names[0x3]);
            Assert.AreEqual("last-update-before", names[0x6A]);

            const string accounting = "[{title:'IP Accounting',type:'item',path:[ 40,0 ],c:[" +
                "{name:'Enable Accounting',type:'bool',id:'b15'},{name:'Threshold',type:'number',id:'u14'}]}]";
            Assert.AreEqual("enabled", Resolver(accounting, "/ip/accounting", new[] { 40, 0 }).BuildKeyToApiName()[0x15]);
        }

        [TestMethod]
        public void OpenVpnCipher_IsTheRoutersToken()
        {
            const string ovpn = "[{title:'OVPN Server',type:'item',path:[ 60,4 ],c:[" +
                "{name:'Cipher',type:'set',id:'ud4',values:{type:'static',map:['blowfish 128','aes 128','aes 192','aes 256','null']}}]}]";
            var field = Resolver(ovpn, "/interface/ovpn-server/server", new[] { 60, 4 }).BuildKeyToField()[0xD4];

            CollectionAssert.AreEquivalent(new[] { "blowfish128", "aes128", "aes192", "aes256", "null" },
                field.EnumMap!.Values.ToArray());
        }

        [TestMethod]
        public void QueueRateHalves_SpellZeroAsTheApiDoes()
        {
            const string queue = "[{name:'Simple Queue',type:'map',path:[ 21,1 ],c:[{name:'Max Limit',type:'tuple',separate:1,c:[" +
                "{name:'Upload Max Limit',type:'enm',id:'u3c',sortbyvalue:1,values:{type:'static',map:{0:'unlimited',64000:'64k'}}}]}]}]";
            var field = Resolver(queue, "/queue/simple", new[] { 21, 1 }).BuildKeyToField()[0x3C];

            Assert.AreEqual("0", field.EnumMap![0]);
            Assert.AreEqual("64k", field.EnumMap![64000]);
        }

        [TestMethod]
        public void EthernetAdvertise_CountsInMegabitsOnRouterOs6()
        {
            const string ether = "[{name:'Interface',type:'map',path:[ 20,0 ],c:[{name:'Advertise',type:'set',id:'u409',on:'autoneg'," +
                "values:{type:'static',map:{0:'10M half',1:'10M full',5:'1000M full',12:'10G full',30:'2.5G full',31:'5G full'}}}]}]";
            var field = Resolver(ether, "/interface/ethernet", new[] { 20, 0 }).BuildKeyToField()[0x409];

            Assert.AreEqual("10M-half", field.EnumMap![0]);
            Assert.AreEqual("1000M-full", field.EnumMap![5]);
            Assert.AreEqual("10000M-full", field.EnumMap![12]);
            Assert.AreEqual("2500M-full", field.EnumMap![30]);
            Assert.AreEqual("5000M-full", field.EnumMap![31]);

            // Printed slowest first, as 6.49.13's API does — not in bit order, which puts 10G (bit 12) before 2.5G.
            var fields = Decode(ether, "/interface/ethernet", new[] { 20, 0 },
                new Dictionary<int, Tuple<string, object>>
                {
                    [0x409] = Tuple.Create("u32", (object)((1u << 1) | (1u << 5) | (1u << 12) | (1u << 30) | (1u << 31))),
                });
            Assert.AreEqual("10M-full,1000M-full,2500M-full,5000M-full,10000M-full", fields["advertise"], Dump(fields));
        }
    }
}
