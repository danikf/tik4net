// WinboxCrossVersionFieldTests.cs — fields whose API name or flag set differs between RouterOS versions,
// answered WITHOUT asking the router for its version (see _notes: the library never compares versions).
//
// Two mechanisms, both fed by the version-matched .jg catalog:
//
//   * numflag — one numeric key carrying a SET of named row flags, one of which the row is. RouterOS 6's
//     route window declares {2:'connected',3:'static',…} at 0x7, RouterOS 7's {2:'connect',…,10:'DHCP',…}
//     at 0x112. Each catalog names the flags its version knows, so `dhcp` appears on 7 and cannot on 6.
//   * AlsoKnownAs — the same field under both of the words RouterOS has used for it, where the catalog says
//     the same thing on both versions and only the API's vocabulary changed.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxCrossVersionFieldTests
    {
        private static Dictionary<string, string> Decode(string apiPath, int[] handler, string window,
            params (int key, string type, object val)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + window + "]}]"), "window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = new Dictionary<int, Tuple<string, object>> { [0xFE0001] = Tuple.Create("u32", (object)1u) };
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog).DecodeRecord(
                rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField(),
                resolver.DerivedBoolFields, resolver.BuildNumFlags(), resolver.ExtraSpellings);
        }

        // ── numflag: the route's origin ──────────────────────────────────────

        private const string RouteWindow6 =
            "{name:'Route',title:'Routes',type:'map',path:[ 44,1 ],c:[{name:'Dst. Address',type:'network',id:'u1',maskid:'u2'}," +
            "{name:'active',type:'flag',id:'be'}," +
            "{type:'numflag',id:'u7',c:{2:[ 'connected','C' ],3:[ 'static','S' ],8:[ 'BGP','b' ]}}]}";

        private const string RouteWindow7 =
            "{name:'Route',title:'Routes',type:'map',path:[ 44,21 ],c:[{name:'Dst. Address',type:'addr',id:'m105'}," +
            "{type:'numflag',id:'u112',c:{2:[ 'connect','C' ],3:[ 'static','S' ],8:[ 'BGP','b' ],10:[ 'DHCP','d' ]}}]}";

        [TestMethod]
        public void OnTheRouterOs6Catalog_AConnectedRouteReadsConnect()
        {
            // 6.49.13 spells the member 'connected' where the API — and the 7.x catalog — say `connect`.
            var row = Decode("/ip/route", new[] { 44, 1 }, RouteWindow6,
                (0x1, "u32", 0x0100000Au), (0x7, "u32", 2u), (0xE, "bool", true));

            Assert.AreEqual("true", row["connect"]);
            Assert.IsFalse(row.ContainsKey("connected"), "the router's word for the flag, not the window's");
            Assert.IsFalse(row.ContainsKey("static"), "the API prints only the flag the row is");
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_AStaticRouteReadsStatic()
        {
            var row = Decode("/ip/route", new[] { 44, 1 }, RouteWindow6,
                (0x1, "u32", 0x0100000Au), (0x7, "u32", 3u), (0xE, "bool", true));

            Assert.AreEqual("true", row["static"]);
            Assert.IsFalse(row.ContainsKey("connect"));
        }

        [TestMethod]
        public void OnTheRouterOs6Catalog_NoRouteCanReadDhcp()
        {
            // RouterOS 6 has no dhcp origin — its DHCP client's route is static — and its catalog declares no
            // such member, so this decode cannot invent one whatever the key carries.
            var row = Decode("/ip/route", new[] { 44, 1 }, RouteWindow6,
                (0x1, "u32", 0x0100000Au), (0x7, "u32", 10u), (0xE, "bool", true));

            Assert.IsFalse(row.ContainsKey("dhcp"));
            Assert.IsFalse(row.ContainsKey("static"), "10 names no member of this catalog");
        }

        [TestMethod]
        public void OnTheRouterOs7Catalog_TheSameMechanismReadsDhcp()
        {
            var row = Decode("/ip/route", new[] { 44, 21 }, RouteWindow7,
                (0x112, "u32", 10u));

            Assert.AreEqual("true", row["dhcp"]);
        }

        [TestMethod]
        public void ANumflagKeyTheRowDoesNotCarry_ReadsNoFlagAtAll()
        {
            var row = Decode("/ip/route", new[] { 44, 1 }, RouteWindow6, (0x1, "u32", 0x0100000Au));

            Assert.IsFalse(row.ContainsKey("connect"));
            Assert.IsFalse(row.ContainsKey("static"));
        }

        // ── AlsoKnownAs: one field, both of RouterOS's words ─────────────────

        private const string ServiceWindow =
            "{name:'IP Service',title:'Services',type:'map',path:[ 68,1 ],c:[{name:'Name',type:'string',id:'s1'}," +
            "{name:'Available From',type:'multi',id:'M6',c:[{type:'network',id:'u1',maskid:'u2'}]}]}";

        [TestMethod]
        public void TheServicesAvailableFromIsReportedAsAddressToo()
        {
            var row = Decode("/ip/service", new[] { 68, 1 }, ServiceWindow,
                (0x1, "str", "ftp"), (0x6, "msg[]", new List<Dictionary<int, Tuple<string, object>>>()));

            Assert.IsTrue(row.ContainsKey("available-from"), "7.24's word");
            Assert.AreEqual(row["available-from"], row["address"], "6.49.13's word, same value");
        }

        [TestMethod]
        public void TheServicesAddressResolvesForAWrite()
        {
            // The write side needs no second word: the 6.x spelling maps onto the label both catalogs have.
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + ServiceWindow + "]}]"));
            var resolver = new WinboxFieldResolver("/ip/service", new[] { 68, 1 }, catalog, new Dictionary<string, int>());

            Assert.AreEqual(0x6, resolver.ResolveKey("address"));
            Assert.AreEqual(0x6, resolver.ResolveKey("available-from"));
        }

        private const string EmailWindow =
            "{title:'Email Settings',type:'item',path:[ 40 ],c:[{name:'Server',type:'union',single:1,c:[" +
            "{type:'ipaddr',id:'u1'},{type:'string',id:'se',min:1}]},{name:'Port',type:'number',id:'u7'}]}";

        [TestMethod]
        public void TheEmailServerIsReportedAsAddressToo()
        {
            var row = Decode("/tool/e-mail", new[] { 40 }, EmailWindow, (0x1, "u32", 0x0100000Au), (0x7, "u32", 25u));

            Assert.AreEqual("10.0.0.1", row["server"]);
            Assert.AreEqual("10.0.0.1", row["address"]);
        }

        [TestMethod]
        public void TheOspfAreasStateFlagIsReportedUnderBothWords()
        {
            var row = Decode("/routing/ospf/area", new[] { 44, 122 },
                "{name:'OSPF Area',title:'Areas',type:'map',path:[ 44,122 ],c:[{name:'Area Name',type:'string',id:'s2'}]}",
                (0x2, "str", "backbone"), (0xFE0008, "bool", false));

            Assert.AreEqual("false", row["inactive"]);
            Assert.AreEqual("false", row["invalid"]);
            Assert.AreEqual("backbone", row["name"]);
        }

        // ── A marker RouterOS 6 prints as a word ─────────────────────────────

        private const string LogActionWindow =
            "{name:'Log Action',title:'Actions',type:'map',path:[ 3,1 ],c:[{name:'Name',type:'string',id:'s1'}," +
            "{name:'Syslog Severity',type:'number',id:'ue',def:4294967295,max:7,opt:1,values:{type:'static'," +
            "map:['emergency','alert','critical','error','warning','notice','info','debug']}}]}";

        [TestMethod]
        public void AnUnsetSyslogSeverityReadsAuto()
        {
            // 6.49.13 prints the remote action's as `auto`; 7.24 prints nothing. Reported on both, rather
            // than asking the router which version it is.
            var row = Decode("/system/logging/action", new[] { 3, 1 }, LogActionWindow,
                (0x1, "str", "remote"), (0xE, "u32", 4294967295u));

            Assert.AreEqual("auto", row["syslog-severity"]);
        }

        [TestMethod]
        public void AutoIsWrittenAsTheUnsetMarker()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + LogActionWindow + "]}]"));
            var resolver = new WinboxFieldResolver("/system/logging/action", new[] { 3, 1 }, catalog,
                new Dictionary<string, int>());

            var encoded = resolver.EncodeField("syslog-severity", "auto");
            var expected = M2Message.U32Sys(0xE, unchecked((int)4294967295u));
            Assert.AreEqual(BitConverter.ToString(expected), BitConverter.ToString(encoded[encoded.Count - 1]));
        }
    }
}
