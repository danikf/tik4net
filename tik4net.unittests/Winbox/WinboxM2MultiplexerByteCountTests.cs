using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The byte count a receive timeout reports is measured from before the request leaves, not after.
    /// </summary>
    /// <remarks>
    /// A reply can start arriving while <c>Send</c> is still returning: the reader loop owns the read side and
    /// counts bytes on its own thread. Sampled after the send, those bytes are already in the baseline, and a
    /// half-delivered reply is reported as a router that answered nothing. Over loopback this happened often
    /// enough to fail CI (<c>Timeout_ReportsBytesArrivingWithoutACompletedFrame</c>); the fake channel here
    /// makes the ordering deterministic by counting the reply's bytes inside <c>Send</c> itself.
    /// </remarks>
    [TestClass]
    public class WinboxM2MultiplexerByteCountTests
    {
        [TestMethod]
        public void Timeout_CountsBytesThatArrivedWhileTheSendWasReturning()
        {
            using (var channel = new ReplyDuringSendChannel(bytesOfPartialReply: 257))
            using (var mux = new WinboxM2Multiplexer(channel))
            {
                byte[] request = M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(), mux.NextReqIdField());

                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => mux.SendReceive(request, 300));

                StringAssert.Contains(ex.Message, "without completing a frame",
                    "bytes that arrived during the send belong to this request's wait");
                StringAssert.Contains(ex.Message, "257 byte(s) have arrived");
            }
        }

        /// <summary>
        /// A channel whose reply starts arriving before <see cref="Send"/> returns and never completes a frame.
        /// </summary>
        private sealed class ReplyDuringSendChannel : IWinboxM2Channel
        {
            private readonly int _bytesOfPartialReply;
            private readonly ManualResetEventSlim _closed = new ManualResetEventSlim();
            private long _bytesReceived;

            public ReplyDuringSendChannel(int bytesOfPartialReply) => _bytesOfPartialReply = bytesOfPartialReply;

            public long BytesReceived => Interlocked.Read(ref _bytesReceived);
            public void Send(byte[] m2) => Interlocked.Add(ref _bytesReceived, _bytesOfPartialReply);

            public byte[] ReceiveNextFrame()
            {
                _closed.Wait();          // the frame is never completed
                return null!;
            }

            public bool SupportsReaderLoop => true;
            public bool IsEncrypted => false;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs,
                int sendTimeoutMs = 0) { }
            public byte[] NextReqIdField() => throw new System.NotSupportedException();
            public byte[] Receive(int timeoutMs) => throw new System.NotSupportedException();
            public byte[] SendReceive(byte[] m2, int timeoutMs) => throw new System.NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() => _closed.Set();
        }
    }
}
