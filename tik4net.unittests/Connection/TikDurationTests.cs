using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikDuration"/> exists because the router writes the same duration two ways depending on
    /// which transport asked, so what is tested here is that both ways arrive as the same value — and that
    /// the words the router uses in place of a duration survive rather than becoming a zero.
    /// </summary>
    [TestClass]
    public class TikDurationTests
    {
        /// <summary>
        /// The pairs are real: each is one field as the API/REST/native-WinBox transports report it next to
        /// the same field as the CLI transports report it, taken from a live RouterOS 7.24.
        /// </summary>
        [DataTestMethod]
        [DataRow("200ms", "00:00:00.200")]      // /interface/wireless/sniffer channel-time
        [DataRow("10s", "00:00:10")]            // /ip/firewall/connection/tracking icmp-timeout
        [DataRow("5m", "00:05:00")]             // tcp-max-retrans-timeout
        [DataRow("3m", "00:03:00")]             // udp-stream-timeout
        [DataRow("10m", "00:10:00")]            // generic-timeout
        [DataRow("1d", "1d00:00:00")]           // tcp-established-timeout
        [DataRow("21h16m40s", "21:16:40")]      // /system/resource uptime
        public void TheTwoFormsTheRouterWritesAreTheSameValue(string compact, string clock)
        {
            Assert.AreEqual(TikDuration.Parse(compact), TikDuration.Parse(clock),
                compact + " and " + clock + " are the same duration");
        }

        [TestMethod]
        public void TheClockFormIsReadAtTheRightScale()
        {
            // ".200" is 200 milliseconds. Read as anything else — 200 ticks, 2/10 of a minute — the value
            // would still parse and still look plausible, which is why it is asserted rather than assumed.
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), TikDuration.Parse("00:00:00.200").Value);
            Assert.AreEqual(TimeSpan.FromSeconds(10), TikDuration.Parse("00:00:10").Value);
            Assert.AreEqual(TimeSpan.FromDays(1), TikDuration.Parse("1d00:00:00").Value);
            Assert.AreEqual(new TimeSpan(0, 21, 16, 40), TikDuration.Parse("21:16:40").Value);
            Assert.AreEqual(TimeSpan.FromDays(9), TikDuration.Parse("1w2d00:00:00").Value);
        }

        [TestMethod]
        public void TheCompactFormIsReadAtTheRightScale()
        {
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), TikDuration.Parse("200ms").Value);
            Assert.AreEqual(TimeSpan.FromMinutes(5), TikDuration.Parse("5m").Value);
            Assert.AreEqual(TimeSpan.FromDays(1), TikDuration.Parse("1d").Value);
            Assert.AreEqual(new TimeSpan(0, 21, 16, 40), TikDuration.Parse("21h16m40s").Value);
            Assert.AreEqual(TimeSpan.FromDays(9), TikDuration.Parse("1w2d").Value);

            // "1m" is one minute and "1ms" one millisecond — the minute suffix is a prefix of the
            // millisecond one, so the grammar has to prefer the longer match.
            Assert.AreEqual(TimeSpan.FromMinutes(1), TikDuration.Parse("1m").Value);
            Assert.AreEqual(TimeSpan.FromMilliseconds(1), TikDuration.Parse("1ms").Value);
        }

        [TestMethod]
        public void ABareNumberIsSeconds()
        {
            // Several entity defaults are written this way and the router accepts it on write.
            Assert.AreEqual(TimeSpan.FromSeconds(60), TikDuration.Parse("60").Value);
            Assert.AreEqual(TimeSpan.Zero, TikDuration.Parse("0").Value);
        }

        [TestMethod]
        public void TheRoutersWordsAreKeptRatherThanBecomingZero()
        {
            // The trap this type exists to avoid: "none" is a state of the field, not a duration of zero.
            // A TimeSpan-typed property cannot tell the two apart, and the old parser answered zero for
            // both — turning "this timeout is off" into "this timeout is instant".
            foreach (var word in new[] { "none", "disabled", "auto", "any", "always", "dynamic" })
            {
                var duration = TikDuration.Parse(word);
                Assert.IsFalse(duration.HasValue, word + " should not be a length of time");
                Assert.AreEqual(word, duration.Token, word);
                Assert.AreEqual(word, duration.ToString(), word);
            }
        }

        [TestMethod]
        public void SomethingThatIsNotADurationIsNotSilentlyZero()
        {
            // The previous parser matched an all-optional pattern with Match rather than against the whole
            // string, so it "succeeded" on the empty prefix of anything and reported TimeSpan.Zero.
            foreach (var text in new[] { "none", "banana", "10x", "1d2q", "::", "10s extra" })
                Assert.IsFalse(TikDuration.TryParseTimeSpan(text, out _), text);
        }

        [TestMethod]
        public void FromTikTimeToTimeSpanReadsBothFormsAndRefusesTheRest()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(10), TikTimeHelper.FromTikTimeToTimeSpan("10s"));
            Assert.AreEqual(TimeSpan.FromSeconds(10), TikTimeHelper.FromTikTimeToTimeSpan("00:00:10"));
            Assert.ThrowsException<FormatException>(() => TikTimeHelper.FromTikTimeToTimeSpan("none"));
        }

        [TestMethod]
        public void ToStringWritesTheFormTheRouterAcceptsEverywhere()
        {
            // The compact form is what RouterOS takes on write over every transport, so a value read over
            // the CLI must not be written back in the clock form it arrived in.
            Assert.AreEqual("200ms", TikDuration.Parse("00:00:00.200").ToString());
            Assert.AreEqual("10s", TikDuration.Parse("00:00:10").ToString());
            Assert.AreEqual("1d", TikDuration.Parse("1d00:00:00").ToString());
            Assert.AreEqual("21h16m40s", TikDuration.Parse("21:16:40").ToString());
        }

        [TestMethod]
        public void ZeroIsWrittenAsZeroAndNotAsNone()
        {
            // "no time at all" and "this field is off" are different states on the router; the old
            // seconds-based formatter rendered zero as "none" and collapsed them.
            Assert.AreEqual("0s", TikDuration.FromTimeSpan(TimeSpan.Zero).ToString());
            Assert.AreEqual("0s", TikDuration.Parse("00:00:00").ToString());
        }

        [TestMethod]
        public void ATimeSpanConvertsImplicitly()
        {
            TikDuration duration = TimeSpan.FromMinutes(5);
            Assert.AreEqual("5m", duration.ToString());
            Assert.AreEqual(TimeSpan.FromMinutes(5), (TimeSpan?)duration);
        }
    }
}
