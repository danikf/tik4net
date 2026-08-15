// WinboxDeckPaneTests.cs — router-free tests for A3: a `type:'deck'` PANE holds the settings WinBox shows
// only for one KIND of record, and RouterOS puts every kind's parameters in one flat record, telling them
// apart by prefixing the kind (memory-lines, pcq-rate).
//
// Two things went wrong before this. Panes routinely reuse a label — 'Stop on Full' is memory's b4 AND
// disk's b6 — and a per-label field map kept only the first, so disk-stop-on-full could not be written at
// all; and every pane's keys ride on every row, so a memory action decoded with a disk action's fields.
//
// The .jg fragments are the real 7.23.2 declarations; the API spellings were read off the live router with
// tab completion (`/system/logging/action add ?`, `/queue/type add ?`).

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxDeckPaneTests
    {
        // roteros.jg Log Action [3,1], cut to the memory/disk/remote panes. 'Type' is the deck's selector
        // (the API calls it 'target'); memory=0, disk=1, remote=3.
        private const string LogActionWindow =
            "[{name:'Log Action',title:'Actions',type:'map',path:[ 3,1 ],c:[" +
            "{name:'Name',type:'string',id:'s1'}," +
            "{name:'Type',title:'Target',type:'enm',id:'u2'," +
              "values:{type:'static',map:['memory','disk','echo','remote','email','script']}}," +
            "{type:'deck',panes:[" +
              "{vals:[ 0 ],c:[{name:'Lines',type:'number',id:'u3',def:1000},{name:'Stop on Full',type:'bool',id:'b4'}]}," +
              "{vals:[ 1 ],c:[{name:'File Name',type:'string',id:'s10'},{name:'Lines Per File',type:'number',id:'u5'}," +
                "{name:'File Count',type:'number',id:'u11'},{name:'Stop on Full',type:'bool',id:'b6'}]}," +
              "{vals:[ 3 ],c:[{name:'Remote Port',type:'number',id:'u8',def:514}," +
                "{name:'Src. Address',type:'ipaddr',id:'uf'}]}]," +
              "selon:'Type'}]}]";

        private static readonly int[] LogAction = { 3, 1 };

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog, string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>());

        private static Dictionary<string, string> Decode(
            WinboxJgCatalog catalog, string apiPath, int[] handler, Dictionary<int, Tuple<string, object>> rec)
        {
            var resolver = Resolver(catalog, apiPath, handler);
            return new WinboxRecordCodec(null, catalog)
                .DecodeRecord(rec, resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        private static Dictionary<int, Tuple<string, object>> Rec(params (int key, string type, object val)[] fields)
        {
            var rec = new Dictionary<int, Tuple<string, object>>();
            foreach (var f in fields) rec[f.key] = Tuple.Create(f.type, f.val);
            return rec;
        }

        // ── addressability (writes) ────────────────────────────────────────────

        [TestMethod]
        public void BothPanesFieldsAreAddressableUnderTheirKind()
        {
            var r = Resolver(Parse(LogActionWindow), "/system/logging/action", LogAction);

            Assert.AreEqual(0x3, r.ResolveKey("memory-lines"));
            Assert.AreEqual(0x4, r.ResolveKey("memory-stop-on-full"));
            Assert.AreEqual(0x6, r.ResolveKey("disk-stop-on-full"),
                "the disk pane's 'Stop on Full' was unreachable — the memory pane's label had taken the name");
            Assert.AreEqual(0x10, r.ResolveKey("disk-file-name"));
            Assert.AreEqual(0x5, r.ResolveKey("disk-lines-per-file"));
            Assert.AreEqual(0x11, r.ResolveKey("disk-file-count"));
        }

        [TestMethod]
        public void ThePlainLabelKeepsResolvingToWhatItAlwaysDid()
        {
            // The prefixed name is an ADDITIONAL registration. Anything that resolved before — including a
            // caller who learned the plain WinBox label — must still resolve, and to the same key.
            var r = Resolver(Parse(LogActionWindow), "/system/logging/action", LogAction);

            Assert.AreEqual(0x3, r.ResolveKey("lines"));
            Assert.AreEqual(0x4, r.ResolveKey("stop-on-full"), "first pane wins the plain label, as before");
        }

        [TestMethod]
        public void TheKindIsNotDoubledWhenTheLabelAlreadyCarriesIt()
        {
            // WinBox writes 'Remote Port' inside the remote pane and the API says remote-port — not
            // remote-remote-port. Same rule gives /queue/type's 'PFIFO Queue Size' one 'pfifo', not two.
            var r = Resolver(Parse(LogActionWindow), "/system/logging/action", LogAction);

            Assert.AreEqual(0x8, r.ResolveKey("remote-port"));
            Assert.AreEqual("remote-port", WinboxFieldResolver.PrefixWithKind("remote", "remote-port"));
            Assert.AreEqual("memory-lines", WinboxFieldResolver.PrefixWithKind("memory", "lines"));
        }

        // ── naming (reads) ─────────────────────────────────────────────────────

        [TestMethod]
        public void AnOptedInPathReportsThePaneFieldsUnderTheirApiNames()
        {
            // target=memory (0). The API prints exactly name/target/memory-lines/memory-stop-on-full here.
            var decoded = Decode(Parse(LogActionWindow), "/system/logging/action", LogAction,
                Rec((0x1, "str", "memory"), (0x2, "u32", (uint)0),
                    (0x3, "u32", (uint)1000), (0x4, "bool", false),
                    (0x10, "str", "log"), (0x5, "u32", (uint)100), (0x6, "bool", true)));

            Assert.AreEqual("1000", decoded["memory-lines"]);
            Assert.AreEqual("false", decoded["memory-stop-on-full"]);
            Assert.IsFalse(decoded.ContainsKey("lines"), "the API has no field called 'lines'");
        }

        [TestMethod]
        public void AnotherKindsPaneIsNotReportedOnThisRecord()
        {
            // The router sends the disk pane's keys on a memory row too; RouterOS's API reports none of them.
            var decoded = Decode(Parse(LogActionWindow), "/system/logging/action", LogAction,
                Rec((0x2, "u32", (uint)0),
                    (0x3, "u32", (uint)1000),
                    (0x10, "str", "log"), (0x5, "u32", (uint)100), (0x6, "bool", true)));

            Assert.IsFalse(decoded.ContainsKey("disk-file-name"), "a memory action has no disk settings");
            Assert.IsFalse(decoded.ContainsKey("disk-stop-on-full"));
            Assert.IsFalse(decoded.ContainsKey("file-name"));
            Assert.AreEqual("memory", decoded["target"], "the selector itself is still reported");
        }

        [TestMethod]
        public void TheDiskPaneIsReportedOnADiskRecord()
        {
            var decoded = Decode(Parse(LogActionWindow), "/system/logging/action", LogAction,
                Rec((0x2, "u32", (uint)1),
                    (0x3, "u32", (uint)1000), (0x4, "bool", false),
                    (0x10, "str", "log"), (0x5, "u32", (uint)100), (0x11, "u32", (uint)2), (0x6, "bool", true)));

            Assert.AreEqual("log", decoded["disk-file-name"]);
            Assert.AreEqual("100", decoded["disk-lines-per-file"]);
            Assert.AreEqual("2", decoded["disk-file-count"]);
            Assert.AreEqual("true", decoded["disk-stop-on-full"], "…and it is b6, not the memory pane's b4");
            Assert.IsFalse(decoded.ContainsKey("memory-lines"));
        }

        [TestMethod]
        public void ARecordWithoutItsSelectorKeepsEveryPane()
        {
            // Without the kind there is no honest way to say which pane is live, so nothing is dropped.
            var decoded = Decode(Parse(LogActionWindow), "/system/logging/action", LogAction,
                Rec((0x3, "u32", (uint)1000), (0x10, "str", "log")));

            Assert.IsTrue(decoded.ContainsKey("memory-lines"));
            Assert.IsTrue(decoded.ContainsKey("disk-file-name"));
        }

        [TestMethod]
        public void APathThatDoesNotOptInKeepsTheNamesItAlwaysDecodedTo()
        {
            // The blast radius guard. ~70 windows in the 7.23.2 catalog have decks and we have ground truth
            // for two of them, so a path that is not in the shipped table must decode exactly as before:
            // plain names, and the second pane's shadowed field still absent (never under the first's name).
            var decoded = Decode(Parse(LogActionWindow), "/not/shipped/here", LogAction,
                Rec((0x2, "u32", (uint)1), (0x3, "u32", (uint)1000), (0x4, "bool", false),
                    (0x10, "str", "log"), (0x6, "bool", true)));

            Assert.IsFalse(decoded.ContainsKey("memory-lines"), "this path is not opted in");
            Assert.AreEqual("log", decoded["file-name"], "the plain names are untouched");
            // 'Stop on Full' is reported by NEITHER pane here: the memory one (b4) is another kind's on this
            // disk record, and the disk one (b6) owns no plain registration because the memory pane took the
            // label. That is one field short — the same field that was missing before — but never b6's value
            // under b4's key or the other way round, which is the outcome that would be indistinguishable
            // from a correct read.
            Assert.IsFalse(decoded.ContainsKey("stop-on-full"));
            Assert.IsFalse(decoded.ContainsKey("disk-stop-on-full"));
        }

        // ── a pane covering several kinds ──────────────────────────────────────

        // secure.jg IPsec Identity: 'My ID' is shown for fqdn(1)/user fqdn(2)/key id(3) — one pane, three
        // kinds — and 'My ID Address' only for address(4). The API composes both into one `my-id` field, and
        // says plain "auto" when the type is auto(100).
        private const string IpsecIdentityWindow =
            "[{name:'IPsec Identity',type:'map',path:[ 85,3 ],c:[" +
            "{name:'My ID Type',type:'enm',id:'ue',def:100," +
              "values:{type:'static',map:{1:'fqdn',2:'user fqdn',3:'key id',4:'address',5:'dn',100:'auto'}}}," +
            "{type:'deck',panes:[" +
              "{vals:[ 1,2,3 ],c:[{name:'My ID',type:'string',id:'s11'}]}," +
              "{vals:[ 4 ],c:[{name:'My ID Address',type:'ipaddr',id:'uf'}]}]," +
              "selon:'My ID Type'}]}]";

        [TestMethod]
        public void APaneCoveringSeveralKindsIsStillFilteredByTheRecordsKind()
        {
            var catalog = Parse(IpsecIdentityWindow);

            // my-id-type = auto (100): neither pane applies, and the API prints my-id=auto with no separate
            // value field. Reporting the pane's empty string instead gave the mapper a my-id it could not
            // convert to the enum.
            var auto = Decode(catalog, "/ip/ipsec/identity", new[] { 85, 3 },
                Rec((0xE, "u32", (uint)100), (0x11, "str", "")));
            Assert.IsFalse(auto.ContainsKey("my-id"), "no pane is live for 'auto'");

            // my-id-type = fqdn (1): the pane IS live, so its value is reported (under its plain name — the
            // API's composed "fqdn:host" spelling is not something this path claims to produce).
            var fqdn = Decode(catalog, "/ip/ipsec/identity", new[] { 85, 3 },
                Rec((0xE, "u32", (uint)1), (0x11, "str", "host.example.com")));
            Assert.AreEqual("host.example.com", fqdn["my-id"]);
        }

        // ── /queue/type, where every pane is kind-prefixed ─────────────────────

        private const string QueueTypeWindow =
            "[{name:'Queue Type',title:'Queue Types',type:'map',path:[ 20,10 ],c:[" +
            "{name:'Type Name',type:'string',id:'s16'}," +
            "{name:'Kind',type:'enm',id:'u15',def:2," +
              "values:{type:'static',map:['','bfifo','pfifo','red','sfq','pcq','mq pfifo','none','codel','fq codel']}}," +
            "{type:'deck',panes:[" +
              "{vals:[ 3 ],c:[{name:'RED Queue Size',title:'Queue Size',type:'number',id:'u12d'}," +
                "{name:'Burst',type:'number',id:'u130'},{name:'Avg. Packet Size',type:'number',id:'u131'}]}," +
              "{vals:[ 5 ],c:[{name:'Rate',type:'unit',id:'u1f5'},{name:'Queue Size',type:'unit',id:'u1f6'}," +
                "{name:'Classifier',type:'number',id:'u1f7'}]}," +
              "{vals:[ 8 ],c:[{name:'Limit',type:'number',id:'u2bd'},{name:'Interval',type:'number',id:'u2be'}]}," +
              "{vals:[ 9 ],c:[{name:'Limit',type:'number',id:'u321'},{name:'Interval',type:'number',id:'u322'}]}]," +
              "selon:'Kind'}]}]";

        private static readonly int[] QueueTypeHandler = { 20, 10 };

        [TestMethod]
        public void QueueTypePaneFieldsResolveUnderTheirApiNames()
        {
            var r = Resolver(Parse(QueueTypeWindow), "/queue/type", QueueTypeHandler);

            Assert.AreEqual(0x1F5, r.ResolveKey("pcq-rate"));
            Assert.AreEqual(0x1F7, r.ResolveKey("pcq-classifier"));
            Assert.AreEqual(0x130, r.ResolveKey("red-burst"));
            Assert.AreEqual(0x131, r.ResolveKey("red-avg-packet"), "API 'avg-packet', WinBox 'Avg. Packet Size'");
            Assert.AreEqual(0x12D, r.ResolveKey("red-limit"), "API 'limit', WinBox 'RED Queue Size'");
            Assert.AreEqual(0x1F6, r.ResolveKey("pcq-limit"), "…and the pcq pane's own 'Queue Size'");
        }

        [TestMethod]
        public void TwoPanesRepeatingEveryLabelStayApart()
        {
            // codel and fq-codel declare the same five field names with different keys — the worst case of
            // the collision, and one where dropping the second pane loses a whole kind's parameters.
            var r = Resolver(Parse(QueueTypeWindow), "/queue/type", QueueTypeHandler);

            Assert.AreEqual(0x2BD, r.ResolveKey("codel-limit"));
            Assert.AreEqual(0x321, r.ResolveKey("fq-codel-limit"));
            Assert.AreEqual(0x2BE, r.ResolveKey("codel-interval"));
            Assert.AreEqual(0x322, r.ResolveKey("fq-codel-interval"));
        }

        [TestMethod]
        public void AQueueTypeRecordReportsOnlyItsOwnKind()
        {
            var decoded = Decode(Parse(QueueTypeWindow), "/queue/type", QueueTypeHandler,
                Rec((0x16, "str", "pcq-download-default"), (0x15, "u32", (uint)5),
                    (0x1F5, "u32", (uint)0), (0x1F6, "u32", (uint)50),
                    (0x12D, "u32", (uint)60), (0x2BD, "u32", (uint)1000)));

            Assert.AreEqual("pcq-download-default", decoded["name"], "'Type Name' is the API's 'name'");
            Assert.AreEqual("pcq", decoded["kind"]);
            Assert.AreEqual("0", decoded["pcq-rate"]);
            Assert.AreEqual("50", decoded["pcq-limit"]);
            Assert.IsFalse(decoded.ContainsKey("red-limit"), "a pcq queue has no red settings");
            Assert.IsFalse(decoded.ContainsKey("codel-limit"));
        }
    }
}
