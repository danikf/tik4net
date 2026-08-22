// WinboxMessageElementWriteTests.cs — router-free tests for the message-array element shapes.
//
// A message array is a list whose element is a whole submessage, and what that submessage holds varies:
// an `addr` compound, one addressable leaf, a `union` of families, or a `tuple` of parts joined by a
// separator. Only the `addr` shape could be written; the rest were refused, which covered ~300 writable
// fields — /queue/simple's `target` among them, the field that made a queue uncreatable over native.
//
// The .jg fragments are the real declarations from RouterOS 7.24, cut to the fields under test.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxMessageElementWriteTests
    {
        private static readonly int[] QueueHandler = { 20, 9 };
        private static readonly int[] SnmpHandler = { 34 };
        private static readonly int[] SwitchHandler = { 24, 3 };

        // roteros.jg, Simple Queue [20,9]: 'Target' is a union of an IPv4 network, an IPv6 network and an
        // interface reference, behind the usual presence flag.
        private const string QueueWindow =
            "[{name:'Queues',title:'Queues',group:'Queues',c:[" +
            "{name:'Simple Queue',title:'Simple Queues',type:'map',path:[ 20,9 ],c:[" +
              "{name:'Name',type:'string',id:'s2'}," +
              "{name:'Target',type:'multi',id:'M72',max:128,min:1,optid:'b75',c:[{type:'union',single:1,c:[" +
                 "{type:'network',id:'u1',maskid:'u2'}," +
                 "{type:'network6',id:'a3',deflen:0,maskid:'u4'}," +
                 "{type:'enm',id:'u5',values:{type:'dynamic',path:[ 20,0 ]}}]}]}]}" +
            "]}]";

        // roteros.jg, SNMP settings [34]: 'Trap Interfaces' is a list whose element is ONE dropdown.
        private const string SnmpWindow =
            "[{name:'SNMP',title:'SNMP',group:'IP',c:[" +
            "{name:'SNMP Settings',title:'SNMP Settings',type:'item',path:[ 34 ],c:[" +
              "{name:'Trap Interfaces',type:'multi',id:'M18',c:[{type:'enm',id:'u19',values:{type:'defenum'," +
                 "defid:0,defname:'all',values:{type:'dynamic',path:[ 20,0 ]}}}]}]}" +
            "]}]";

        // roteros.jg, Switch Port [24,3]: 'Priority To Queue' is a tuple — a number range and a number,
        // joined by ':'. And 'Custom Fields' is a `not` WRAPPER, which carries an id of its own without
        // being a value.
        private const string SwitchWindow =
            "[{name:'Switch',title:'Switch',group:'Interfaces',c:[" +
            "{name:'Switch Port',title:'Port',type:'map',path:[ 24,3 ],c:[" +
              "{name:'Priority To Queue',type:'multi',id:'M46b',c:[{type:'tuple',sep:':',separate:1,c:[" +
                 "{type:'numberrange',id:'u1',highid:'u2',max:15},{type:'number',id:'u3',max:7}]}]}," +
              "{name:'Per Queue Scheduling',type:'multi',id:'M469',max:8,c:[{type:'tuple',sep:':',separate:1," +
                 "c:[{type:'enm',id:'u1',values:{type:'static',map:['strict priority','wrr group 0']}}," +
                 "{type:'number',id:'u2',max:255,opt:1}]}]}," +
              "{name:'Custom Fields',type:'multi',id:'M71a',c:[{type:'not',id:'b6',c:[" +
                 "{type:'number',id:'u1',max:127}]}]}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(string body, string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, Parse(body), new Dictionary<string, int>());

        // The elements of the one encoded message-array field, as the real M2 parser reads them back.
        private static List<Dictionary<int, object>> Elements(IList<byte[]> encoded, int expectedKey)
        {
            var fields = M2Message.ParseAllFields(M2Message.BuildM2(encoded.ToArray()));
            Assert.IsTrue(fields.ContainsKey(expectedKey), "the list field itself");
            Assert.AreEqual("msg[]", fields[expectedKey].Item1);
            return ((List<Dictionary<int, Tuple<string, object>>>)fields[expectedKey].Item2)
                .Select(e => e.ToDictionary(kv => kv.Key, kv => kv.Value.Item2))
                .ToList();
        }

        private static long Num(object wireValue) => Convert.ToInt64(wireValue);

        // ── one addressable leaf ───────────────────────────────────────────────

        [TestMethod]
        public void ASingleLeafElementRidesAtItsOwnSubKey()
        {
            var ids = new Dictionary<string, int> { ["ether1"] = 2 };
            var encoded = Resolver(SnmpWindow, "/snmp", SnmpHandler)
                .EncodeField("trap-interfaces", "ether1",
                             (handler, name) => ids.TryGetValue(name, out int id) ? id : (int?)null);

            var elements = Elements(encoded, 0x18);
            Assert.AreEqual(1, elements.Count);
            Assert.AreEqual(2L, Num(elements[0][0x19]), "the interface id, at the leaf's key");
        }

        [TestMethod]
        public void EachElementOfAListIsItsOwnSubmessage()
        {
            var ids = new Dictionary<string, int> { ["ether1"] = 2, ["lo"] = 1 };
            var encoded = Resolver(SnmpWindow, "/snmp", SnmpHandler)
                .EncodeField("trap-interfaces", "ether1,lo",
                             (handler, name) => ids.TryGetValue(name, out int id) ? id : (int?)null);

            var elements = Elements(encoded, 0x18);
            Assert.AreEqual(2, elements.Count);
            Assert.AreEqual(2L, Num(elements[0][0x19]));
            Assert.AreEqual(1L, Num(elements[1][0x19]));
        }

        // ── union ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void AUnionElementPicksTheFamilyThatCanHoldTheValue()
        {
            var resolver = Resolver(QueueWindow, "/queue/simple", QueueHandler);

            var v4 = Elements(resolver.EncodeField("target", "192.168.251.0/24"), 0x72);
            Assert.AreEqual(WinboxFieldResolver.PackIpV4("192.168.251.0"), (uint)Num(v4[0][0x1]));
            Assert.AreEqual(0x00FFFFFFL, Num(v4[0][0x2]), "the netmask, packed octet-LSB like the address");

            var v6 = Elements(resolver.EncodeField("target", "2001:db8::/32"), 0x72);
            Assert.IsTrue(v6[0].ContainsKey(0x3), "the IPv6 family has its own key");
            Assert.AreEqual(32L, Num(v6[0][0x4]), "a network6 sibling holds the PREFIX LENGTH, not a mask");
        }

        [TestMethod]
        public void AUnionFallsThroughToItsReferenceFamily()
        {
            var ids = new Dictionary<string, int> { ["lo"] = 1 };
            var encoded = Resolver(QueueWindow, "/queue/simple", QueueHandler)
                .EncodeField("target", "lo",
                             (handler, name) => ids.TryGetValue(name, out int id) ? id : (int?)null);

            var elements = Elements(encoded, 0x72);
            Assert.AreEqual(1L, Num(elements[0][0x5]), "neither address family fits, the dropdown does");
        }

        [TestMethod]
        public void AValueNoFamilyCanHoldIsRefused()
        {
            Assert.ThrowsException<WinboxFieldValueException>(
                () => Resolver(QueueWindow, "/queue/simple", QueueHandler)
                          .EncodeField("target", "not-an-address"));
        }

        [TestMethod]
        public void AListWithAPresenceFlagSendsItAlongside()
        {
            // optid b75: without the flag the router takes the request and ignores the list.
            var fields = M2Message.ParseAllFields(M2Message.BuildM2(
                Resolver(QueueWindow, "/queue/simple", QueueHandler)
                    .EncodeField("target", "192.168.251.0/24").ToArray()));

            Assert.AreEqual(true, fields[0x75].Item2);
        }

        // ── tuple ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void ATupleElementIsSplitByItsSeparator()
        {
            var elements = Elements(
                Resolver(SwitchWindow, "/interface/ethernet/switch/port", SwitchHandler)
                    .EncodeField("priority-to-queue", "3:1"), 0x46B);

            Assert.AreEqual(3L, Num(elements[0][0x1]), "the first part");
            Assert.AreEqual(1L, Num(elements[0][0x3]), "the second part, at its OWN key");
        }

        [TestMethod]
        public void ATuplePartWithAMapTakesTheWord()
        {
            var elements = Elements(
                Resolver(SwitchWindow, "/interface/ethernet/switch/port", SwitchHandler)
                    .EncodeField("per-queue-scheduling", "wrr-group-0:5"), 0x469);

            Assert.AreEqual(1L, Num(elements[0][0x1]), "the map index of the members normalized label");
            Assert.AreEqual(5L, Num(elements[0][0x2]));
        }

        [TestMethod]
        public void ATupleWithFewerPiecesThanPartsLeavesTheRestOut()
        {
            // types.tuple.tostr omits a part that renders empty, so a value can legitimately arrive short.
            var elements = Elements(
                Resolver(SwitchWindow, "/interface/ethernet/switch/port", SwitchHandler)
                    .EncodeField("priority-to-queue", "3"), 0x46B);

            Assert.AreEqual(3L, Num(elements[0][0x1]));
            Assert.IsFalse(elements[0].ContainsKey(0x3));
        }

        [TestMethod]
        public void ATupleWithMorePiecesThanPartsIsRefused()
        {
            Assert.ThrowsException<WinboxFieldValueException>(
                () => Resolver(SwitchWindow, "/interface/ethernet/switch/port", SwitchHandler)
                          .EncodeField("priority-to-queue", "3:1:7"));
        }

        // ── a wrapper is not a value ───────────────────────────────────────────

        [TestMethod]
        public void ANotWrappedElementPutsTheValueInTheLeafAndNotInTheFlag()
        {
            // {type:'not',id:'b6',c:[…]} carries an id without being a value. Taking it for one would write
            // the caller's number into the NEGATION FLAG and drop the value entirely — a request the router
            // accepts and reads as a rule matching everything. The value belongs to the leaf INSIDE, and the
            // flag stays a flag: it is the '!' of one element, and is written either way round.
            var elements = Elements(
                Resolver(SwitchWindow, "/interface/ethernet/switch/port", SwitchHandler)
                    .EncodeField("custom-fields", "5"), 0x71A);

            Assert.AreEqual(5L, Num(elements[0][0x1]), "the value, at the wrapped leaf's own key");
            Assert.AreEqual(false, elements[0][0x6], "and the wrapper's id carries the flag, not the value");
        }
    }
}
