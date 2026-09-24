using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// RouterOS 6's WinBox terminal echoes a command by repainting the whole line — prompt included — after
    /// every character (measured on 6.49.13: ~40 KB of echo for a 150-character command). A frame can end on
    /// one of those repainted prompts, mid-echo. That prompt must not end the read: it is followed on the same
    /// line by the rest of the typed text, while the prompt that ends a command starts a line of its own.
    /// </summary>
    [TestClass]
    public class WinboxCliEchoRedrawTests
    {
        private const string Esc = "\u001b";
        private const string Prompt = "[admin@CHR2] > ";

        /// <summary>
        /// Hands out one queued mepty frame per thing the client sends — the command itself, then each pull —
        /// the way mepty releases output only when asked for it. A send while a frame is still unread asks for
        /// nothing new, and nothing is released before the command itself has been sent (the client drains
        /// residue first, and that drain must not eat the command's own output).
        /// </summary>
        private sealed class PullDrivenChannel : IWinboxM2Channel
        {
            private readonly Queue<string> _frames = new Queue<string>();
            private readonly Queue<byte[]> _ready = new Queue<byte[]>();

            private readonly byte[] _command;
            private bool _armed;

            internal PullDrivenChannel(string command, IEnumerable<string> frames)
            {
                _command = Encoding.UTF8.GetBytes(command);
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
                if (!_armed) _armed = Contains(m2, _command);
                if (!_armed || _frames.Count == 0 || _ready.Count > 0) return;
                _ready.Enqueue(M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                    M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(_frames.Dequeue()))));
            }

            public byte[] Receive(int timeoutMs) => _ready.Count == 0 ? null : _ready.Dequeue();
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        /// <summary>The 6.49.13 echo of <paramref name="command"/>: each character, then the whole line repainted.</summary>
        private static string CharByCharEcho(string command)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= command.Length; i++)
                sb.Append(command[i - 1]).Append('\r').Append(Prompt).Append(command, 0, i).Append(Esc).Append("[K");
            return sb.ToString();
        }

        [TestMethod]
        public void FrameEndingOnARepaintedPrompt_DoesNotEndTheRead()
        {
            const string command = ":local d [/system resource print without-paging as-value]; :put $d";
            string echo = CharByCharEcho(command);

            // Cut the echo right after a repainted prompt, well past the point where the command's leading
            // 40 characters (the echo key) are already on screen.
            int cut = echo.IndexOf("\r" + Prompt, echo.Length * 4 / 5, StringComparison.Ordinal) + 1 + Prompt.Length;
            string first = echo.Substring(0, cut);
            string second = echo.Substring(cut)
                + "\r\nuptime=01:00:00\r\n#n=1/nil\r\n\r\r\r" + Esc + "[9999B" + Prompt;

            using (var client = new WinboxCliClient(new PullDrivenChannel(command, new[] { first, second }), Encoding.UTF8, 3000, 3000))
            {
                string answer = client.SendCommandAndReadAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                StringAssert.Contains(answer, "#n=1/nil",
                    "The read ended on a prompt repainted inside the echo, before the command's output was pulled.");
            }
        }

        [TestMethod]
        public void CompletionPrompt_StartsALineOfItsOwn()
        {
            const string cmd = ":put [/system identity get name]";
            // 6.49.13 mid-echo: a repainted prompt at the end of the buffer, typed text before it on the same line.
            Assert.IsFalse(tik4net.Cli.CliOutputHelper.EndsWithCompletionPrompt(":\r" + Prompt + ":\r" + Prompt, cmd));
            // The prompt after the output: only carriage returns between the line break and it.
            Assert.IsTrue(tik4net.Cli.CliOutputHelper.EndsWithCompletionPrompt(":\r" + Prompt + cmd + "\r\nCHR2\r\n\r\r\r" + Prompt, cmd));
            // 7.24.4: one repaint of the whole command, then its line break — no output at all.
            Assert.IsTrue(tik4net.Cli.CliOutputHelper.EndsWithCompletionPrompt(cmd + "\r" + Prompt + cmd + "\r\n\r\r\r\r" + Prompt, cmd));
            // A control key is not ended by a line break (Safe Mode's Ctrl+X): the plain prompt test.
            Assert.IsTrue(tik4net.Cli.CliOutputHelper.EndsWithCompletionPrompt("\r[admin@CHR2] <SAFE>", null));
        }
    }
}
