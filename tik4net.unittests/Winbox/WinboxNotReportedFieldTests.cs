// WinboxNotReportedFieldTests.cs — fields a WinBox window decodes that the API's menu does not have (WinBox review F11).
//
// Measured on 7.25 with the API's `get value-name=`, which traps on a name the menu lacks: the /system/clock window's
// Manual Time Zone tab is the API's /system/clock/manual; /interface's live rates are /interface/monitor-traffic (which
// native decodes through /interface's alias set); /interface/bridge/settings ([20,17]) had the bridge's synthetic
// auto-mac (0x65), inherited from the /interface/bridge set as if it read the bridge's rows.
// Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxNotReportedFieldTests
    {
        private static Dictionary<string, string> Decode(string catalogText, string apiPath, int[] handler,
            string windowSuffix, Func<string, WinboxHandler?> pathHandler, params (int key, M2Type wire, object value)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(catalogText), "the trimmed catalog must parse");
            string windowKey = windowSuffix == null ? null
                : catalog.GetDerivedPaths().Keys.FirstOrDefault(k => k.EndsWith(windowSuffix, StringComparison.Ordinal));
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                windowKey: windowKey, pathHandler: pathHandler);
            var rec = fields.ToM2Record(f => f.key, f => new M2Value(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.DecodePlan());
        }

        [TestMethod]
        public void ATabTheApiServesFromAnotherCommandIsLeftOut()
        {
            const string clock =
                "[{name:'System',c:[{name:'Clock',title:'Clock',type:'item',path:[ 24,0 ],c:[" +
                "{name:'Time',type:'tab'},{name:'Time Zone Name',type:'string',id:'s7'}," +
                "{name:'Manual Time Zone',type:'tab'},{name:'Time Zone',type:'string',id:'s8'}]}]}]";

            var decoded = Decode(clock, "/system/clock", new[] { 24, 0 }, "clock", null,
                (0x7, M2Type.String, (object)"Europe/Prague"), (0x8, M2Type.String, (object)"+00:00"));

            Assert.AreEqual("Europe/Prague", decoded["time-zone-name"]);
            Assert.IsFalse(decoded.ContainsKey("time-zone"), "the Manual Time Zone tab is /system/clock/manual");
        }

        [TestMethod]
        public void LiveRatesAreLeftOutOfTheInterfaceListAndKeptForMonitorTraffic()
        {
            var rec = new[] { (0x10006, M2Type.String, (object)"ether1"), (0x100D3, M2Type.U64, (object)51928UL) };

            var list = Decode("[]", "/interface", new[] { 20, 0 }, null, null, rec);
            var monitor = Decode("[]", "/interface/monitor-traffic", new[] { 20, 0 }, null, null, rec);

            Assert.AreEqual("ether1", list["name"]);
            Assert.IsFalse(list.ContainsKey("rx-bits-per-second"), "/interface prints no live rate");
            Assert.AreEqual("51928", monitor["rx-bits-per-second"], "monitor-traffic reads the same record for its rates");
        }

        [TestMethod]
        public void AChildMenuOnAnotherHandlerInheritsNoSynthetic()
        {
            Func<string, WinboxHandler?> handlers = p => p == "/interface/bridge" ? new WinboxHandler(20, 0)
                : p == "/interface/bridge/settings" ? new WinboxHandler(20, 17) : (WinboxHandler?)null;
            var rec = new[] { (0x65, M2Type.Bool, (object)false) };

            var settings = Decode("[]", "/interface/bridge/settings", new[] { 20, 17 }, null, handlers, rec);
            var bridge = Decode("[]", "/interface/bridge", new[] { 20, 0 }, null, handlers, rec);

            Assert.IsFalse(settings.ContainsKey("auto-mac"), "0x65 is the bridge's auto-mac, not a bridge-settings field");
            Assert.AreEqual("false", bridge["auto-mac"]);
        }

        [TestMethod]
        public void ASubtypeOnTheAncestorsHandlerStillInheritsItsSynthetics()
        {
            Func<string, WinboxHandler?> handlers = p => new WinboxHandler(20, 0);
            var vlan = Decode("[]", "/interface/vlan", new[] { 20, 0 }, null, handlers,
                (0x3E9, M2Type.Raw, (object)new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }));

            Assert.AreEqual("AA:BB:CC:DD:EE:FF", vlan["mac-address"], "a vlan row is an /interface row");
        }
    }
}
