// WinboxEnslavedInterfaceTypeTests.cs — router-free tests for /interface `type` on an enslaved interface.
//
// The API's `type` is the name at M2 key 0x1001E ("ether", "bridge"). On an interface enslaved to a bridge or a
// bond, WinBox nests the row under its master and 0x1001E carries the MASTER's numeric id instead — measured on
// 7.24.4: ether2 in a bridge carried 0x1001E=1840 (the bridge's id) next to its own type id 0x10001=1, the same
// type id ether1 carries beside 0x1001E="ether". The API says type=ether for both. The name is learned from the
// router's own rows (WinboxJgField.KindIdKey), never guessed.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxEnslavedInterfaceTypeTests
    {
        private const string InterfaceWindow =
            "[{name:'Interfaces',c:[{name:'Interface',title:'Interface',type:'map',path:[ 20,0 ],generic:'iface',typeon:'type',c:[" +
            "{name:'type',type:'number',id:'u10001',nonpublic:1,ro:1}," +
            "{name:'Name',type:'string',id:'s10006'}" +
            "]}]}]";

        private static readonly int[] Interface = { 20, 0 };

        private static Dictionary<int, Tuple<string, object>> Row(uint id, string name, uint typeId, string wire, object typeKey)
            => new Dictionary<int, Tuple<string, object>>
            {
                [0xFE0001] = Tuple.Create("u32", (object)id),
                [0x10006] = Tuple.Create("str", (object)name),
                [0x10001] = Tuple.Create("u32", (object)typeId),
                [0x1001E] = Tuple.Create(wire, typeKey),
            };

        private static (WinboxRecordCodec codec, IReadOnlyDictionary<int, string> names, IReadOnlyDictionary<int, WinboxJgField> fields) Setup()
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(InterfaceWindow), "the trimmed window must parse");
            var resolver = new WinboxFieldResolver("/interface", Interface, catalog, new Dictionary<string, int>());
            return (new WinboxRecordCodec(null, catalog), resolver.BuildKeyToApiName(), resolver.BuildKeyToField());
        }

        [TestMethod]
        public void AnEnslavedInterfaceIsNamedByItsTypeIdEvenWhenItsPeerComesLater()
        {
            var (codec, names, fields) = Setup();
            var slave = Row(3, "ether2", 1, "u32", 1840u);
            var peer = Row(2, "ether1", 1, "str", "ether");

            codec.LearnKindWords(new[] { slave, peer }, fields);   // the list read learns the whole batch first

            Assert.AreEqual("ether", codec.DecodeRecord(slave, names, fields)["type"]);
            Assert.AreEqual("ether", codec.DecodeRecord(peer, names, fields)["type"]);
        }

        [TestMethod]
        public void ATypeNoRowHasNamedIsLeftOut()
        {
            // A lone EoIP tunnel enslaved to a bond: nothing in the answer names type id 22, and the master's id
            // is not a type.
            var (codec, names, fields) = Setup();
            var slave = Row(9, "t4n-eoip", 22, "u32", 1840u);
            var other = Row(2, "ether1", 1, "str", "ether");

            codec.LearnKindWords(new[] { slave, other }, fields);

            Assert.IsFalse(codec.DecodeRecord(slave, names, fields).ContainsKey("type"));
        }

        [TestMethod]
        public void ANameLearnedOnOneReadNamesASlaveOnTheNext()
        {
            var (codec, names, fields) = Setup();
            codec.DecodeRecord(Row(2, "ether1", 1, "str", "ether"), names, fields);

            Assert.AreEqual("ether", codec.DecodeRecord(Row(3, "ether2", 1, "u32", 1840u), names, fields)["type"]);
        }
    }
}
