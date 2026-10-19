using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A WinBox connection the router closes before sending a single byte names the case RouterOS uses it for.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses a WinBox connection over its WinBox/Dude session limit (100 on 7.24, not <c>/ip service
    /// max-sessions</c>) by accepting it and resetting it at once, and logs <c>too many winbox/dude sessions, drop connect
    /// from &lt;client&gt;</c>. The client saw only "Connection closed unexpectedly", 20–100 times in a burst (RoMON relay
    /// churn). A local listener stands in.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a local listener answers within a receive timeout
    public class WinboxTcpRefusedConnectionTests
    {
        private static IOException ReadAfter(System.Action<Socket> router)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var transport = new WinboxTcpTransport())
                {
                    transport.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port, 2000, 3000);
                    using (Socket accepted = listener.AcceptSocket())
                        router(accepted);
                    try { transport.ReadExact(64); }
                    catch (IOException ex) { return ex; }
                    Assert.Fail("the read did not fail");
                    return null;
                }
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void AConnectionClosedBeforeAnyByte_NamesTheSessionLimit()
        {
            var ex = ReadAfter(s => s.Shutdown(SocketShutdown.Both));
            StringAssert.Contains(ex.Message, "before sending anything");
            StringAssert.Contains(ex.Message, "too many winbox/dude sessions");
            Assert.IsInstanceOfType(ex, typeof(WinboxSessionLimitException));
        }

        [TestMethod]
        public void AConnectionResetBeforeAnyByte_NamesTheSessionLimit()
        {
            var ex = ReadAfter(s => s.LingerState = new LingerOption(true, 0));   // the close that follows is a reset
            StringAssert.Contains(ex.Message, "before sending anything");
            StringAssert.Contains(ex.Message, "too many winbox/dude sessions");
        }

        [TestMethod]
        public void AConnectionClosedMidFrame_KeepsTheGenericMessage()
        {
            var ex = ReadAfter(s => { s.Send(new byte[] { 1, 2, 3 }); s.Shutdown(SocketShutdown.Both); });
            Assert.IsFalse(ex.Message.Contains("winbox/dude"), ex.Message);
            Assert.IsNotInstanceOfType(ex, typeof(WinboxSessionLimitException));
        }

        // The refusal reaches TikOpenRetry wrapped as the connection's login failure.
        private static System.Exception Refused() => new TikConnectionLoginException(new WinboxSessionLimitException(null));

        private static int RunCounting(System.Func<int, System.Exception> failure)
        {
            int saved = TikOpenRetry.SessionLimitDelayMs, calls = 0;
            TikOpenRetry.SessionLimitDelayMs = 1;
            try
            {
                TikOpenRetry.Run(() => { var f = failure(++calls); if (f != null) throw f; }, TikOpenRetry.IsTransientRefusal);
                return calls;
            }
            finally { TikOpenRetry.SessionLimitDelayMs = saved; }
        }

        [TestMethod]
        public void TheSessionLimitRefusal_IsRetried()
            => Assert.AreEqual(2, RunCounting(call => call == 1 ? Refused() : null));

        [TestMethod]
        public void TheSessionLimitRefusal_GivesUpAfterTheLastAttempt()
        {
            int calls = 0;
            Assert.ThrowsException<TikConnectionLoginException>(() => RunCounting(call => { calls = call; return Refused(); }));
            Assert.AreEqual(TikOpenRetry.SessionLimitAttempts, calls);
        }

        // A RoMON link the agent opened but nothing answers through is retried on a new link; callers would see it as
        // the public TikRomonRelayException with TargetDidNotRespond.
        [TestMethod]
        public void ASilentRomonLink_IsRetried()
        {
            var silent = new WinboxRomonLinkSilentException("silent", null);
            Assert.AreEqual(TikRomonRelayFailure.TargetDidNotRespond, silent.Reason);
            Assert.AreEqual(2, RunCounting(call => call == 1 ? silent : null));
        }

        [TestMethod]
        public void AnotherRelayFailure_IsNotRetried()
        {
            int calls = 0;
            Assert.ThrowsException<TikRomonRelayException>(() => RunCounting(call =>
            { calls = call; return new TikRomonRelayException(TikRomonRelayFailure.TargetDidNotRespond, "no prompt"); }));
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void AClosureMidFrame_IsNotRetried()
        {
            int calls = 0;
            Assert.ThrowsException<TikConnectionLoginException>(() => RunCounting(call =>
            { calls = call; return new TikConnectionLoginException(new IOException("Connection closed unexpectedly")); }));
            Assert.AreEqual(1, calls);
        }
    }
}
