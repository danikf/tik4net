// WinboxJgActionFieldTests.cs — router-free tests for the A1 defect: an action window's ARGUMENTS and the
// record list's COLUMNS share one M2 handler, and merged into one field map the column wins.
//
// The live symptom was silent: /ip/ipsec/key/rsa/generate-key name=X key-size=2048 produced an unnamed
// 1024-bit key and reported success. Two separate causes, both covered here — the action's fields were
// resolved against the record window (whose 'Key Size' is a read-only column, so the argument encoded to
// nothing), and the dispatcher sent no arguments at all. The .jg fragments are the real shapes from
// RouterOS 7.23.2, cut down to what the rules read.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxJgActionFieldTests
    {
        // secure.jg, the IPsec 'Keys' window [85,5]: a record list with a read-only 'Key Size' column, plus
        // three global doits whose arguments include a WRITABLE 'Key Size' enum under the same label.
        private const string IpsecKeyWindow =
            "[{name:'IPsec Key',title:'Keys',type:'map',path:[ 85,5 ],nameval:'Name',nonaddable:1,removable:1,c:[" +
            "{name:'Name',type:'string',id:'sfe0010',min:1}," +
            "{name:'Key Size',type:'number',id:'u1',postfix:'bits',ro:1,width:60}," +
            "{name:'private key',type:'flag',id:'bb',hint:'P'}," +
            "{name:'Export Pub. Key',type:'doit',cmd:2,global:1,c:[" +
              "{name:'Key',type:'enm',id:'ufe0001',values:{type:'dynamic',path:[ 85,5 ]}}," +
              "{name:'File Name',type:'string',id:'s1',min:1}]}," +
            "{name:'Generate Key',type:'doit',cmd:1,global:1,c:[" +
              "{name:'Name',type:'string',id:'sfe0010',min:1}," +
              "{name:'Key Size',type:'enm',id:'u1',values:{type:'static',map:{1024:'1024',2048:'2048',4096:'4096'}}}]}" +
            "]}]";

        // advtool.jg's Wake on LAN [82]: a STANDALONE action window — the handler has no record list at all,
        // so its arguments are the only fields it has.
        private const string WolWindow =
            "[{name:'WoL',title:'WoL',group:'Tools',c:[{title:'Wake on LAN',type:'doit',path:[ 82 ],cmd:1,c:[" +
            "{name:'MAC Address',type:'macaddr',id:'r1'}," +
            "{name:'Interface',type:'enm',id:'u2',def:4294967295,opt:1,values:{type:'dynamic',path:[ 20,0 ]}}]}]}]";

        private static readonly int[] IpsecKeyHandler = { 85, 5 };
        private static readonly int[] WolHandler = { 82 };

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog, string apiPath, int[] handler,
            string actionLabel = null)
            => new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                useGuiNames: false, windowKey: null,
                actionFields: actionLabel == null ? null : catalog.GetActionFields(handler, actionLabel));

        // key → value of a single encoded field, read back through the real M2 parser.
        private static KeyValuePair<int, object> Decoded(IList<byte[]> encoded)
        {
            Assert.AreEqual(1, encoded.Count, "expected exactly one encoded field");
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded[0]).ToArray();
            var fields = M2Message.ParseAllFields(msg);
            Assert.AreEqual(1, fields.Count);
            foreach (var kv in fields) return new KeyValuePair<int, object>(kv.Key, kv.Value.Item2);
            return default(KeyValuePair<int, object>);
        }

        [TestMethod]
        public void TheHandlerMapStillAnswersWithTheRecordColumn()
        {
            // The collision itself, stated as a fact about the catalog: both windows call the field 'Key Size',
            // the record list is parsed first, and its read-only column is what the handler map holds. This is
            // correct FOR A READ — the columns are what a getall row carries — which is why the fix is an
            // overlay for the action rather than a change to this map.
            var fields = Parse(IpsecKeyWindow).GetHandlerFields(IpsecKeyHandler);

            Assert.IsTrue(fields.TryGetValue("key-size", out var keySize));
            Assert.IsTrue(keySize.ReadOnly, "the record list's 'Key Size' column is ro:1");
        }

        [TestMethod]
        public void AnActionsArgumentsAreKeptUnderTheActionsOwnLabel()
        {
            var catalog = Parse(IpsecKeyWindow);

            var generate = catalog.GetActionFields(IpsecKeyHandler, "generate-key");
            Assert.IsNotNull(generate, "the 'Generate Key' doit's arguments must be addressable on their own");
            Assert.IsTrue(generate.TryGetValue("key-size", out var argKeySize));
            Assert.IsFalse(argKeySize.ReadOnly, "the ARGUMENT is writable — only the list column is read-only");
            Assert.AreEqual(0x1, argKeySize.Key);
            Assert.IsNotNull(argKeySize.EnumMap, "and it is the doit's enum, not the list's plain number");

            // Each action keeps its own vocabulary: 'file-name'/'key' belong to Export Pub. Key, not to Generate.
            Assert.IsFalse(generate.ContainsKey("file-name"));
            var export = catalog.GetActionFields(IpsecKeyHandler, "export-pub-key");
            Assert.IsTrue(export.ContainsKey("file-name") && export.ContainsKey("key"));
        }

        [TestMethod]
        public void WithoutTheActionOverlay_TheArgumentEncodesToNothing()
        {
            // The defect, reproduced at the layer it happened on: resolved against the handler alone,
            // 'key-size' is the read-only column, and EncodeField drops read-only fields — so the request
            // reached the router without the argument and RouterOS applied its own default (1024 bits).
            var catalog = Parse(IpsecKeyWindow);
            var plain = Resolver(catalog, "/ip/ipsec/key/rsa", IpsecKeyHandler);

            Assert.AreEqual(0, plain.EncodeField("key-size", "2048").Count,
                "a read-only field encodes to no bytes at all — a silently dropped argument");
        }

        [TestMethod]
        public void WithTheActionOverlay_TheArgumentIsEncoded()
        {
            var catalog = Parse(IpsecKeyWindow);
            var action = Resolver(catalog, "/ip/ipsec/key/rsa", IpsecKeyHandler, "generate-key");

            var keySize = Decoded(action.EncodeField("key-size", "2048"));
            Assert.AreEqual(0x1, keySize.Key);
            Assert.AreEqual((uint)2048, keySize.Value, "the doit's enum maps '2048' to the numeric key size");

            // A field the two windows agree on is unaffected by the overlay.
            var name = Decoded(action.EncodeField("name", "t4n-key"));
            Assert.AreEqual(0xFE0010, name.Key);
            Assert.AreEqual("t4n-key", name.Value);
        }

        [TestMethod]
        public void AStandaloneActionWindowStillResolvesThroughItsHandler()
        {
            // Wake on LAN's fields must keep working both ways: the action map is new, and the handler map —
            // where they used to live, and where anything reaching this window by a raw PathOverride still
            // looks — must be unchanged.
            var catalog = Parse(WolWindow);

            Assert.AreEqual(1, catalog.GetSoleActionCmd(WolHandler, out string label));
            Assert.AreEqual("wake-on-lan", label);
            Assert.IsTrue(catalog.IsActionOnlyHandler(WolHandler), "the handler backs no record window");

            Assert.IsTrue(catalog.GetHandlerFields(WolHandler).ContainsKey("mac-address"));
            Assert.IsTrue(catalog.GetActionFields(WolHandler, label).ContainsKey("mac-address"));

            var mac = Decoded(Resolver(catalog, "/tool/wol", WolHandler, label)
                .EncodeField("mac-address", "00:11:22:33:44:55"));
            Assert.AreEqual(0x1, mac.Key);
            Assert.AreEqual("001122334455", mac.Value, "a macaddr rides as 6 raw bytes (parsed back as hex)");
        }
    }
}
