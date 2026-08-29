// QueueRatePairTest.cs — a paired rate field must mean the same thing on every transport.
//
// /queue/simple max-limit reads 1000000/2000000 over the binary API and 1M/2M over the CLI transports,
// which read `print as-value`. While the property was a string that difference reached the caller: the
// same queue compared unequal to itself depending on which transport had loaded it. The single-valued
// form of the same field on /queue/tree reads 1000000 on both — it is the pairing that changes the
// spelling, not the magnitude, which is why only the paired fields are typed.
//
// The queue is created and removed over a SIDE API CONNECTION, and only READ over the transport under
// test: reading is what this test is about, and a fixture built over the transport under test would fold
// two questions into one result. (Writing it there works — see QueueTargetTest, which creates the same
// kind of queue, target and all, over whichever transport is running.)
//
// WinboxNative has no `max-limit` of its own — the M2 model keeps `upload-max-limit` and
// `download-max-limit` as two scalars — so the resolver composes the API field out of both halves, in
// both directions. Writing it there is two M2 keys going out for one API field.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests.Queue
{
    [TestClass]
    public class QueueRatePairTest : TestBase
    {
        private const string QueueName = "tik4net-test-rate-pair";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateTestQueue()
        {
            using (var api = OpenSideApi())
            {
                RemoveTestQueue(api);
                api.Save(new QueueSimple
                {
                    Name = QueueName,
                    Target = "192.168.253.0/24",   // a subnet nothing in this lab uses
                    MaxLimit = "1M/2M",
                    LimitAt = "500k/1M",
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
            foreach (var q in conn.LoadList<QueueSimple>().Where(q => q.Name == QueueName).ToList())
                conn.Delete(q);
        }

        /// <summary>
        /// Reads the queue over the transport under test and checks the VALUES — not the spelling, which
        /// is the transport's business and not the caller's.
        /// </summary>
        [TestMethod]
        public void APairedRateMeansTheSameOnEveryTransport()
        {
            var loaded = Connection.LoadList<QueueSimple>().Single(q => q.Name == QueueName);

            Assert.AreEqual(1000000L, loaded.MaxLimit.Value.Upload.Value, "max-limit upload");
            Assert.AreEqual(2000000L, loaded.MaxLimit.Value.Download.Value, "max-limit download");
            Assert.AreEqual(500000L, loaded.LimitAt.Value.Upload.Value, "limit-at upload");
            Assert.AreEqual(1000000L, loaded.LimitAt.Value.Download.Value, "limit-at download");

            // The two spellings compare equal, which is the whole point: this assertion is the one that
            // failed on exactly the transports the string-typed property used to be wrong on.
            Assert.AreEqual(TikRatePair.Parse("1M/2M"), loaded.MaxLimit.Value, "max-limit");
            Assert.AreEqual(TikRatePair.Parse("1000000/2000000"), loaded.MaxLimit.Value, "max-limit, plain spelling");
        }

        /// <summary>
        /// Writes the field over the transport under test and reads it back, so the composite is exercised
        /// in both directions. Over WinboxNative this is two M2 keys going out for one API field.
        /// </summary>
        [TestMethod]
        public void APairedRateCanBeWrittenOverTheTransportUnderTest()
        {
            var loaded = Connection.LoadList<QueueSimple>().Single(q => q.Name == QueueName);
            loaded.MaxLimit = "3M/4M";
            Connection.Save(loaded);

            var reloaded = Connection.LoadList<QueueSimple>().Single(q => q.Name == QueueName);
            Assert.AreEqual(3000000L, reloaded.MaxLimit.Value.Upload.Value, "upload after write");
            Assert.AreEqual(4000000L, reloaded.MaxLimit.Value.Download.Value, "download after write");

            // And the API agrees — the write went to the router, not just to our own decode.
            using (var api = OpenSideApi())
            {
                var viaApi = api.LoadList<QueueSimple>().Single(q => q.Name == QueueName);
                Assert.AreEqual(TikRatePair.Parse("3M/4M"), viaApi.MaxLimit.Value, "as the API reads it");
            }
        }

        /// <summary>
        /// The menu's read-only statistics reach the mapper on every transport — including the ones whose
        /// ordinary read does not carry them.
        /// </summary>
        /// <remarks>
        /// <c>print detail as-value</c> has no statistics block at all, so a plain CLI read returns the
        /// configuration fields and nothing else. The mapper does not stop there: <c>/queue/simple</c> is
        /// declared <c>IncludeCliStats</c>, so the CLI issues a second <c>print stats</c> query and merges it
        /// by <c>.id</c>. This is the test for that merge, and it is worth having because the two transports
        /// then disagree about the spelling — measured on RouterOS 7.24, <c>rate</c> is <c>0/0</c> over the
        /// API and <c>0bps/0bps</c> over the CLI, which is why the field is still a string and sits on the
        /// backlog in <c>EntityRatePairConventionTests</c>: <c>bps</c> is not a suffix
        /// <see cref="TikDataRate"/> reads.
        /// </remarks>
        [TestMethod]
        public void TheReadOnlyStatisticsArriveOnEveryTransport()
        {
            var loaded = Connection.LoadList<QueueSimple>().Single(q => q.Name == QueueName);

            Assert.IsTrue(loaded.Rate.HasValue,
                "rate was not reported — on a CLI transport that means the IncludeCliStats merge did not "
                + "happen, since 'print detail as-value' alone never carries the statistics block; on native "
                + "WinBox it means the two halves of the pair were not joined into the API's field");
            Assert.IsTrue(loaded.PacketRate.HasValue, "packet-rate was not reported");

            // The whole statistics block, not just the two paired rates: native WinBox reported none of it
            // until the Statistics tab's tuples were paired, and a check that stopped at 'rate' would have
            // passed with six of the nine still missing.
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Bytes), "bytes was not reported");
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Packets), "packets was not reported");
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Dropped), "dropped was not reported");
            Assert.IsFalse(string.IsNullOrEmpty(loaded.QueuedBytes), "queued-bytes was not reported");
            Assert.IsFalse(string.IsNullOrEmpty(loaded.QueuedPackets), "queued-packets was not reported");

            // Nothing is routed through 192.168.253.0/24, so both sides are zero however they are spelled —
            // and THAT is the assertion the type exists for: the API writes rate=0/0 and the CLI writes
            // rate=0bps/0bps, and this compares equal on both because TikDataRate reads the unit. While the
            // property was a string this line could only be a StringAssert.StartsWith("0"), which passed
            // without ever comparing the two transports' answers to each other.
            Assert.AreEqual(TikRatePair.Parse("0/0"), loaded.Rate.Value, "an idle queue passes no traffic");
            Assert.AreEqual(TikRatePair.Parse("0bps/0bps"), loaded.Rate.Value, "the CLI's spelling of it");
            Assert.AreEqual(0L, loaded.Rate.Value.Upload.Value, "rate upload");
            Assert.AreEqual(0L, loaded.Rate.Value.Download.Value, "rate download");
            Assert.AreEqual(TikRatePair.Parse("0/0"), loaded.PacketRate.Value,
                "packet-rate is spelled the same on every transport");
            Assert.AreEqual("0/0", loaded.Packets, "packets");
            Assert.AreEqual("0/0", loaded.Dropped, "dropped");
            Assert.AreEqual("0/0", loaded.QueuedBytes, "queued-bytes");
            Assert.AreEqual("0/0", loaded.QueuedPackets, "queued-packets");

            // The 'Total Statistics' tab is single-valued, and WinBox spells two of its fields 'Avg.' where
            // the API does not — so these are the ones a rename, not a pairing, has to get right.
            Assert.AreEqual(0L, loaded.TotalRate, "total-rate");
            Assert.AreEqual(0L, loaded.TotalPacketRate, "total-packet-rate");
            Assert.AreEqual(0L, loaded.TotalBytes, "total-bytes");
            Assert.AreEqual(0L, loaded.TotalPackets, "total-packets");
        }

        /// <summary>
        /// A queue at its written value must not look modified. With the spellings unreconciled, a value
        /// loaded over the CLI ("1M/2M") differed from the same value written from code
        /// ("1000000/2000000"), so change tracking saw a change that was not there.
        /// </summary>
        [TestMethod]
        public void TheTwoSpellingsAreTheSameValueToChangeTracking()
        {
            var loaded = Connection.LoadList<QueueSimple>().Single(q => q.Name == QueueName);
            loaded.MaxLimit = "1000000/2000000";   // the same rates, the API's spelling

            Assert.AreEqual(TikRatePair.Parse("1M/2M"), loaded.MaxLimit.Value);
        }
    }
}
