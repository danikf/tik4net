#nullable enable
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// What <see cref="TikDurationText"/> writes for the same lengths through both of its callers: <see cref="TikDuration"/>'s
    /// <c>ToString</c> from a <see cref="TimeSpan"/>, and WinBox native's renderer from wire ticks and a scale.
    /// </summary>
    [TestClass]
    public class DurationTextTests
    {
        // ── TikDuration, from a TimeSpan ──────────────────────────────────────

        [DataTestMethod]
        [DataRow(0L, "0s")]
        [DataRow(10_000_000L, "1s")]
        [DataRow(3_000_000_000L, "5m")]
        [DataRow(6_048_000_000_000L, "1w")]
        [DataRow(6_048_000_000_000L + 864_000_000_000L + 36_000_000_000L + 600_000_000L + 10_000_000L + 10_000L + 10L, "1w1d1h1m1s1ms1us")]
        [DataRow(10_520L, "1ms52us")]
        [DataRow(9L, "0s")]
        [DataRow(-10_000_000L, "-1s")]
        [DataRow(-9L, "-0s")]
        [DataRow(3_134_409_420_000_000L, "518w1d18h55m42s")]
        public void TikDuration_WritesTheNonZeroUnitsLargestFirst(long ticks, string expected)
        {
            Assert.AreEqual(expected, TikDuration.FromTimeSpan(TimeSpan.FromTicks(ticks)).ToString());
        }

        // ── WinBox native, from wire ticks ────────────────────────────────────

        [DataTestMethod]
        [DataRow(0L, 1, "s", "0s")]
        [DataRow(0L, 1000, "ms", "0ms")]
        [DataRow(0L, 1000000, "us", "0us")]
        [DataRow(604800L, 1, "s", "1w")]
        [DataRow(300L, 1, "s", "5m")]
        [DataRow(236103L, 1, "s", "2d17h35m3s")]
        [DataRow(313440942L, 1, "s", "518w1d18h55m42s")]
        [DataRow(150L, 100, "s", "1s500ms")]
        [DataRow(1L, 100, "s", "10ms")]
        [DataRow(105488L, 1000000, "s", "105ms488us")]
        [DataRow(1500L, 1000, "s", "1s500ms")]
        [DataRow(1L, 3, "s", "333ms333us")]
        [DataRow(-5L, 1, "s", "-5s")]
        [DataRow(-0L, 1, "ms", "0ms")]
        [DataRow(5L, 0, "s", "5s")]
        [DataRow(long.MaxValue, 1, "s", "15250284452471w3d15h30m7s")]
        public void TheWinboxRenderer_WritesTheSameSpelling(long ticks, int scale, string zeroUnit, string expected)
        {
            Assert.AreEqual(expected, TikDurationText.Format(ticks, scale, zeroUnit));
        }
    }
}
