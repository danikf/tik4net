using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Queue;
using tik4net.Objects.Tool.Graphing;

namespace tik4net.integrationtests
{
    [TestClass]
    public class GraphingTest : TestBase
    {
        // ── GraphingInterface ────────────────────────────────────────────────────

        [TestMethod]
        public void ListGraphingInterfacesWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/interface");
            var list = Connection.LoadAll<GraphingInterface>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddGraphingInterfaceWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/interface");
            // "all" entry may already exist on the router → use a specific interface name.
            var entry = new GraphingInterface
            {
                Interface = "ether1",
                AllowAddress = "192.0.2.0/24",
            };
            Connection.Save(entry);

            var loaded = Connection.LoadById<GraphingInterface>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("ether1", loaded.Interface);

            Connection.Delete(loaded);
        }

        // ── GraphingResource ─────────────────────────────────────────────────────

        [TestMethod]
        public void ListGraphingResourcesWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/resource");
            var list = Connection.LoadAll<GraphingResource>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddGraphingResourceWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/resource");
            // Use a TEST-NET address (192.0.2.0/24) to avoid conflict with any existing entry.
            var entry = new GraphingResource
            {
                AllowAddress = "192.0.2.0/24",
            };
            Connection.Save(entry);

            var loaded = Connection.LoadById<GraphingResource>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("192.0.2.0/24", loaded.AllowAddress);

            Connection.Delete(loaded);
        }

        // ── GraphingQueue ────────────────────────────────────────────────────────

        [TestMethod]
        public void ListGraphingQueuesWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/queue");
            var list = Connection.LoadAll<GraphingQueue>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddGraphingQueueWillNotFail()
        {
            EnsureCommandAvailable("/tool/graphing/queue");
            EnsureCommandAvailable("/queue/simple");
            // Router validates simple-queue existence → create a throwaway queue first.
            var sq = new QueueSimple { Name = "tik4net-gq-test", Target = "192.0.2.1/32" };
            Connection.Save(sq);
            try
            {
                var entry = new GraphingQueue
                {
                    SimpleQueue = sq.Name,
                    AllowAddress = "192.0.2.0/24",
                };
                Connection.Save(entry);

                var loaded = Connection.LoadById<GraphingQueue>(entry.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(sq.Name, loaded.SimpleQueue);

                Connection.Delete(entry);
            }
            finally
            {
                Connection.Delete(sq);
            }
        }
    }
}
