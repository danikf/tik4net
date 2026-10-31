// WinboxRowShapeRuleTests.cs — two things a window cannot say about the API's row (WinBox review F15, F16).
//
// F15, measured over the API: /ip/service says address on 6.49.21 and 7.21.5 and available-from on 7.25; /tool/e-mail says
// address on 6.49.21 and server on 7.21.5 and 7.25. One key and one label on all of them, so only the router's version
// tells the spellings apart; between the versions measured, nothing does, and both stay.
// F16, measured on a t4n VRRP interface (7.25): connection-tracking-port is printed only with sync-connection-tracking=yes,
// and the window declares no condition on it.
// Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRowShapeRuleTests
    {
        private const string Service =
            "[{name:'IP',c:[{name:'Services',title:'Services',type:'map',path:[ 25,1 ],c:[" +
            "{name:'Available From',type:'string',id:'s7'}]}]}]";

        private const string Email =
            "[{name:'Tools',c:[{name:'Email',title:'Email',type:'item',path:[ 26,1 ],c:[" +
            "{name:'Server',type:'string',id:'s1'}]}]}]";

        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler, Version version,
            params (int key, M2Type wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(), routerVersion: version);
            var rec = fields.ToM2Record(f => f.key, f => new M2Value(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.DecodePlan());
        }

        private static Dictionary<string, string> DecodeService(Version version)
            => Decode(Service, "/ip/service", new[] { 25, 1 }, version, (0x7, M2Type.String, (object)"10.0.0.0/8"));

        private static Dictionary<string, string> DecodeEmail(Version version)
            => Decode(Email, "/tool/e-mail", new[] { 26, 1 }, version, (0x1, M2Type.String, (object)"10.0.0.1"));

        private static void AssertSpellings(Dictionary<string, string> row, string value, string[] present, string[] absent)
        {
            foreach (string name in present) Assert.AreEqual(value, row[name], name);
            foreach (string name in absent) Assert.IsFalse(row.ContainsKey(name), name + " is the other version's spelling");
        }

        [TestMethod]
        public void AFieldRenamedBetweenVersions_IsReportedAsThatRouterSpellsIt()
        {
            AssertSpellings(DecodeService(new Version(6, 49, 21)), "10.0.0.0/8", new[] { "address" }, new[] { "available-from" });
            AssertSpellings(DecodeService(new Version(7, 21, 5)), "10.0.0.0/8", new[] { "address" }, new[] { "available-from" });
            AssertSpellings(DecodeService(new Version(7, 25)), "10.0.0.0/8", new[] { "available-from" }, new[] { "address" });

            AssertSpellings(DecodeEmail(new Version(6, 49, 21)), "10.0.0.1", new[] { "address" }, new[] { "server" });
            AssertSpellings(DecodeEmail(new Version(7, 21, 5)), "10.0.0.1", new[] { "server" }, new[] { "address" });
            AssertSpellings(DecodeEmail(new Version(7, 25)), "10.0.0.1", new[] { "server" }, new[] { "address" });
        }

        [TestMethod]
        public void BetweenTheVersionsMeasured_OrWithNoVersion_BothSpellingsStay()
        {
            var both = new[] { "address", "available-from" };
            AssertSpellings(DecodeService(new Version(7, 23)), "10.0.0.0/8", both, new string[0]);
            AssertSpellings(DecodeService(null), "10.0.0.0/8", both, new string[0]);
        }

        [TestMethod]
        public void TheSpellingThisVersionDoesNotUse_IsNotReadable()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(Service), "the trimmed catalog must parse");
            WinboxFieldResolver At(Version version) => new WinboxFieldResolver("/ip/service", new[] { 25, 1 }, catalog,
                new Dictionary<string, int>(), routerVersion: version);

            Assert.IsTrue(At(new Version(7, 25)).IsUnusedSpelling("address"));
            Assert.IsFalse(At(new Version(7, 25)).IsUnusedSpelling("available-from"));
            Assert.IsTrue(At(new Version(7, 21, 5)).IsUnusedSpelling("available-from"));
            Assert.IsFalse(At(new Version(7, 23)).IsUnusedSpelling("address"), "between the versions measured, both stay");
            Assert.IsFalse(At(null).IsUnusedSpelling("address"), "with no version, both stay");
        }

        [DataTestMethod]
        [DataRow("7.25", "7.25")]
        [DataRow("7.21.5 (stable)", "7.21.5")]
        [DataRow("7.26beta3", "7.26")]
        [DataRow("6.49.21", "6.49.21")]
        public void TheRouterVersionIsItsLeadingNumbers(string text, string expected)
            => Assert.AreEqual(new Version(expected), tik4net.WinboxNative.WinboxNativeConnection.ParseRouterVersion(text));

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("unknown")]
        public void NoNumberIsNoVersion(string text)
            => Assert.IsNull(tik4net.WinboxNative.WinboxNativeConnection.ParseRouterVersion(text));

        private const string Vrrp =
            "[{name:'Interfaces',c:[{name:'VRRP',title:'VRRP',type:'map',path:[ 20,0 ],c:[" +
            "{name:'Sync. Connection Tracking',type:'bool',id:'b70'}," +
            "{name:'Conn. Tracking Port',type:'number',id:'u71'}]}]}]";

        private static Dictionary<string, string> DecodeVrrp(params (int key, M2Type wire, object value)[] fields)
            => Decode(Vrrp, "/interface/vrrp", new[] { 20, 0 }, new Version(7, 25), fields);

        [TestMethod]
        public void AFieldTheApiPrintsOnlyWithASibling_FollowsTheSibling()
        {
            var off = DecodeVrrp((0x70, M2Type.Bool, (object)false), (0x71, M2Type.U32, (object)8275u));
            Assert.IsFalse(off.ContainsKey("connection-tracking-port"), "the API leaves it out with sync off");

            var on = DecodeVrrp((0x70, M2Type.Bool, (object)true), (0x71, M2Type.U32, (object)8275u));
            Assert.AreEqual("8275", on["connection-tracking-port"]);
        }

        [TestMethod]
        public void WithTheSiblingUnread_TheFieldStays()
        {
            // Dropping a value the router did report is the worse error (as for a .jg condition that answers unknown).
            var row = DecodeVrrp((0x71, M2Type.U32, (object)8275u));
            Assert.AreEqual("8275", row["connection-tracking-port"]);
        }
    }
}
