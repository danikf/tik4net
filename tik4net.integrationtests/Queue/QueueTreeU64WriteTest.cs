// QueueTreeU64WriteTest.cs — a plain 64-bit field must survive a write.
//
// /queue/tree max-limit is a single value, not a pair, and it is `u64` on the wire (.jg type bigunit).
// Over WinBox native every u64 used to be encoded in the 32-bit form, which RouterOS accepts and ignores
// because it reads the type byte — so the write reported success and the value did not move. The value
// itself fits in 32 bits, which is what made it look like a permissions problem rather than an encoding
// one: 356 writable fields in the catalog are u64, and none of them could be written.
//
// This is the pair machinery's control case. It shares nothing with TikRatePair, so it fails if the u64
// encoding regresses even when the paired fields still pass.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests.Queue
{
    [TestClass]
    public class QueueTreeU64WriteTest : TestBase
    {
        private const string QueueName = "tik4net-test-u64-tree";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateTestQueue()
        {
            using (var api = OpenSideApi())
            {
                RemoveTestQueue(api);
                api.Save(new QueueTree
                {
                    Name = QueueName,
                    Parent = "global",
                    MaxLimit = 1000000,
                });
            }
        }

        [TestCleanup]
        public void DeleteTestQueue()
        {
            using (var api = OpenSideApi())
                RemoveTestQueue(api);
        }

        private static void RemoveTestQueue(ITikConnection conn)
        {
            foreach (var q in conn.LoadList<QueueTree>().Where(q => q.Name == QueueName).ToList())
                conn.Delete(q);
        }

        [TestMethod]
        public void AU64FieldWrittenOverTheTransportUnderTestReachesTheRouter()
        {
            var queue = Connection.LoadList<QueueTree>().Single(q => q.Name == QueueName);
            Assert.AreEqual(1000000L, queue.MaxLimit, "the fixture, as this transport reads it");

            queue.MaxLimit = 7000000;
            Connection.Save(queue);

            // Read back over the API, not over the transport that wrote it: a transport that echoed its own
            // request would pass this test while the router kept the old value, which is exactly the failure
            // being guarded against.
            using (var api = OpenSideApi())
                Assert.AreEqual(7000000L,
                    api.LoadList<QueueTree>().Single(q => q.Name == QueueName).MaxLimit,
                    "as the API reads it after the write");
        }
    }
}
