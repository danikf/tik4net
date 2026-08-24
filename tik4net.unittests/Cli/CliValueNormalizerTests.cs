// CliValueNormalizerTests.cs — router-free tests for the as-value → API duration spelling.
//
// A CLI read is `:put [/path print as-value]`, which spells a duration the way the router stores it
// (00:00:15) rather than the way the API prints it (15s). The audit against Telnet found 21 value
// differences across four classes; durations were the largest, and the only one a reader can identify
// from the value alone.
//
// Most of these tests are about what must NOT be touched. A duration is recognised by its shape, so the
// risk is not a missed conversion — it is a timestamp, an address or an identifier rewritten into
// nonsense because it happened to contain digits and colons.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    [TestClass]
    public class CliValueNormalizerTests
    {
        private static string N(string field, string value) => CliValueNormalizer.Normalize(field, value);

        [TestMethod]
        public void SpellsTheClockPartInUnits()
        {
            Assert.AreEqual("15s", N("doh-timeout", "00:00:15"));
            Assert.AreEqual("5m", N("group-key-update", "00:05:00"));
            Assert.AreEqual("2s", N("query-server-timeout", "00:00:02"));
            Assert.AreEqual("40s", N("dead-interval", "00:00:40"));
        }

        /// <summary>Days and weeks ride in front of the clock, and the API keeps them as days and weeks
        /// rather than folding them into hours.</summary>
        [TestMethod]
        public void KeepsWeeksAndDays()
        {
            Assert.AreEqual("1w", N("cache-max-ttl", "1w00:00:00"));
            Assert.AreEqual("1d", N("ttl", "1d00:00:00"));
            Assert.AreEqual("1w2d3h4m5s", N("ttl", "1w2d03:04:05"));
        }

        /// <summary>A fraction of a second is milliseconds, and '.1' is a tenth — padded on the right.</summary>
        [TestMethod]
        public void ReadsTheFractionAsMilliseconds()
        {
            Assert.AreEqual("100ms", N("arp-interval", "00:00:00.100"));
            Assert.AreEqual("100ms", N("mii-interval", "00:00:00.1"));
            Assert.AreEqual("1s500ms", N("mii-interval", "00:00:01.5"));
        }

        /// <summary>
        /// Zero has no unit in as-value, and the API's depends on the field's resolution — <c>0s</c> for
        /// most, <c>0ms</c> for a millisecond field. The common case is what is emitted; the alternative
        /// is the same duration in another unit, not another duration.
        /// </summary>
        [TestMethod]
        public void ZeroIsSpelledInSeconds()
        {
            Assert.AreEqual("0s", N("interim-update", "00:00:00"));
        }

        /// <summary>The two fields whose HH:MM:SS is a time of day. Converting them would turn the clock
        /// into an elapsed time and nothing downstream could tell.</summary>
        [TestMethod]
        public void LeavesAClockTimeAlone()
        {
            Assert.AreEqual("00:27:36", N("time", "00:27:36"));
            Assert.AreEqual("00:00:00", N("start-time", "00:00:00"));
        }

        /// <summary>
        /// The values that look duration-ish and are not. A loose match would rewrite a lease's timestamp
        /// or an address into a plausible-looking duration, which reads as data rather than as an error.
        /// </summary>
        [TestMethod]
        public void LeavesEverythingElseAlone()
        {
            foreach (string v in new[]
            {
                "2026-08-24 00:27:30",      // a timestamp
                "aug/24/2026 00:27:30",     // RouterOS's other timestamp spelling
                "::ffff:192.168.4.236",     // an address
                "00:15:5D:04:1F:03",        // a MAC
                "192.168.4.236",
                "15s",                      // already an API duration
                "1w",
                "0",
                "",
                "ether1",
                "0:0:0",                    // not two digits per component
                "00:00:15x",                // trailing rubbish
                "00:00:00.1234",            // more than millisecond resolution
            })
            {
                Assert.AreEqual(v, N("some-field", v), "must pass through unchanged: '" + v + "'");
            }
        }

        /// <summary>A null value survives — a field can be present and empty.</summary>
        [TestMethod]
        public void LeavesNullAlone()
        {
            Assert.IsNull(CliValueNormalizer.Normalize("x", null));
        }
    }
}
