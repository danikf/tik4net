// WinboxJgReadFlagsTests.cs — router-free tests for the getall/get-singleton flag word (ufe000c) a window asks for.
//
// webfig builds it as `base | attrs.refetchonopen | attrs.refreshfilter` (master.js ObjectMap.getall and
// ObjectHolder.fetch, on 6.49 and 7.x alike), so the flags are a property of the WINDOW. RouterOS 6.49's route
// window declares refreshfilter:131072; without that bit its getall carries only the list columns and leaves out
// scope, target-scope, routing-mark and vrf-interface. The fragments below are the real 6.49.13 shapes, cut down
// to the attributes the rule reads.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxJgReadFlagsTests
    {
        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[" + body + "]"), "the fragment should parse");
            return catalog;
        }

        [TestMethod]
        public void RefreshFilter_IsOrdIntoTheReadFlags()
        {
            var catalog = Parse("{name:'Routes',c:[{name:'Route',title:'Routes',type:'map',path:[ 44,1 ],"
                + "refetchonchange:1,refreshfilter:131072,c:[{name:'Scope',type:'number',id:'uf'}]}]}");

            Assert.AreEqual(WinboxM2Protocol.GetAllFlags | 0x20000, catalog.GetReadFlags(new[] { 44, 1 }));
        }

        [TestMethod]
        public void RefetchOnOpen_IsOrdIntoTheReadFlags()
        {
            var catalog = Parse("{name:'Tools',c:[{name:'Sniffer Packet',title:'Packets',type:'map',path:[ 45,6 ],"
                + "refetchonopen:4,refreshfilter:2,c:[{name:'Size',type:'number',id:'u1'}]}]}");

            Assert.AreEqual(WinboxM2Protocol.GetAllFlags | 0x4 | 0x2, catalog.GetReadFlags(new[] { 45, 6 }));
        }

        [TestMethod]
        public void Autorefresh_KeepsTheStatsBit_WithoutARefreshFilter()
        {
            // webfig sends no stats bit here; the transport always has, and the counters it returns depend on it.
            var catalog = Parse("{name:'DNS',c:[{name:'DNS Entry',title:'Cache',type:'map',path:[ 14,1 ],"
                + "autorefresh:3000,c:[{name:'Name',type:'string',id:'s1'}]}]}");

            Assert.AreEqual(WinboxM2Protocol.GetAllFlags | WinboxM2Protocol.GetAllStatsFlag,
                catalog.GetReadFlags(new[] { 14, 1 }));
        }

        [TestMethod]
        public void WindowsSharingAHandler_ContributeTheirFlagsTogether()
        {
            var catalog = Parse("{name:'X',c:["
                + "{name:'A',title:'A',type:'item',path:[ 60,1 ],refreshfilter:2,c:[{name:'One',type:'bool',id:'b1'}]},"
                + "{name:'B',title:'B',type:'map',path:[ 60,1 ],refreshfilter:65536,c:[{name:'Two',type:'number',id:'u2'}]}]}");

            Assert.AreEqual(WinboxM2Protocol.GetAllFlags | 0x2 | 0x10000, catalog.GetReadFlags(new[] { 60, 1 }));
        }

        [TestMethod]
        public void AWindowWithNoFlagAttributes_ReadsWithTheBaseFlags()
        {
            var catalog = Parse("{name:'X',c:[{name:'A',title:'A',type:'map',path:[ 61,1 ],"
                + "c:[{name:'One',type:'number',id:'u1'}]}]}");

            Assert.AreEqual(WinboxM2Protocol.GetAllFlags, catalog.GetReadFlags(new[] { 61, 1 }));
            Assert.AreEqual(WinboxM2Protocol.GetAllFlags, catalog.GetReadFlags(new[] { 99, 99 }),
                "an unknown handler asks for nothing extra");
        }
    }
}
