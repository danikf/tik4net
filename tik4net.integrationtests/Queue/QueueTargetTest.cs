// QueueTargetTest.cs — the list field whose element is a union of families.
//
// /queue/simple `target` is a list, and one element may be an IPv4 network, an IPv6 network or an
// interface — webfig declares it as `union single:1` over {network, network6, enm→interface table}. It is
// the field that made a queue uncreatable over WinBox native: every element shape other than a plain
// address was refused, so the fixture of every other queue test had to be built over a side API
// connection.
//
// Each element is encoded through the SAME rules the scalar of that type would use, and the union tries
// its families in .jg order — the mirror of how the decode reads back the first family the element
// carries.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests.Queue
{
    [TestClass]
    public class QueueTargetTest : TestBase
    {
        private const string QueueName = "tik4net-test-target";

        // A subnet and an interface nothing in this lab routes through.
        private const string TestSubnet = "192.168.251.0/24";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        [TestCleanup]
        public void RemoveTestQueue()
        {
            using (var api = OpenSideApi())
                foreach (var q in api.LoadList<QueueSimple>().Where(q => q.Name == QueueName).ToList())
                    api.Delete(q);
        }

        private static QueueSimple TestQueue(ITikConnection conn)
            => conn.LoadList<QueueSimple>().Single(q => q.Name == QueueName);

        [TestMethod]
        public void AQueueIsCreatedWithItsTargetOverTheTransportUnderTest()
        {
            Connection.Save(new QueueSimple { Name = QueueName, Target = TestSubnet, MaxLimit = "1M/2M" });

            using (var api = OpenSideApi())
                Assert.AreEqual(TestSubnet, TestQueue(api).Target, "as the API reads it after the write");
        }

        [TestMethod]
        public void ATargetTakesSeveralElementsAtOnce()
        {
            Connection.Save(new QueueSimple
            {
                Name = QueueName,
                Target = TestSubnet + ",192.168.252.7/32",
                MaxLimit = "1M/2M",
            });

            using (var api = OpenSideApi())
            {
                // The router keeps a /32 as a /32; the order it prints them in is its own.
                var targets = TestQueue(api).Target.Split(',').OrderBy(t => t).ToArray();
                CollectionAssert.AreEqual(new[] { "192.168.251.0/24", "192.168.252.7/32" }, targets);
            }
        }

        [TestMethod]
        public void AnInterfaceIsAValidTargetToo()
        {
            // The union's third family: an element that names a record in the interface table rather than
            // an address. 'lo' exists on every RouterOS device.
            Connection.Save(new QueueSimple { Name = QueueName, Target = "lo", MaxLimit = "1M/2M" });

            using (var api = OpenSideApi())
                Assert.AreEqual("lo", TestQueue(api).Target, "as the API reads it after the write");
        }
    }
}
