using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Routing.Ospf;
using tik4net.Objects.System;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <c>DeleteById</c> / <c>DeleteByIdAsync</c>: the same <c>/remove</c> that <c>Delete(entity)</c> sends for a
    /// loaded row, without loading it, and the same refusals before anything is sent.
    /// </summary>
    [TestClass]
    public class DeleteByIdTests
    {
        private static string Conversation(TikFakeConnection connection)
            => string.Join("\n", connection.SentCommands.Select(rows => string.Join(" ", rows)));

        private static TikFakeConnection Router()
            => new TikFakeConnection().WithNonQuery(rows => rows.First().EndsWith("/remove"));

        [TestMethod]
        public void DeleteByIdSendsWhatDeleteSendsForTheLoadedRow()
        {
            var byEntity = Router();
            var byId = Router();

            byEntity.Delete(new FirewallAddressList { List = "BLACKLIST", Address = "10.0.0.3" }.WithId("*3"));
            byId.DeleteById<FirewallAddressList>("*3");

            Assert.AreEqual(Conversation(byEntity), Conversation(byId));
            Assert.AreEqual(1, byId.SentCommands.Count, "no load before the remove");
            byId.AssertWasSent(rows => rows.First() == "/ip/firewall/address-list/remove" && rows.Contains("=.id=*3"));
        }

        [TestMethod]
        public async Task DeleteByIdAsyncSendsWhatDeleteByIdSends()
        {
            var sync = Router();
            var async = Router();

            sync.DeleteById<FirewallAddressList>("*3");
            await async.DeleteByIdAsync<FirewallAddressList>("*3");

            Assert.AreEqual(Conversation(sync), Conversation(async));
        }

        [TestMethod]
        public async Task ANullIdIsRefusedByNameBeforeAnythingIsSent()
        {
            var connection = Router();

            var ex = Assert.ThrowsException<ArgumentNullException>(() => connection.DeleteById<FirewallAddressList>(null!));
            Assert.AreEqual("id", ex.ParamName);
            var asyncEx = await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => connection.DeleteByIdAsync<FirewallAddressList>(null!));
            Assert.AreEqual("id", asyncEx.ParamName);
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public async Task AnEmptyIdIsRefusedByNameBeforeAnythingIsSent()
        {
            // An empty .id would be sent as `=.id=`; refused here as Delete refuses an entity that has none.
            var connection = Router();

            var ex = Assert.ThrowsException<ArgumentException>(() => connection.DeleteById<FirewallAddressList>(""));
            Assert.AreEqual("id", ex.ParamName);
            var asyncEx = await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => connection.DeleteByIdAsync<FirewallAddressList>(""));
            Assert.AreEqual("id", asyncEx.ParamName);
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public void AMenuWithoutRemoveIsRefusedAsDeleteRefusesIt()
        {
            var connection = new TikFakeConnection();

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => connection.DeleteById<OspfNeighbor>("*2B"));

            StringAssert.Contains(ex.Message, "'remove'");
            StringAssert.Contains(ex.Message, "/routing/ospf/neighbor");
            Assert.AreEqual(0, connection.SentCommands.Count);
        }

        [TestMethod]
        public void AnEntityWithoutAnIdPropertyIsRefused()
        {
            // A singleton has no rows to address.
            var connection = new TikFakeConnection();

            Assert.ThrowsException<InvalidOperationException>(() => connection.DeleteById<SystemIdentity>("*1"));
            Assert.AreEqual(0, connection.SentCommands.Count);
        }
    }
}
