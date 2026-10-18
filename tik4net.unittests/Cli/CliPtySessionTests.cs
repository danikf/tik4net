using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Telnet;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The PTY console the Telnet and SSH clients share, over a scripted byte stream: how a read ends on a carrier
    /// that closes, with and without a RoMON relay, and what is written.
    /// </summary>
    [TestClass]
    public class CliPtySessionTests
    {
        private const string Prompt = "[admin@CHR2] > ";
        private const string Command = ":put [/system identity get name]";
        private const int ReceiveTimeoutMs = 10000;

        /// <summary>
        /// Releases its queued chunks once the session has written something (the command or the key), or from the
        /// start when <c>preloaded</c>. With <c>closeWhenEmpty</c> the carrier reports closed-and-drained once
        /// everything has been read, as SSH's closed channel does. Records every write.
        /// </summary>
        private sealed class ScriptedByteStream : ICliByteStream
        {
            private readonly Queue<byte[]> _chunks = new Queue<byte[]>();
            private readonly bool _closeWhenEmpty;
            private bool _released;

            internal ScriptedByteStream(IEnumerable<string> chunks, bool closeWhenEmpty = false, bool preloaded = false)
                : this(chunks.Select(c => Encoding.ASCII.GetBytes(c)), closeWhenEmpty, preloaded) { }

            internal ScriptedByteStream(IEnumerable<byte[]> chunks, bool closeWhenEmpty = false, bool preloaded = false)
            {
                foreach (var c in chunks) _chunks.Enqueue(c);
                _closeWhenEmpty = closeWhenEmpty;
                _released = preloaded;
            }

            internal List<byte[]> Writes { get; } = new List<byte[]>();

            public bool HasData => _released && _chunks.Count > 0;

            public Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
            {
                byte[] chunk = _chunks.Dequeue();
                Array.Copy(chunk, buffer, chunk.Length);
                return Task.FromResult(chunk.Length);
            }

            public bool ClosedAndDrained => _closeWhenEmpty && _released && _chunks.Count == 0;

            public Task WriteAsync(byte[] bytes, CancellationToken ct)
            {
                Writes.Add(bytes);
                _released = true;
                return Task.CompletedTask;
            }
        }

        private static CliPtySession Session(ICliByteStream stream, string enter = "\r", CliInboundFilter filter = null)
            => new CliPtySession(stream, Encoding.ASCII, () => ReceiveTimeoutMs, "SSH", "ssh.pty", enter, filter);

        [TestMethod]
        public void AClosedAndDrainedCarrier_EndsTheRead()
        {
            var session = Session(new ScriptedByteStream(new[] { Command + "\r\nCH" }, closeWhenEmpty: true));
            var sw = Stopwatch.StartNew();

            var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                () => session.SendCommandAndReadAsync(Command, null, CancellationToken.None).GetAwaiter().GetResult());

            Assert.IsTrue(sw.ElapsedMilliseconds < ReceiveTimeoutMs / 2, sw.ElapsedMilliseconds + " ms: the read waited for the deadline");
            StringAssert.Contains(ex.Message, "SSH");
        }

        [TestMethod]
        public void ARelayThatClosesMidCommand_IsReportedAsTheRelayEnding()
        {
            var session = Session(new ScriptedByteStream(new[] { Command + "\r\nCH" }, closeWhenEmpty: true));
            session.RomonTarget = "AA:BB:CC:DD:EE:FF";

            var ex = Assert.ThrowsException<TikRomonRelayEndedException>(
                () => session.SendCommandAndReadAsync(Command, null, CancellationToken.None).GetAwaiter().GetResult());

            Assert.IsTrue(ex.CommandMayHaveRun);
        }

        [TestMethod]
        public void AControlKeyOnAClosedCarrier_ReturnsWhatArrived()
        {
            var session = Session(new ScriptedByteStream(new[] { "\r\nTaking Safe Mode" }, closeWhenEmpty: true));
            session.RomonTarget = "AA:BB:CC:DD:EE:FF";

            string answer = session.SendRawAndReadAsync(new byte[] { 0x18 }, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains(answer, "Taking Safe Mode");
        }

        [TestMethod]
        public void AnInboundFilter_IsAppliedAndAnswered()
        {
            const byte Iac = 255, Do = 253, Wont = 252, Echo = 1;
            byte[] chunk = new byte[] { Iac, Do, Echo }
                .Concat(Encoding.ASCII.GetBytes(Command + "\r\nCHR2\r\n" + Prompt)).ToArray();
            var stream = new ScriptedByteStream(new[] { chunk });
            var session = Session(stream, "\r\n", TelnetNegotiator.FilterAndRespond);

            string answer = session.SendCommandAndReadAsync(Command, null, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("CHR2", answer.Trim());
            Assert.IsTrue(stream.Writes.Any(w => w.SequenceEqual(new[] { Iac, Wont, Echo })), "the DO was not answered");
            Assert.IsFalse(answer.Contains((char)Iac));
        }

        [TestMethod]
        public void ResidueShowingTheRelayEnded_StopsTheCommandBeforeItIsSent()
        {
            var stream = new ScriptedByteStream(
                new[] { "\r\n" + RouterOsCliLogin.RomonRelayEndText + "\r\n[admin@CHR] > " }, preloaded: true);
            var session = Session(stream);
            session.RomonTarget = "AA:BB:CC:DD:EE:FF";

            var ex = Assert.ThrowsException<TikRomonRelayEndedException>(
                () => session.SendCommandAndReadAsync(Command, null, CancellationToken.None).GetAwaiter().GetResult());

            Assert.IsFalse(ex.CommandMayHaveRun);
            Assert.AreEqual(0, stream.Writes.Count, "the command was written into the ended relay");
        }

        [TestMethod]
        public void OneEnter_IsTheConfiguredLineEnd()
        {
            foreach (string enter in new[] { "\r", "\r\n" })
            {
                var stream = new ScriptedByteStream(new[] { Command + "\r\nCHR2\r\n" + Prompt });
                Session(stream, enter).SendCommandAndReadAsync(Command, null, CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(Command + enter, Encoding.ASCII.GetString(stream.Writes[0]));
            }
        }

        // ── VT100 replies and the change-password prompt ─────────────────────

        [TestMethod]
        public void ACursorProbeWhileTheChangePasswordPromptIsUp_IsNotAnswered()
        {
            // A probe answer is keystrokes: typed into 'new password>' it would become password input.
            var stream = new ScriptedByteStream(new[] { "\r\nnew password> ", "\x1b[6n" }, preloaded: true);

            Session(stream).DrainAsync(50, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(0, stream.Writes.Count, "a reply was typed into the password prompt");
        }

        [TestMethod]
        public void ACursorProbeAtTheShellPrompt_IsAnswered()
        {
            var stream = new ScriptedByteStream(new[] { "\r\n" + Prompt, "\x1b[6n" }, preloaded: true);

            Session(stream).DrainAsync(50, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, stream.Writes.Count);
            StringAssert.EndsWith(Encoding.ASCII.GetString(stream.Writes[0]), "R", "a cursor position report");
        }

        // ── Ending the session ────────────────────────────────────────────────

        [TestMethod]
        public void ACloseInSafeMode_AnswersWithOneEnterAndWaitsForTheRouterToEndTheSession()
        {
            // SSH: one Enter is "\r", so the y answers "Quit? [y/N]" (a "\r\n" would be a second Enter, answering N).
            var stream = new ScriptedByteStream(new[] { "/quit\r\n", "You are in Safe Mode. Quit? [y/N] ", "y\r\n" }, closeWhenEmpty: true);
            var sw = Stopwatch.StartNew();

            Session(stream, "\r").EndSessionAsync(answerSafeModeQuestion: true, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("/quit\ry", Encoding.ASCII.GetString(stream.Writes.SelectMany(w => w).ToArray()));
            Assert.IsTrue(stream.ClosedAndDrained, "returned before the router had ended the session");
            Assert.IsTrue(sw.ElapsedMilliseconds < RouterOsCliLogin.SafeModeQuitWaitMs, sw.ElapsedMilliseconds + " ms: waited past the close");
        }

        [TestMethod]
        public void ACloseInSafeMode_GivesUpWaitingAfterItsBound()
        {
            var stream = new ScriptedByteStream(new[] { "/quit\r\n" });   // the router never ends the session
            var sw = Stopwatch.StartNew();

            Session(stream, "\r\n").EndSessionAsync(answerSafeModeQuestion: true, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("/quit\r\ny", Encoding.ASCII.GetString(stream.Writes.SelectMany(w => w).ToArray()), "Telnet's Enter");
            Assert.IsTrue(sw.ElapsedMilliseconds >= RouterOsCliLogin.SafeModeQuitWaitMs - 50, sw.ElapsedMilliseconds + " ms");
            Assert.IsTrue(sw.ElapsedMilliseconds < RouterOsCliLogin.SafeModeQuitWaitMs + 1500, sw.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public void APlainClose_WritesQuitAndOneEnterAndDoesNotWait()
        {
            var stream = new ScriptedByteStream(new[] { "/quit\r\n" });
            var sw = Stopwatch.StartNew();

            Session(stream, "\r").EndSessionAsync(answerSafeModeQuestion: false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("/quit\r", Encoding.ASCII.GetString(stream.Writes.SelectMany(w => w).ToArray()));
            Assert.IsTrue(stream.HasData, "a plain close read what the router sent");
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000, sw.ElapsedMilliseconds + " ms");
        }
    }
}
