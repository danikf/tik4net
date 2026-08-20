// WinboxJgSubtypeHarvestTests.cs — router-free tests for the two .jg harvest rules that decide whether an
// API path is reachable over WinBox-native at all:
//   • interface SUBTYPE windows, which are not handlers of their own but the generic interface table read
//     with a `type` filter — and which are declared through an inherit CHAIN, not a single level;
//   • per-WINDOW singleton-ness, because one handler routinely hosts both a settings item and a list.
//
// Both were coverage gaps that a live suite reported as router facts: an unmapped path skipped with "the
// required RouterOS package may not be installed", and a handler-level singleton answer turned a list into
// one record. The .jg fragments below are the real shapes from RouterOS 7.23.2, cut down to what the rules
// read.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxJgSubtypeHarvestTests
    {
        // The generic interface window: the only one with a real handler, and the anchor of every subtype.
        private const string IfaceBase =
            "{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],generic:'iface'," +
            "typeon:'type',c:[{name:'type',type:'number',id:'u10001',ro:1},{name:'Name',type:'string',id:'s10006'}]}," +
            "{name:'Interface',title:'EoIP Tunnel',type:'map',inherit:'iface',typevalue:17,c:[" +
            "{name:'Remote Address',type:'addr',id:'m1'}]}]}";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[" + body + "]"), "the fragment should parse");
            return catalog;
        }

        [TestMethod]
        public void DirectIfaceSubtype_IsHarvestedWithItsTypeFilter()
        {
            var catalog = Parse(IfaceBase);

            CollectionAssert.AreEqual(new[] { 20, 0 }, (System.Array)catalog.GetDerivedPaths()["/interfaces/eoip-tunnel"],
                "a subtype window reads the generic interface handler");
            var filter = catalog.GetSubtypeFilters()["/interfaces/eoip-tunnel"];
            Assert.AreEqual(0x10001, filter.Item1, "filtered on the base window's `type` field key");
            Assert.AreEqual(17, filter.Item2);
        }

        [TestMethod]
        public void InheritChain_ThroughAnIntermediateGeneric_IsHarvested()
        {
            // ppp.jg's real shape: the tunnel windows inherit a generic:'ppp' base which itself inherits
            // 'iface'. Recognising only inherit:'iface' left /interface/l2tp-client with no window at all,
            // and the transport reported that as "the router may not have the package".
            var catalog = Parse(IfaceBase + ",{name:'PPP',c:["
                + "{title:'Interface',type:'map',generic:'ppp',inherit:'iface',typeon:'type',typevalue:4294967295},"
                + "{name:'Interface',title:'L2TP Client',type:'map',inherit:'ppp',prefix:'l2tp-out',typevalue:34}]}");

            var derived = catalog.GetDerivedPaths();
            Assert.IsTrue(derived.ContainsKey("/ppp/l2tp-client"), "the client window is reachable through the chain");
            CollectionAssert.AreEqual(new[] { 20, 0 }, (System.Array)derived["/ppp/l2tp-client"]);
            Assert.AreEqual(34, catalog.GetSubtypeFilters()["/ppp/l2tp-client"].Item2);
        }

        [TestMethod]
        public void WildcardTypevalue_IsNotHarvested()
        {
            // A base standing for "any of my subtypes" (typevalue:4294967295) is a SET, not a value: filtering
            // interfaces on type == 4294967295 would match nothing at all.
            var catalog = Parse(IfaceBase + ",{name:'PPP',c:["
                + "{title:'Interface',type:'map',generic:'ppp',inherit:'iface',typeon:'type',typevalue:4294967295}]}");

            Assert.IsFalse(catalog.GetDerivedPaths().ContainsKey("/ppp/interface"));
        }

        [TestMethod]
        public void SubtypeOfABaseDiscriminatingOnAnotherField_IsNotHarvested()
        {
            // wlan6.jg switches to typeon:'hwtype' below the 'Wireless' window, so its hardware variants
            // (Atheros AR5212, …) are told apart by hwtype — NOT by the interface `type` the filter applies.
            // Harvesting them would produce a filter on the wrong field, i.e. a plausible wrong answer.
            var catalog = Parse(IfaceBase + ",{name:'Wireless',c:["
                + "{title:'Wireless',type:'map',generic:'ath',inherit:'iface',typeon:'hwtype',typevalue:35},"
                + "{title:'Wireless (Atheros AR5212)',type:'map',inherit:'ath',typevalue:1}]}");

            var derived = catalog.GetDerivedPaths();
            Assert.IsTrue(derived.ContainsKey("/wireless/wireless"), "the 'type'-discriminated window IS harvested");
            Assert.IsFalse(derived.ContainsKey("/wireless/wireless-(atheros-ar5212)"),
                "but not the one discriminated by hwtype");
        }

        // The routes tree, cut to the rule it exercises: ONE table for both address families, a base that
        // owns its handler and declares typeon:'rtype', and two windows over it. This is the shape that is
        // NOT the interface family — the base has a real path of its own, where an interface subtype base
        // does not — and it had no harvest rule at all, so /ip/route addressed the base and answered with
        // the IPv6 routes too (six rows against the API's two on the lab CHR).
        private const string RouteBase =
            "{name:'Routes',title:'Routes',group:'IP',c:[{name:'Route',title:'All Routes',type:'map'," +
            "path:[ 44,21 ],generic:'route',hide:1,typeon:'rtype',c:[{name:'rtype',type:'number',id:'u81',ro:1}," +
            "{name:'Dst. Address',type:'addr',id:'m105'}]}," +
            "{title:'Routes',type:'map',inherit:'route',typevalue:2,c:[{name:'Distance',type:'number',id:'u107'}]}]}";

        [TestMethod]
        public void NonIfaceSubtype_OverABaseThatOwnsItsHandler_IsHarvested()
        {
            var catalog = Parse(RouteBase);

            var derived = catalog.GetDerivedPaths();
            CollectionAssert.AreEqual(new[] { 44, 21 }, (System.Array)derived["/ip/routes/routes"],
                "the IPv4 window reads the shared routes table");
            var filter = catalog.GetSubtypeFilters()["/ip/routes/routes"];
            Assert.AreEqual(0x81, filter.Item1, "filtered on the base window's own `rtype` key, not on `type`");
            Assert.AreEqual(2, filter.Item2);
        }

        [TestMethod]
        public void ANonIfaceGenericInheritedAcrossPlugins_ResolvesToTheSameTable()
        {
            // ipv6.jg declares `inherit:'route'` for a generic that lives in roteros.jg. roteros.jg is
            // parsed first by design, so the base is registered before anything inherits it — this pins
            // that, because the two families sharing one table is the whole reason a filter is needed.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[" + RouteBase + "]"));
            Assert.IsTrue(catalog.TryParseInto(
                "[{name:'Routes',title:'Routes',group:'IPv6',c:[{title:'Routes6',type:'map'," +
                "inherit:'route',typevalue:3,c:[{name:'Distance',type:'number',id:'u107'}]}]}]"));

            CollectionAssert.AreEqual(new[] { 44, 21 },
                (System.Array)catalog.GetDerivedPaths()["/ipv6/routes/routes6"]);
            Assert.AreEqual(3, catalog.GetSubtypeFilters()["/ipv6/routes/routes6"].Item2,
                "the IPv6 window filters the same table on the other rtype");
        }

        [TestMethod]
        public void ANonIfaceBaseWithoutADiscriminator_IsNotHarvested()
        {
            // No `typeon` field to filter on means there is nothing to tell the children apart with, and
            // registering the window anyway would answer with the base's whole table under a subtype's name.
            var catalog = Parse(
                "{name:'Things',c:[{name:'Thing',title:'All Things',type:'map',path:[ 60,1 ],generic:'thing'}," +
                "{title:'Some Things',type:'map',inherit:'thing',typevalue:2,c:[{name:'X',type:'number',id:'u1'}]}]}");

            Assert.IsFalse(catalog.GetDerivedPaths().ContainsKey("/things/some-things"),
                "a base with no resolvable discriminator cannot filter its children");
        }

        [TestMethod]
        public void SingletonIsRecordedPerWindow_NotPerHandler()
        {
            // [28,0] is both the UPnP settings singleton and the per-interface list. Asking the HANDLER
            // answers "singleton" for both, so the list came back as a single record.
            var catalog = Parse("{name:'UPnP',title:'UPnP',group:'IP',c:["
                + "{name:'UPnP Settings',title:'UPnP Settings',type:'item',path:[ 28,0 ],c:[{name:'Enabled',type:'bool',id:'b1'}]},"
                + "{name:'UPnP',title:'Interfaces',type:'map',path:[ 28,0 ],c:[{name:'Type',type:'number',id:'u2'}]}]}");

            Assert.IsTrue(catalog.TryIsSingletonPath("/ip/upnp/upnp-settings", out bool settings) && settings,
                "the settings window is a singleton");
            Assert.IsTrue(catalog.TryIsSingletonPath("/ip/upnp/upnp", out bool list) && !list,
                "the interface list on the SAME handler is not");
            Assert.IsTrue(catalog.IsSingletonHandler(new[] { 28, 0 }),
                "the handler-level answer cannot tell them apart — which is why the per-window one exists");
            Assert.IsFalse(catalog.TryIsSingletonPath("/ip/upnp/nothing-here", out _),
                "an unknown path leaves the caller on the handler-level fallback");
        }

        [TestMethod]
        public void HandlerMap_ResolveDerivedKey_NamesTheWindowBehindAnAliasedPath()
        {
            var map = new WinboxHandlerMap();
            map.SetDerivedPaths(new Dictionary<string, int[]>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["/ip/upnp/upnp-settings"] = new[] { 28, 0 },
                ["/ip/upnp/upnp"] = new[] { 28, 0 },
            });

            // Shipped aliases: /ip/upnp is the settings window, /ip/upnp/interfaces the list — same handler,
            // so only the derived KEY can tell a caller which window it is really reading.
            Assert.AreEqual("/ip/upnp/upnp-settings", map.ResolveDerivedKey("/ip/upnp"));
            Assert.AreEqual("/ip/upnp/upnp", map.ResolveDerivedKey("/ip/upnp/interfaces"));

            map.AddOverride("/ip/upnp", new[] { 99, 9 });
            Assert.IsNull(map.ResolveDerivedKey("/ip/upnp"),
                "a raw handler override names no window, so the caller falls back to the handler-level answer");
        }
    }
}
