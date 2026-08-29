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
            // The conversion to a number is EXPLICIT and nullable, because a rate field may hold a word:
            // 'unlimited' has no number, and an implicit long would have to invent one.
            long? asNumber = (long?)rate;
            string asText = rate;

            Assert.AreEqual(1000L, asNumber);
            Assert.AreEqual("1000", asText);
        }

        // ── the bps unit ──────────────────────────────────────────────────────
        //
        // A second divergence on top of the k/M/G one, and in the opposite direction: the CLI adds a unit
        // where the API writes a bare number. Both spellings below are measured on RouterOS 7.24 — a raw
        // ':put [/queue simple print stats as-value]' over Telnet answers rate=0bps/0bps where the API
        // answers rate=0/0, and ':put [/interface ethernet monitor ether1 once as-value]' answers
        // rate=1Gbps. Until the type read these, typing the field threw a FormatException that failed the
        // load of the whole entity, which is why /queue/simple's rate stayed a string.

        [TestMethod]
        public void TheBpsUnitIsRead()
        {
            Assert.AreEqual(0L, TikDataRate.Parse("0bps").Value);
            Assert.AreEqual(1000000000L, TikDataRate.Parse("1Gbps").Value);
            Assert.AreEqual(1000L, TikDataRate.Parse("1kbps").Value);
            Assert.AreEqual(1000000L, TikDataRate.Parse("1Mbps").Value);
            Assert.AreEqual(1500L, TikDataRate.Parse("1500bps").Value);
        }

        [TestMethod]
        public void TheUnitIsCaseInsensitiveLikeTheSuffix()
        {
            Assert.AreEqual(TikDataRate.Parse("1Gbps"), TikDataRate.Parse("1GBPS"));
            Assert.AreEqual(TikDataRate.Parse("1Mbps"), TikDataRate.Parse("1mbps"));
        }

        [TestMethod]
        public void TheThreeSpellingsOfOneRateAreOneValue()
        {
            // The whole point of the type, now across all three: the API's plain number, the CLI's
            // scaled suffix and the display unit are the same rate and compare equal.
            Assert.AreEqual(TikDataRate.Parse("1000000"), TikDataRate.Parse("1M"));
            Assert.AreEqual(TikDataRate.Parse("1000000"), TikDataRate.Parse("1Mbps"));
            Assert.AreEqual("1000000", TikDataRate.Parse("1Mbps").ToString());
        }

        [TestMethod]
        public void AFractionIsScaledButOnlyWithAMultiplier()
        {
            // RouterOS renders a scaled rate the way it renders any scaled number, so a fraction has to
            // read as the rate it names.
            Assert.AreEqual(1500000L, TikDataRate.Parse("1.5Mbps").Value);
            Assert.AreEqual(1500L, TikDataRate.Parse("1.5k").Value);

            // But a bare fraction is NOT a rate — /queue/simple's bucket-size is '0.1/0.1', and reading it
            // as the 0 it rounds to would be worse than refusing it.
            Assert.IsFalse(TikDataRate.TryParse("0.1", out _));
            Assert.IsFalse(TikDataRate.Parse("0.1").HasValue);
            Assert.AreEqual("0.1", TikDataRate.Parse("0.1").Token);
        }

        // ── words ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void AWordIsKeptRatherThanRefused()
        {
            // /interface/ethernet bandwidth defaults to 'unlimited/unlimited'. A rate field holding a word
            // is the same situation TikDuration has with 'none', and it is handled the same way: the value
            // survives, HasValue says it is not a number, and ToString writes the word back unchanged.
            var rate = TikDataRate.Parse("unlimited");

            Assert.IsFalse(rate.HasValue);
            Assert.IsNull(rate.Value);
            Assert.AreEqual("unlimited", rate.Token);
            Assert.AreEqual("unlimited", rate.ToString());
        }

        [TestMethod]
        public void APairOfWordsIsAPair()
        {
            var pair = TikRatePair.Parse("unlimited/unlimited");

            Assert.AreEqual("unlimited", pair.Upload.Token);
            Assert.AreEqual("unlimited", pair.Download.Token);
            Assert.AreEqual("unlimited/unlimited", pair.ToString());
        }

        [TestMethod]
        public void TryParseStaysStrictSoTheEncoderCanTellANumberFromAWord()
        {
            // WinboxFieldResolver picks the M2 encoding from this answer: a number goes out as a u64 and a
            // word as a string. If TryParse accepted 'unlimited' as a token, the encoder would put a
            // 64-bit integer on the wire for it, which the router accepts and then ignores.
            Assert.IsFalse(TikDataRate.TryParse("unlimited", out _));
            Assert.IsFalse(TikDataRate.TryParse("auto", out _));
            Assert.IsTrue(TikDataRate.TryParse("1Gbps", out _));
        }

        [TestMethod]
        public void AWordIsTheSameWordWhateverItsCase()
        {
            Assert.AreEqual(TikDataRate.Parse("unlimited"), TikDataRate.Parse("UNLIMITED"));
            Assert.AreNotEqual(TikDataRate.Parse("unlimited"), TikDataRate.Parse("auto"));

            // And a word is never equal to a number, however the number is spelled.
            Assert.AreNotEqual(TikDataRate.Parse("unlimited"), TikDataRate.Parse("0"));
        }

        /// <summary>The rate half of the same rule — see TikDurationTests for why the two states differ.</summary>
        [TestMethod]
        public void AKnownWordIsSpecialAndAnythingElseIsUnknown()
        {
            Assert.AreEqual(TikValueKind.Value, TikDataRate.Parse("1M").Kind);
            Assert.AreEqual(TikValueKind.Value, TikDataRate.Parse("1Gbps").Kind);

            TikDataRate unlimited = TikDataRate.Parse("unlimited");
            Assert.AreEqual(TikValueKind.Special, unlimited.Kind);
            Assert.AreEqual(TikDataRateSpecial.Unlimited, unlimited.Special);

            TikDataRate gap = TikDataRate.Parse("made-up-rate");
            Assert.AreEqual(TikValueKind.Unknown, gap.Kind);
            Assert.IsNull(gap.Special);
            Assert.AreEqual("made-up-rate", gap.ToString());

            Assert.AreEqual("unlimited", TikDataRate.FromSpecial(TikDataRateSpecial.Unlimited).ToString());
        }

        /// <summary>
        /// A bare NUMBER is the upload side with download zero — measured, writing max-limit=1M reads back
        /// 1000000/0. A bare WORD is not: it describes the whole field, and pairing it with a zero download
        /// wrote back 'unlimited/0', a different configuration from the one the router reported.
        /// </summary>
        [TestMethod]
        public void ABareWordAppliesToBothSidesOfAPair()
        {
            TikRatePair number = TikRatePair.Parse("1M");
            Assert.AreEqual(1000000L, number.Upload.Value);
            Assert.AreEqual(0L, number.Download.Value);
            Assert.AreEqual("1000000/0", number.ToString());

            TikRatePair word = TikRatePair.Parse("unlimited");
            Assert.AreEqual(TikDataRateSpecial.Unlimited, word.Upload.Special);
            Assert.AreEqual(TikDataRateSpecial.Unlimited, word.Download.Special);
            Assert.AreEqual("unlimited/unlimited", word.ToString());

            // A mixed pair still keeps each half as the router wrote it.
            TikRatePair mixed = TikRatePair.Parse("1M/unlimited");
            Assert.AreEqual(1000000L, mixed.Upload.Value);
            Assert.AreEqual(TikDataRateSpecial.Unlimited, mixed.Download.Special);
        }
    }
}
