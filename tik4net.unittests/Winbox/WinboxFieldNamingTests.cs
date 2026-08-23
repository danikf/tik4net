// WinboxFieldNamingTests.cs — which of a key's several .jg names the decode reports, and one unit.
//
// A key can be declared more than once in one window. The commonest shape is a pair guarded by opposite
// conditions, the disused one prefixed 'Old' and carrying the other's name as its `title`:
//
//     {name:'Old Cache Path',title:'Cache Path',id:'se',on:'oldfileman'}
//     {name:'Cache Path',                       id:'se',on:'newfileman'}
//
// Both are registered — a caller that resolved either spelling must keep reaching the same key — but only
// one may CLAIM the key's reported name, and first-wins picked the 'Old' one. /ip/proxy read
// `old-cache-path` where RouterOS says `cache-path`, and so did twenty-four other fields.
//
// The twin has to exist. A `title` with no same-key namesake is a heading rather than the field's other
// name, and acting on those moved eighteen keys onto names nothing had confirmed.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxFieldNamingTests
    {
        private static readonly int[] Handler = { 96, 1 };

        // /ip/proxy, cut to the pair plus a neighbour.
        private const string TwinWindow =
            "[{name:'IP',title:'IP',group:'IP',c:[" +
            "{name:'Web Proxy',title:'Web Proxy Settings',type:'item',path:[ 96,1 ],c:[" +
              "{name:'Old Cache Path',title:'Cache Path',type:'string',id:'se',on:'oldfileman'}," +
              "{name:'Cache Path',type:'string',id:'se',on:'newfileman'}," +
              "{name:'Cache Hit DSCP (TOS)',type:'number',id:'u18'}]}" +
            "]}]";

        // The same window with only the 'Old' half — the conditional twin absent, as it is on a build that
        // ships one of the two.
        private const string LoneWindow =
            "[{name:'IP',title:'IP',group:'IP',c:[" +
            "{name:'Web Proxy',title:'Web Proxy Settings',type:'item',path:[ 96,1 ],c:[" +
              "{name:'Type',title:'Target',type:'string',id:'se'}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog)
            => new WinboxFieldResolver("/ip/proxy", Handler, catalog, new Dictionary<string, int>());

        private static Dictionary<string, string> Decode(
            WinboxFieldResolver resolver, WinboxJgCatalog catalog,
            params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void OfTwoDeclarationsOnOneKeyTheOneTheOtherTitlesIsTheReportedName()
        {
            var catalog = Parse(TwinWindow);
            var decoded = Decode(Resolver(catalog), catalog,
                (0xFE0001, "u32", 1u), (0xE, "str", "web-proxy"));

            Assert.AreEqual("web-proxy", decoded["cache-path"]);
            Assert.IsFalse(decoded.ContainsKey("old-cache-path"), "not under both names at once");
        }

        [TestMethod]
        public void TheDisusedSpellingStillResolvesForAWrite()
        {
            // Both names were registered before and both must stay registered: dropping one would break a
            // caller that had found the working spelling by experiment.
            var resolver = Resolver(Parse(TwinWindow));

            var byOld = resolver.EncodeField("old-cache-path", "web-proxy");
            var byNew = resolver.EncodeField("cache-path", "web-proxy");

            Assert.AreEqual(BitConverter.ToString(byNew[0]), BitConverter.ToString(byOld[0]),
                "same key, same value, same field either way");
        }

        [TestMethod]
        public void ATitleWithNoTwinLeavesTheDeclarationAlone()
        {
            // {name:'Type',title:'Target'} on /system/logging/action is the API's `target` — but nothing in
            // the catalog says so, and a shipped alias is what settles it. This rule must not guess: with no
            // second declaration on the key, the label keeps the name it has always had.
            var catalog = Parse(LoneWindow);
            var decoded = Decode(Resolver(catalog), catalog,
                (0xFE0001, "u32", 1u), (0xE, "str", "memory"));

            Assert.AreEqual("memory", decoded["type"]);
            Assert.IsFalse(decoded.ContainsKey("target"), "the title alone does not rename anything");
        }

        [TestMethod]
        public void AnAllOnesNetmaskIsSlashThirtyTwoAndNotAnUnsetMarker()
        {
            // A .jg field declaring 4294967295 as its default is normally the router saying "not set" — a
            // logging action's Syslog Severity arrives that way on a row the API prints nothing for. A
            // netmask is the exception: types.netmask.tostr is netmask2len(val), so all-ones is /32, and
            // all five netmask fields in the 7.24 catalog declare it as their default. The three stock pcq
            // queue types carry it, and the API prints pcq-src-address-mask=32 where the field used to be
            // dropped entirely.
            const string Window =
                "[{name:'Queues',title:'Queues',group:'Queues',c:[" +
                "{name:'Queue Types',title:'Queue Type',type:'map',path:[ 20,10 ],c:[" +
                  "{name:'Src. Address Mask',type:'netmask',id:'u1fc',def:4294967295}," +
                  "{name:'Total Limit',type:'number',id:'u1f8'}]}" +
                "]}]";
            var catalog = Parse(Window);
            var resolver = new WinboxFieldResolver("/queue/type", new[] { 20, 10 }, catalog,
                                                   new Dictionary<string, int>());
            var decoded = Decode(resolver, catalog,
                (0x1FC, "u32", 4294967295u), (0x1F8, "u32", 2000u));

            Assert.AreEqual("32", decoded["src-address-mask"],
                "the LENGTH, as types.netmask.tostr and RouterOS both give it");
            Assert.AreEqual("2000", decoded["total-limit"]);
        }

        [TestMethod]
        public void ANarrowerNetmaskReadsAsItsOwnLength()
        {
            const string Window =
                "[{name:'Queues',title:'Queues',group:'Queues',c:[" +
                "{name:'Queue Types',title:'Queue Type',type:'map',path:[ 20,10 ],c:[" +
                  "{name:'Src. Address Mask',type:'netmask',id:'u1fc',def:4294967295}]}" +
                "]}]";
            var catalog = Parse(Window);
            var resolver = new WinboxFieldResolver("/queue/type", new[] { 20, 10 }, catalog,
                                                   new Dictionary<string, int>());

            Assert.AreEqual("24", Decode(resolver, catalog, (0x1FC, "u32", 0xFFFFFF00u))["src-address-mask"]);

            // And the write side takes the same spelling back — plus the dotted one, as
            // types.netmask.fromstr does.
            var byLength = resolver.EncodeField("src-address-mask", "24");
            var byMask = resolver.EncodeField("src-address-mask", "255.255.255.0");
            Assert.AreEqual(BitConverter.ToString(byMask[0]), BitConverter.ToString(byLength[0]));
        }

        [TestMethod]
        public void AKibibyteFieldReadsAsTheBytesTheApiPrints()
        {
            // webfig's types.kbytes renders "N KiB" straight from the wire number — no scale multiply,
            // unlike types.bytes, which is already in bytes. RouterOS prints bytes on both, so a kbytes
            // field read 1024 times too small. /system/resource on 7.24: 91372 -> 93564928, the API's own
            // number to the byte.
            const string Window =
                "[{name:'System',title:'System',group:'System',c:[" +
                "{name:'Resources',title:'Resources',type:'item',path:[ 24,3 ],c:[" +
                  "{name:'Total HDD Size',type:'kbytes',id:'ua'}," +
                  "{name:'CPU Count',type:'number',id:'u18'}]}" +
                "]}]";
            var catalog = Parse(Window);
            // Deliberately NOT "/system/resource": that path carries a shipped alias renaming this very
            // field, and the unit under test here is the kbytes conversion, not the alias table.
            var resolver = new WinboxFieldResolver("/x", new[] { 24, 3 }, catalog,
                                                   new Dictionary<string, int>());
            var decoded = Decode(resolver, catalog, (0xA, "u32", 91372u), (0x18, "u32", 16u));

            Assert.AreEqual("93564928", decoded["total-hdd-size"]);
            Assert.AreEqual("16", decoded["cpu-count"], "and a plain number is still a plain number");
        }
    }
}
