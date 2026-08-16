using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Covers the Task-based mapper surface (A7) by the only property that matters for it: <b>a
    /// <c>*Async</c> method and its synchronous twin send the router exactly the same thing.</b>
    /// </summary>
    /// <remarks>
    /// Asserting on the sent commands rather than on the return value is deliberate. An async CRUD path
    /// that loaded the right rows while sending a different <c>/set</c>, skipping an <c>/unset</c>, or
    /// losing the change-tracker snapshot would pass every result-based test and still be a second,
    /// slightly different mapper — which is the failure this whole item risks, since Save's rules
    /// (what counts as a create, what OnlyChanges does for an entity that was never loaded, which fields
    /// are unset rather than set, and that the unsets go first) are subtle and were previously written
    /// down in exactly one place.
    /// </remarks>
    [TestClass]
    public class MapperAsyncEquivalenceTests
    {
        private const string ListName = "BLACKLIST";

        // The whole conversation with the router, in order, as one comparable string.
        private static string Conversation(TikFakeConnection connection)
            => string.Join("\n", connection.SentCommands.Select(rows => string.Join(" ", rows)));

        private static FirewallAddressList Entry(string id, string address, string comment = "")
            => new FirewallAddressList { List = ListName, Address = address, Comment = comment }.WithId(id);

        private static TikFakeConnection ConnectionWith(params FirewallAddressList[] entries)
            => new TikFakeConnection()
                .WithEntities(entries)
                .WithNonQuery(rows => rows.First().EndsWith("/set"))
                .WithNonQuery(rows => rows.First().EndsWith("/unset"))
                .WithNonQuery(rows => rows.First().EndsWith("/remove"))
                .WithScalarResponse(rows => rows.First().EndsWith("/add"), "*99");

        // ── Load ───────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task LoadAllAsyncSendsTheSameAsLoadAllAndReturnsTheSameRows()
        {
            var syncConn = ConnectionWith(Entry("*1", "10.0.0.1"), Entry("*2", "10.0.0.2", "bad actor"));
            var asyncConn = ConnectionWith(Entry("*1", "10.0.0.1"), Entry("*2", "10.0.0.2", "bad actor"));

            var expected = syncConn.LoadAll<FirewallAddressList>().ToList();
            var actual = await asyncConn.LoadAllAsync<FirewallAddressList>();

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
            CollectionAssert.AreEqual(
                expected.Select(e => e.Id + "|" + e.Address + "|" + e.Comment).ToList(),
                actual.Select(e => e.Id + "|" + e.Address + "|" + e.Comment).ToList());
        }

        [TestMethod]
        public async Task LoadByIdAsyncSendsTheSameAsLoadById()
        {
            var syncConn = ConnectionWith(Entry("*1", "10.0.0.1"));
            var asyncConn = ConnectionWith(Entry("*1", "10.0.0.1"));

            var expected = syncConn.LoadById<FirewallAddressList>("*1");
            var actual = await asyncConn.LoadByIdAsync<FirewallAddressList>("*1");

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
            Assert.AreEqual(expected.Address, actual.Address);
        }

        [TestMethod]
        public async Task LoadByIdAsyncThrowsTheSameExceptionWhenNothingMatches()
        {
            var conn = ConnectionWith(/* nothing on the router */);

            await Assert.ThrowsExceptionAsync<TikNoSuchItemException>(
                () => conn.LoadByIdAsync<FirewallAddressList>("*1"));
        }

        [TestMethod]
        public async Task LoadSingleOrDefaultAsyncReturnsDefaultRatherThanThrowing()
        {
            var conn = ConnectionWith(/* nothing on the router */);

            var loaded = await conn.LoadSingleOrDefaultAsync<FirewallAddressList>();

            Assert.IsNull(loaded);
        }

        // ── Save: create ───────────────────────────────────────────────────────

        [TestMethod]
        public async Task SaveAsyncOfANewEntitySendsTheSameAddAndTakesTheSameId()
        {
            var syncConn = ConnectionWith();
            var asyncConn = ConnectionWith();

            var syncEntity = new FirewallAddressList { List = ListName, Address = "192.168.1.100", Comment = "new" };
            var asyncEntity = new FirewallAddressList { List = ListName, Address = "192.168.1.100", Comment = "new" };

            syncConn.Save(syncEntity);
            await asyncConn.SaveAsync(asyncEntity);

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
            Assert.AreEqual("*99", asyncEntity.Id, "the new .id must be written back into the entity");
            Assert.AreEqual(syncEntity.Id, asyncEntity.Id);
        }

        // ── Save: update ───────────────────────────────────────────────────────

        [TestMethod]
        public async Task SaveAsyncOfAModifiedEntitySendsTheSameSet()
        {
            var syncConn = ConnectionWith(Entry("*1", "10.0.0.1", "before"));
            var asyncConn = ConnectionWith(Entry("*1", "10.0.0.1", "before"));

            var syncEntity = syncConn.LoadById<FirewallAddressList>("*1");
            var asyncEntity = await asyncConn.LoadByIdAsync<FirewallAddressList>("*1");
            syncEntity.Comment = "after";
            asyncEntity.Comment = "after";

            syncConn.Save(syncEntity, saveMode: TikSaveMode.OnlyChanges);
            await asyncConn.SaveAsync(asyncEntity, saveMode: TikSaveMode.OnlyChanges);

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
            Assert.IsTrue(Conversation(asyncConn).Contains("comment=after"), "the change must actually be sent");
        }

        [TestMethod]
        public async Task SaveAsyncOfAnUnchangedEntitySendsNothing()
        {
            // The tracker's "nothing changed" outcome is the one that skips the router entirely — an async
            // path that lost it would still pass a result-based test while writing on every save.
            var conn = ConnectionWith(Entry("*1", "10.0.0.1", "unchanged"));
            var entity = await conn.LoadByIdAsync<FirewallAddressList>("*1");
            int afterLoad = conn.SentCommands.Count;

            await conn.SaveAsync(entity, saveMode: TikSaveMode.OnlyChanges);

            Assert.AreEqual(afterLoad, conn.SentCommands.Count, "an unmodified entity must not be written");
        }

        [TestMethod]
        public async Task SaveAsyncInFullUpdateModeLoadsTheUnmodifiedEntityJustAsSaveDoes()
        {
            // FullUpdate is the branch that has to reach the router while deciding what to send. It is the
            // one place where the async twin does a load of its own, so it is the one most likely to drift.
            var syncConn = ConnectionWith(Entry("*1", "10.0.0.1", "before"));
            var asyncConn = ConnectionWith(Entry("*1", "10.0.0.1", "before"));

            var syncEntity = syncConn.LoadById<FirewallAddressList>("*1");
            var asyncEntity = await asyncConn.LoadByIdAsync<FirewallAddressList>("*1");
            syncEntity.Comment = "after";
            asyncEntity.Comment = "after";

            syncConn.Save(syncEntity, saveMode: TikSaveMode.FullUpdate);
            await asyncConn.SaveAsync(asyncEntity, saveMode: TikSaveMode.FullUpdate);

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
        }

        [TestMethod]
        public async Task SaveAsyncSendsTheUnsetsBeforeTheSetJustAsSaveDoes()
        {
            // FirewallFilter carries UnsetOnDefault fields; an empty one is unset rather than set, and the
            // unsets must precede the /set. Ordering is invisible to a result assertion.
            var syncConn = new TikFakeConnection()
                .WithNonQuery(rows => rows.First().EndsWith("/set"))
                .WithNonQuery(rows => rows.First().EndsWith("/unset"));
            var asyncConn = new TikFakeConnection()
                .WithNonQuery(rows => rows.First().EndsWith("/set"))
                .WithNonQuery(rows => rows.First().EndsWith("/unset"));

            var fields = new[] { "connection-mark", "comment" };
            var syncEntity = new FirewallFilter { Comment = "rule", ConnectionMark = "" }.WithId("*7");
            var asyncEntity = new FirewallFilter { Comment = "rule", ConnectionMark = "" }.WithId("*7");

            syncConn.Save(syncEntity, usedFieldsFilter: fields);
            await asyncConn.SaveAsync(asyncEntity, usedFieldsFilter: fields);

            string conversation = Conversation(asyncConn);
            Assert.AreEqual(Conversation(syncConn), conversation);
            Assert.IsTrue(conversation.IndexOf("/unset", StringComparison.Ordinal)
                        < conversation.IndexOf("/set", StringComparison.Ordinal),
                "the unset must be sent before the set:\n" + conversation);
        }

        // ── Delete ─────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task DeleteAsyncSendsTheSameRemove()
        {
            var syncConn = ConnectionWith(Entry("*3", "10.0.0.3"));
            var asyncConn = ConnectionWith(Entry("*3", "10.0.0.3"));

            syncConn.Delete(Entry("*3", "10.0.0.3"));
            await asyncConn.DeleteAsync(Entry("*3", "10.0.0.3"));

            Assert.AreEqual(Conversation(syncConn), Conversation(asyncConn));
            asyncConn.AssertWasSent(rows => rows.Any(r => r.Contains(".id=*3")));
        }

        [TestMethod]
        public async Task DeleteAsyncRefusesAnEntityWithNoIdJustAsDeleteDoes()
        {
            var conn = ConnectionWith();

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => conn.DeleteAsync(new FirewallAddressList { Address = "10.0.0.9" }));
        }

        // ── Capability gate ────────────────────────────────────────────────────

        [TestMethod]
        public async Task ATransportWithoutAsyncCommandsIsRefusedRatherThanBlocked()
        {
            var conn = ConnectionWith(Entry("*1", "10.0.0.1"));
            conn.Capabilities = TikConnectionCapability.Crud;   // no AsyncCommands

            await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(
                () => conn.LoadAllAsync<FirewallAddressList>());
        }
    }
}
