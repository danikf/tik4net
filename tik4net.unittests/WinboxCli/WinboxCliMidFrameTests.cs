using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// A WinBox terminal frame that has started arriving and has not finished.
    /// </summary>
    /// <remarks>
    /// Found by the 4.0.0-beta3 gating matrix: a mangle read over WinboxCli failed inside a 5.7 s test with
    /// "the router did not finish answering within 30000 ms". The frame read ran on a fixed 5 s socket
    /// deadline, and its failure broke out to the generic timeout, which claimed the whole deadline had elapsed
    /// and dropped the channel's exception. The router is measured pausing its terminal output for 10-12 s
    /// (findings-winbox.md §20); between frames that pause gets the full receive deadline, mid-frame it got 5 s.
    /// </remarks>
    [TestClass]
    public class WinboxCliMidFrameTests
    {
        private const string Command = ":put \"x\"";
        private const string Answer = ":put \"x\"\r\nhello\r\n[admin@CHR] > ";

        /// <summary>A channel that answers the command, and records the deadline every frame read is given.</summary>
        private sealed class RecordingChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _toDeliver = new Queue<byte[]>();
            private bool _answered;

            internal readonly List<int> FrameDeadlines = new List<int>();
            internal Exception ThrowOnReceive;

            private void Deliver(string text)
                => _toDeliver.Enqueue(M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                    M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(text))));

            public bool IsEncrypted => true;
            public bool SupportsStaleDrain => false;
            public bool SupportsReaderLoop => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool DataAvailable => _toDeliver.Count > 0;
            public long BytesReceived => 0;

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public void StartIdleServicing() { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);

            public void Send(byte[] m2)
            {
                // Answered on the first send: the command path drains residue before issuing the command, so
                // output planted in advance would be consumed by that drain.
                if (_answered) return;
                _answered = true;
                Deliver(Answer.Substring(0, 12));
                Deliver(Answer.Substring(12));
            }

            public byte[] Receive(int timeoutMs)
            {
                FrameDeadlines.Add(timeoutMs);
                if (ThrowOnReceive != null) throw ThrowOnReceive;
                return _toDeliver.Count == 0 ? null : _toDeliver.Dequeue();
            }

            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        private static string Run(WinboxCliClient client)
            => client.SendCommandAndReadAsync(Command, CancellationToken.None).GetAwaiter().GetResult();

        [TestMethod]
        public void AFrameAlreadyArrivingIsGivenTheRestOfTheReceiveDeadline()
        {
            var channel = new RecordingChannel();
            using (var client = new WinboxCliClient(channel, Encoding.UTF8, 30000, 30000))
            {
                Assert.AreEqual("hello", Run(client).Trim());

                Assert.AreNotEqual(0, channel.FrameDeadlines.Count);
                Assert.IsTrue(channel.FrameDeadlines.All(ms => ms > 25000),
                    "a router pause that lands mid-frame must get the receive deadline, as one between frames "
                    + "does — deadlines given: " + string.Join(", ", channel.FrameDeadlines));
            }
        }

        [TestMethod]
        public void AFrameThatFailsPartWayIsReportedAsWhatHappened()
        {
            var reset = new IOException("Unable to read data from the transport connection: reset by peer.");
            var channel = new RecordingChannel { ThrowOnReceive = reset };
            using (var client = new WinboxCliClient(channel, Encoding.UTF8, 30000, 30000))
            {
                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => Run(client));
                sw.Stop();

                // Still this type: the response is incomplete and the connection must be closed, which is
                // what the base class does for it.
                Assert.AreSame(reset, ex.InnerException, "the channel's own exception is the diagnosis");
                StringAssert.Contains(ex.Message, "part-way through");
                StringAssert.Contains(ex.Message, "IOException");
                Assert.IsFalse(ex.Message.Contains("did not finish answering within"),
                    "it did not run for the whole deadline, so it must not say it did: " + ex.Message);
                Assert.IsTrue(sw.ElapsedMilliseconds < 5000, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }
    }
}
