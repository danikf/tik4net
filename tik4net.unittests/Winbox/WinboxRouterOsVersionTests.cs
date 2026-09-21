// WinboxRouterOsVersionTests.cs — API names and printed values that depend on the RouterOS VERSION.
//
// Some fields keep their M2 key and their WinBox label from RouterOS 6 to 7 while the API renames them
// (/ip/service address → available-from, /tool/e-mail address → server, the OSPF area's invalid →
// inactive), and one unset marker is printed on 6 (syslog-severity=auto) and left out on 7. A label alias
// cannot tell the versions apart; the connection reads the version at open and hands the major number in.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRouterOsVersionTests
    {
        private static Dictionary<string, string> Decode(string apiPath, int[] handler, string window, int? major,
            params (int key, string type, object val)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + window + "]}]"), "window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                routerMajorVersion: major);
            var rec = new Dictionary<int, Tuple<string, object>> { [0xFE0001] = Tuple.Create("u32", (object)1u) };
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog) { RouterMajorVersion = major }
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private const string ServiceWindow =
            "{name:'IP Service',title:'Services',type:'map',path:[ 68,1 ],c:[{name:'Name',type:'string',id:'s1'}," +
            "{name:'Available From',type:'multi',id:'M6',c:[{type:'network',id:'u1',maskid:'u2'}]}]}";

        private static readonly (int, string, object)[] ServiceRow =
        {
            (0x1, "str", "ftp"),
            (0x6, "msg[]", new List<Dictionary<int, Tuple<string, object>>>()),
        };

        [TestMethod]
        public void OnRouterOs6_TheServicesAvailableFromIsAddress()
        {
            var row = Decode("/ip/service", new[] { 68, 1 }, ServiceWindow, 6, ServiceRow);

            Assert.IsTrue(row.ContainsKey("address"), "6.x prints address");
            Assert.IsFalse(row.ContainsKey("available-from"));
        }

        [TestMethod]
        public void OnRouterOs7_TheServicesAvailableFromKeepsItsName()
        {
            var row = Decode("/ip/service", new[] { 68, 1 }, ServiceWindow, 7, ServiceRow);

            Assert.IsTrue(row.ContainsKey("available-from"), "7.x prints available-from");
            Assert.IsFalse(row.ContainsKey("address"));
        }

        [TestMethod]
        public void WithTheVersionUnknown_TheCurrentNamesApply()
        {
            var row = Decode("/ip/service", new[] { 68, 1 }, ServiceWindow, null, ServiceRow);

            Assert.IsTrue(row.ContainsKey("available-from"));
        }

        private const string EmailWindow =
            "{title:'Email Settings',type:'item',path:[ 40 ],c:[{name:'Server',type:'union',single:1,c:[" +
            "{type:'ipaddr',id:'u1'},{type:'string',id:'se',min:1}]},{name:'Port',type:'number',id:'u7'}]}";

        [TestMethod]
        public void OnRouterOs6_TheEmailServerIsAddress_AndResolvesForAWrite()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + EmailWindow + "]}]"));
            var resolver = new WinboxFieldResolver("/tool/e-mail", new[] { 40 }, catalog, new Dictionary<string, int>(),
                routerMajorVersion: 6);
            Assert.IsTrue(resolver.EncodeField("address", "10.0.0.1").Count > 0, "address must encode on 6.x");

            var row = Decode("/tool/e-mail", new[] { 40 }, EmailWindow, 6, (0x1, "u32", 0x0100000Au), (0x7, "u32", 25u));
            Assert.AreEqual("10.0.0.1", row["address"]);
            Assert.IsFalse(row.ContainsKey("server"));
        }

        [TestMethod]
        public void OnRouterOs7_TheEmailServerIsServer()
        {
            var row = Decode("/tool/e-mail", new[] { 40 }, EmailWindow, 7, (0x1, "u32", 0x0100000Au), (0x7, "u32", 25u));

            Assert.AreEqual("10.0.0.1", row["server"]);
            Assert.IsFalse(row.ContainsKey("address"));
        }

        private const string AreaWindow =
            "{name:'OSPF Area',title:'Areas',type:'map',path:[ 44,122 ],c:[{name:'Area Name',type:'string',id:'s2'}]}";

        [TestMethod]
        public void OnRouterOs6_TheOspfAreasStateFlagIsInvalid()
        {
            var six = Decode("/routing/ospf/area", new[] { 44, 122 }, AreaWindow, 6,
                (0x2, "str", "backbone"), (0xFE0008, "bool", false));
            var seven = Decode("/routing/ospf/area", new[] { 44, 122 }, AreaWindow, 7,
                (0x2, "str", "backbone"), (0xFE0008, "bool", false));

            Assert.AreEqual("false", six["invalid"]);
            Assert.IsFalse(six.ContainsKey("inactive"));
            Assert.AreEqual("false", seven["inactive"]);
            Assert.AreEqual("backbone", six["name"], "the version-neutral label alias still applies");
        }

        private const string LogActionWindow =
            "{name:'Log Action',title:'Actions',type:'map',path:[ 3,1 ],c:[{name:'Name',type:'string',id:'s1'}," +
            "{name:'Syslog Severity',type:'number',id:'ue',def:4294967295,max:7,opt:1,values:{type:'static'," +
            "map:['emergency','alert','critical','error','warning','notice','info','debug']}}]}";

        [TestMethod]
        public void OnRouterOs6_AnUnsetSyslogSeverityIsPrintedAuto()
        {
            var six = Decode("/system/logging/action", new[] { 3, 1 }, LogActionWindow, 6,
                (0x1, "str", "remote"), (0xE, "u32", 4294967295u));
            var seven = Decode("/system/logging/action", new[] { 3, 1 }, LogActionWindow, 7,
                (0x1, "str", "remote"), (0xE, "u32", 4294967295u));

            Assert.AreEqual("auto", six["syslog-severity"]);
            Assert.IsFalse(seven.ContainsKey("syslog-severity"), "7.x leaves the unset marker out");
        }

        [TestMethod]
        public void OnRouterOs6_AutoIsWrittenAsTheUnsetMarker()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto("[{name:'M',title:'M',group:'M',c:[" + LogActionWindow + "]}]"));
            var resolver = new WinboxFieldResolver("/system/logging/action", new[] { 3, 1 }, catalog,
                new Dictionary<string, int>(), routerMajorVersion: 6);

            var encoded = resolver.EncodeField("syslog-severity", "auto");
            var expected = M2Message.U32Sys(0xE, unchecked((int)4294967295u));
            Assert.AreEqual(BitConverter.ToString(expected), BitConverter.ToString(encoded[encoded.Count - 1]));
        }

        [DataTestMethod]
        [DataRow("6.49.13", 6)]
        [DataRow("7.24.4 (stable)", 7)]
        [DataRow("", null)]
        [DataRow(null, null)]
        [DataRow("stable", null)]
        public void TheMajorVersionIsTheLeadingNumber(string? version, int? expected)
            => Assert.AreEqual(expected, WinboxNativeConnection.ParseMajorVersion(version));
    }
}
