// WinboxSystemStateKeyTests.cs — router-free tests for the system keys that carry ROW STATE.
//
// The router sends a handful of 0xFE keys on table after table that no .jg window declares, so the catalog
// cannot name them and every one of them reached no caller: /ip/arp read nine fields where the API prints
// twelve, and `dynamic` — the field that says whether the row was learned or configured — was among the
// missing three on every table that has it.
//
// They are ROW STATE, not configuration: the router computes them and neither WinBox nor the API lets you
// write one. So they resolve for a READ and must keep refusing for a write, rather than resolving to an
// untyped seed value aimed at a bool key.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxSystemStateKeyTests
    {
        private static readonly int[] ArpHandler = { 20, 8 };
        private static readonly int[] ListHandler = { 20, 90 };

        // roteros.jg, ARP [20,8], cut to the fields the record below carries. Note what is NOT here: no
        // window in the 7.24 catalog declares a field labelled 'Dynamic', 'Invalid' or 'Builtin' on any
        // handler, which is why these keys reached no caller before.
        private const string ArpWindow =
            "[{name:'ARP',title:'ARP List',group:'IP',c:[" +
            "{name:'ARP',title:'ARP List',type:'map',path:[ 20,8 ],c:[" +
              "{name:'IP Address',type:'ipaddr',id:'u1'}," +
              "{name:'MAC Address',type:'macaddr',id:'r2'}," +
              "{name:'Published',type:'bool',id:'u5'}]}" +
            "]}]";

        // roteros.jg, Interface List [20,90]: a table whose rows are partly shipped with the router.
        private const string ListWindow =
            "[{name:'Interface',title:'Interface',group:'Interfaces',c:[" +
            "{name:'Interface List',title:'Interface List',type:'map',path:[ 20,90 ],nameval:'Name',c:[" +
              "{name:'Name',type:'string',id:'sfe0010'}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(string body, string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, Parse(body), new Dictionary<string, int>());

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
        public void ALearnedRowReportsDynamic()
        {
            var catalog = Parse(ArpWindow);
            var decoded = Decode(Resolver(ArpWindow, "/ip/arp", ArpHandler), catalog,
                (0xFE0001, "u32", 1u), (0xFE0007, "bool", true), (0x1, "u32", 17082560u));

            Assert.AreEqual("true", decoded["dynamic"]);
        }

        [TestMethod]
        public void AConfiguredRowReportsItFalseRatherThanNotAtAll()
        {
            // The two rows differ in this key and nothing else — which is how the pairing was established
            // in the first place, on a live 7.24 ARP table holding both kinds at once.
            var catalog = Parse(ArpWindow);
            var decoded = Decode(Resolver(ArpWindow, "/ip/arp", ArpHandler), catalog,
                (0xFE0001, "u32", 3u), (0xFE0007, "bool", false), (0x1, "u32", 17082560u));

            Assert.AreEqual("false", decoded["dynamic"]);
        }

        [TestMethod]
        public void ARowThatIsConfiguredButNotInEffectReportsInvalid()
        {
            // Confirmed the same way: an /ip/address on an interface that was then disabled answers True
            // where the row beside it answers False, matching the API's `invalid` on the same two rows.
            var catalog = Parse(ArpWindow);
            var resolver = Resolver(ArpWindow, "/ip/arp", ArpHandler);

            Assert.AreEqual("true", Decode(resolver, catalog,
                (0xFE0001, "u32", 1u), (0xFE0008, "bool", true))["invalid"]);
            Assert.AreEqual("false", Decode(resolver, catalog,
                (0xFE0001, "u32", 2u), (0xFE0008, "bool", false))["invalid"]);
        }

        [TestMethod]
        public void AShippedRowReportsTheFlagUnderTheNameItsOwnTableUsesForIt()
        {
            // One flag, two API spellings. Most tables that have it say `default`, which is the seed;
            // /interface/list says `builtin`, which is a key alias — and a key alias wins over the seed.
            var catalog = Parse(ListWindow);
            var decoded = Decode(Resolver(ListWindow, "/interface/list", ListHandler), catalog,
                (0xFE0001, "u32", 33554432u), (0xFE000D, "bool", true), (0xFE0007, "bool", false),
                (0xFE0010, "string", "all"));

            Assert.AreEqual("true", decoded["builtin"]);
            Assert.IsFalse(decoded.ContainsKey("default"), "not under both names at once");
            Assert.AreEqual("false", decoded["dynamic"]);
            Assert.AreEqual("all", decoded["name"], "and the row still reads the way it always did");
        }

        [TestMethod]
        public void ATableWithNoAliasReportsTheMajoritySpelling()
        {
            var catalog = Parse(ArpWindow);
            var decoded = Decode(Resolver(ArpWindow, "/ip/arp", ArpHandler), catalog,
                (0xFE0001, "u32", 1u), (0xFE000D, "bool", true));

            Assert.AreEqual("true", decoded["default"]);
        }

        [TestMethod]
        public void RowStateCannotBeWritten()
        {
            // Not a gap: the router computes these and the API refuses them too. Refusing by NAME is the
            // point — a seed that resolved would send an untyped value at a bool key, which the router
            // accepts, answers, and ignores.
            var resolver = Resolver(ArpWindow, "/ip/arp", ArpHandler);

            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => resolver.EncodeField("dynamic", "false"));
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => resolver.EncodeField("invalid", "false"));
            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => resolver.EncodeField("default", "false"));
        }

        [TestMethod]
        public void ACatalogFieldOnTheSameKeyKeepsItsOwnName()
        {
            // The seeds are filled in LAST. A window that declares a field at one of these keys owns the
            // name, exactly as it owns every other key it declares.
            const string OwnedWindow =
                "[{name:'X',title:'X',group:'X',c:[" +
                "{name:'X',title:'X',type:'map',path:[ 99,1 ],c:[" +
                  "{name:'Shipped',type:'bool',id:'bfe000d'}]}" +
                "]}]";
            var catalog = Parse(OwnedWindow);
            var decoded = Decode(Resolver(OwnedWindow, "/x", new[] { 99, 1 }), catalog,
                (0xFE000D, "bool", true));

            Assert.AreEqual("true", decoded["shipped"]);
            Assert.IsFalse(decoded.ContainsKey("default"), "the window's own name wins over the seed");
        }
    }
}
