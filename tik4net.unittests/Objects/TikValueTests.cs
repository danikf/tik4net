#nullable enable
// TikValueTests.cs — TikValue<T>, one list item as the router spells it: a value of T or the router's word; and
// TikNegatableValue<T>, a member with its own '!'.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueTests
    {

        // ── The value on its own ─────────────────────────────────────────────

        [TestMethod]
        public void AValue_IsNotAWord()
        {
            TikValue<int?> port = 8080;
            Assert.IsFalse(port.IsWord);
            Assert.AreEqual(8080, port.Value);
            Assert.IsNull(port.RawValue);
            Assert.IsTrue(port == 8080);
        }

        [TestMethod]
        public void Not_NegatesAMember_WhichThenNeverEqualsThePlainOne()
        {
            var negated = TikNegatableValue<string>.Not("debug");
            Assert.IsTrue(negated.IsNegated);
            Assert.AreEqual("debug", negated.Value);
            Assert.IsFalse(negated == "debug", "!debug excludes the topic");
            Assert.IsTrue(negated == "!debug", "the router's spelling");
            Assert.AreNotEqual((TikNegatableValue<string>)"debug", negated);
            Assert.AreEqual((TikNegatableValue<string>)"debug", negated.WithoutNegation());
        }

        [TestMethod]
        public void Not_RefusesNull_ThereIsNothingToNegate()
            => Assert.ThrowsException<ArgumentNullException>(() => TikNegatableValue<string>.Not(null!));

        [TestMethod]
        public void AStringMember_ReadsItsLeadingBangAsTheNegation_ButABareBangStaysText()
        {
            TikNegatableValue<string> member = "!debug";
            Assert.IsTrue(member.IsNegated);
            Assert.AreEqual("debug", member.Value);
            TikNegatableValue<string> bang = "!";
            Assert.IsFalse(bang.IsNegated);
        }

        [TestMethod]
        public void FromWire_IsAWord_WhoseValueThrowsRatherThanInventingOne()
        {
            var word = TikValue<TikFieldMapperTests.Mode?>.FromWire("ec2n155");
            Assert.IsTrue(word.IsWord);
            Assert.AreEqual("ec2n155", word.RawValue);
            Assert.IsFalse(word.TryGetValue(out _));
            var thrown = Assert.ThrowsException<TikUnparsedValueException>(() => word.Value);
            Assert.AreEqual("ec2n155", thrown.RawValue);
        }

        [TestMethod]
        public void ToString_IsTheRouterSpelling()
        {
            Assert.AreEqual("auto", ((TikValue<TikFieldMapperTests.Mode?>)TikFieldMapperTests.Mode.Auto).ToString());
            Assert.AreEqual("!ack", TikNegatableValue<tik4net.Objects.Ip.Firewall.FirewallTcpFlag>.Not(tik4net.Objects.Ip.Firewall.FirewallTcpFlag.Ack).ToString());
            Assert.AreEqual("ec2n155", TikValue<TikFieldMapperTests.Mode?>.FromWire("ec2n155").ToString());
            Assert.AreEqual("true", ((TikValue<bool?>)true).ToString());
        }

        [TestMethod]
        public void Equality_IsValueOrWord_AndNegation_WithAMatchingHashCode()
        {
            var a = TikNegatableValue<string>.Not("x");
            var b = TikNegatableValue<string>.Not("x");
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(TikValue<string?>.FromWire("x"), (TikValue<string?>)"x", "a word is not the value of the same spelling");
        }

        [TestMethod]
        public void CompareTo_PutsWordsFirst_ThenValues_APlainValueBeforeItsNegation()
        {
            Assert.IsTrue(TikValue<int?>.FromWire("auto").CompareTo(1) < 0);
            Assert.IsTrue(((TikValue<int?>)1).CompareTo(2) < 0);
            Assert.IsTrue(((TikNegatableValue<int>)5).CompareTo(TikNegatableValue<int>.Not(5)) < 0);
        }

        // ── A field's own word ──────────────────────────────────────────────

        [TestMethod]
        public void AFieldsWord_IsAnUnparsedField_EqualToTheOneTheMapperReads()
        {
            TikField<TikFieldMapperTests.Mode?> field = TikField<TikFieldMapperTests.Mode?>.FromWire("ec2n155");
            Assert.AreEqual(TikFieldState.Unparsed, field.State);
            Assert.AreEqual("ec2n155", field.RawValue);
            Assert.AreEqual(TikField<TikFieldMapperTests.Mode?>.FromUnparsed("ec2n155"), field);
        }
    }
}
