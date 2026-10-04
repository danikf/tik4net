#nullable enable
// TikValueTests.cs — TikValue<T>, one value as the router spells it: a value of T or the router's word, with its own
// '!'. A TikField<T> holds one; these pin the value on its own and what it becomes when assigned to a field.

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
        public void AValue_IsNotAWord_NorNegated()
        {
            TikValue<int?> port = 8080;
            Assert.IsFalse(port.IsWord);
            Assert.IsFalse(port.IsNegated);
            Assert.AreEqual(8080, port.Value);
            Assert.IsNull(port.RawValue);
            Assert.IsTrue(port == 8080);
        }

        [TestMethod]
        public void Not_NegatesAValue_WhichThenNeverEqualsThePlainOne()
        {
            var negated = TikValue<string?>.Not("10.0.0.0/8");
            Assert.IsTrue(negated.IsNegated);
            Assert.AreEqual("10.0.0.0/8", negated.Value);
            Assert.IsFalse(negated == "10.0.0.0/8", "!10.0.0.0/8 matches everything else");
            Assert.AreNotEqual((TikValue<string?>)"10.0.0.0/8", negated);
            Assert.AreEqual((TikValue<string?>)"10.0.0.0/8", negated.WithoutNegation());
        }

        [TestMethod]
        public void Not_RefusesNull_ThereIsNothingToNegate()
            => Assert.ThrowsException<ArgumentNullException>(() => TikValue<string?>.Not(null));

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
            Assert.AreEqual("!10.0.0.0/8", TikValue<string?>.Not("10.0.0.0/8").ToString());
            Assert.AreEqual("ec2n155", TikValue<TikFieldMapperTests.Mode?>.FromWire("ec2n155").ToString());
            Assert.AreEqual("true", ((TikValue<bool?>)true).ToString());
        }

        [TestMethod]
        public void Equality_IsValueOrWord_AndNegation_WithAMatchingHashCode()
        {
            var a = TikValue<string?>.Not("x");
            var b = TikValue<string?>.Not("x");
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(TikValue<string?>.FromWire("x"), (TikValue<string?>)"x", "a word is not the value of the same spelling");
        }

        [TestMethod]
        public void CompareTo_PutsWordsFirst_ThenValues_APlainValueBeforeItsNegation()
        {
            Assert.IsTrue(TikValue<int?>.FromWire("auto").CompareTo(1) < 0);
            Assert.IsTrue(((TikValue<int?>)1).CompareTo(2) < 0);
            Assert.IsTrue(((TikValue<int?>)5).CompareTo(TikValue<int?>.Not(5)) < 0);
        }

        // ── Assigned to a field ──────────────────────────────────────────────

        [TestMethod]
        public void AssignedToAField_ANegatedValue_IsAPresentNegatedField()
        {
            TikField<string?> field = TikValue<string?>.Not("10.0.0.0/8");
            Assert.AreEqual(TikFieldState.Present, field.State);
            Assert.IsTrue(field.IsNegated);
            Assert.AreEqual("10.0.0.0/8", field.Value);
            Assert.AreEqual("!10.0.0.0/8", field.ToString());
        }

        [TestMethod]
        public void AssignedToAField_AWord_IsAnUnparsedField_EqualToTheOneTheMapperReads()
        {
            TikField<TikFieldMapperTests.Mode?> field = TikValue<TikFieldMapperTests.Mode?>.FromWire("ec2n155");
            Assert.AreEqual(TikFieldState.Unparsed, field.State);
            Assert.AreEqual("ec2n155", field.RawValue);
            Assert.AreEqual(TikField<TikFieldMapperTests.Mode?>.FromUnparsed("ec2n155"), field);
        }

        [TestMethod]
        public void AssignedToAField_APlainValue_EqualsTheFieldOfTheSameValue()
        {
            TikField<int?> fromValue = (TikValue<int?>)8080;
            TikField<int?> direct = 8080;
            Assert.AreEqual(direct, fromValue);
            Assert.AreEqual(direct.GetHashCode(), fromValue.GetHashCode());
        }
    }
}
