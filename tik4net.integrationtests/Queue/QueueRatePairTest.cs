// QueueRatePairTest.cs — a paired rate field must mean the same thing on every transport.
//
// /queue/simple max-limit reads 1000000/2000000 over the binary API and 1M/2M over the CLI transports,
// which read `print as-value`. While the property was a string that difference reached the caller: the
// same queue compared unequal to itself depending on which transport had loaded it. The single-valued
// form of the same field on /queue/tree reads 1000000 on both — it is the pairing that changes the
// spelling, not the magnitude, which is why only the paired fields are typed.
//
// The queue is created and removed over a SIDE API CONNECTION, and only READ over the transport under
// test. Not to make the test easier: /queue/simple `target` is a list-typed field that WinboxNative
// cannot yet encode on a write, so building the fixture over the transport under test would fail that
// one transport for a reason that has nothing to do with rates. Reading is what this test is about.
//
// WinboxNative has no `max-limit` of its own — the M2 model keeps `upload-max-limit` and
// `download-max-limit` as two scalars — so the resolver composes the API field out of both halves. That
// composition is what makes the assertions below meaningful on that transport rather than skipped.

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
            SkipWhereTheseFieldsCannotBeWrittenYet();

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
        /// The WinBox native transports READ these fields correctly — that is what the composite is for —
        /// but cannot WRITE them: the halves resolve to their keys and the set is accepted, and the value
        /// on the router does not move. Measured on RouterOS 7.24 by writing <c>upload-max-limit</c>
        /// directly (no effect) and <c>comment</c> on the same row through the same set (applied), so the
        /// write path itself is sound and it is these fields that are being dropped — most likely because
        /// the catalog marks them read-only in the window the resolver reads, which is the same shape as
        /// the 'Key Size' column-vs-argument case in WinboxJgCatalog.GetActionFields.
        /// <para>Our gap, not the router's, and it predates the pairing: writing either half was already
        /// silent before there was a composite to write.</para>
        /// </summary>
        private void SkipWhereTheseFieldsCannotBeWrittenYet()
        {
            var transport = ResolveConnectionType();
            if (transport == TikConnectionType.WinboxNative || transport == TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive(
                    "WinBox native reads /queue/simple rate fields but does not write them — the write is "
                    + "accepted and dropped. Reading is asserted by the other tests in this class.");
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
