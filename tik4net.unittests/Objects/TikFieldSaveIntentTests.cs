#nullable enable
// TikFieldSaveIntentTests.cs — what a TikField's state says to an update: Absent says nothing, an assigned null
// unsets, a value sets — whether the entity was loaded in full, with a partial .proplist, or not on this connection
// at all. The decisions are D1–D3 of the 5.0 entity-layer review.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class TikFieldSaveIntentTests
    {
        [TikEntity("/box")]
        public class Box
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public TikField<string?> Name { get; set; }

            [TikProperty("comment")]
            public TikField<string?> Comment { get; set; }

            [TikProperty("port")]
            public TikField<int?> Port { get; set; }

            [TikProperty("disabled")]
            public TikField<bool?> Disabled { get; set; }
        }

        private static TikFakeConnection Router(params (string, string)[] fields)
        {
            var row = new Dictionary<string, string> { [".id"] = "*1" };
            foreach (var (k, v) in fields) row[k] = v;
            return new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/box/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(row), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == "/box/set" || cmd.First() == "/box/unset");
        }

        private static Box LoadPartial(TikFakeConnection connection, string proplist)
            => connection.LoadList<Box>(connection.CreateParameter(TikSpecialProperties.Proplist, proplist)).Single();

        private static string[] Set(TikFakeConnection c)
            => c.SentCommands.Where(x => x.First() == "/box/set").SelectMany(x => x.Skip(1)).ToArray();

        private static string[] Unset(TikFakeConnection c)
            => c.SentCommands.Where(x => x.First() == "/box/unset")
                .Select(x => x.Single(w => w.StartsWith("=value-name="))).ToArray();

        private static int Writes(TikFakeConnection c)
            => c.SentCommands.Count(x => x.First() == "/box/set" || x.First() == "/box/unset");

        private static async Task SaveAsyncOrNot(TikFakeConnection connection, Box box, bool async)
        {
            if (async)
                await connection.SaveAsync(box);
            else
                connection.Save(box);
        }

        // ── D1: Absent on a loaded entity says nothing ─────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Update_ALoadedValueResetToAbsent_SendsNothing(bool async)
        {
            var connection = Router(("name", "a"), ("comment", "x"), ("port", "8080"));
            var box = connection.LoadAll<Box>().Single();

            box.Comment = default;
            box.Port = TikField<int?>.Absent;
            await SaveAsyncOrNot(connection, box, async);

            Assert.AreEqual(0, Writes(connection), "Absent is 'nothing said': neither comment=\"\" nor an unset");
            Assert.IsFalse(connection.ChangeTracker().HasChanges(box, TikEntityMetadataCache.GetMetadata<Box>()));
        }

        [TestMethod]
        public void Update_ALoadedValueResetToAbsent_AlongsideAChange_SendsOnlyTheChange()
        {
            var connection = Router(("name", "a"), ("comment", "x"), ("port", "8080"));
            var box = connection.LoadAll<Box>().Single();

            box.Comment = default;
            box.Name = "b";
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=name=b", "=.id=*1" }, Set(connection));
            Assert.AreEqual(0, Unset(connection).Length);
        }

        [TestMethod]
        public void Update_AValueResetToAbsentAndSaved_IsStillKnown_SoALaterNullUnsetsIt()
        {
            var connection = Router(("name", "a"), ("port", "8080"));
            var box = connection.LoadAll<Box>().Single();

            box.Port = default;
            connection.Save(box);       // says nothing: the router still holds 8080
            box.Port = null;
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=value-name=port" }, Unset(connection));
        }

        // ── D2: a partial .proplist load ───────────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task PartialLoad_AValueAssignedOutsideTheList_IsSent(bool async)
        {
            var connection = Router(("comment", "x"));
            var box = LoadPartial(connection, ".id,comment");

            box.Disabled = true;
            await SaveAsyncOrNot(connection, box, async);

            CollectionAssert.AreEquivalent(new[] { "=disabled=yes", "=.id=*1" }, Set(connection));
        }

        [TestMethod]
        public void PartialLoad_ANullAssignedOutsideTheList_IsUnset()
        {
            var connection = Router(("comment", "x"));
            var box = LoadPartial(connection, ".id,comment");

            box.Port = null;
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=value-name=port" }, Unset(connection));
        }

        [TestMethod]
        public void PartialLoad_FieldsOutsideTheListLeftAlone_SendNothing()
        {
            var connection = Router(("comment", "x"));
            var box = LoadPartial(connection, ".id,comment");

            connection.Save(box);

            Assert.AreEqual(0, Writes(connection));
        }

        [TestMethod]
        public void PartialLoad_AChangeInsideTheList_IsSentAlone()
        {
            var connection = Router(("comment", "x"));
            var box = LoadPartial(connection, ".id,comment");

            box.Comment = "y";
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=comment=y", "=.id=*1" }, Set(connection));
        }

        [TestMethod]
        public void PartialLoad_AFieldOutsideTheListOnceSaved_IsNotSentAgain()
        {
            var connection = Router(("comment", "x"));
            var box = LoadPartial(connection, ".id,comment");

            box.Disabled = true;
            connection.Save(box);
            connection.Save(box);

            Assert.AreEqual(1, connection.SentCommands.Count(x => x.First() == "/box/set"), "the save made it known");
        }

        // ── D3: an entity with an .id this connection never loaded ─────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task NeverLoaded_AnAssignedNull_IsUnset(bool async)
        {
            var connection = Router();
            var box = new Box { Id = "*1", Port = null, Comment = null };

            await SaveAsyncOrNot(connection, box, async);

            CollectionAssert.AreEquivalent(new[] { "=value-name=port" }, Unset(connection));
            CollectionAssert.AreEquivalent(new[] { "=comment=", "=.id=*1" }, Set(connection),
                "a comment is cleared with comment=\"\", as on a loaded entity");
        }

        [TestMethod]
        public void NeverLoaded_AbsentFields_SendNothing_AssignedValuesAreSent()
        {
            var connection = Router();
            var box = new Box { Id = "*1", Name = "n" };

            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=name=n", "=.id=*1" }, Set(connection));
            Assert.AreEqual(0, Unset(connection).Length);
        }

        [TestMethod]
        public void NeverLoaded_FullUpdate_AnAssignedNull_UnsetsWhatTheRouterHolds()
        {
            var connection = Router(("name", "a"), ("port", "8080"));
            var box = new Box { Id = "*1", Port = null };

            connection.Save(box, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "=value-name=port" }, Unset(connection));
            Assert.AreEqual(0, Set(connection).Length, "name is Absent here: nothing said, so the router's 'a' is left");
        }

        // ── The extra rules together ───────────────────────────────────────────────────────────────

        [TikEntity("/box")]
        public class TickingBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("time", ChangesOnItsOwn = true)]
            public TikField<string?> Time { get; set; }

            [TikProperty("comment")]
            public TikField<string?> Comment { get; set; }
        }

        [TestMethod]
        public void FullUpdate_AFieldThatChangesOnItsOwn_AssignedNull_IsStillUnset()
        {
            // FullUpdate leaves out such a field when the caller has not touched it; an assigned null is a touch.
            var connection = Router(("time", "12:00"));
            var box = connection.LoadAll<TickingBox>().Single();

            box.Time = null;
            connection.Save(box, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "=value-name=time" }, Unset(connection));
        }

        [TestMethod]
        public void FullUpdate_AClearedComment_IsCommentEmpty_NotAnUnset()
        {
            var connection = Router(("comment", "x"));
            var box = connection.LoadAll<TickingBox>().Single();

            box.Comment = null;
            connection.Save(box, saveMode: TikSaveMode.FullUpdate);

            CollectionAssert.AreEquivalent(new[] { "=comment=", "=.id=*1" }, Set(connection));
            Assert.AreEqual(0, Unset(connection).Length);
        }

        [TikEntity("/box")]
        public class UnannotatedBox
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public TikField<string> Name { get; set; }

            [TikProperty("comment")]
            public TikField<string> Comment { get; set; }
        }

        [TestMethod]
        public void ATikFieldOfAReferenceTypeWithoutQuestionMark_BehavesAsTheNullableForm()
        {
            // Only a value type has to be written T? (TikField<int> is refused); for a reference type the annotation
            // changes nothing at run time, and the field still reads Absent and saves by its state.
            var connection = Router(("name", "a"));
            var box = connection.LoadAll<UnannotatedBox>().Single();

            Assert.AreEqual(TikFieldState.Absent, box.Comment.State);
            box.Name = "b";
            connection.Save(box);

            CollectionAssert.AreEquivalent(new[] { "=name=b", "=.id=*1" }, Set(connection));
        }
    }
}
