#nullable enable
// TikValueMapperTests.cs — the 5.0 entity value model (TikValue<T>) against the mapper: what a load reads, what a
// save sends. The tables are the ones in the design (_notes/5.0/features/entity-value-model.md, "Read" and "Write"),
// and the wire shapes are the ones measured in its validation runs V1–V5.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikValueMapperTests
    {
        public enum Mode { [TikEnum("no")] No, [TikEnum("yes")] Yes, [TikEnum("auto")] Auto }

        [Flags]
        public enum States
        {
            [TikEnum("new")] New = 1,
            [TikEnum("established")] Established = 2,
            [TikEnumUnknown] Unknown = 1 << 30,
        }

        [TikEntity("/box")]
        public class Box
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("name")]
            public TikValue<string?> Name { get; set; }

            [TikProperty("comment")]
            public TikValue<string?> Comment { get; set; }

            [TikProperty("port", DefaultValue = "80")]
            public TikValue<int?> Port { get; set; }

            [TikProperty("mode")]
            public TikValue<Mode?> Mode { get; set; }

            [TikProperty("state")]
            public TikValue<States?> State { get; set; }

            [TikProperty("fib", IsPresenceFlag = true)]
            public TikValue<bool?> Fib { get; set; }

            [TikProperty("running", IsReadOnly = true)]
            public TikValue<bool?> Running { get; private set; }
        }

        private static TikFakeConnection Router(params Dictionary<string, string>[] rows)
            => new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => rows.Select(r => (ITikSentence)new TikFakeReSentence(r)).Concat(new[] { (ITikSentence)new TikFakeDoneSentence() }).ToArray())
                .WithNonQuery(cmd => cmd.First() == "/box/set" || cmd.First() == "/box/unset")
                .WithScalarResponse(cmd => cmd.First() == "/box/add", "*9");

        private static Dictionary<string, string> Row(params (string, string)[] fields)
        {
            var d = new Dictionary<string, string> { [".id"] = "*1" };
            foreach (var (k, v) in fields) d[k] = v;
            return d;
        }

        private static string[] Sent(TikFakeConnection c, string verb)
            => c.SentCommands.Where(x => x.First() == "/box/" + verb).SelectMany(x => x.Skip(1)).ToArray();

        // ── Read ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void AFieldTheRowLacks_IsAbsent_AndTheDeclaredDefaultIsNotInvented()
        {
            var box = Router(Row(("name", "a"))).LoadAll<Box>().Single();

            Assert.AreEqual(TikValueState.Absent, box.Port.State, "DefaultValue = \"80\" documents the router; it is not read");
            Assert.AreEqual(TikValueState.Absent, box.Comment.State);
            Assert.AreEqual(TikValueState.Absent, box.Running.State);
        }

        [TestMethod]
        public void APrintedValue_IsPresent()
        {
            var box = Router(Row(("name", "a"), ("port", "8080"), ("mode", "auto"), ("running", "true"))).LoadAll<Box>().Single();

            Assert.IsTrue(box.Port == 8080);
            Assert.IsTrue(box.Mode == Mode.Auto);
            Assert.IsTrue(box.Running == true);
            Assert.AreEqual("a", box.Name.Value);
        }

        [TestMethod]
        public void AnEmptyString_IsPresentEmpty()
        {
            Assert.IsTrue(Router(Row(("name", ""))).LoadAll<Box>().Single().Name == "");
        }

        [TestMethod]
        public void AnEmptyPresenceFlag_IsPresentTrue()
        {
            Assert.IsTrue(Router(Row(("fib", ""))).LoadAll<Box>().Single().Fib == true);
        }

        [TestMethod]
        public void AValueTheTypeCannotHold_IsUnparsed_WithTheRouterWord()
        {
            // 6.49.13 prints ddns-enabled=false where 7.x has yes|no|auto (V5): a type change, not a new word.
            var box = Router(Row(("port", "none"), ("mode", "false"), ("running", ""))).LoadAll<Box>().Single();

            Assert.AreEqual(TikValueState.Unparsed, box.Port.State);
            Assert.AreEqual("none", box.Port.RawValue);
            Assert.AreEqual(TikValueState.Unparsed, box.Mode.State);
            Assert.AreEqual("false", box.Mode.RawValue);
            Assert.AreEqual(TikValueState.Unparsed, box.Running.State, "an empty value on a non-presence bool");
            Assert.AreEqual("", box.Running.RawValue);
            Assert.IsFalse(box.Mode == Mode.No, "an unparsed value equals no typed value");
        }

        [TestMethod]
        public void AFlagsValueWithAnUnknownWord_KeepsTheKnownParts_OutsideValue()
        {
            var box = Router(Row(("state", "new,untracked"))).LoadAll<Box>().Single();

            Assert.AreEqual(TikValueState.Present, box.State.State);
            Assert.IsTrue(box.State == States.New, "the unknown word is not a bit inside Value, so the known set compares");
            Assert.AreEqual("untracked", box.State.UnknownFlagWords);
            Assert.AreEqual("new,untracked", box.State.ToString());
        }

        [TestMethod]
        public void AFlagsValue_With_KeepsTheRouterWord_OnSave()
        {
            var connection = Router(Row(("state", "new,untracked")));
            var box = connection.LoadAll<Box>().Single();

            box.State = box.State.With(States.Established);
            connection.Save(box);

            var state = Sent(connection, "set").Single(w => w.StartsWith("=state="));
            StringAssert.Contains(state, "untracked", "With keeps the router's unknown word: " + state);
            StringAssert.Contains(state, "established");
        }

        [TestMethod]
        public void AFlagsValue_AssignedAfresh_IsExactlyThatSet()
        {
            var connection = Router(Row(("state", "new,untracked")));
            var box = connection.LoadAll<Box>().Single();

            box.State = States.Established;
            connection.Save(box);

            CollectionAssert.Contains(Sent(connection, "set"), "=state=established");
        }

        [TestMethod]
        public void AFlagsValue_UnchangedWithAnUnknownWord_SendsNothing()
        {
            var connection = Router(Row(("state", "new,untracked")));
            connection.Save(connection.LoadAll<Box>().Single());
            Assert.AreEqual(0, connection.SentCommands.Count(c => c.First() == "/box/set"));
        }

        [TestMethod]
        public void AFlagsValue_SurvivesCloneEntity_WithItsUnknownWord()
        {
            var box = Router(Row(("state", "new,untracked"))).LoadAll<Box>().Single();
            Assert.AreEqual("untracked", box.CloneEntity().State.UnknownFlagWords);
        }

        // ── Write ────────────────────────────────────────────────────────────

        [TestMethod]
        public void Add_SendsWhatWasAssigned_AndNothingElse()
        {
            var connection = Router();
            connection.Save(new Box { Name = "b", Port = 80, Comment = null });

            CollectionAssert.AreEquivalent(new[] { "=name=b", "=port=80" }, Sent(connection, "add"),
                "an explicit 80 is sent although it is the declared default; an assigned null and Absent are not");
        }

        [TestMethod]
        public void Update_Unchanged_SendsNothing_AnUnparsedValueSurvives()
        {
            var connection = Router(Row(("name", "a"), ("mode", "false"), ("port", "none")));
            var box = connection.LoadAll<Box>().Single();

            connection.Save(box);

            Assert.AreEqual(0, connection.SentCommands.Count(c => c.First() == "/box/set" || c.First() == "/box/unset"));
        }

        [TestMethod]
        public void Update_ChangedToAValue_SendsIt()
        {
            var connection = Router(Row(("name", "a"), ("port", "8080")));
            var box = connection.LoadAll<Box>().Single();

            box.Port = 9090;
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=port=9090", "=.id=*1" }, Sent(connection, "set"));
        }

        [TestMethod]
        public void Update_ALoadedValueAssignedNull_IsUnset_EvenAnUnparsedOne()
        {
            var connection = Router(Row(("name", "a"), ("comment", "x"), ("mode", "false")));
            var box = connection.LoadAll<Box>().Single();

            box.Comment = null;
            box.Mode = null;
            connection.Save(box);

            var unset = connection.SentCommands.Where(c => c.First() == "/box/unset")
                .Select(c => c.Single(w => w.StartsWith("=value-name="))).ToArray();
            CollectionAssert.AreEquivalent(new[] { "=value-name=mode" }, unset);
            // A comment is cleared with comment="" rather than an unset (ClearedCommentTests).
            CollectionAssert.Contains(connection.SentCommands.Single(c => c.First() == "/box/set").ToArray(), "=comment=");
        }

        [TestMethod]
        public void Update_AnAbsentFieldAssignedNull_SendsNothing()
        {
            var connection = Router(Row(("name", "a")));
            var box = connection.LoadAll<Box>().Single();

            box.Comment = null;
            connection.Save(box);

            Assert.AreEqual(0, connection.SentCommands.Count(c => c.First() == "/box/set" || c.First() == "/box/unset"));
        }

        [TestMethod]
        public void ACopiedValue_CarriesItsState_AndASaveStillSeesTheChange()
        {
            var connection = Router(Row(("name", "a"), ("port", "8080")));
            var box = connection.LoadAll<Box>().Single();
            var other = new Box { Port = 1234 };

            box.Port = other.Port;
            connection.Save(box);

            CollectionAssert.Contains(Sent(connection, "set"), "=port=1234");
        }

        [TestMethod]
        public void CloneEntity_KeepsAnAssignedNull()
        {
            var box = new Box { Comment = null, Name = "n" };

            var clone = box.CloneEntity();

            Assert.AreEqual(TikValueState.Present, clone.Comment.State, "an assigned null is an intent to unset, not Absent");
            Assert.IsTrue(clone.Name == "n");
        }

        [TikEntity("/box")]
        public class PlainFlagsBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; private set; }

            [TikProperty("state")]
            public States? State { get; set; }
        }

        [TestMethod]
        public void CloneEntity_OfAPlainFlagsProperty_KeepsItsUnknownWord()
        {
            // Review C5 claimed a clone loses the word its Unknown bit stood for. It does not: a plain clone goes through the
            // string form, which carries the word, and the clone records it again. Kept as the guard for that.
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(Row(("state", "new,untracked"))), new TikFakeDoneSentence() });
            var clone = connection.LoadAll<PlainFlagsBox>().Single().CloneEntity();

            var state = TikEntityMetadataCache.GetMetadata<PlainFlagsBox>().Properties.Single(p => p.FieldName == "state");
            StringAssert.Contains(state.GetEntityValue(clone), "untracked");
        }

        [TestMethod]
        public void TikValueOfANonNullableValueType_IsRefused_WithTheFix()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikEntityMetadataCache.GetMetadata<Bad>());
            StringAssert.Contains(ex.Message, "TikValue<Int32?>");
        }

        [TikEntity("/bad")]
        public class Bad
        {
            [TikProperty("port")]
            public TikValue<int> Port { get; set; }
        }

        [TestMethod]
        public void IsMandatoryOnATikValue_IsRefused()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikEntityMetadataCache.GetMetadata<MandatoryBox>());
            StringAssert.Contains(ex.Message, "IsMandatory");
        }

        [TestMethod]
        public void UnsetOnDefaultOnATikValue_IsRefused()
        {
            var ex = Assert.ThrowsException<ArgumentException>(() => TikEntityMetadataCache.GetMetadata<UnsetBox>());
            StringAssert.Contains(ex.Message, "UnsetOnDefault");
        }

        [TestMethod]
        public void ARowWithoutAFieldTheEntityRelied_OnStillLoads()
        {
            // The C1 case: a field that was IsMandatory on the plain property. Absent, and the load succeeds.
            var box = Router(new Dictionary<string, string> { [".id"] = "*1" }).LoadAll<Box>().Single();
            Assert.AreEqual(TikValueState.Absent, box.Name.State);
        }

        [TikEntity("/bad")]
        public class MandatoryBox
        {
            [TikProperty("name", IsMandatory = true)]
            public TikValue<string?> Name { get; set; }
        }

        [TikEntity("/bad")]
        public class UnsetBox
        {
            [TikProperty("comment", UnsetOnDefault = true)]
            public TikValue<string?> Comment { get; set; }
        }
    }
}
