using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// What <see cref="WinboxCliClient"/> does when the carrier reports that the router never took the
    /// command's bytes (<see cref="IWinboxM2Channel.SendAbandoned"/>) — P2.54.
    /// <para>
    /// The defect being pinned is not that the read failed: it is that it failed <b>slowly and at the wrong
    /// layer</b>. A dropped MAC-layer console used to cost the caller the whole receive timeout and then
    /// report "nothing was received", which describes our read rather than the router's session, and which
    /// leaves the caller unable to tell a dead session from a slow command. That distinction is the whole
    /// value of the signal, so the tests below assert both halves of it: it is honoured when the router
    /// demonstrably has not taken our bytes, and ignored once it demonstrably has.
    /// </para>
    /// </summary>
    [TestClass]
    public class WinboxCliSessionClosedTests
    {
        // Short enough that the "spends the whole timeout" case is a fast test, long enough that the
        // difference between it and an immediate throw cannot be measurement noise.
        private const int ReceiveTimeoutMs = 1500;

        /// <summary>
        /// A channel that accepts everything and answers nothing, with <see cref="SendAbandoned"/> under the
        /// test's control. It stands in for the MAC carrier at the moment the router has stopped
        /// acknowledging: the socket is open, sends still "succeed", and no frame will ever come back.
        /// </summary>
        private sealed class SilentChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _toDeliver = new Queue<byte[]>();

            internal volatile bool Abandoned;
            internal int SentCount;

            // Answered on the first send rather than queued up front: the command path drains whatever is
            // already buffered BEFORE issuing the command (residue from the previous one), so output planted
            // in advance is consumed by that drain and never reaches the read under test.
            private string _answerFirstSendWith;

            /// <summary>Makes the channel answer the first thing sent with one mepty output frame.</summary>
            internal void AnswerFirstSendWith(string text) => _answerFirstSendWith = text;

            private void Deliver(string text)
                => _toDeliver.Enqueue(M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                    M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(text))));

            public bool IsEncrypted => true;
            public bool SupportsStaleDrain => false;
            public bool SupportsReaderLoop => false;
            public bool SendAbandoned => Abandoned;
            public bool DataAvailable => _toDeliver.Count > 0;

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs) { }
            public void StartIdleServicing() { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);
            public void Send(byte[] m2)
            {
                SentCount++;
                if (_answerFirstSendWith != null)
                {
                    Deliver(_answerFirstSendWith);
                    _answerFirstSendWith = null;
                }
            }
            public byte[] Receive(int timeoutMs) => _toDeliver.Count > 0 ? _toDeliver.Dequeue() : null;
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        private static WinboxCliClient ClientOver(SilentChannel channel)
            => new WinboxCliClient(channel, Encoding.UTF8, ReceiveTimeoutMs, ReceiveTimeoutMs);

        private static Exception RunCommand(WinboxCliClient client)
        {
            try
            {
                client.SendCommandAndReadAsync("/system/identity/print", CancellationToken.None)
                      .GetAwaiter().GetResult();
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        [TestMethod]
        public void UnacknowledgedCommand_IsReportedAsAClosedSession_NotAsAnEmptyRead()
        {
            var channel = new SilentChannel { Abandoned = true };
            using (var client = ClientOver(channel))
            {
                var sw = Stopwatch.StartNew();
                Exception ex = RunCommand(client);
                sw.Stop();

                Assert.IsInstanceOfType(ex, typeof(TikConnectionSessionClosedException),
                    "A command the router never acknowledged must be reported as a closed session — "
                    + "'nothing was received' names our read, not what the session did. Got: " + ex);
                StringAssert.Contains(ex.Message, "/system/identity/print",
                    "The message has to name the command that did not run, or the caller cannot act on it.");
                Assert.IsTrue(sw.ElapsedMilliseconds < ReceiveTimeoutMs,
                    "The answer was already known, so waiting out the receive timeout buys nothing — "
                    + "took " + sw.ElapsedMilliseconds + " ms of " + ReceiveTimeoutMs + " ms.");
            }
        }

        [TestMethod]
        public void SilentChannelThatStillAcknowledges_StillSpendsItsWholeTimeout()
        {
            // The deliberate contrast to the test above. A router that took the bytes and has not answered
            // yet is a slow command, and cutting that short would turn a working long-running command into a
            // spurious failure — so the fast exit must be gated on the acknowledgement, not on the silence.
            var channel = new SilentChannel { Abandoned = false };
            using (var client = ClientOver(channel))
            {
                var sw = Stopwatch.StartNew();
                Exception ex = RunCommand(client);
                sw.Stop();

                Assert.IsInstanceOfType(ex, typeof(TikConnectionReceiveTimeoutException),
                    "Without the abandonment signal there is nothing to distinguish a dead session from a "
                    + "slow one, so the read must run its course. Got: " + ex);
                Assert.IsTrue(sw.ElapsedMilliseconds >= ReceiveTimeoutMs - 100,
                    "Returned after only " + sw.ElapsedMilliseconds + " ms — a slow command would have been "
                    + "cut short.");
            }
        }

        [TestMethod]
        public void OutputAlreadyReceived_DisarmsTheSignal()
        {
            // Once the console has produced output, the command provably reached it, so it may have executed.
            // Reporting "the command did not run" then would be a lie the caller cannot check — and the
            // reconnect-and-retry built on this exception would re-run a command that already ran.
            var channel = new SilentChannel { Abandoned = true };
            channel.AnswerFirstSendWith("some output but no prompt");
            using (var client = ClientOver(channel))
            {
                Exception ex = RunCommand(client);

                Assert.IsInstanceOfType(ex, typeof(TikConnectionReceiveTimeoutException),
                    "The command's output came back, so this is an unfinished read, not an un-run command. "
                    + "Got: " + ex);
            }
        }
    }
}
