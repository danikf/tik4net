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
                "::ffff:not.an.address",    // ffff-shaped but not a mapped IPv4
                "::ffff:192.168.4.999",     // nor is this
                "2001:db8::1",              // a real IPv6
                "AA:BB:CC:DD:EE:FF",        // a MAC
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

        /// <summary>
        /// The numeric sentinels the API prints as a word. Keyed by field name, because the value cannot
        /// carry it: the same <c>0</c> is <c>auto</c>, <c>none</c>, <c>disabled</c> or a plain zero
        /// depending only on which field it came from.
        /// </summary>
        [TestMethod]
        public void SpellsTheSentinelTheWayTheApiDoes()
        {
            Assert.AreEqual("auto", N("mtu", "0"));
            Assert.AreEqual("auto", N("ttl", "0"));
            Assert.AreEqual("none", N("horizon", "0"));
            Assert.AreEqual("disabled", N("mrru", "0"));
            Assert.AreEqual("unlimited", N("max-sessions", "0"));
            Assert.AreEqual("inherit", N("dscp", "256"));
        }

        /// <summary>
        /// Only the sentinel number maps, and only on its own field. Each of these was read back from the
        /// router as the plain number over BOTH transports.
        /// </summary>
        [TestMethod]
        public void LeavesRealValuesOfSentinelFieldsAlone()
        {
            Assert.AreEqual("1400", N("mtu", "1400"));
            Assert.AreEqual("5", N("horizon", "5"));
            Assert.AreEqual("64", N("ttl", "64"));
            Assert.AreEqual("1600", N("mrru", "1600"));
            Assert.AreEqual("10", N("max-sessions", "10"));
            // dscp=0 is a real DSCP class; its sentinel is 256, outside the 0..63 range. Mapping the zero
            // here — as every other field in the table does — would have silently corrupted it.
            Assert.AreEqual("0", N("dscp", "0"));
            Assert.AreEqual("63", N("dscp", "63"));
            // The same 0 on a field that is not in the table.
            Assert.AreEqual("0", N("priority", "0"));
            Assert.AreEqual("0", N("vni", "0"));
        }

        /// <summary>A scale, not a sentinel: every value of these fields is a thousand times the API's.</summary>
        [TestMethod]
        public void ScalesTheThousandthsFields()
        {
            Assert.AreEqual("5", N("bucket-size", "5000"));
            Assert.AreEqual("0.1", N("bucket-size", "100"));
            Assert.AreEqual("10", N("bucket-size", "10000"));
            Assert.AreEqual("0", N("bucket-size", "0"));
            Assert.AreEqual("-47.516", N("freq-drift", "-47516"));
            Assert.AreEqual("-0.001", N("freq-drift", "-1"));
            // Not scaled on a field that is not in the table.
            Assert.AreEqual("5000", N("burst-limit", "5000"));
        }

        /// <summary>Seconds east of UTC, which the API prints as a signed clock offset.</summary>
        [TestMethod]
        public void RendersGmtOffsetAsASignedClock()
        {
            Assert.AreEqual("+02:00", N("gmt-offset", "7200"));
            Assert.AreEqual("+00:00", N("gmt-offset", "0"));
            Assert.AreEqual("-05:30", N("gmt-offset", "-19800"));
            Assert.AreEqual("+05:45", N("gmt-offset", "20700"));
            // The same number on any other field is just a number.
            Assert.AreEqual("7200", N("port", "7200"));
        }

        /// <summary>
        /// An IPv4 sitting in an IPv6-shaped slot. Recognised by SHAPE like a duration, because
        /// <c>::ffff:</c> followed by a dotted quad cannot be anything else.
        /// </summary>
        [TestMethod]
        public void UnwrapsAnIpv4MappedAddress()
        {
            Assert.AreEqual("192.168.4.236", N("local", "::ffff:192.168.4.236"));
            Assert.AreEqual("0.0.0.0", N("address", "::FFFF:0.0.0.0"));
        }

        /// <summary>
        /// The zero of a millisecond-resolution duration. as-value gives <c>00:00:00</c> for both
        /// resolutions, so only the field name distinguishes them.
        /// </summary>
        [TestMethod]
        public void ZeroOfAMillisecondFieldIsSpelledInMilliseconds()
        {
            Assert.AreEqual("0ms", N("down-delay", "00:00:00"));
            Assert.AreEqual("0ms", N("up-delay", "00:00:00"));
            // A non-zero one needs no help — the fraction already carries the unit.
            Assert.AreEqual("500ms", N("down-delay", "00:00:00.5"));
            Assert.AreEqual("1s", N("down-delay", "00:00:01"));
            // And a second-resolution field keeps 0s.
            Assert.AreEqual("0s", N("interim-update", "00:00:00"));
        }

        private static string J(string field, string value)
            => CliValueNormalizer.Normalize(field, value, fromJson: true);

        /// <summary>
        /// <c>:serialize to=json</c> renders a duration as a DATE counted from the Unix epoch, where
        /// as-value renders it as <c>[Nw][Nd]HH:MM:SS</c>. Both have to come out in the API's spelling.
        /// </summary>
        [TestMethod]
        public void ReadsTheJsonEpochDateAsADuration()
        {
            Assert.AreEqual("1d", J("ttl", "1970-01-02 00:00:00"));
            Assert.AreEqual("52w1d", J("ttl", "1971-01-01 00:00:00"));
            Assert.AreEqual("15s", J("timeout", "1970-01-01 00:00:15"));
            Assert.AreEqual("0s", J("interval", "1970-01-01 00:00:00"));
            Assert.AreEqual("1w2d3h4m5s", J("ttl", "1970-01-10 03:04:05"));
        }

        /// <summary>
        /// The same shape is what a REAL timestamp has through the same serialiser
        /// (<c>last-link-up-time</c> reads <c>2026-08-25 00:27:44</c>), and nothing in the value separates
        /// them. So only fields measured to be durations are converted, and everything else is left as the
        /// router sent it — an unlisted duration shows up in the audit, an unlisted timestamp would have
        /// become a nonsense duration.
        /// </summary>
        [TestMethod]
        public void LeavesADateShapedValueAloneOnAnyOtherField()
        {
            Assert.AreEqual("2026-08-25 00:27:44", J("last-link-up-time", "2026-08-25 00:27:44"));
            Assert.AreEqual("1970-01-02 00:00:00", J("creation-time", "1970-01-02 00:00:00"));
            Assert.AreEqual("2026-08-24 01:32:43", J("creation-time", "2026-08-24 01:32:43"));
        }

        /// <summary>The epoch rewrite is a JSON-read rule; as-value never produces that shape.</summary>
        [TestMethod]
        public void DoesNotApplyTheEpochRuleToAnAsValueRead()
        {
            Assert.AreEqual("1970-01-02 00:00:00", N("ttl", "1970-01-02 00:00:00"));
            // and the as-value spelling of the same duration still works on both paths
            Assert.AreEqual("1d", N("ttl", "1d00:00:00"));
            Assert.AreEqual("1d", J("ttl", "1d00:00:00"));
        }

        /// <summary>A null value survives — a field can be present and empty.</summary>
        [TestMethod]
        public void LeavesNullAlone()
        {
            Assert.IsNull(CliValueNormalizer.Normalize("x", null));
        }
    }
}
