using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Objects.Tool.Romon;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Time on the public surface is a <see cref="TimeSpan"/>, read one way everywhere: a wait budget
    /// (<c>CancelAndJoin</c>) is positive or <see cref="Timeout.InfiniteTimeSpan"/>, a read duration
    /// (<c>ExecuteListWithDuration</c>, <c>ExecuteListUntilDone</c>) clamps a negative value to zero.
    /// </summary>
    [TestClass]
    public class TimeSpanParameterTests
    {
        [TestMethod]
        public void AWaitBudgetIsMillisecondsRoundedUp()
        {
            Assert.AreEqual(1500, TikTimeSpans.ToWaitMilliseconds(TimeSpan.FromSeconds(1.5), "t"));
            Assert.AreEqual(1, TikTimeSpans.ToWaitMilliseconds(TimeSpan.FromTicks(1), "t"),
                "a positive budget never becomes zero, which a Join reads as 'do not wait'");
            Assert.AreEqual(Timeout.Infinite, TikTimeSpans.ToWaitMilliseconds(Timeout.InfiniteTimeSpan, "t"));
            Assert.AreEqual(int.MaxValue - 1, TikTimeSpans.ToWaitMilliseconds(TimeSpan.FromDays(100), "t"));
        }

        [DataTestMethod]
        [DataRow(0L, DisplayName = "zero")]
        [DataRow(-20000L, DisplayName = "negative, not infinite (-1 ms IS Timeout.InfiniteTimeSpan)")]
        public void AWaitBudgetThatIsNotPositiveIsRefused(long ticks)
        {
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => TikTimeSpans.ToWaitMilliseconds(TimeSpan.FromTicks(ticks), "timeout"));
            Assert.AreEqual("timeout", ex.ParamName);
        }

        [TestMethod]
        public void AReadDurationClampsANegativeValueToZero()
        {
            Assert.AreEqual(TimeSpan.Zero, TikTimeSpans.ToDuration(TimeSpan.FromSeconds(-3)));
            Assert.AreEqual(TimeSpan.FromSeconds(3), TikTimeSpans.ToDuration(TimeSpan.FromSeconds(3)));
            Assert.AreEqual(TimeSpan.FromMilliseconds(int.MaxValue - 1), TikTimeSpans.ToDuration(TimeSpan.MaxValue),
                "ManualResetEventSlim.Wait refuses a span over int.MaxValue ms");
        }

        [TestMethod]
        public void TheFakeCommandHoldsTheSameBudgetContract()
        {
            var command = new TikFakeConnection().CreateCommand("/interface/monitor-traffic");

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => command.CancelAndJoin(TimeSpan.Zero));
            Assert.IsTrue(command.CancelAndJoin(Timeout.InfiniteTimeSpan), "nothing running: joins at once");
            Assert.IsTrue(command.CancelAndJoin(TimeSpan.FromSeconds(1)));
        }

        [TestMethod]
        public void ARomonDiscoverShorterThanTwoSecondsIsRefusedBeforeAnythingIsSent()
        {
            var connection = new TikFakeConnection();

            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => connection.RomonDiscover(TimeSpan.FromSeconds(1.5)).ToList());
            Assert.AreEqual("duration", ex.ParamName);
        }
    }
}
