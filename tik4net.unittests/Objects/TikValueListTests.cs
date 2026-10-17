#nullable enable
// TikValueListTests.cs — TikValueList<T>, the immutable list a multi-value field holds, and TikPortRange, a port-list
// item. Equality ignores order because the router reorders some lists (measured 2026-10-04: tcp-flags `ack,!syn,fin`
// reads back `fin,ack,!syn`); ports keep the order and duplicates they were given, so a duplicate still counts.

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueListTests
    {
        private static TikValueList<TikFieldMapperTests.Mode> Modes(params TikValue<TikFieldMapperTests.Mode>[] items)
            => new TikValueList<TikFieldMapperTests.Mode>(items);

        private static TikValueList<TikNegatableValue<TikFieldMapperTests.Mode>> NegatableModes(params TikNegatableValue<TikFieldMapperTests.Mode>[] items)
            => new TikValueList<TikNegatableValue<TikFieldMapperTests.Mode>>(items);

        private static TikNegatableValue<TikFieldMapperTests.Mode> NotMode(TikFieldMapperTests.Mode mode)
            => TikNegatableValue<TikFieldMapperTests.Mode>.Not(mode);

        // ── Construction ─────────────────────────────────────────────────────

        [TestMethod]
        public void PlainValues_ConvertFromTheirType()
        {
            var ports = new TikValueList<TikPortRange>(22, 8291, new TikPortRange(1000, 2000));
            Assert.AreEqual(3, ports.Count);
            Assert.AreEqual(new TikPortRange(1000, 2000), ports[2].Value);
            Assert.AreEqual("22,8291,1000-2000", ports.ToString());
        }

        [TestMethod]
        public void NegatedMembersAndWords_KeepTheirMarks()
        {
            var list = new TikValueList<TikNegatableValue<TikFieldMapperTests.Mode>>(NotMode(TikFieldMapperTests.Mode.Auto),
                (TikNegatableValue<TikFieldMapperTests.Mode>)TikFieldMapperTests.Mode.Yes, TikValue<TikNegatableValue<TikFieldMapperTests.Mode>>.FromWire("!newer"));
            Assert.IsTrue(list[0].Value.IsNegated);
            Assert.IsFalse(list[1].Value.IsNegated);
            Assert.IsTrue(list[2].IsWord);
            Assert.AreEqual("!auto,yes,!newer", list.ToString());
        }

        [TestMethod]
        public void AStringMember_TakesTheRoutersSpelling()
        {
            var topics = new TikValueList<TikNegatableValue<string>>("info", "!debug");
            Assert.IsTrue(topics[1].Value.IsNegated);
            Assert.AreEqual("debug", topics[1].Value.Value);
            Assert.AreEqual(new TikValueList<TikNegatableValue<string>>("info", TikNegatableValue<string>.Not("debug")), topics);
            Assert.AreEqual("info,!debug", topics.ToString());
        }

        [TestMethod]
        public void OnAPlainList_ABangIsPartOfTheItem()
        {
            var names = new TikValueList<string>("!important");
            Assert.AreEqual("!important", names[0].Value);
            Assert.AreEqual("!important", TikValueList<string>.Parse("!important")[0].Value);
        }

        [TestMethod]
        public void ABareBang_IsNotAMember()
            => Assert.ThrowsException<ArgumentException>(() => new TikValueList<TikNegatableValue<string>>("info", "!"));

        [TestMethod]
        public void ANullItem_IsRefused()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new TikValueList<string>("a", null!));
            Assert.ThrowsException<ArgumentNullException>(() => new TikValueList<string>((string[])null!));
        }

        // "a,b" as ONE item printed like the two-item list but never equalled it, nor what a load returns (split on ','),
        // so a merge updated the row on every run (reported from an upgrade, 2026-10-10). Every way in refuses it.
        [TestMethod]
        public void AnItemContainingAComma_IsRefused_OnEveryWayIn()
        {
            Assert.ThrowsException<ArgumentException>(() => new TikValueList<string>("a,b"));
            Assert.ThrowsException<ArgumentException>(() => new TikValueList<TikNegatableValue<string>>(TikNegatableValue<string>.Not("a,b")));
            Assert.ThrowsException<ArgumentException>(() => new TikValueList<TikNegatableValue<string>>("!a,b"));
            Assert.ThrowsException<ArgumentException>(() => Modes(TikValue<TikFieldMapperTests.Mode>.FromWire("newer,older")));
            Assert.ThrowsException<ArgumentException>(() => new TikValueList<string>("a").With("b,c"));

            var parsed = TikValueList<string>.Parse("a,b");
            Assert.AreEqual(2, parsed.Count, "Parse is the way for text holding several items");
        }

        [TestMethod]
        public void Empty_HasNoItems()
        {
            Assert.AreEqual(0, TikValueList<TikPortRange>.Empty.Count);
            Assert.AreEqual("", TikValueList<TikPortRange>.Empty.ToString());
        }

        // ── Parse ────────────────────────────────────────────────────────────

        [TestMethod]
        public void Parse_ReadsTheRoutersSpelling_WithEachItemsOwnNegation()
        {
            var flags = TikValueList<TikNegatableValue<FirewallTcpFlag>>.Parse("syn, !ack");
            Assert.AreEqual(new TikValueList<TikNegatableValue<FirewallTcpFlag>>(FirewallTcpFlag.Syn, TikNegatableValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Ack)), flags);
            Assert.AreEqual("syn,!ack", flags.ToString());

            var ports = TikValueList<TikPortRange>.Parse("22,8291,1000-2000");
            Assert.AreEqual(new TikPortRange(1000, 2000), ports[2].Value);
            Assert.AreSame(TikValueList<TikPortRange>.Empty, TikValueList<TikPortRange>.Parse(""));
        }

        [TestMethod]
        [DataRow("syn,,ack")]
        [DataRow("!,syn")]
        [DataRow("syn,typo")]
        [DataRow("syn,!typo")]
        public void Parse_RefusesWhatIsNotAnItem(string text)
        {
            var thrown = Assert.ThrowsException<FormatException>(() => TikValueList<TikNegatableValue<FirewallTcpFlag>>.Parse(text));
            if (text.StartsWith("!,"))
                StringAssert.Contains(thrown.Message, ".Not(");
            if (text.EndsWith("typo"))
                StringAssert.Contains(thrown.Message, "FromWire");
        }

        [TestMethod]
        public void Parse_OnAPlainEnumList_RefusesABang()
            => Assert.ThrowsException<FormatException>(() => TikValueList<FirewallTcpFlag>.Parse("syn,!ack"));

        [TestMethod]
        public void Parse_RefusesAPortItIsNot()
            => Assert.ThrowsException<FormatException>(() => TikValueList<TikPortRange>.Parse("22,http"));

        // ── Immutability ─────────────────────────────────────────────────────

        [TestMethod]
        public void With_ReturnsANewList_AndLeavesThisOneAsItWas()
        {
            var ports = new TikValueList<TikPortRange>(22, 8291);
            var more = ports.With(443);
            Assert.AreEqual("22,8291", ports.ToString());
            Assert.AreEqual("22,8291,443", more.ToString());
        }

        [TestMethod]
        public void Without_RemovesEqualItems_ANegatedOneOnlyByItsNegation()
        {
            var list = NegatableModes(NotMode(TikFieldMapperTests.Mode.Auto), TikFieldMapperTests.Mode.Auto);
            Assert.AreEqual("!auto", list.Without(TikFieldMapperTests.Mode.Auto).ToString());
            Assert.AreEqual("auto", list.Without(NotMode(TikFieldMapperTests.Mode.Auto)).ToString());
        }

        // ── Equality ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Equality_IgnoresOrder_WithAMatchingHashCode()
        {
            var printed = new TikValueList<TikPortRange>(8291, 22, new TikPortRange(1000, 2000));
            var assigned = new TikValueList<TikPortRange>(new TikPortRange(1000, 2000), 22, 8291);
            Assert.AreEqual(assigned, printed);
            Assert.IsTrue(assigned == printed);
            Assert.AreEqual(assigned.GetHashCode(), printed.GetHashCode());
        }

        [TestMethod]
        public void Equality_CountsDuplicates_AndNegation()
        {
            Assert.AreNotEqual(new TikValueList<TikPortRange>(22, 22, 80), new TikValueList<TikPortRange>(22, 80, 80));
            Assert.AreNotEqual(new TikValueList<TikPortRange>(22), new TikValueList<TikPortRange>(22, 22));
            Assert.AreNotEqual(NegatableModes(TikFieldMapperTests.Mode.Auto), NegatableModes(NotMode(TikFieldMapperTests.Mode.Auto)));
        }

        [TestMethod]
        public void InAField_ACopyIsTheSameValue_AndAnEditIsAChange()
        {
            TikField<TikValueList<TikPortRange>?> loaded = new TikValueList<TikPortRange>(22, 8291);
            TikField<TikValueList<TikPortRange>?> reordered = new TikValueList<TikPortRange>(8291, 22);
            Assert.AreEqual(loaded, reordered, "the router's own order is not a change");

            TikField<TikValueList<TikPortRange>?> edited = loaded.Value!.With(443);
            Assert.AreNotEqual(loaded, edited);
            Assert.AreEqual("22,8291", loaded.Value!.ToString(), "the loaded value is untouched by the edit");
        }

        [TestMethod]
        public void InAField_AWholeListNot_IsTheFieldsNegation()
        {
            TikNegatableField<TikValueList<TikPortRange>?> field = TikValueList<TikPortRange>.Not(22, 8291);
            Assert.IsTrue(field.IsNegated);
            Assert.AreEqual("!22,8291", field.ToString());
            Assert.AreEqual(TikNegatableField<TikValueList<TikPortRange>?>.Not(new TikValueList<TikPortRange>(22, 8291)), field);
        }

        // The type, not the content, decides the whole-list spelling: tcp-flags=!syn would be one negated member.
        [TestMethod]
        public void InAField_AWholeListNot_OfNegatableMembers_IsABareLeadingElement()
        {
            TikNegatableField<TikValueList<TikNegatableValue<FirewallTcpFlag>>?> flags =
                TikValueList<TikNegatableValue<FirewallTcpFlag>>.Not(FirewallTcpFlag.Syn);
            Assert.AreEqual("!,syn", flags.ToString());
        }

        // ── TikPortRange ─────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("22", 22, 22)]
        [DataRow("1000-2000", 1000, 2000)]
        [DataRow("0", 0, 0)]
        [DataRow("65535", 65535, 65535)]
        [DataRow(" 80-80 ", 80, 80)]
        public void PortRange_ParsesThePortsAndRanges(string text, int from, int to)
        {
            var range = TikPortRange.Parse(text);
            Assert.AreEqual(from, range.From);
            Assert.AreEqual(to, range.To);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("http")]
        [DataRow("65536")]
        [DataRow("2000-1000")]
        [DataRow("-5")]
        [DataRow("1-2-3")]
        public void PortRange_RefusesWhatIsNotAPortOrRange(string text)
        {
            Assert.IsFalse(TikPortRange.TryParse(text, out _));
        }

        [TestMethod]
        public void PortRange_SpellsAsTheRouter()
        {
            Assert.AreEqual("22", new TikPortRange(22).ToString());
            Assert.AreEqual("1000-2000", new TikPortRange(1000, 2000).ToString());
            Assert.IsFalse(new TikPortRange(22).IsRange);
            Assert.IsTrue(new TikPortRange(1, 2).IsRange);
        }

        [TestMethod]
        public void PortRange_RefusesOutOfRangeConstruction()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new TikPortRange(70000));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new TikPortRange(10, 5));
        }

        // ── TikNumberRange ───────────────────────────────────────────────────

        [TestMethod]
        [DataRow("10", 10L, 10L)]
        [DataRow("300-399", 300L, 399L)]
        [DataRow("4294967295", 4294967295L, 4294967295L)]
        public void NumberRange_ParsesTheNumbersAndRanges(string text, long from, long to)
        {
            var range = TikNumberRange.Parse(text);
            Assert.AreEqual(from, range.From);
            Assert.AreEqual(to, range.To);
            Assert.AreEqual(text, range.ToString());
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("-1")]
        [DataRow("30-20")]
        [DataRow("1x")]
        [DataRow("10-")]
        public void NumberRange_RefusesWhatIsNotANumberOrRange(string text)
            => Assert.IsFalse(TikNumberRange.TryParse(text, out _));

        [TestMethod]
        public void NumberRange_ConvertsFromAnInt_AndRefusesANegativeOne()
        {
            var codes = new TikValueList<TikNumberRange>(200, new TikNumberRange(300, 399));
            Assert.AreEqual("200,300-399", codes.ToString());
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new TikNumberRange(-1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new TikNumberRange(30, 20));
        }
    }
}
