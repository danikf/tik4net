using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// When a WinBox CLI command's read ends. mepty releases output one batch per frame the client sends, and
    /// RouterOS also pushes a frame unasked right after the one before it (the Safe Mode status lines).
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // the client reads a fake channel under a receive timeout
    public class WinboxCliReadRuleTests
    {
        private const string Prompt = "[admin@CHR2] > ";
        private const string Command = ":put [/system identity get name]";

        /// <summary>One frame of terminal output; a pushed one is ready as soon as the frame before it is read.</summary>
        private sealed class Frame
        {
            internal Frame(string text, bool pushed = false) { Text = text; Pushed = pushed; }
            internal string Text { get; }
            internal bool Pushed { get; }
        }

        /// <summary>
        /// Hands out one queued frame per thing the client sends (the input itself, then each pull), and a pushed
        /// frame straight after the one before it without a send. Nothing is released before the input has been
        /// sent.
        /// </summary>
        private sealed class PushingChannel : IWinboxM2Channel
        {
            private readonly Queue<Frame> _frames = new Queue<Frame>();
            private readonly Queue<byte[]> _ready = new Queue<byte[]>();
            private readonly byte[] _input;
            private bool _armed;

            internal PushingChannel(byte[] input, IEnumerable<Frame> frames)
            {
                _input = input;
                foreach (var f in frames) _frames.Enqueue(f);
            }

            private static bool Contains(byte[] haystack, byte[] needle)
            {
                for (int i = 0; i + needle.Length <= haystack.Length; i++)
                {
                    int j = 0;
                    while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                    if (j == needle.Length) return true;
                }
                return false;
            }

            private void Release()
                => _ready.Enqueue(M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                    M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(_frames.Dequeue().Text))));

            public bool IsEncrypted => true;
            public bool SupportsStaleDrain => false;
            public bool SupportsReaderLoop => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool DataAvailable => _ready.Count > 0;
            public long BytesReceived => 0;

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public void StartIdleServicing() { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);

            public void Send(byte[] m2)
            {
                if (!_armed) _armed = Contains(m2, _input);
                if (!_armed || _frames.Count == 0 || _ready.Count > 0 || _frames.Peek().Pushed) return;
                Release();
            }

            public byte[] Receive(int timeoutMs)
            {
                if (_ready.Count == 0) return null;
                byte[] frame = _ready.Dequeue();
                if (_frames.Count > 0 && _frames.Peek().Pushed)
                    Release();
                return frame;
            }

            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        private static WinboxCliClient Client(PushingChannel channel)
            => new WinboxCliClient(channel, Encoding.UTF8, CliClientTimeouts.Fixed(3000, 3000));

        [TestMethod]
        public void TheSafeModeKey_KeepsAStatusLinePushedAfterThePrompt()
        {
            var channel = new PushingChannel(new byte[] { 0x18 }, new[]
            {
                new Frame("\r[admin@CHR2] <SAFE> "),
                new Frame("\r[Safe Mode released]\r\n\r\r\r" + Prompt, pushed: true),
            });
            using (var client = Client(channel))
            {
                string answer = client.SendRawAndReadAsync(new byte[] { 0x18 }, CancellationToken.None).GetAwaiter().GetResult();

                StringAssert.Contains(answer, "[Safe Mode released]");
            }
        }

        [TestMethod]
        public void ACountedRead_ReturnsAtItsCountLine()
        {
            string command = CliCommandBuilder.BuildCountedRead("/system identity print as-value", false);
            string typed = CliOutputHelper.InjectWithoutPaging(command);
            var channel = new PushingChannel(Encoding.UTF8.GetBytes(typed), new[]
            {
                new Frame(typed + "\r\nname=CHR2\r\n" + CliCommandBuilder.CountMarker + "1/nil\r\n\r\r\r" + Prompt),
            });
            using (var client = Client(channel))
            {
                string answer = client.SendCommandAndReadAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                StringAssert.Contains(answer, "name=CHR2");
            }
        }
    }
}
