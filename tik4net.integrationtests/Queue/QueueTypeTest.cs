using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests
{
    /// <summary>
    /// /queue/type — the table whose parameters RouterOS names after the record's KIND (pcq-rate,
    /// red-limit, sfq-allot) while WinBox shows them plainly inside that kind's pane of one window.
    /// </summary>
    /// <remarks>
    /// The entity existed with no test at all, which is why the native transport could report a queue type's
    /// depth as 'queue-size' and refuse to write 'pcq-rate' without anything going red (A3). Every assertion
    /// here is on a field that lives in a deck pane, so it fails on any transport that loses the kind.
    /// </remarks>
    [TestClass]
    public class QueueTypeTest : TestBase
    {
        [TestMethod]
        public void ListQueueTypesWillNotFail()
        {
            EnsureCommandAvailable("/queue/type");
            var list = Connection.LoadAll<QueueType>();
            Assert.IsNotNull(list);
            Assert.IsTrue(list.Any(), "the router ships built-in queue types");
        }

        [TestMethod]
        public void BuiltInPcqQueueTypeReadsItsKindScopedFields()
        {
            EnsureCommandAvailable("/queue/type");

            // 'pcq-download-default' is a factory row on every RouterOS: kind=pcq, pcq-classifier=dst-address,
            // pcq-limit=50, pcq-total-limit=2000. Reading it exercises the pane fields without changing
            // anything on the router.
            var pcq = Connection.LoadAll<QueueType>()
                .FirstOrDefault(q => q.Name == "pcq-download-default");
            if (pcq == null)
                Assert.Inconclusive("the built-in 'pcq-download-default' queue type is not on this router");

            Assert.AreEqual("pcq", pcq.Kind);
            Assert.AreEqual("dst-address", pcq.PcqClassifier, "pcq-classifier is a field of the pcq pane");
            Assert.AreEqual(50, pcq.PcqLimit, "pcq-limit is WinBox's 'Queue Size' inside the pcq pane");
            Assert.AreEqual(2000, pcq.PcqTotalLimit);
        }

        [TestMethod]
        public void BuiltInRedQueueTypeReadsItsKindScopedFields()
        {
            EnsureCommandAvailable("/queue/type");

            // A second kind on the SAME window, so a transport that merged the panes into one field map
            // cannot satisfy both: red's thresholds and pcq's classifier come from different panes.
            var red = Connection.LoadAll<QueueType>()
                .FirstOrDefault(q => q.Name == "synchronous-default");
            if (red == null)
                Assert.Inconclusive("the built-in 'synchronous-default' queue type is not on this router");

            Assert.AreEqual("red", red.Kind);
            Assert.AreEqual(60, red.RedLimit, "red-limit is WinBox's 'RED Queue Size'");
            Assert.AreEqual(10, red.RedMinThreshold);
            Assert.AreEqual(50, red.RedMaxThreshold);
            Assert.AreEqual(1000, red.RedAvgPacket, "red-avg-packet is WinBox's 'Avg. Packet Size'");
        }

        [TestMethod]
        public void AddPcqQueueTypeWillNotFail()
        {
            EnsureCommandAvailable("/queue/type");

            var entity = new QueueType
            {
                Name = "t4ntest" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Kind = "pcq",
                PcqRate = 1000000,
                PcqLimit = 100,
                PcqClassifier = "src-address",
            };
            SaveTracked(entity);

            var loaded = Connection.LoadById<QueueType>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("pcq", loaded.Kind);
            Assert.AreEqual(1000000, loaded.PcqRate, "the write must reach pcq-rate, not the pane's plain 'Rate'");
            Assert.AreEqual(100, loaded.PcqLimit);
            Assert.AreEqual("src-address", loaded.PcqClassifier);

            Connection.Delete(loaded);
        }
    }
}
