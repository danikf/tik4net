#nullable enable
// TikFieldNegationTests.cs — a negated matcher (src-address=!10.0.0.0/8) on a TikField<T> property marked Negatable:
// what a load reads, what a save sends, and what refuses. The wire form is the one measured on 6.49.13 and 7.24.4
// (API, REST and the CLI print the same '!'; WinBox native carries it as the field's `not` flag).

using System;
using System.Collections.Generic;
using System.Linq;
#if NET8_0_OR_GREATER
using System.Text.Json;
#endif
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikFieldNegationTests
    {
        [TikEntity("/rule")]
        public class Rule
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("src-address", Negatable = true)]
            public TikField<string?> SrcAddress { get; set; }

            [TikProperty("port", Negatable = true)]
            public TikField<int?> Port { get; set; }

            [TikProperty("state", Negatable = true)]
            public TikField<TikFieldMapperTests.States?> State { get; set; }

            [TikProperty("comment")]
            public TikField<string?> Comment { get; set; }
        }

        private static TikFakeConnection Router(params (string, string)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1" };
            foreach (var (k, v) in fields) row[k] = v;
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/rule/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == "/rule/set" || cmd.First() == "/rule/unset")
                .WithScalarResponse(cmd => cmd.First() == "/rule/add", "*9");
        }

        private static string[] Sent(TikFakeConnection c, string verb)
            => c.SentCommands.Where(x => x.First() == "/rule/" + verb).SelectMany(x => x.Skip(1)).ToArray();

        // ── Read ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ALeadingBang_ReadsAsTheNegation_NotAsPartOfTheValue()
        {
            var rule = Router(("src-address", "!10.0.0.0/8"), ("port", "!80")).LoadAll<Rule>().Single();

            Assert.IsTrue(rule.SrcAddress.IsNegated);
            Assert.AreEqual("10.0.0.0/8", rule.SrcAddress.Value);
            Assert.AreEqual("!10.0.0.0/8", rule.SrcAddress.ToString());
            Assert.IsTrue(rule.Port.IsNegated);
            Assert.AreEqual(80, rule.Port.Value, "the rest parses as T");
        }

        [TestMethod]
        public void ANegatedValue_IsNeverEqualToThePlainOne()
        {
            var rule = Router(("src-address", "!10.0.0.0/8"), ("port", "!80")).LoadAll<Rule>().Single();

            Assert.IsFalse(rule.SrcAddress == "10.0.0.0/8", "!10.0.0.0/8 matches everything but 10.0.0.0/8");
            Assert.IsTrue(rule.SrcAddress != "10.0.0.0/8");
            Assert.IsTrue(rule.SrcAddress == TikField<string?>.Not("10.0.0.0/8"));
            Assert.IsFalse(rule.Port > 10, "a negated number is not ordered");
            Assert.IsFalse(rule.SrcAddress == null, "a negated value has a value");
        }

        [TestMethod]
        public void AWholeFlagsSet_IsNegatedAsOne()
        {
            var rule = Router(("state", "!new,established")).LoadAll<Rule>().Single();

            Assert.IsTrue(rule.State.IsNegated);
            Assert.AreEqual(TikFieldMapperTests.States.New | TikFieldMapperTests.States.Established, rule.State.Value);
            Assert.IsTrue(rule.State.With(TikFieldMapperTests.States.New).IsNegated, "With keeps the negation");
        }

        [TestMethod]
        public void OnAFieldNotMarkedNegatable_TheBangIsText()
        {
            var rule = Router(("comment", "!important")).LoadAll<Rule>().Single();

            Assert.IsFalse(rule.Comment.IsNegated);
            Assert.AreEqual("!important", rule.Comment.Value);
        }

        [TestMethod]
        public void ANegatedValueThatDoesNotParse_IsUnparsed_WithTheWholeWord()
        {
            var connection = Router(("port", "!http"));
            var rule = connection.LoadAll<Rule>().Single();

            Assert.AreEqual(TikFieldState.Unparsed, rule.Port.State);
            Assert.AreEqual("!http", rule.Port.RawValue);
            connection.Save(rule);
            Assert.AreEqual(0, Sent(connection, "set").Length, "an untouched unparsed value is not written");
        }

        // ── Write ────────────────────────────────────────────────────────────

        [TestMethod]
        public void AnUnchangedNegation_SendsNothing()
        {
            var connection = Router(("src-address", "!10.0.0.0/8"));
            var rule = connection.LoadAll<Rule>().Single();

            connection.Save(rule);

            Assert.AreEqual(0, connection.SentCommands.Count(c => c.First() == "/rule/set"));
        }

        [TestMethod]
        public void DroppingTheNegation_IsAChange_AndIsSent()
        {
            var connection = Router(("src-address", "!10.0.0.0/8"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.SrcAddress = rule.SrcAddress.WithoutNegation();
            connection.Save(rule);

            CollectionAssert.AreEquivalent(new[] { "=src-address=10.0.0.0/8", "=.id=*1" }, Sent(connection, "set"));
        }

        [TestMethod]
        public void NegatingAValue_SendsTheBang()
        {
            var connection = Router(("src-address", "10.0.0.0/8"), ("state", "new"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.SrcAddress = TikField<string?>.Not("10.0.0.0/8");
            rule.State = TikField<TikFieldMapperTests.States?>.Not(TikFieldMapperTests.States.New | TikFieldMapperTests.States.Established);
            connection.Save(rule);

            CollectionAssert.AreEquivalent(new[] { "=src-address=!10.0.0.0/8", "=state=!new,established", "=.id=*1" },
                Sent(connection, "set"));
        }

        [TestMethod]
        public void Add_SendsANegatedValueWithItsBang()
        {
            var connection = Router();
            connection.Save(new Rule { Port = TikField<int?>.Not(22) });

            CollectionAssert.AreEquivalent(new[] { "=port=!22" }, Sent(connection, "add"));
        }

        [TestMethod]
        public void ANegationOnAFieldNotMarkedNegatable_IsRefused_NotDropped()
        {
            var connection = Router(("comment", "a"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.Comment = TikField<string?>.Not("a");

            var ex = Assert.ThrowsException<InvalidOperationException>(() => connection.Save(rule));
            StringAssert.Contains(ex.Message, "Negatable");
            Assert.AreEqual(0, Sent(connection, "set").Length);
        }

        // ── Surface ──────────────────────────────────────────────────────────

        [TestMethod]
        public void OnlyAValueCanBeNegated()
        {
            Assert.ThrowsException<ArgumentNullException>(() => TikField<string?>.Not(null));
            Assert.IsFalse(TikField<string?>.Absent.IsNegated);
            Assert.IsFalse(TikField<string?>.FromWire("!x").IsNegated, "an unparsed word keeps its '!' as it was printed");
        }

        [TestMethod]
        public void ANegatedValueSortsAfterThePlainOne_AndHashesApart()
        {
            TikField<int?> plain = 80, negated = TikField<int?>.Not(80);

            Assert.IsTrue(plain.CompareTo(negated) < 0);
            Assert.AreNotEqual(plain, negated);
            Assert.AreNotEqual(plain.GetHashCode(), negated.GetHashCode());
            Assert.AreEqual(plain, negated.WithoutNegation());
        }

        [TikEntity("/bad")]
        public class PlainNegatable
        {
            [TikProperty("src-address", Negatable = true)]
            public string? SrcAddress { get; set; }
        }

        [TestMethod]
        public void NegatableOnAPlainProperty_IsRefused()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikEntityMetadataCache.GetMetadata<PlainNegatable>());
            StringAssert.Contains(ex.Message, "Negatable");
        }

#if NET8_0_OR_GREATER
        public class Dto
        {
            public TikField<string?> SrcAddress { get; set; }
            public TikField<TikFieldMapperTests.States?> State { get; set; }
        }

        [TestMethod]
        public void ANegationSurvivesJson()
        {
            var dto = new Dto
            {
                SrcAddress = TikField<string?>.Not("10.0.0.0/8"),
                State = TikField<TikFieldMapperTests.States?>.Not(TikFieldMapperTests.States.New),
            };

            string json = JsonSerializer.Serialize(dto);
            var back = JsonSerializer.Deserialize<Dto>(json)!;

            StringAssert.Contains(json, "\"SrcAddress\":{\"$not\":\"10.0.0.0/8\"}");
            StringAssert.Contains(json, "\"State\":{\"$not\":\"new\"}");
            Assert.AreEqual(dto.SrcAddress, back.SrcAddress);
            Assert.AreEqual(dto.State, back.State);
        }
#endif
    }
}
