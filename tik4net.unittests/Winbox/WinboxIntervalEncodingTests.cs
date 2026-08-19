using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// G4: the write side of a <c>.jg</c> <c>interval</c> field. The wire carries a COUNT of 1/scale-second
    /// units while the API spells the same value "5m" / "1w" / "500ms", so an interval needs an encoder as
    /// much as it needs the decoder it already had. Without one "5m" failed <c>long.TryParse</c> in the
    /// generic u32 branch and went out as the STRING "5m", which the router accepts, answers with status 0,
    /// and ignores — a write that reports success and does nothing.
    /// </summary>
    [TestClass]
    public class WinboxIntervalEncodingTests
    {
        private static long Parse(string value, int scale = 1)
        {
            Assert.IsTrue(WinboxFieldResolver.TryParseDuration(value, scale, out long ticks),
                $"'{value}' must parse as a RouterOS duration");
            return ticks;
        }

        [TestMethod]
        public void Duration_ParsesEveryUnitRouterOsPrints()
        {
            Assert.AreEqual(300L, Parse("5m"));
            Assert.AreEqual(604800L, Parse("1w"));
            Assert.AreEqual(86400L, Parse("1d"));
            Assert.AreEqual(3600L, Parse("1h"));
            Assert.AreEqual(1L, Parse("1s"));
            Assert.AreEqual(90L, Parse("1m30s"));
            Assert.AreEqual(694861L, Parse("1w1d1h1m1s"));
        }

        /// <summary>
        /// A bare number is SECONDS — what RouterOS means by one, and what makes a value read over the API
        /// safe to write straight back.
        /// </summary>
        [TestMethod]
        public void Duration_TreatsABareNumberAsSeconds()
        {
            Assert.AreEqual(300L, Parse("300"));
            Assert.AreEqual(0L, Parse("0"));
        }

        /// <summary>
        /// The scale is the whole reason this cannot be <c>long.Parse</c>: a <c>scale:100</c> field counts
        /// hundredths of a second, so 5m is 30000 on the wire and not 300. Sending the number through raw —
        /// what the generic u32 branch did — was already off by the scale factor on such a field.
        /// </summary>
        [TestMethod]
        public void Duration_AppliesTheFieldsScale()
        {
            Assert.AreEqual(30000L, Parse("5m", scale: 100));
            Assert.AreEqual(30000L, Parse("300", scale: 100));
            Assert.AreEqual(50L, Parse("500ms", scale: 100));
        }

        /// <summary>Milliseconds survive at scale 1000 and truncate below it, the way the decode renders them.</summary>
        [TestMethod]
        public void Duration_HandlesMilliseconds()
        {
            Assert.AreEqual(500L, Parse("500ms", scale: 1000));
            Assert.AreEqual(1500L, Parse("1s500ms", scale: 1000));
            Assert.AreEqual(0L, Parse("500ms"));   // scale 1 has no sub-second unit to put it in
        }

        /// <summary>
        /// The clock form is not exotic input: it is what <c>/system/scheduler</c> prints for
        /// <c>interval</c> and <c>/ip/hotspot/user</c> for <c>limit-uptime</c>, so an encoder that only knew
        /// "5m" would refuse the router's own spelling of the value.
        /// </summary>
        [TestMethod]
        public void Duration_ParsesTheClockForm()
        {
            Assert.AreEqual(300L, Parse("00:05:00"));
            Assert.AreEqual(3600L, Parse("1:00:00"));
            Assert.AreEqual(3661L, Parse("01:01:01"));
            Assert.AreEqual(86400L + 300L, Parse("1d 00:05:00"));
            Assert.AreEqual(86400L + 300L, Parse("1d00:05:00"));
            Assert.AreEqual(500L, Parse("00:00:00.500", scale: 1000));
        }

        /// <summary>
        /// A two-part clock is refused rather than guessed: "05:00" could as reasonably be five minutes as
        /// five hours, and the value is on its way to the router.
        /// </summary>
        [TestMethod]
        public void Duration_RefusesATwoPartClock()
            => Assert.IsFalse(WinboxFieldResolver.TryParseDuration("05:00", 1, out _));

        [TestMethod]
        public void Duration_ParsesASignedValue()
        {
            Assert.AreEqual(-300L, Parse("-5m"));
            Assert.AreEqual(300L, Parse("+5m"));
        }

        /// <summary>
        /// Anything that is not a duration must be REFUSED, not accepted with a guess. The caller gets an
        /// exception from the encoder; the alternative is the string-on-a-numeric-key write that looks like a
        /// success and changes nothing.
        /// </summary>
        [TestMethod]
        public void Duration_RefusesWhatIsNotOne()
        {
            foreach (string bad in new[] { "", "   ", "auto", "5x", "m", "5m3", "1w2q", "-", "5 m" })
                Assert.IsFalse(WinboxFieldResolver.TryParseDuration(bad, 1, out _),
                    $"'{bad}' must not be accepted as a duration");
        }
    }
}
