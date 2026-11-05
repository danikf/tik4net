using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.unittests.Cli;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// A MAC-Telnet command read over loopback UDP against <see cref="MacTelnetFakeRouter"/>, for what only a datagram
    /// carrier has: a lost datagram, a router that never acknowledges, a <c>PKT_END</c> while the session is idle. The
    /// read cases every terminal shares are in <see cref="TerminalReadTableTests"/>.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // reads bounded by a receive timeout against a fake peer
    public class MacTelnetCommandReadTests
    {
        private const string Prompt = TerminalReadFixture.Prompt;
        private const string First = ":put [/system identity get name]";
        private const string Second = ":put [/system clock get time]";

        private static TerminalReadFixture.MacTelnetFixture Fixture(RouterScript script, int timeoutMs)
            => (TerminalReadFixture.MacTelnetFixture)TerminalReadFixture.Create(TerminalCarrier.MacTelnet, script, timeoutMs);

        private static RouterScript TwoCommands()
            => RouterScript.Answer((0, First + "\r\nCHR2\r\n" + Prompt))
                .Then((0, Second + "\r\n12:00:00\r\n" + Prompt));

        [TestMethod]
        public void ALostCommandDatagram_IsResent_AndAnswered()
        {
            // The router's ACK for the command never comes: the pump resends the datagram after the retransmit
            // interval, the router takes the copy, and the read gets its answer inside the deadline.
            using (var f = Fixture(TwoCommands(), 10000))
            {
                Assert.AreEqual("CHR2", f.Client.SendCommandAndReadAsync(First, null, CancellationToken.None).GetAwaiter().GetResult().Trim());
                f.DropOnce = Second + "\r";

                string answer = f.Client.SendCommandAndReadAsync(Second, null, CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual("12:00:00", answer.Trim());
                Assert.AreEqual(1, f.Dropped);
            }
        }

        [TestMethod]
        public void ACommandTheRouterNeverAcknowledges_IsASessionClosed_BeforeTheDeadline()
        {
            const int TimeoutMs = 15000;
            using (var f = Fixture(TwoCommands(), TimeoutMs))
            {
                // The first exchange gives the session its first ACK, which the retransmit waits for.
                f.Client.SendCommandAndReadAsync(First, null, CancellationToken.None).GetAwaiter().GetResult();
                f.NeverAcknowledge = Second + "\r";
                var sw = Stopwatch.StartNew();

                var ex = Assert.ThrowsException<TikConnectionSessionClosedException>(
                    () => f.Client.SendCommandAndReadAsync(Second, null, CancellationToken.None).GetAwaiter().GetResult());

                Assert.IsTrue(sw.ElapsedMilliseconds < TimeoutMs / 2, sw.ElapsedMilliseconds + " ms: the read waited for the deadline");
                StringAssert.Contains(ex.Message, Second);
                Assert.IsTrue(f.Dropped > 1, "the command was resent: " + f.Dropped);
            }
        }

        [TestMethod]
        public void APktEndWhileIdle_RefusesTheNextCommand_BeforeItIsWritten()
        {
            using (var f = Fixture(TwoCommands(), 10000))
            {
                f.Client.SendCommandAndReadAsync(First, null, CancellationToken.None).GetAwaiter().GetResult();
                f.Router.SendEnd();
                Thread.Sleep(300);   // the pump takes it

                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => f.Client.SendCommandAndReadAsync(Second, null, CancellationToken.None).GetAwaiter().GetResult());

                StringAssert.Contains(ex.Message, "did not run");
                Assert.AreEqual(1, f.Inputs.Count, "the command was written into the ended session");
            }
        }
    }
}
