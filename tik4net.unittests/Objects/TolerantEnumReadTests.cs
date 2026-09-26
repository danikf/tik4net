// TolerantEnumReadTests.cs — on a plain (non-TikValue) enum property, a word the enum does not know reads as its
// [TikEnumUnknown] member, and the word is kept; writing stays strict.
//
// It used to fail the read of the WHOLE menu (TikEnumMetadata.Parse → FormatException): one firewall rule with
// an action a newer RouterOS added, and LoadAll<FirewallFilter> read nothing at all. The built-in entities are
// TikValue<T?> since 5.0 — an unknown word reads Unparsed there (TikValueSurfaceTests) — so these tests keep the
// mechanism covered for a caller's own entity with plain properties.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TolerantEnumReadTests
    {
        public enum RuleAction
        {
            [TikEnum("accept")] Accept,
            [TikEnum("drop")] Drop,
            [TikEnumUnknown] Unknown = -1,
        }

        [Flags]
        public enum RuleState
        {
            [TikEnum("established")] Established = 1,
            [TikEnum("related")] Related = 2,
            [TikEnumUnknown] Unknown = 1 << 30,
        }

        [TikEntity("/ip/firewall/filter")]
        public class PlainRule
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("chain")]
            public string? Chain { get; set; }

            [TikProperty("action")]
            public RuleAction Action { get; set; }

            [TikProperty("connection-state")]
            public RuleState ConnectionState { get; set; }

            [TikProperty("comment")]
            public string? Comment { get; set; }
        }

        private static TikFakeConnection Router(params Dictionary<string, string>[] rows)
            => new TikFakeConnection()
                .WithResponse(
                    cmd => cmd.FirstOrDefault() == "/ip/firewall/filter/print",
                    rows.Select(r => (ITikSentence)new TikFakeReSentence(r))
                        .Concat(new ITikSentence[] { new TikFakeDoneSentence() }).ToList())
                .WithNonQuery(cmd => cmd.First() == "/ip/firewall/filter/set" || cmd.First() == "/ip/firewall/filter/unset");

        private static Dictionary<string, string> Rule(string id, string action, string? connectionState = null)
        {
            var row = new Dictionary<string, string> { [".id"] = id, ["chain"] = "forward", ["action"] = action };
            if (connectionState != null) row["connection-state"] = connectionState;
            return row;
        }

        private static string[] TheSet(TikFakeConnection connection)
            => connection.SentCommands.Single(c => c.First() == "/ip/firewall/filter/set");

        [TestMethod]
        public void AnUnknownWord_ReadsAsUnknown_AndTheOtherRowsStillRead()
        {
            var rules = Router(Rule("*1", "accept"), Rule("*2", "send-to-the-moon")).LoadAll<PlainRule>().ToList();

            Assert.AreEqual(2, rules.Count, "one unknown word must not cost the whole menu");
            Assert.AreEqual(RuleAction.Accept, rules[0].Action);
            Assert.AreEqual(RuleAction.Unknown, rules[1].Action);
        }

        [TestMethod]
        public void TheRoutersWordIsKept()
        {
            var rule = Router(Rule("*2", "send-to-the-moon")).LoadAll<PlainRule>().Single();

            Assert.AreEqual("send-to-the-moon", rule.GetUnknownWord(nameof(PlainRule.Action)));
            Assert.IsNull(rule.GetUnknownWord(nameof(PlainRule.Chain)), "a property that read a known value has none");
        }

        [TestMethod]
        public void SavingAnotherChange_DoesNotSendTheUnknownField()
        {
            var connection = Router(Rule("*2", "send-to-the-moon"));
            var rule = connection.LoadAll<PlainRule>().Single();

            rule.Comment = "changed";
            connection.Save(rule);

            Assert.IsFalse(TheSet(connection).Any(w => w.StartsWith("=action=")), string.Join(" ", TheSet(connection)));
        }

        [TestMethod]
        public void ACloneSavedWithoutASnapshot_WritesTheRoutersWordBack()
        {
            // No snapshot, so every field goes — the Unknown one as the word the router printed, never invented.
            var connection = Router(Rule("*2", "send-to-the-moon"));
            var clone = connection.LoadAll<PlainRule>().Single().CloneEntity();

            connection.Save(clone);

            CollectionAssert.Contains(TheSet(connection), "=action=send-to-the-moon");
        }

        [TestMethod]
        public void AnUnknownTheCallerAssigned_CannotBeWritten()
        {
            var connection = Router(Rule("*2", "accept"));
            var rule = connection.LoadAll<PlainRule>().Single();

            rule.Action = RuleAction.Unknown;

            var ex = Assert.ThrowsException<FormatException>(() => connection.Save(rule));
            StringAssert.Contains(ex.Message, "action");
        }

        [TestMethod]
        public void AFlagsValue_KeepsItsKnownParts_AndTheUnknownOnes()
        {
            var connection = Router(Rule("*3", "accept", "established,brand-new-state"));
            var rule = connection.LoadAll<PlainRule>().Single();

            Assert.IsTrue(rule.ConnectionState.HasFlag(RuleState.Established));
            Assert.IsTrue(rule.ConnectionState.HasFlag(RuleState.Unknown));
            Assert.AreEqual("brand-new-state", rule.GetUnknownWord(nameof(PlainRule.ConnectionState)));

            connection.Save(rule.CloneEntity());
            CollectionAssert.Contains(TheSet(connection), "=connection-state=established,brand-new-state");
        }

        // An enum without a [TikEnumUnknown] member — a caller's own — keeps the strict read it always had.
        public enum StrictColour { [TikEnum("red")] Red, [TikEnum("blue")] Blue }

        [TikEntity("/ip/firewall/filter")]
        public class StrictEntity
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("action")]
            public StrictColour Action { get; set; }
        }

        [TestMethod]
        public void AnEnumWithoutAnUnknownMember_StillThrows()
        {
            Assert.ThrowsException<FormatException>(
                () => Router(Rule("*2", "send-to-the-moon")).LoadAll<StrictEntity>().ToList());
        }

        [TestMethod]
        public void NoEnumABuiltInEntityMaps_HasAnUnknownMember()
        {
            // A TikValue<T?> property reads an unknown word as Unparsed and never produces the member; one left in an
            // enum would be a value a caller can assign and the save refuses.
            var enums = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<TikEntityAttribute>() != null)
                .SelectMany(t => t.GetProperties())
                .Where(p => p.GetCustomAttribute<TikPropertyAttribute>() != null)
                .Select(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(TikValue<>)
                    ? p.PropertyType.GetGenericArguments()[0] : p.PropertyType)
                .Select(t => Nullable.GetUnderlyingType(t) ?? t)
                .Where(t => t.IsEnum).Distinct().ToList();

            var withUnknown = enums
                .Where(e => e.GetFields(BindingFlags.Public | BindingFlags.Static).Any(f => f.GetCustomAttribute<TikEnumUnknownAttribute>() != null))
                .Select(e => e.FullName).ToList();

            Assert.IsTrue(enums.Count > 100, "found the entity enums");
            Assert.AreEqual(0, withUnknown.Count, "remove the [TikEnumUnknown] member: " + string.Join(", ", withUnknown));
        }
    }
}
