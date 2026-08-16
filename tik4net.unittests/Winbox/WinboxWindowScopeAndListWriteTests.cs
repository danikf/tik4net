// WinboxWindowScopeAndListWriteTests.cs — router-free tests for the two A4 defects.
//
// (1) Two unrelated windows share one M2 handler and number their fields from scratch, so the merged
//     per-handler map answers the FIRST window's name for a key the second one also owns. Live symptom:
//     /ip/upnp/interfaces read back rows with no 'interface' field at all (the mapper threw "Missing field
//     'interface'"), because [28,0]'s key 1 is the UPnP settings singleton's 'Enabled' bool as well as the
//     interface list's 'Interface' dropdown.
//
// (2) A 'multinumber' list field had a decoder and no encoder — bridge-vlan tagged/untagged and the log
//     rule's topics could be read and not written; the resolver refused them loudly.
//
// The .jg fragments are the real shapes from RouterOS 7.23.2, cut down to what the rules read.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxWindowScopeAndListWriteTests
    {
        // roteros.jg's UPnP menu: a settings singleton and an interface list, BOTH on handler [28,0], both
        // numbering their fields from 1. The settings window is parsed first, exactly as in the real catalog.
        private const string UpnpMenu =
            "[{name:'UPnP',title:'UPnP',group:'IP',c:[" +
            "{name:'UPnP Settings',title:'UPnP Settings',type:'item',path:[ 28,0 ],c:[" +
              "{name:'Enabled',type:'bool',id:'b1'}," +
              "{name:'Allow To Disable External Interface',type:'bool',id:'b2'}," +
              "{name:'Show Dummy Rule',type:'bool',id:'b3'}]}," +
            "{name:'UPnP',title:'Interfaces',type:'map',path:[ 28,0 ],nameval:'Interface',c:[" +
              "{name:'Interface',type:'enm',id:'u1',values:{type:'dynamic',path:[ 20,0 ]}}," +
              "{name:'Type',type:'enm',id:'u2',def:2,values:{type:'static',map:[ '','external','internal' ]}}]}" +
            "]}]";

        // roteros.jg's Bridge VLAN window [16,13]: 'VLAN IDs' is a range list (already encodable), while
        // 'Tagged' is a multinumber whose element type is a dynamic dropdown onto the interface table.
        private const string BridgeVlanWindow =
            "[{name:'Bridge',title:'Bridge',group:'Interfaces',c:[" +
            "{name:'Bridge VLAN',title:'VLANs',type:'map',path:[ 16,13 ],nameval:'VLAN IDs',c:[" +
              "{name:'VLAN IDs',type:'multinumberrange',id:'U1',c:[{type:'numberrange',max:4094,min:1}]}," +
              "{name:'Tagged',type:'multinumber',id:'U3',c:[{type:'enm',values:{type:'dynamic',path:[ 20,0 ]}}]}," +
              "{name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:'static'," +
                 "map:{0:'info',1:'error',2:'script'}}}]}," +
              "{name:'Ports',type:'multinumber',id:'U5'}," +
              "{name:'Protocol',type:'multinumber',id:'U6',max:16,optid:'b99'}]}" +
            "]}]";

        // roteros.jg's Log Rule window [3,2]: 'Topics' is the catalog's only multitristatearray — one API
        // list riding on two keys, the plain members at `id` and the negated ones at `oid`.
        private const string LogRuleWindow =
            "[{name:'Logging',title:'Logging',group:'System',c:[" +
            "{name:'Log Rule',title:'Rules',type:'map',path:[ 3,2 ],nameval:'Topics',c:[" +
              "{name:'Topics',type:'multitristatearray',id:'U1',max:16,oid:'U2'," +
                 "c:[{type:'tristate',values:{type:'dynamic',path:[ 3,3 ]}}]}," +
              "{name:'Prefix',type:'string',id:'s3',opt:1}]}" +
            "]}]";

        private static readonly int[] LogRuleHandler = { 3, 2 };

        private static readonly int[] UpnpHandler = { 28, 0 };
        private static readonly int[] BridgeVlanHandler = { 16, 13 };

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(WinboxJgCatalog catalog, string apiPath, int[] handler,
            string windowKey = null)
            => new WinboxFieldResolver(apiPath, handler, catalog, new Dictionary<string, int>(),
                useGuiNames: false, windowKey: windowKey);

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

        // ── (1) two windows, one handler ───────────────────────────────────────

        [TestMethod]
        public void TheHandlerMapHoldsBothWindowsNamesForTheSameKey()
        {
            // The collision itself, stated as a fact about the catalog. Nothing overwrites anything here —
            // the two names are different dictionary entries — which is exactly why a key→name inversion of
            // this map cannot be made to answer correctly for both windows.
            var fields = Parse(UpnpMenu).GetHandlerFields(UpnpHandler);

            Assert.AreEqual(0x1, fields["enabled"].Key);
            Assert.AreEqual(0x1, fields["interface"].Key);
        }

        [TestMethod]
        public void WithoutTheWindowScope_AnInterfaceRowDecodesAsTheSingletonsField()
        {
            // The defect, reproduced at the layer it happened on: asked by KEY, the handler map answers with
            // whichever window the catalog parsed first. The row then reached the O/R mapper with an
            // 'enabled' field and no 'interface' — "Missing field 'interface'".
            var keyToName = Resolver(Parse(UpnpMenu), "/ip/upnp/interfaces", UpnpHandler).BuildKeyToApiName();

            Assert.AreEqual("enabled", keyToName[0x1]);
        }

        [TestMethod]
        public void WithTheWindowScope_EachWindowIsAnsweredWithItsOwnVocabulary()
        {
            var catalog = Parse(UpnpMenu);

            var list = Resolver(catalog, "/ip/upnp/interfaces", UpnpHandler, "/ip/upnp/upnp").BuildKeyToApiName();
            Assert.AreEqual("interface", list[0x1]);
            Assert.AreEqual("type", list[0x2]);

            var settings = Resolver(catalog, "/ip/upnp", UpnpHandler, "/ip/upnp/upnp-settings").BuildKeyToApiName();
            Assert.AreEqual("enabled", settings[0x1]);
            Assert.AreEqual("show-dummy-rule", settings[0x3]);
        }

        [TestMethod]
        public void TheWindowScopeAlsoDecidesWhichFieldTYPESAKeyIsFormattedBy()
        {
            // Same collision, other consumer: key 1 is a bool on the settings window and a dropdown reference
            // on the list. Formatting an interface id through the bool's metadata would print a plain number.
            var catalog = Parse(UpnpMenu);

            var list = Resolver(catalog, "/ip/upnp/interfaces", UpnpHandler, "/ip/upnp/upnp").BuildKeyToField();
            Assert.AreEqual("enm", list[0x1].UiType);
            CollectionAssert.AreEqual(new[] { 20, 0 }, list[0x1].RefHandler);

            var settings = Resolver(catalog, "/ip/upnp", UpnpHandler, "/ip/upnp/upnp-settings").BuildKeyToField();
            Assert.AreEqual("bool", settings[0x1].UiType);
        }

        [TestMethod]
        public void AWindowsFieldsStayInItsHandlersMapAsWell()
        {
            // Back-compat: the per-handler map is what a path reached by a raw PathOverride (which derives no
            // window) still resolves against, so window scoping must be an overlay, never a move.
            var catalog = Parse(UpnpMenu);

            var handlerFields = catalog.GetHandlerFields(UpnpHandler);
            Assert.IsTrue(handlerFields.ContainsKey("enabled"));
            Assert.IsTrue(handlerFields.ContainsKey("interface"));
            Assert.IsTrue(handlerFields.ContainsKey("show-dummy-rule"));

            var byName = Resolver(catalog, "/ip/upnp/interfaces", UpnpHandler);
            Assert.AreEqual(0x2, byName.ResolveKey("type"));
        }

        // ── (2) multinumber list writes ────────────────────────────────────────

        [TestMethod]
        public void AReferenceListEncodesToAU32ArrayOfResolvedIds()
        {
            var catalog = Parse(BridgeVlanWindow);
            var resolver = Resolver(catalog, "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");
            var ids = new Dictionary<string, int> { ["ether1"] = 3, ["ether2"] = 4 };

            var tagged = Decoded(resolver.EncodeField("tagged", "ether1,ether2",
                (handler, name) => ids.TryGetValue(name, out int id) ? id : (int?)null));

            Assert.AreEqual(0x3, tagged.Key);
            Assert.AreEqual("[3,4]", tagged.Value.ToString(), "one u32[] in the order given, not two fields");
        }

        [TestMethod]
        public void AStaticEnumListEncodesEachElementThroughTheMap()
        {
            var resolver = Resolver(Parse(BridgeVlanWindow), "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");

            var topics = Decoded(resolver.EncodeField("topics", "script,error"));

            Assert.AreEqual(0x4, topics.Key);
            Assert.AreEqual("[2,1]", topics.Value.ToString());
        }

        [TestMethod]
        public void ALiteralNumberListRidesAsItself()
        {
            var resolver = Resolver(Parse(BridgeVlanWindow), "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");

            var ports = Decoded(resolver.EncodeField("ports", "8080, 3128"));

            Assert.AreEqual(0x5, ports.Key);
            Assert.AreEqual("[8080,3128]", ports.Value.ToString());
        }

        [TestMethod]
        public void AnUnresolvableElementFailsRatherThanShorteningTheList()
        {
            // Dropping it would send a shorter list the router accepts without complaint: "ether1,typo" would
            // tag ether1 alone and report success.
            var resolver = Resolver(Parse(BridgeVlanWindow), "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");

            Assert.ThrowsException<WinboxFieldValueException>(() =>
                resolver.EncodeField("tagged", "ether1,typo", (handler, name) => name == "ether1" ? 3 : (int?)null));
        }

        [TestMethod]
        public void AnEmptyListIsStillAnUnsetAndNotAnEmptyArray()
        {
            var resolver = Resolver(Parse(BridgeVlanWindow), "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");

            Assert.AreEqual(0, resolver.EncodeField("tagged", "", (handler, name) => 1).Count);
        }

        [TestMethod]
        public void AListsOwnPresentFlagIsSentAlongsideIt()
        {
            // `optid` on the node is the same thing an enclosing opt wrapper's bool is, and webfig writes it
            // from the list's LENGTH. Without it the router ignores the field: the 21 firewall/bridge match
            // lists that carry it were written and silently dropped.
            var resolver = Resolver(Parse(BridgeVlanWindow), "/interface/bridge/vlan", BridgeVlanHandler,
                                    "/interfaces/bridge/bridge-vlan");

            var encoded = resolver.EncodeField("protocol", "6,17");
            Assert.AreEqual(2, encoded.Count, "the present flag rides with the array");
            Assert.AreEqual(0x99, Decoded(new[] { encoded[0] }).Key);
            Assert.AreEqual(true, Decoded(new[] { encoded[0] }).Value);
            Assert.AreEqual("[6,17]", Decoded(new[] { encoded[1] }).Value.ToString());

            // Clearing it is the same bool the other way round, and is the whole write.
            var cleared = resolver.EncodeField("protocol", "");
            Assert.AreEqual(1, cleared.Count);
            Assert.AreEqual(false, Decoded(cleared).Value);
        }

        // ── multitristatearray: one API list, two keys ─────────────────────────

        [TestMethod]
        public void ATriStateListSplitsTheNegatedMembersOntoTheSecondKey()
        {
            var resolver = Resolver(Parse(LogRuleWindow), "/system/logging", LogRuleHandler,
                                    "/system/logging/log-rule");
            var ids = new Dictionary<string, int> { ["info"] = 1, ["debug"] = 7, ["error"] = 3 };

            var encoded = resolver.EncodeField("topics", "info,!debug,error",
                (handler, name) => ids.TryGetValue(name, out int id) ? id : (int?)null);

            Assert.AreEqual(2, encoded.Count);
            var on = Decoded(new[] { encoded[0] });
            var off = Decoded(new[] { encoded[1] });
            Assert.AreEqual(0x1, on.Key);
            Assert.AreEqual("[1,3]", on.Value.ToString(), "the plain members, in the order given");
            Assert.AreEqual(0x2, off.Key);
            Assert.AreEqual("[7]", off.Value.ToString(), "the '!' member is a different KEY, not a prefix");
        }

        [TestMethod]
        public void ATriStateListSendsBothArraysEvenWhenOneIsEmpty()
        {
            // Sending only the half that has members leaves the other half as the router last stored it, so
            // "topics=info" after "topics=info,!debug" would silently keep the exclusion.
            var resolver = Resolver(Parse(LogRuleWindow), "/system/logging", LogRuleHandler,
                                    "/system/logging/log-rule");

            var encoded = resolver.EncodeField("topics", "info", (handler, name) => 1);

            Assert.AreEqual(2, encoded.Count);
            Assert.AreEqual("[1]", Decoded(new[] { encoded[0] }).Value.ToString());
            Assert.AreEqual("[]", Decoded(new[] { encoded[1] }).Value.ToString());
        }

        [TestMethod]
        public void TheTriStateSecondKeyIsHarvestedFromTheCatalog()
        {
            var topics = Parse(LogRuleWindow).GetHandlerFields(LogRuleHandler)["topics"];

            Assert.AreEqual(0x1, topics.Key);
            Assert.AreEqual(0x2, topics.OffKey);
            CollectionAssert.AreEqual(new[] { 3, 3 }, topics.RefHandler,
                "the element type is a tristate onto the log-topic table");
        }
    }
}
