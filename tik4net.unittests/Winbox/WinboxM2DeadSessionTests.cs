using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// G1: a WinBox-MAC M2 session that RouterOS logged out during an idle stretch is <b>silent</b>, not
    /// closed — the UDP socket stays open and nothing answers. The carrier knows
    /// (<see cref="IWinboxM2Channel.SendAbandoned"/>: it retransmitted the request to exhaustion and was
    /// never acknowledged), and the multiplexer must ask rather than wait out the full
    /// <see cref="ITikConnection.ReceiveTimeout"/> and then report "no reply", which names the symptom at
    /// the wrong layer.
    /// </summary>
    /// <remarks>
    /// Driven by a fake channel rather than <c>FakeWinboxServer</c> on purpose: the TCP channel answers
    /// <see cref="IWinboxM2Channel.SendAbandoned"/> with a constant <c>false</c> (nothing below it
    /// acknowledges individual messages), so the signal under test only exists on the MAC carrier and only a
    /// fake can produce it deterministically.
    /// </remarks>
    [TestClass]
    public class WinboxM2DeadSessionTests
    {
        [TestMethod]
        public void SendAbandoned_FailsTheWaiterAsSessionClosed_RatherThanWaitingOutTheDeadline()
        {
            using (var channel = new AbandoningChannel())
            using (var mux = new WinboxM2Multiplexer(channel))
            {
                byte[] request = M2Message.BuildM2(
                    M2Message.SysToArr(24, 1), M2Message.SysFrom(), mux.NextReqIdField());

                // The router took nothing and never will. The deadline is deliberately far longer than the
                // test is willing to wait: passing by expiry would prove nothing.
                channel.AbandonAfterSend = true;

                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionSessionClosedException>(
                    () => mux.SendReceive(request, 30000));
                sw.Stop();

                StringAssert.Contains(ex.Message, "did not take",
                    "the message must say the router never took the bytes — that is what makes it safe to "
                    + "state the command did not run");
                Assert.IsTrue(sw.ElapsedMilliseconds < 10000,
                    $"the dead session must be reported promptly, not after the deadline; took {sw.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// The counterpart: a channel that is merely slow must still time out as a
        /// <see cref="TimeoutException"/>. Reporting a slow router as a closed session would tell the caller
        /// its command did not run when it may well have.
        /// </summary>
        [TestMethod]
        public void SlowChannel_StillTimesOut_RatherThanBeingReportedAsAClosedSession()
        {
            using (var channel = new AbandoningChannel())
            using (var mux = new WinboxM2Multiplexer(channel))
            {
                byte[] request = M2Message.BuildM2(
                    M2Message.SysToArr(24, 1), M2Message.SysFrom(), mux.NextReqIdField());

                Assert.ThrowsException<TimeoutException>(() => mux.SendReceive(request, 500));
            }
        }

        /// <summary>
        /// An <see cref="IWinboxM2Channel"/> that never answers, and can be told to report the send as
        /// abandoned the way <c>MacLayerTransport</c> does once its retransmit budget is spent.
        /// </summary>
        private sealed class AbandoningChannel : IWinboxM2Channel
        {
            private readonly ManualResetEventSlim _closed = new ManualResetEventSlim(false);
            private volatile bool _abandoned;
            private int _reqId;

            /// <summary>When set, the next <see cref="Send"/> makes <see cref="SendAbandoned"/> true.</summary>
            internal bool AbandonAfterSend { get; set; }

            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => _abandoned;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => true;

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs)
                => throw new NotSupportedException("The fake channel is handed to the multiplexer already open.");

            public byte[] NextReqIdField()
                => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)(Interlocked.Increment(ref _reqId) & 0xFF));

            public void Send(byte[] m2)
            {
                if (AbandonAfterSend) _abandoned = true;
            }

            public byte[] Receive(int timeoutMs) => null!;
            public byte[] SendReceive(byte[] m2, int timeoutMs) => null!;

            public byte[] ReceiveNextFrame()
            {
                _closed.Wait();
                return null!;   // channel closed
            }

            public void StartIdleServicing() { }

            public void Dispose() => _closed.Set();
        }
    }
}
