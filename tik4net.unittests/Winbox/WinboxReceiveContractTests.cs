using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// One "no frame" contract on every M2 carrier (review D9): <see cref="IWinboxM2Channel.Receive"/> answers
    /// <c>null</c> when nothing arrived within the timeout and throws when the stream is broken.
    /// </summary>
    /// <remarks>
    /// The MAC carrier always answered <c>null</c>; the TCP one threw the socket's timeout as an
    /// <see cref="IOException"/>, so three callers each caught it a different way (review X8). On TCP a timeout
    /// before the first byte of a frame leaves the stream aligned and is "nothing arrived"; a timeout after part of a
    /// frame has arrived is not, because the next read would start inside it.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class WinboxReceiveContractTests
    {
        private static async Task WithSession(Action<FakeWinboxServer> serverScript,
            Action<WinboxM2Session, FakeWinboxServer> clientScript)
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence();
                    serverScript?.Invoke(server);
                });
                await Task.Run(() =>
                {
                    session.Open("127.0.0.1", server.Port, "admin", "", 5000, 5000);
                    clientScript(session, server);
                });
                await serverTask;
            }
        }

        private static byte[] Frame(int value)
            => M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(), M2Message.U32User(0x2, value));

        [TestMethod]
        public async Task Tcp_NothingWithinTheTimeout_IsNull()
        {
            await WithSession(null, (session, server) =>
            {
                Assert.IsNull(session.Receive(300), "no byte arrived: nothing, not a broken stream");
            });
        }

        [TestMethod]
        public async Task Tcp_AfterAnEmptyReceive_TheNextFrameIsReadWhole()
        {
            using (var sent = new System.Threading.ManualResetEventSlim())
            {
                await WithSession(server => { sent.Wait(5000); server.SendRawFrame(Frame(7)); }, (session, server) =>
                {
                    Assert.IsNull(session.Receive(200));
                    sent.Set();
                    byte[] next = session.Receive(3000);
                    Assert.IsNotNull(next);
                    Assert.AreEqual(7, M2Message.ParseSysU32(next, 0x2), "the stream is still aligned on a frame boundary");
                });
            }
        }

        [TestMethod]
        public async Task Tcp_ATimeoutInsideAFrame_Throws()
        {
            await WithSession(server => server.SendFrameFirstChunkOnly(), (session, server) =>
            {
                Assert.ThrowsException<IOException>(() => session.Receive(500),
                    "part of a frame arrived: the stream cannot be read on from here");
            });
        }

        [TestMethod]
        public async Task Tcp_SendReceive_NothingWithinTheTimeout_IsNull()
        {
            await WithSession(server => server.ReadRawFrame(), (session, server) =>
            {
                Assert.IsNull(session.SendReceive(Frame(1), 300));
            });
        }

        // ── the open-phase round trip (review F4) ──

        /// <summary>
        /// While the connection opens, the round trip reads past a reply to another request. When nothing more comes
        /// it stops at the first empty receive and reports a timeout: it used to read sixteen more times (4 s on the
        /// MAC carrier, whose receive answers <c>null</c>) and then hand the interpreters <c>null</c>, which they read
        /// as an empty reply.
        /// </summary>
        [TestMethod]
        public void OpenPhase_AStaleReplyAndThenNothing_IsATimeout_AtOnce()
        {
            var channel = new QueueChannel();
            channel.AnswerEachSend.Enqueue(M2Message.BuildM2(M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 0x11)));
            var ops = new WinboxNativeM2Operations(channel, 2000);

            var sw = Stopwatch.StartNew();
            Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => ops.GetSingleton(new WinboxHandler(24, 1)));
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
            Assert.AreEqual(2, channel.Receives, "the reply, then one empty receive");
        }

        [TestMethod]
        public void OpenPhase_NoReplyAtAll_IsATimeout()
        {
            var channel = new QueueChannel();
            var ops = new WinboxNativeM2Operations(channel, 2000);
            Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => ops.GetSingleton(new WinboxHandler(24, 1)));
        }

        private sealed class QueueChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _inbound = new Queue<byte[]>();
            internal readonly Queue<byte[]> AnswerEachSend = new Queue<byte[]>();
            internal int Receives;

            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);
            public void Send(byte[] m2)
            {
                if (AnswerEachSend.Count > 0) _inbound.Enqueue(AnswerEachSend.Dequeue());
            }
            public byte[] Receive(int timeoutMs) { Receives++; return _inbound.Count > 0 ? _inbound.Dequeue() : null; }
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
