// WinboxOlderLabelFieldTests.cs — a field whose WinBox label changed between RouterOS versions.
//
// The address list's own key, 0xFE0010, is labelled 'List' by the 7.x catalog and 'Name' by the 6.49.13 one,
// while the API calls it `list` on both. A shipped field alias bridges the older label, and must not disturb
// the newer catalog, where 'Name' does not exist: the generic `name` fallback key (0x10006) is a different
// field, and aliasing onto it would write to the wrong key.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxOlderLabelFieldTests
    {
        private static readonly int[] Handler = { 20, 35 };
        private const int ListKey = 0xFE0010;

        // /ip/firewall/address-list, cut to the list name and the address.
        private static string Window(string listLabel) =>
            "[{name:'IP',title:'IP',group:'IP',c:[" +
            "{name:'Firewall Address List',title:'Address Lists',type:'map',path:[ 20,35 ],nameval:'" + listLabel + "',c:[" +
              "{name:'" + listLabel + "',type:'string',id:'sfe0010',sorted:1}," +
              "{name:'Address',type:'string',id:'s8'}]}" +
            "]}]";

        private static (WinboxFieldResolver resolver, WinboxJgCatalog catalog) Build(string listLabel)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Window(listLabel)), "the trimmed window must parse");
            return (new WinboxFieldResolver("/ip/firewall/address-list", Handler, catalog, new Dictionary<string, int>()),
                    catalog);
        }

        private static Dictionary<string, string> Decode(WinboxFieldResolver resolver, WinboxJgCatalog catalog)
        {
            var rec = new Dictionary<int, Tuple<string, object>>
            {
                [0xFE0001] = Tuple.Create("u32", (object)1u),
                [ListKey] = Tuple.Create("str", (object)"blocked"),
                [0x8] = Tuple.Create("str", (object)"example.com"),
            };
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField(),
                    resolver.DerivedBoolFields);
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_TheNameLabelReadsAsList()
        {
            var (resolver, catalog) = Build("Name");
            var decoded = Decode(resolver, catalog);

            Assert.AreEqual("blocked", decoded["list"]);
            Assert.IsFalse(decoded.ContainsKey("name"), "the API has no `name` on this menu");
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_ListResolvesToTheListKey()
        {
            var (resolver, _) = Build("Name");

            Assert.AreEqual(ListKey, resolver.ResolveKey("list"));
        }

        [TestMethod]
        public void OnTheRouterOs7Catalog_ListStillResolvesToItsOwnLabel()
        {
            // 'Name' is absent here, so the alias must yield to the catalog's own 'List' rather than fall
            // through to the generic name key.
            var (resolver, catalog) = Build("List");

            Assert.AreEqual(ListKey, resolver.ResolveKey("list"));
            Assert.AreEqual("blocked", Decode(resolver, catalog)["list"]);
        }

        // ── The other 6.49.13 labels and keys (Docs/findings-routeros-6.md, 1b) ──

        private static Dictionary<string, string> DecodeOne(string apiPath, int[] handler, string window,
            params (int key, string type, object val)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + window + "]}]"), "window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = new Dictionary<int, Tuple<string, object>> { [0xFE0001] = Tuple.Create("u32", (object)1u) };
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField(),
                    resolver.DerivedBoolFields);
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_TheOspfAreaNameIsTheAreaNameLabel()
        {
            var row = DecodeOne("/routing/ospf/area", new[] { 44, 122 },
                "{name:'OSPF Area',title:'Areas',type:'map',path:[ 44,122 ],c:[{name:'Area Name',type:'string',id:'s2'}]}",
                (0x2, "str", "backbone"));

            Assert.AreEqual("backbone", row["name"]);
        }

        [TestMethod]
        public void OnTheRouterOs7Catalog_TheOspfAreaNameKeepsItsOwnLabel()
        {
            var row = DecodeOne("/routing/ospf/area", new[] { 44, 113 },
                "{name:'OSPF Area',title:'Areas',type:'map',path:[ 44,113 ],c:[{name:'Name',type:'string',id:'sfe0010'}]}",
                (0xFE0010, "str", "backbone"));

            Assert.AreEqual("backbone", row["name"]);
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_ABgpInstanceRouterIdWriteFindsItsKey()
        {
            // The shipped alias sends router-id to the 7.x label 'IP'; 6.49.13 labels the field 'Router ID',
            // which is the API name itself — so the alias yields, and the write is no longer refused by name.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" +
                "{name:'BGP Instance',title:'Instances',type:'map',path:[ 44,101 ],c:[{name:'Router ID',type:'ipaddr',id:'u1'}]}]}]"));
            var resolver = new WinboxFieldResolver("/routing/bgp/instance", new[] { 44, 101 }, catalog,
                new Dictionary<string, int>());

            Assert.AreEqual(0x1, resolver.ResolveKey("router-id"));
        }

        [TestMethod]
        public void TheRoutesPrefSourceIsPrefSrc()
        {
            var row = DecodeOne("/ip/route", new[] { 44, 1 },
                "{name:'Route',title:'Routes',type:'map',path:[ 44,1 ],c:[{name:'Pref. Source',type:'opt',id:'b3eb',c:[{type:'ipaddr',id:'u3'}]}]}",
                (0x3EB, "bool", true), (0x3, "u32", 0x0100000Au));

            Assert.AreEqual("10.0.0.1", row["pref-src"]);
            Assert.IsFalse(row.ContainsKey("pref-source"));
        }

        [TestMethod]
        public void TheConnectionTrackingTotalEntriesIsReadWhereTheWindowDoesNotDeclareIt()
        {
            // The 6.49.13 Tracking window has Max Entries (ue) and nothing at uf; the router sends uf anyway.
            var row = DecodeOne("/ip/firewall/connection/tracking", new[] { 20, 17 },
                "{name:'Connection Tracking',title:'Tracking',type:'item',path:[ 20,17 ],c:[{name:'Max Entries',type:'number',id:'ue'}]}",
                (0xE, "u32", 1048576u), (0xF, "u32", 9u));

            Assert.AreEqual("9", row["total-entries"]);
        }

        [TestMethod]
        public void AnInterfacesDefaultNameIsReadWhereTheWindowDoesNotDeclareIt()
        {
            var row = DecodeOne("/interface", new[] { 20, 0 },
                "{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],c:[{name:'Name',type:'string',id:'s10006'}]}",
                (0x10006, "str", "wan"), (0x10031, "str", "ether1"));

            Assert.AreEqual("ether1", row["default-name"]);
        }
    }
}
