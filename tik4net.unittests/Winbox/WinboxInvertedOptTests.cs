// WinboxInvertedOptTests.cs — an opt wrapper with inv:1, whose flag is DOWN while the value is in force.
//
// The bridge's 'Admin. MAC Address' is {opt,id:'b65',inv:1,c:[{macaddr,id:'r66'}]} (7.25.1), and the flag b65 is
// the API's auto-mac: webfig's types.opt.put writes obj[id] = !attrs.inv?1:0 for a present value, and
// types.opt.get reads the value only when Number(!obj[id]) ^ attrs.inv is 0. The API calls the field admin-mac.
// Before the fix the field was unnameable ("unknown parameter admin-mac"), and with only the name it raised the flag,
// i.e. sent auto-mac=yes beside the MAC, which the router took as "keep the automatic MAC".
// Router-free: the catalog, the resolver and the codec.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxInvertedOptTests
    {
        private static readonly int[] BridgeHandler = { 20, 0 };

        // roteros.jg 7.25.1, the bridge window cut to these fields.
        private const string BridgeWindow =
            "[{name:'Bridge',title:'Bridge',type:'map',path:[ 20,0 ],c:[" +
              "{name:'Name',type:'string',id:'s10006'}," +
              "{name:'Admin. MAC Address',type:'opt',id:'b65',inv:1,c:[{type:'macaddr',id:'r66'}]}" +
            "]}]";

        private static WinboxJgCatalog Catalog()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(BridgeWindow), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog)
            => new WinboxFieldResolver("/interface/bridge", BridgeHandler, catalog, new Dictionary<string, int>());

        private static Dictionary<int, object> Decoded(IList<byte[]> encoded)
        {
            var msg = new[] { (byte)'M', (byte)'2' }.Concat(encoded.SelectMany(f => f)).ToArray();
            return M2Message.ParseAllFields(msg).ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        }

        private static Dictionary<string, string> Decode(params (int key, M2Type wire, object value)[] fields)
        {
            var catalog = Catalog();
            var resolver = Resolver(catalog);
            var rec = fields.ToM2Record(f => f.key, f => new M2Value(f.wire, f.value));
            return new WinboxRecordCodec(null, catalog).DecodeRecord(rec, resolver.DecodePlan());
        }

        [TestMethod]
        public void TheInvertedFlagIsReadFromTheCatalog()
        {
            var field = Catalog().GetHandlerFields(BridgeHandler)["admin-mac-address"];
            Assert.AreEqual(0x65, field.OptKey);
            Assert.IsTrue(field.OptInverted);
        }

        [TestMethod]
        public void AnAdminMacIsWrittenWithTheFlagDown()
        {
            var sent = Decoded(Resolver(Catalog()).EncodeField("admin-mac", "AA:BB:CC:DD:EE:FF"));

            Assert.AreEqual("AABBCCDDEEFF", ((string)sent[0x66]).Replace(":", "").ToUpperInvariant(), "the MAC rides raw on r66");
            Assert.AreEqual(false, sent[0x65], "inv:1 — a present value lowers the flag (auto-mac=no)");
        }

        [TestMethod]
        public void AnEmptyAdminMacRaisesTheFlag()
        {
            var sent = Decoded(Resolver(Catalog()).EncodeField("admin-mac", ""));
            Assert.AreEqual(true, sent[0x65], "inv:1 — an absent value raises the flag (auto-mac=yes)");
        }

        [TestMethod]
        public void TheAdminMacIsReadWhileTheFlagIsDownAndHiddenWhileItIsUp()
        {
            var mac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

            var manual = Decode((0x65, M2Type.Bool, (object)false), (0x66, M2Type.Raw, (object)mac));
            var automatic = Decode((0x65, M2Type.Bool, (object)true), (0x66, M2Type.Raw, (object)mac));

            Assert.AreEqual("AA:BB:CC:DD:EE:FF", manual["admin-mac"]);
            Assert.AreEqual("false", manual["auto-mac"]);
            Assert.IsFalse(automatic.ContainsKey("admin-mac"), "the flag up means auto-mac, the stored MAC is not in force");
            Assert.AreEqual("true", automatic["auto-mac"]);
        }
    }
}
