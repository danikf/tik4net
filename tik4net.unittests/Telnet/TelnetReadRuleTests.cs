using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Telnet;

namespace tik4net.unittests.Telnet
{
    /// <summary>
    /// When a Telnet command's read ends: the command's echo is on screen, the text ends at a completion prompt, and
    /// then either the counted read's last line has arrived or nothing new comes for the settle window. A local
    /// listener stands in for the router and writes the shapes RouterOS writes.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a local listener answers within a time budget
    public class TelnetReadRuleTests
    {
        private const string Esc = "\u001b";
        private const string Prompt = "[admin@CHR2] > ";
        private const string Command = ":put [/system identity get name]";
        private const int ReceiveTimeoutMs = 5000;

        /// <summary>
        /// Opens a client on a local listener, starts <paramref name="read"/>, waits for what it typed (ending in
        /// <paramref name="typedEnd"/>), then plays <paramref name="router"/> on the accepted socket.
        /// </summary>
        private static string Run(Func<TelnetClient, Task<string>> read, byte[] typedEnd, Action<Socket> router,
            out long elapsedMs)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = new TelnetClient(Encoding.ASCII, CliClientTimeouts.Fixed(ReceiveTimeoutMs, connectMs: 2000, sendMs: 5000));
                client.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port);
                try
                {
                    using (Socket socket = listener.AcceptSocket())
                    {
                        var sw = Stopwatch.StartNew();
                        Task<string> reading = read(client);
                        WaitForTyped(socket, typedEnd);
                        router(socket);
                        Assert.IsTrue(reading.Wait(ReceiveTimeoutMs + 2000), "the read did not return");
                        elapsedMs = sw.ElapsedMilliseconds;
                        return reading.Result;
                    }
                }
                finally { client.Close(); }
            }
            finally { listener.Stop(); }
        }

        private static string RunCommand(string command, Action<Socket> router, out long elapsedMs)
            => Run(c => c.SendCommandAndReadAsync(command, null, CancellationToken.None),
                Encoding.ASCII.GetBytes("\r\n"), router, out elapsedMs);

        private static void WaitForTyped(Socket socket, byte[] end)
        {
            var got = new System.Collections.Generic.List<byte>();
            var buffer = new byte[256];
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                if (!socket.Poll(50_000, SelectMode.SelectRead)) continue;
                int n = socket.Receive(buffer);
                if (n == 0) break;
                for (int i = 0; i < n; i++) got.Add(buffer[i]);
                if (got.Count >= end.Length)
                {
                    bool match = true;
                    for (int i = 0; i < end.Length; i++)
                        if (got[got.Count - end.Length + i] != end[i]) { match = false; break; }
                    if (match) return;
                }
            }
            Assert.Fail("the client never typed its input");
        }

        private static void Send(Socket socket, string text) => socket.Send(Encoding.ASCII.GetBytes(text));

        /// <summary>The 6.49.13 echo of <paramref name="command"/>: each character, then the whole line repainted.</summary>
        private static string CharByCharEcho(string command)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= command.Length; i++)
                sb.Append(command[i - 1]).Append('\r').Append(Prompt).Append(command, 0, i).Append(Esc).Append("[K");
            return sb.ToString();
        }

        [TestMethod]
        public void APromptLeftBeforeTheEcho_DoesNotEndTheRead()
        {
            string answer = RunCommand(Command, router =>
            {
                Send(router, Prompt);
                Thread.Sleep(250);   // longer than the settle window
                Send(router, Command + "\r\nCHR2\r\n" + Prompt);
            }, out _);

            Assert.AreEqual("CHR2", answer.Trim());
        }

        [TestMethod]
        public void ARepaintCutMidEcho_DoesNotEndTheRead()
        {
            string echo = CharByCharEcho(Command);
            // Cut right after the last repainted prompt: the whole command is on screen already, so the echo has
            // been seen, and the buffer ends at a prompt that has typed text before it on its own line.
            int cut = echo.LastIndexOf("\r" + Prompt, StringComparison.Ordinal) + 1 + Prompt.Length;

            string answer = RunCommand(Command, router =>
            {
                Send(router, echo.Substring(0, cut));
                Thread.Sleep(250);
                Send(router, echo.Substring(cut) + "\r\nCHR2\r\n\r\r\r" + Prompt);
            }, out _);

            Assert.AreEqual("CHR2", answer.Trim());
        }

        [TestMethod]
        public void OutputAfterARepaintedPrompt_IsPartOfTheAnswer()
        {
            string answer = RunCommand(Command, router =>
            {
                Send(router, Command + "\r\n\r\r\r" + Prompt);   // the repaint before the output
                Thread.Sleep(40);
                Send(router, "\r\nCHR2\r\n" + Prompt);   // the line editor moves off the repainted line first
            }, out _);

            Assert.AreEqual("CHR2", answer.Trim());
        }

        [TestMethod]
        public void ACountedRead_ReturnsAtItsCountLine()
        {
            string command = CliCommandBuilder.BuildCountedRead("/system identity print as-value", false);
            string typed = CliOutputHelper.InjectWithoutPaging(command);

            string answer = RunCommand(command, router =>
                Send(router, typed + "\r\nname=CHR2\r\n" + CliCommandBuilder.CountMarker + "1/nil\r\n\r\r\r" + Prompt),
                out long elapsedMs);   // and then nothing: no settle window, no further prompt

            StringAssert.Contains(answer, "name=CHR2");
            Assert.IsTrue(elapsedMs < ReceiveTimeoutMs, elapsedMs + " ms");
        }

        [TestMethod]
        public void AControlKeyAtABarePrompt_Returns()
        {
            string answer = Run(c => c.SendRawAndReadAsync(new byte[] { 0x18 }, CancellationToken.None),
                new byte[] { 0x18 },
                router => Send(router, "\r\n[admin@CHR2] <SAFE>"),
                out _);

            StringAssert.Contains(answer, "<SAFE>");
        }
    }
}
