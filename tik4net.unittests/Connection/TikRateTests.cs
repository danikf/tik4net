using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Objects.Queue;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikDataRate"/> and <see cref="TikRatePair"/> exist for the same reason
    /// <see cref="TikDuration"/> does: the router spells one value two ways depending on which transport
    /// asked. The pairs below are real — each is one field as the binary API reports it next to the same
    /// field as the CLI transports report it, taken from a live RouterOS 7.24.
    /// </summary>
    [TestClass]
    public class TikRateTests
    {
        [DataTestMethod]
        [DataRow("1000000/2000000", "1M/2M")]        // /queue/simple max-limit
        [DataRow("4000000/8000000", "4M/8M")]        // burst-limit
        [DataRow("500000/1000000", "500k/1M")]       // burst-threshold
        [DataRow("1000/2000", "1k/2k")]              // limit-at
        [DataRow("1000000/0", "1M/0")]               // one side given: upload, download zero
        public void TheTwoFormsTheRouterWritesAreTheSameValue(string plain, string suffixed)
        {
            Assert.AreEqual(TikRatePair.Parse(plain), TikRatePair.Parse(suffixed),
                plain + " and " + suffixed + " are the same pair");
        }

        [TestMethod]
        public void TheSuffixesAreDecimalNotBinary()
        {
            // Measured: limit-at=500k reads back as 500000, not 512000. Reading them as powers of two
            // would be off by 2.4% at k and 4.9% at M — wrong in a way that still looks plausible.
            Assert.AreEqual(500000L, TikDataRate.Parse("500k").Value);
            Assert.AreEqual(1000000L, TikDataRate.Parse("1M").Value);
            Assert.AreEqual(2000000000L, TikDataRate.Parse("2G").Value);
            Assert.AreEqual(1000000L, TikDataRate.Parse("1000000").Value);
        }

        [TestMethod]
        public void OneSideMeansUploadAndNotBothSides()
        {
            // The router's own reading: max-limit=1M becomes 1000000/0. Taking it as "the same on both"
            // would hand the caller a download limit they never asked for.
            var pair = TikRatePair.Parse("1M");
            Assert.AreEqual(1000000L, pair.Upload.Value);
            Assert.AreEqual(0L, pair.Download.Value);
        }

        [TestMethod]
        public void SomethingThatIsNotARateIsRefused()
        {
            foreach (var text in new[] { "none", "1M5", "fast", "1//2", "M" })
                Assert.IsFalse(TikDataRate.TryParse(text, out _), text);
        }

        [TestMethod]
        public void ToStringWritesTheFormEveryTransportAccepts()
        {
            Assert.AreEqual("1000000/2000000", TikRatePair.Parse("1M/2M").ToString());
            Assert.AreEqual("1000000", TikDataRate.Parse("1M").ToString());
        }

        /// <summary>
        /// The point of the implicit conversions: code written against the old string-typed properties
        /// still compiles. This test is mostly a compile-time assertion — if the conversions are missing
        /// or ambiguous, the file does not build.
        /// </summary>
        [TestMethod]
        public void CodeWrittenAgainstTheOldStringPropertiesStillCompiles()
        {
            var queue = new QueueSimple
            {
                Name = "shaper",
                MaxLimit = "1M/2M",
                LimitAt = "500k/1M",
                BurstLimit = "4M/8M",
                BurstThreshold = 0,
            };

            Assert.AreEqual(1000000L, queue.MaxLimit!.Value.Upload.Value);
            Assert.AreEqual(2000000L, queue.MaxLimit!.Value.Download.Value);
            Assert.AreEqual("500000/1000000", queue.LimitAt.ToString());

            var sniffer = new tik4net.Objects.Interface.Wireless.WirelessSniffer
            {
                ChannelTime = "200ms",
            };
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), sniffer.ChannelTime!.Value.Value);

            // The clock spelling the CLI transports report assigns just as well.
            sniffer.ChannelTime = "00:00:00.500";
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), sniffer.ChannelTime!.Value.Value);

            // And so does a word, which is why the property is not a TimeSpan.
            var entry = new tik4net.Objects.Ip.Firewall.FirewallAddressList { Timeout = "none" };
            Assert.AreEqual("none", entry.Timeout!.Value.Token);
        }

        [TestMethod]
        public void ANumberAssignsAsWell()
        {
            var rate = TikDataRate.FromValue(1000);
            long asNumber = rate;
            string asText = rate;

            Assert.AreEqual(1000L, asNumber);
            Assert.AreEqual("1000", asText);
        }
    }
}
