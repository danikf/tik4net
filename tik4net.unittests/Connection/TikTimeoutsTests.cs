using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// What <see cref="ITikConnection.ConnectTimeout"/> ≤ 0 means: no bound of its own. The TCP connect is the OS's, and
    /// every later step of the open that nothing else bounds waits for <see cref="ITikConnection.ReceiveTimeout"/>.
    /// </summary>
    [TestClass]
    public class TikTimeoutsTests
    {
        [DataTestMethod]
        [DataRow(15000, 15000)]
        [DataRow(1, 1)]
        [DataRow(0, Timeout.Infinite)]
        [DataRow(-5, Timeout.Infinite)]
        [DataRow(Timeout.Infinite, Timeout.Infinite)]
        public void ConnectBound(int connectMs, int expected)
            => Assert.AreEqual(expected, TikTimeouts.ConnectBoundMs(connectMs));

        [DataTestMethod]
        [DataRow(15000, 30000, 15000)]   // ConnectTimeout set: it is the bound
        [DataRow(0, 30000, 30000)]       // not positive: ReceiveTimeout bounds the step
        [DataRow(-1, 30000, 30000)]
        [DataRow(0, 0, int.MaxValue)]    // neither positive: nothing does (a value a deadline loop can compare against)
        [DataRow(-1, -1, int.MaxValue)]
        public void HandshakeBound(int connectMs, int receiveMs, int expected)
            => Assert.AreEqual(expected, TikTimeouts.HandshakeBoundMs(connectMs, receiveMs));
    }
}
