#nullable enable
// TikFieldNegationTests.cs — a negated matcher (src-address=!10.0.0.0/8) on a TikNegatableField<T> property: what a load
// reads, what an assignment holds, what a save sends. The wire form is the one measured on 6.49.13 and 7.24.4 (API, REST
// and the CLI print the same '!'; WinBox native carries it as the field's `not` flag). A TikField<T> takes no '!' at all.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            [TikProperty("src-address")]
            public TikNegatableField<string?> SrcAddress { get; set; }

            [TikProperty("port")]
            public TikNegatableField<int?> Port { get; set; }

            [TikProperty("state")]
            public TikNegatableField<TikValueList<TikFieldMapperTests.Conn>?> State { get; set; }

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
            Assert.IsTrue(rule.SrcAddress == TikNegatableField<string?>.Not("10.0.0.0/8"));
            Assert.IsFalse(rule.Port > 10, "a negated number is not ordered");
            Assert.IsFalse(rule.SrcAddress == null, "a negated value has a value");
        }

        [TestMethod]
        public void AWholeList_IsNegatedAsOne()
        {
            var rule = Router(("state", "!new,established")).LoadAll<Rule>().Single();

            Assert.IsTrue(rule.State.IsNegated);
            Assert.AreEqual(new TikValueList<TikFieldMapperTests.Conn>(TikFieldMapperTests.Conn.New, TikFieldMapperTests.Conn.Established),
                rule.State.Value);
            Assert.IsFalse(rule.State.Value!.HasNegatedItems, "the '!' is the list's, not the first member's");
        }

        [TestMethod]
        public void OnATikField_TheBangIsText()
        {
            var rule = Router(("comment", "!important")).LoadAll<Rule>().Single();

            Assert.AreEqual("!important", rule.Comment.Value);
            Assert.IsTrue(rule.Comment == "!important");
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

        // ── Assignment: the router's own spelling ────────────────────────────

        [TestMethod]
        public void AStringWithALeadingBang_IsTheNegation()
        {
            var rule = new Rule { SrcAddress = "!192.168.4.1/32" };

            Assert.IsTrue(rule.SrcAddress.IsNegated, "the router has no literal '!' on a negatable field");
            Assert.AreEqual("192.168.4.1/32", rule.SrcAddress.Value);
            Assert.AreEqual(TikNegatableField<string?>.Not("192.168.4.1/32"), rule.SrcAddress);
            Assert.IsTrue(rule.SrcAddress == TikNegatableField<string?>.Not("192.168.4.1/32"));
            Assert.IsTrue(TikNegatableField<string?>.Not("192.168.4.1/32") == rule.SrcAddress);
            Assert.AreEqual("!192.168.4.1/32", rule.SrcAddress.ToString());
        }

        [TestMethod]
        public void AStringWithALeadingBang_ComparesAsTheMatcherItSpells()
        {
            TikNegatableField<string?> negated = TikNegatableField<string?>.Not("10.0.0.0/8"), plain = "10.0.0.0/8";

            Assert.IsTrue(negated == "!10.0.0.0/8");
            Assert.IsTrue("!10.0.0.0/8" == negated);
            Assert.IsFalse(negated == "10.0.0.0/8");
            Assert.IsFalse(plain == "!10.0.0.0/8", "the opposite matcher");
            Assert.IsTrue(plain == "10.0.0.0/8");
        }

        [TestMethod]
        public void ABareBang_StaysText_AsALoadLeavesIt()
        {
            TikNegatableField<string?> bang = "!";

            Assert.IsFalse(bang.IsNegated);
            Assert.AreEqual("!", bang.Value);
        }

        [TestMethod]
        public void OnATikField_AnAssignedBangIsText()
        {
            var rule = new Rule { Comment = "!important" };

            Assert.AreEqual("!important", rule.Comment.Value);
            Assert.IsTrue(rule.Comment == "!important");
        }

        [TestMethod]
        public void AStringWithALeadingBang_SendsTheSameWireAsNot()
        {
            var spelled = Router();
            spelled.Save(new Rule { SrcAddress = "!10.0.0.0/8", Comment = "!x" });
            var built = Router();
            built.Save(new Rule { SrcAddress = TikNegatableField<string?>.Not("10.0.0.0/8"), Comment = "!x" });

            CollectionAssert.AreEquivalent(new[] { "=src-address=!10.0.0.0/8", "=comment=!x" }, Sent(spelled, "add"));
            CollectionAssert.AreEquivalent(Sent(built, "add"), Sent(spelled, "add"));
        }

        [TestMethod]
        public void ASpelledNegation_OfTheLoadedOne_IsNoChange()
        {
            var connection = Router(("src-address", "!10.0.0.0/8"));
            var rule = connection.LoadAll<Rule>().Single();

            rule.SrcAddress = "!10.0.0.0/8";
            connection.Save(rule);

            Assert.AreEqual(0, Sent(connection, "set").Length);
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

            rule.SrcAddress = TikNegatableField<string?>.Not("10.0.0.0/8");
            rule.State = TikNegatableField<TikValueList<TikFieldMapperTests.Conn>?>.Not(
                new TikValueList<TikFieldMapperTests.Conn>(TikFieldMapperTests.Conn.New, TikFieldMapperTests.Conn.Established));
            connection.Save(rule);

            CollectionAssert.AreEquivalent(new[] { "=src-address=!10.0.0.0/8", "=state=!new,established", "=.id=*1" },
                Sent(connection, "set"));
        }

        [TestMethod]
        public void Add_SendsANegatedValueWithItsBang()
        {
            var connection = Router();
            connection.Save(new Rule { Port = TikNegatableField<int?>.Not(22) });

            CollectionAssert.AreEquivalent(new[] { "=port=!22" }, Sent(connection, "add"));
        }

        // ── Surface ──────────────────────────────────────────────────────────

        [TestMethod]
        public void OnlyAValueCanBeNegated()
        {
            Assert.ThrowsException<ArgumentNullException>(() => TikNegatableField<string?>.Not(null));
            Assert.IsFalse(TikNegatableField<string?>.Absent.IsNegated);
            Assert.IsFalse(TikNegatableField<string?>.FromWire("!x").IsNegated, "an unparsed word keeps its '!' as it was printed");
            Assert.AreEqual(TikFieldState.Unparsed, TikNegatableField<string?>.FromWire("!x").State);
        }

        [TestMethod]
        public void ATikField_HasNoWayToHoldANegation()
        {
            // rule.Comment = TikNegatableField<string?>.Not("a") and rule.Comment = TikValue<string?>.Not("a") do not compile.
            var field = typeof(TikField<string?>);
            Assert.IsNull(field.GetProperty("IsNegated"));
            Assert.IsNull(field.GetMethod("WithoutNegation"));
            Assert.IsNull(field.GetMethod("Not", BindingFlags.Public | BindingFlags.Static));
            Assert.IsFalse(field.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(m => m.Name == "op_Implicit" && m.GetParameters()[0].ParameterType != typeof(string)),
                "the only conversion is from T: no TikValue<T>, no TikNegatableField<T>");
        }

        [TestMethod]
        public void Parse_ReadsTheRouterSpelling()
        {
            Assert.AreEqual(TikNegatableField<int?>.Not(22), TikNegatableField<int?>.Parse("!22"));
            Assert.AreEqual((TikNegatableField<int?>)22, TikNegatableField<int?>.Parse("22"));
            Assert.AreEqual(TikNegatableField<string?>.Not("ether1"), TikNegatableField<string?>.Parse("!ether1"));
            Assert.ThrowsException<FormatException>(() => TikNegatableField<int?>.Parse("!http"));
            Assert.ThrowsException<FormatException>(() => TikNegatableField<int?>.Parse("!"));
            Assert.ThrowsException<NotSupportedException>(() => TikNegatableField<TikValueList<TikPortRange>?>.Parse("!22,8291"));
        }

        [TestMethod]
        public void ANegatedValueSortsAfterThePlainOne_AndHashesApart()
        {
            TikNegatableField<int?> plain = 80, negated = TikNegatableField<int?>.Not(80);

            Assert.IsTrue(plain.CompareTo(negated) < 0);
            Assert.AreNotEqual(plain, negated);
            Assert.AreNotEqual(plain.GetHashCode(), negated.GetHashCode());
            Assert.AreEqual(plain, negated.WithoutNegation());
        }

        [TestMethod]
        public void TheMapper_TakesNegatabilityFromTheType()
        {
            var properties = TikEntityMetadataCache.GetMetadata<Rule>().Properties.ToDictionary(p => p.PropertyName);

            Assert.IsTrue(properties[nameof(Rule.SrcAddress)].IsNegatable);
            Assert.IsTrue(properties[nameof(Rule.State)].IsNegatable);
            Assert.IsFalse(properties[nameof(Rule.Comment)].IsNegatable);
            Assert.IsTrue(properties[nameof(Rule.Comment)].IsWrapped);
        }

#if NET8_0_OR_GREATER
        public class Dto
        {
            public TikNegatableField<string?> SrcAddress { get; set; }
            public TikNegatableField<TikValueList<TikFieldMapperTests.Conn>?> State { get; set; }
            public TikField<string?> Comment { get; set; }
        }

        [TestMethod]
        public void ANegationSurvivesJson()
        {
            var dto = new Dto
            {
                SrcAddress = "!10.0.0.0/8",
                State = TikNegatableField<TikValueList<TikFieldMapperTests.Conn>?>.Not(new TikValueList<TikFieldMapperTests.Conn>(TikFieldMapperTests.Conn.New)),
                Comment = "!x",
            };

            string json = JsonSerializer.Serialize(dto);
            var back = JsonSerializer.Deserialize<Dto>(json)!;

            StringAssert.Contains(json, "\"SrcAddress\":{\"$not\":\"10.0.0.0/8\"}");
            StringAssert.Contains(json, "\"State\":{\"$not\":[\"new\"]}");
            StringAssert.Contains(json, "\"Comment\":\"!x\"");
            Assert.AreEqual(dto.SrcAddress, back.SrcAddress);
            Assert.AreEqual(dto.State, back.State);
            Assert.AreEqual(dto.Comment, back.Comment);
        }

        [TestMethod]
        public void HandWrittenJson_ReadsALeadingBangAsTheNegation_OnlyOnANegatableField()
        {
            var back = JsonSerializer.Deserialize<Dto>("{\"SrcAddress\":\"!10.0.0.0/8\",\"Comment\":\"!x\"}")!;

            Assert.AreEqual(TikNegatableField<string?>.Not("10.0.0.0/8"), back.SrcAddress);
            Assert.AreEqual("!x", back.Comment.Value);
        }

        [TestMethod]
        public void ANegationInJson_IsRefusedByATikField()
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<Dto>("{\"Comment\":{\"$not\":\"x\"}}"));
        }
#endif
    }
}
