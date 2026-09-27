// WinboxConditionDecodeTests.cs — router-free tests for the .jg `on:` conditions (WinboxJgCondition): a field
// whose window condition is false on a row is a field RouterOS's API does not report for that row.
//
// The .jg fragments are trimmed from the 7.24 hotspot profile and log action windows. The API behaviour they
// encode was measured on 7.24.4 in both directions: /ip/hotspot/profile with use-radius=no prints no radius-*
// field while the M2 record carries them all, and with use-radius=yes prints every one, empty ones included.
// The same shape of condition is NOT honoured everywhere (/interface/l2tp-server/server prints ipsec-secret with
// use-ipsec=no), which is why only a measured table of conditions is acted on.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxConditionDecodeTests
    {
        private const string HotspotProfileWindow =
            "[{name:'Hotspot Server Profile',title:'Server Profiles',type:'map',path:[ 63,2 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Login By',type:'bitmap',id:'u5',values:{type:'static',map:['mac','cookie','http-chap','https','http-pap','','trial']}}," +
            "{name:'Trial Uptime Limit',type:'interval',id:'u97',def:1800,on:'trial'}," +
            "{name:'Use RADIUS',type:'bool',id:'b8c'}," +
            "{name:'NAS Port Type',type:'number',id:'u90',def:19,on:'radius'}," +
            "{name:'Default Domain',type:'string',id:'s92',on:'radius'}," +
            "{name:'Use IPsec',type:'bool',id:'b9a'}," +
            "{name:'IPsec Secret',type:'string',id:'s9b',on:'ipsec'}," +
            "{name:'radius',type:'cond',c:[{on:'Use RADIUS',pred:{type:'bool',value:1}}]}," +
            "{name:'ipsec',type:'cond',c:[{on:'Use IPsec',pred:{type:'bool',value:1}}]}," +
            "{name:'trial',type:'cond',c:[{on:'Login By',pred:{type:'not',pred:{type:'bitmap',mask:64,value:0}}}]}" +
            "]}]";

        private static readonly int[] HotspotProfile = { 63, 2 };

        // roteros.jg Log Action [3,1] (7.24), cut to the remote target's format fields.
        private const string LogActionWindow =
            "[{name:'Log Action',title:'Actions',type:'map',path:[ 3,1 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Remote Log Format',type:'enm',id:'u15',values:{type:'static',map:['default','syslog','cef']}}," +
            "{name:'Syslog Facility',type:'number',id:'ud',def:3,on:'bsd'}," +
            "{name:'CEF Event Delimiter',type:'string',id:'s16',on:'cef'}," +
            "{name:'bsd',type:'cond',c:[{on:'Remote Log Format',pred:{type:'number',value:[ 1 ]}}]}," +
            "{name:'cef',type:'cond',c:[{on:'Remote Log Format',pred:{type:'number',value:[ 2 ]}}]}" +
            "]}]";

        private static readonly int[] LogAction = { 3, 1 };

        private static Dictionary<string, string> Decode(string window, string apiPath, int[] handler,
            params (int key, string type, object val)[] fields)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(window), "the trimmed window must parse");
            var resolver = new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void AFieldItsConditionSwitchesOffIsNotReported()
        {
            var decoded = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "default"), (0x8C, "bool", false), (0x90, "u32", (uint)19), (0x92, "str", ""));

            Assert.AreEqual("false", decoded["use-radius"], "the controlling field itself is always reported");
            Assert.IsFalse(decoded.ContainsKey("nas-port-type"));
            Assert.IsFalse(decoded.ContainsKey("radius-default-domain"));
        }

        [TestMethod]
        public void AFieldItsConditionSwitchesOnIsReportedEmptyOrNot()
        {
            var decoded = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "t4n"), (0x8C, "bool", true), (0x90, "u32", (uint)19), (0x92, "str", ""));

            Assert.IsTrue(decoded.ContainsKey("nas-port-type"));
            Assert.AreEqual("", decoded["radius-default-domain"], "the API prints it empty once use-radius=yes");
        }

        [TestMethod]
        public void AConditionOfTheSameShapeTheApiDoesNotHonourHidesNothing()
        {
            // l2tp-server's ipsec-secret is printed with use-ipsec=no; the .jg cannot tell it from use-radius.
            var decoded = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "default"), (0x9A, "bool", false), (0x9B, "str", "secret"));

            Assert.AreEqual("secret", decoded["ipsec-secret"]);
        }

        [TestMethod]
        public void ABitmapConditionReadsTheBitItNames()
        {
            // login-by without trial (bit 6) hides trial-*; with it, they are reported.
            var without = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "default"), (0x5, "u32", (uint)0x6), (0x97, "u32", (uint)1800));
            var with = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "t4n"), (0x5, "u32", (uint)0x45), (0x97, "u32", (uint)1800));

            Assert.IsFalse(without.ContainsKey("trial-uptime-limit"));
            Assert.IsTrue(with.ContainsKey("trial-uptime-limit"));
        }

        [TestMethod]
        public void ANumberConditionPicksTheFieldsOfTheSelectedFormat()
        {
            var syslog = Decode(LogActionWindow, "/system/logging/action", LogAction,
                (0x1, "str", "t4n"), (0x15, "u32", (uint)1), (0xD, "u32", (uint)3), (0x16, "str", "\r\n"));
            var byDefault = Decode(LogActionWindow, "/system/logging/action", LogAction,
                (0x1, "str", "remote"), (0x15, "u32", (uint)0), (0xD, "u32", (uint)3), (0x16, "str", "\r\n"));

            Assert.IsTrue(syslog.ContainsKey("syslog-facility"));
            Assert.IsFalse(syslog.ContainsKey("cef-event-delimiter"));
            Assert.IsFalse(byDefault.ContainsKey("syslog-facility"));
            Assert.IsFalse(byDefault.ContainsKey("cef-event-delimiter"));
        }

        [TestMethod]
        public void AControllingFieldTheRowDoesNotCarryReadsAsItsDefault()
        {
            // No 'Use RADIUS' key and no def on it: webfig reads undefined, i.e. false, and hides the field.
            var decoded = Decode(HotspotProfileWindow, "/ip/hotspot/profile", HotspotProfile,
                (0x1, "str", "default"), (0x90, "u32", (uint)19));

            Assert.IsFalse(decoded.ContainsKey("nas-port-type"));
        }
    }
}
