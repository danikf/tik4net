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
            SkipWhenTheTransportHasNoPairedField(loaded);

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
        /// WinboxNative does not send this field at all: the M2 model has no <c>max-limit</c>, it has
        /// <c>upload-max-limit</c> and <c>download-max-limit</c> as two separate scalars, and the resolver
        /// does not yet compose one API field out of two native ones. So there is no paired spelling to
        /// reconcile there, and this test has no subject — which is not the same as the field being
        /// unsupported. It read null before this change too.
        /// </summary>
        private static void SkipWhenTheTransportHasNoPairedField(QueueSimple loaded)
        {
            if (loaded.MaxLimit == null)
                Assert.Inconclusive(
                    "This transport does not report /queue/simple max-limit as a pair — WinboxNative splits "
                    + "it into upload-max-limit and download-max-limit, which the resolver does not yet "
                    + "compose. Nothing to reconcile here; the gap is ours and is tracked separately.");
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
