#nullable enable
// WireTypeMatrixTests.cs — every type the mapper converts, through every door: an empty word, a word the type cannot
// hold, the round trip through the wire form and through JSON. One table of types (TikWireTypes) is what keeps the doors
// agreeing, and this test is what would have caught a type one of them forgot.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class WireTypeMatrixTests
    {
        public enum Mode { [TikEnum("auto")] Auto, [TikEnum("manual")] Manual }

        [TikEntity("/box")]
        public class Box
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string? Id { get; private set; }
            [TikProperty("a-int")] public TikField<int?> AInt { get; set; }
            [TikProperty("a-long")] public TikField<long?> ALong { get; set; }
            [TikProperty("a-byte")] public TikField<byte?> AByte { get; set; }
            [TikProperty("a-uint")] public TikField<uint?> AUInt { get; set; }
            [TikProperty("a-ulong")] public TikField<ulong?> AULong { get; set; }
            [TikProperty("a-date")] public TikField<DateTime?> ADate { get; set; }
            [TikProperty("a-mac")] public TikField<MacAddress?> AMac { get; set; }
            [TikProperty("a-duration")] public TikField<TikDuration?> ADuration { get; set; }
            [TikProperty("a-rate")] public TikField<TikDataRate?> ARate { get; set; }
            [TikProperty("a-pair")] public TikField<TikRatePair?> APair { get; set; }
            [TikProperty("a-hex")] public TikField<TikHexNumber?> AHex { get; set; }
            [TikProperty("a-ports")] public TikField<TikPortRange?> APorts { get; set; }
            [TikProperty("a-numbers")] public TikField<TikNumberRange?> ANumbers { get; set; }
            [TikProperty("a-enum")] public TikField<Mode?> AEnum { get; set; }
            [TikProperty("a-text")] public TikField<string?> AText { get; set; }
            [TikProperty("a-bool")] public TikField<bool?> ABool { get; set; }
            [TikProperty("a-flag", IsPresenceFlag = true)] public TikField<bool?> AFlag { get; set; }
            [TikProperty("a-list")] public TikField<TikValueList<TikPortRange>?> AList { get; set; }
        }

        // Field, a word in the type's canonical spelling, and a word the type cannot hold (null: every word fits).
        private static readonly (string Field, string Canonical, string? Foreign)[] Samples =
        {
            ("a-int", "8080", "x"),
            ("a-long", "9000000000", "x"),
            ("a-byte", "200", "x"),
            ("a-uint", "4000000000", "x"),
            ("a-ulong", "18000000000000000000", "x"),
            ("a-date", "nov/01/2026 12:30:00", "x"),
            ("a-mac", "AA:BB:CC:DD:EE:FF", "x"),
            ("a-duration", "1d2h", "not-a-duration"),
            ("a-rate", "1000000", "not-a-rate"),
            ("a-pair", "1000000/2000000", "a/b/c"),
            ("a-hex", "0x80", "x"),
            ("a-ports", "1000-2000", "x"),
            ("a-numbers", "20-30", "x"),
            ("a-enum", "auto", "x"),
            ("a-bool", "yes", "maybe"),
            ("a-text", "anything", null),
        };

        private static TikEntityPropertyAccessor Accessor(string field)
            => TikEntityMetadataCache.GetMetadata<Box>().Properties.Single(p => p.FieldName == field);

        private static ITikField Read(string field, string word)
        {
            var box = new Box();
            var accessor = Accessor(field);
            accessor.SetEntityValue(box, word);
            return (ITikField)accessor.GetWrapped(box);
        }

        // ── An empty word (D3) ─────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AnEmptyWord_ReadsAbsent_OnEveryTypeThatCannotHoldOne()
        {
            foreach (var sample in Samples.Where(s => s.Field != "a-text" && s.Field != "a-bool"))
                Assert.AreEqual(TikFieldState.Absent, Read(sample.Field, "").State, sample.Field);
        }

        [TestMethod]
        public void AnEmptyWord_IsAValue_OnAString()
        {
            var field = Read("a-text", "");
            Assert.AreEqual(TikFieldState.Present, field.State);
            Assert.AreEqual("", field.BoxedValue);
        }

        [TestMethod]
        public void AnEmptyWord_IsTheEmptyList_OnAList()
        {
            var field = Read("a-list", "");
            Assert.AreEqual(TikFieldState.Present, field.State);
            Assert.AreEqual(0, ((TikValueList<TikPortRange>)field.BoxedValue!).Count);
        }

        [TestMethod]
        public void AnEmptyWord_IsTrue_OnAPresenceFlag()
        {
            var field = Read("a-flag", "");
            Assert.AreEqual(TikFieldState.Present, field.State);
            Assert.AreEqual(true, field.BoxedValue);
        }

        // Being printed is the value on such a field, so "nothing" would be a wrong answer: a bool the router prints
        // empty and the entity does not declare a presence flag stays visible, in the diagnostics too.
        [TestMethod]
        public void AnEmptyWord_OnABoolNotDeclaredAPresenceFlag_StaysUnparsed()
        {
            var field = Read("a-bool", "");
            Assert.AreEqual(TikFieldState.Unparsed, field.State);
            Assert.AreEqual("", field.RawValue);
        }

        [TestMethod]
        public void AnEmptyWord_LoadedAndSavedOnAnotherConnection_SendsNoUnset()
        {
            var box = new Box();
            Accessor("a-duration").SetEntityValue(box, "");
            Assert.IsNull(Accessor("a-duration").GetEntityValue(box));
            Assert.IsTrue(Accessor("a-duration").IsAbsent(box), "Absent is the state that writes nothing");
        }

        // ── A word the type cannot hold ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AWordTheTypeCannotHold_ReadsUnparsed_AndWritesBackAsItCame()
        {
            foreach (var sample in Samples.Where(s => s.Foreign != null))
            {
                var field = Read(sample.Field, sample.Foreign!);
                Assert.AreEqual(TikFieldState.Unparsed, field.State, sample.Field);
                Assert.AreEqual(sample.Foreign, field.RawValue, sample.Field);

                var box = new Box();
                Accessor(sample.Field).SetEntityValue(box, sample.Foreign);
                Assert.AreEqual(sample.Foreign, Accessor(sample.Field).GetEntityValue(box), sample.Field);
            }
        }

        // ── Round trips ────────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void TheWireRoundTrip_IsExact()
        {
            foreach (var sample in Samples)
            {
                var box = new Box();
                Accessor(sample.Field).SetEntityValue(box, sample.Canonical);
                Assert.AreEqual(TikFieldState.Present, ((ITikField)Accessor(sample.Field).GetWrapped(box)).State, sample.Field);
                Assert.AreEqual(sample.Canonical, Accessor(sample.Field).GetEntityValue(box), sample.Field);
            }
        }

#if NET8_0_OR_GREATER
        [TestMethod]
        public void TheJsonRoundTrip_KeepsEveryValue()
        {
            var box = new Box();
            foreach (var sample in Samples)
                Accessor(sample.Field).SetEntityValue(box, sample.Canonical);

            string json = System.Text.Json.JsonSerializer.Serialize(box);
            var back = System.Text.Json.JsonSerializer.Deserialize<Box>(json)!;

            foreach (var sample in Samples)
                Assert.AreEqual(sample.Canonical, Accessor(sample.Field).GetEntityValue(back), sample.Field + " in " + json);
        }

        [TestMethod]
        public void TheJsonRoundTrip_KeepsAWordTheTypeCannotHold()
        {
            var box = new Box();
            foreach (var sample in Samples.Where(s => s.Foreign != null))
                Accessor(sample.Field).SetEntityValue(box, sample.Foreign);

            var back = System.Text.Json.JsonSerializer.Deserialize<Box>(System.Text.Json.JsonSerializer.Serialize(box))!;

            foreach (var sample in Samples.Where(s => s.Foreign != null))
                Assert.AreEqual(sample.Foreign, ((ITikField)Accessor(sample.Field).GetWrapped(back)).RawValue, sample.Field);
        }

        // A hand-written word in the field's place reads as the wire does: a value it holds, or the word itself.
        [TestMethod]
        public void AJsonStringTheTypeCannotHold_ReadsUnparsed()
        {
            var back = System.Text.Json.JsonSerializer.Deserialize<Box>("{\"ADuration\":\"not-a-duration\",\"AHex\":\"x\",\"APorts\":\"x\"}")!;
            Assert.AreEqual("not-a-duration", back.ADuration.RawValue);
            Assert.AreEqual("x", back.AHex.RawValue);
            Assert.AreEqual("x", back.APorts.RawValue);
        }

        [TestMethod]
        public void AListItemTheTypeCannotHold_ReadsFromJsonAsAWord()
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<TikValueList<TikPortRange>>("[\"22\",\"x\"]")!;
            Assert.AreEqual(22, list[0].Value.From);
            Assert.IsTrue(list[1].IsWord);
            Assert.AreEqual("x", list[1].RawValue);
        }
#endif

        // ── A read never fails on a value (X3) ─────────────────────────────────────────────────────────

        [TikEntity("/rule")]
        public class Rule
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string? Id { get; private set; }
            [TikProperty("flags")] public TikNegatableField<TikValueList<TikNegatableValue<string>>?> Flags { get; set; }
            [TikProperty("plain-flags")] public TikField<TikValueList<TikNegatableValue<string>>?> PlainFlags { get; set; }
            [TikProperty("ports")] public TikNegatableField<TikValueList<TikPortRange>?> Ports { get; set; }
            [TikProperty("number")] public TikNegatableField<int?> Number { get; set; }
        }

        private static TikEntityPropertyAccessor RuleAccessor(string field)
            => TikEntityMetadataCache.GetMetadata<Rule>().Properties.Single(p => p.FieldName == field);

        [DataTestMethod]
        [DataRow("flags", "!")]
        [DataRow("flags", "!,")]
        [DataRow("ports", "!")]
        [DataRow("number", "!")]
        public void AWholeValueBangWithNothingBehindIt_ReadsUnparsed_AndWritesBackAsItCame(string field, string word)
        {
            var rule = new Rule();
            RuleAccessor(field).SetEntityValue(rule, word);
            Assert.AreEqual(TikFieldState.Unparsed, ((ITikField)RuleAccessor(field).GetWrapped(rule)).State);
            Assert.AreEqual(word, RuleAccessor(field).GetEntityValue(rule), "the snapshot taken during a load formats it");
        }

        [DataTestMethod]
        [DataRow("!")]
        [DataRow("!,syn")]
        public void ABareBangMember_OnAFieldThatIsNotNegatedAsAWhole_ReadsAsAWord(string word)
        {
            var rule = new Rule();
            RuleAccessor("plain-flags").SetEntityValue(rule, word);
            Assert.AreEqual(word, RuleAccessor("plain-flags").GetEntityValue(rule));
            Assert.IsTrue(rule.PlainFlags.Value!.First().IsWord);
        }

        // ── Strict bool everywhere (X4) ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void AWordThatIsNoBool_IsNoBool_ThroughEveryDoor()
        {
            Assert.ThrowsException<FormatException>(() => TikNegatableField<bool?>.Parse("maybe"));
            Assert.ThrowsException<FormatException>(() => TikValueList<bool>.Parse("maybe"));
        }

        // ── Parse throws, TryParse does not: the same answer (D2) ───────────────────────────────────────

        private static void AssertPair<T>(Func<string, T> parse, TryParser<T> tryParse, string[] readable, string[] refused)
        {
            foreach (string word in readable)
            {
                parse(word);
                Assert.IsTrue(tryParse(word, out _), word);
            }
            foreach (string word in refused)
            {
                Assert.ThrowsException<FormatException>(() => parse(word), word);
                Assert.IsFalse(tryParse(word, out _), word);
            }
        }

        private delegate bool TryParser<T>(string? word, out T value);

        [TestMethod]
        public void ParseAndTryParse_GiveOneAnswer_OnEveryType()
        {
            string[] junk = { "banana", "  ", "1//2" };
            AssertPair<TikDuration>(TikDuration.Parse, TikDuration.TryParse, new[] { "10s", "00:00:10", "none", "NEVER" }, junk);
            AssertPair<TikDataRate>(TikDataRate.Parse, TikDataRate.TryParse, new[] { "1M", "1Gbps", "unlimited", "AUTO" }, junk);
            AssertPair<TikRatePair>(TikRatePair.Parse, TikRatePair.TryParse, new[] { "1M/2M", "1M", "unlimited/unlimited", "auto" }, junk);
            AssertPair<TikHexNumber>(TikHexNumber.Parse, TikHexNumber.TryParse, new[] { "0x80", "128" }, junk);
            AssertPair<TikPortRange>(TikPortRange.Parse, TikPortRange.TryParse, new[] { "22", "1000-2000" }, junk);
            AssertPair<TikNumberRange>(TikNumberRange.Parse, TikNumberRange.TryParse, new[] { "10", "20-30" }, junk);
            AssertPair<MacAddress?>(MacAddress.Parse, MacAddress.TryParse, new[] { "AA:BB:CC:DD:EE:FF" }, junk);
            AssertPair<TikValueList<TikPortRange>?>(TikValueList<TikPortRange>.Parse, TikValueList<TikPortRange>.TryParse, new[] { "22,1000-2000", "" }, new[] { "banana", "22,,23" });
            AssertPair<TikNegatableField<int?>>(TikNegatableField<int?>.Parse, TikNegatableField<int?>.TryParse, new[] { "22", "!22" }, new[] { "banana", "!", "" });
        }

        [TestMethod]
        public void TheNamedStrictReads_RefuseAWord()
        {
            Assert.IsTrue(TikDataRate.TryParseNumber("1Gbps", out long rate));
            Assert.AreEqual(1000000000L, rate);
            Assert.IsFalse(TikDataRate.TryParseNumber("unlimited", out _));

            Assert.AreEqual(TimeSpan.FromSeconds(10), TikDuration.ParseTimeSpan("00:00:10"));
            Assert.IsFalse(TikDuration.TryParseTimeSpan("none", out _));
            Assert.ThrowsException<FormatException>(() => TikDuration.ParseTimeSpan("none"));
        }

        // ── TimeSpan to TikDuration (D4) ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void ATimeSpanParses_WhenTheRouterCanSpellItExactly()
        {
            Assert.AreEqual("1d2h", TikDuration.Parse(new TimeSpan(1, 2, 0, 0)).ToString());
            Assert.AreEqual("0s", TikDuration.Parse(TimeSpan.Zero).ToString(), "zero is 0s, never none");
            Assert.AreEqual("200ms", TikDuration.Parse(TimeSpan.FromMilliseconds(200)).ToString());
            Assert.AreEqual("1ms52us", TikDuration.Parse(TimeSpan.FromTicks(10520)).ToString());

            Assert.IsFalse(TikDuration.TryParse(TimeSpan.FromTicks(1), out _), "the router counts whole microseconds");
            Assert.ThrowsException<FormatException>(() => TikDuration.Parse(TimeSpan.FromTicks(1)));
            Assert.IsFalse(TikDuration.TryParse(TimeSpan.MinValue, out _));
        }

        // ── Overflow (X6) ──────────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ARateTooLargeForItsType_IsNoRate()
        {
            Assert.IsFalse(TikDataRate.TryParseNumber("99999999999T", out _));
            Assert.AreEqual(TikFieldState.Unparsed, Read("a-rate", "99999999999T").State);
        }
    }
}
