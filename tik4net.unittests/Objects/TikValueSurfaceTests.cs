#nullable enable
// TikValueSurfaceTests.cs — the caller-facing semantics of TikValue<T>, settled after the design review
// (_notes/5.0/features/entity-value-model-review.md, C3, H1, H4, M-a, M-d).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueSurfaceTests
    {
        public enum Mode { [TikEnum("no")] No, [TikEnum("yes")] Yes, [TikEnum("auto")] Auto }

        private static TikValue<string?> Absent => default;
        private static TikValue<string?> PresentNull => (string?)null;
        private static TikValue<string?> Unparsed => TikValue<string?>.FromWire("word");

        // ── == null means "has no value" (C3) ────────────────────────────────

        [TestMethod]
        public void EqualsNull_IsTrueForAbsentAndPresentNull_FalseForUnparsedAndAValue()
        {
            Assert.IsTrue(Absent == null, "V4: an uncommented row is Absent; Where(r => r.Comment == null) must find it");
            Assert.IsTrue(PresentNull == null);
            Assert.IsFalse(Unparsed == null, "the router printed something");
            Assert.IsFalse((TikValue<string?>)"x" == null);
            Assert.IsTrue(null == Absent);
            Assert.IsTrue(Unparsed != null);
        }

        [TestMethod]
        public void TwoWrappersCompareStrictly()
        {
            Assert.IsFalse(Absent == PresentNull, "state is part of a TikValue's identity");
            Assert.IsTrue((TikValue<int?>)5 == (TikValue<int?>)5);
        }

        // ── .Value throws on Unparsed (H1) ───────────────────────────────────

        [TestMethod]
        public void Value_OnUnparsed_Throws_WithTheRouterWord()
        {
            var ex = Assert.ThrowsException<TikUnparsedValueException>(() => TikValue<Mode?>.FromWire("false").Value);
            Assert.AreEqual("false", ex.RawValue);
            StringAssert.Contains(ex.Message, "Mode");
        }

        [TestMethod]
        public void Value_OnAbsent_IsNull_TryGetValue_SaysWhetherThereIsOne()
        {
            Assert.IsNull(Absent.Value);
            Assert.IsFalse(Absent.TryGetValue(out _));
            Assert.IsFalse(Unparsed.TryGetValue(out _));
            Assert.IsTrue(((TikValue<int?>)7).TryGetValue(out int? seven));
            Assert.AreEqual(7, seven);
            Assert.AreEqual(3, TikValue<int?>.FromWire("x").ValueOrDefault(3));
        }

        [TestMethod]
        public void GetValueOrDefault_IsTheTypesDefaultWithoutAReadableValue_AsNullable()
        {
            // A reference type reads null, a value type its default — the 4.x `bool d = addr.Disabled.GetValueOrDefault();`
            // compiles and behaves as it did.
            Assert.IsNull(Absent.GetValueOrDefault());
            Assert.IsNull(Unparsed.GetValueOrDefault());
            Assert.AreEqual("x", ((TikValue<string?>)"x").GetValueOrDefault());

            bool absent = default(TikValue<bool?>).GetValueOrDefault();
            bool unparsed = TikValue<bool?>.FromWire("maybe").GetValueOrDefault();
            Assert.IsFalse(absent);
            Assert.IsFalse(unparsed);
            Assert.IsTrue(((TikValue<bool?>)true).GetValueOrDefault());
            Assert.AreEqual(0, ((TikValue<int?>)null).GetValueOrDefault());
            Assert.AreEqual(7, ((TikValue<int?>)7).GetValueOrDefault());
        }

        // ── FromWire writes a word the enum lacks (H4) ───────────────────────

        [TestMethod]
        public void FromWire_IsSentAsWritten()
        {
            var connection = new TikFakeConnection()
                .WithScalarResponse(cmd => cmd.First() == "/box/add", "*1");

            connection.Save(new TikValueMapperTests.Box { Name = "b", Mode = TikValue<TikValueMapperTests.Mode?>.FromWire("ec2n155") });

            CollectionAssert.Contains(connection.SentCommands.Single().ToArray(), "=mode=ec2n155");
        }

        // ── Ordering (M-a) ───────────────────────────────────────────────────

        [TestMethod]
        public void OrderBy_Works_AbsentThenUnparsedThenPresent()
        {
            var values = new List<TikValue<int?>> { 30, TikValue<int?>.FromWire("none"), default, 10, (int?)null };

            var sorted = values.OrderBy(v => v).Select(v => v.ToString()).ToList();

            CollectionAssert.AreEqual(new[] { "", "none", "", "10", "30" }, sorted);
            Assert.AreEqual(TikValueState.Absent, values.OrderBy(v => v).First().State);
        }

        [TestMethod]
        public void Comparison_IsLifted_FalseWithoutAValue()
        {
            TikValue<int?> port = 8080;
            Assert.IsTrue(port > 1024);
            Assert.IsTrue(port <= 8080);
            Assert.IsFalse(default(TikValue<int?>) < 5, "Absent has no value to compare");
            Assert.IsFalse(TikValue<int?>.FromWire("auto") > 0);
        }

        // ── Display in the router's spelling (M-d) ───────────────────────────

        [TestMethod]
        public void ToString_SpellsTheValueTheRouterWay()
        {
            Assert.AreEqual("auto", ((TikValue<Mode?>)Mode.Auto).ToString(), "the enum's word, not its member name");
            Assert.AreEqual("true", ((TikValue<bool?>)true).ToString());
            Assert.AreEqual("false", TikValue<Mode?>.FromWire("false").ToString());
            Assert.AreEqual("", Absent.ToString());
            Assert.AreEqual("1.5", ((TikValue<double?>)1.5).ToString(), "invariant, whatever the thread's culture");
        }
    }
}
