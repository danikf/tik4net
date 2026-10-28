// QueueSimpleMoveTest.cs — what a move to a destination that does not exist does, on every transport.
//
// Measured 2026-10-28 on RouterOS 7.25 (WinBox + MAC review, §5 X2): the API and the CLI resolve a destination NAME
// on the router and trap 'no such item' when it names nothing; an unknown '*HEX' destination moves the row to the end
// on every transport, the M2 handler included — that part is RouterOS's own answer. WinBox native resolves names
// client-side and used to read an unresolved one as "move to end".

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests.Queue
{
    [TestClass]
    public class QueueSimpleMoveTest : TestBase
    {
        private static readonly string Prefix = TestNames.Unique("tik4net-test-move");
        private static readonly string[] Names = { Prefix + "-a", Prefix + "-b", Prefix + "-c" };

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateQueues()
        {
            RemoveQueues();
            using (var api = OpenSideApi())
                for (int i = 0; i < Names.Length; i++)
                    api.Save(new QueueSimple
                    {
                        Name = Names[i],
                        Target = new TikValueList<string>("192.168.253." + (i + 1) + "/32"),
                        MaxLimit = (TikRatePair)"1M/2M",
                        Disabled = true,
                    });
        }

        [TestCleanup]
        public void RemoveQueues()
        {
            using (var api = OpenSideApi())
                foreach (var q in api.LoadList<QueueSimple>().Where(q => Names.Contains(q.Name.Value)).ToList())
                    api.Delete(q);
        }

        private static List<string> Order()
        {
            using (var api = OpenSideApi())
                return api.LoadList<QueueSimple>().Select(q => q.Name.Value).Where(Names.Contains).ToList();
        }

        [TestMethod]
        public void AMoveToAnUnknownDestinationName_IsNoSuchItem_AndMovesNothing()
        {
            var before = Order();
            CollectionAssert.AreEqual(Names, before, "precondition: the queues are in creation order");

            Assert.ThrowsException<TikNoSuchItemException>(() =>
                Connection.CreateCommandAndParameters("/queue/simple/move",
                    "numbers", Names[0], "destination", Prefix + "-nosuch").ExecuteNonQuery());

            CollectionAssert.AreEqual(before, Order(), "a refused move must leave the order as it was");
        }

        [TestMethod]
        public void AMoveToAKnownDestinationName_MovesBeforeIt()
        {
            Connection.CreateCommandAndParameters("/queue/simple/move",
                "numbers", Names[0], "destination", Names[2]).ExecuteNonQuery();

            CollectionAssert.AreEqual(new[] { Names[1], Names[0], Names[2] }, Order());
        }
    }
}
