using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// One table of read cases run against the three terminal read engines — <c>CliPtySession</c> (Telnet, SSH),
    /// <c>MacTelnetUdpClient</c> and <c>WinboxCliClient</c> (WinBox CLI, WinBox CLI MAC) — over fake carriers playing the
    /// same router script (<see cref="TerminalReadFixture"/>). Every engine runs its own copy of the read loop; this is
    /// what holds them to one behaviour while they are consolidated (terminal clients review, Phase 0).
    /// </summary>
    /// <remarks>
    /// Where the engines differ today, the case pins each one's current answer and names the review's finding in its
    /// name (<c>X4</c>, <c>X5</c>, <c>X7</c>). Those are the rows a later phase flips on purpose, under its decision;
    /// until then a change to one of them is a behaviour change, not a tidy-up.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // reads bounded by a receive timeout against timed fakes
    public class TerminalReadTableTests
    {
        private const string Prompt = TerminalReadFixture.Prompt;
        private const string Command = ":put [/system identity get name]";
        private const int TimeoutMs = 3000;

        private static readonly string Counted = CliCommandBuilder.BuildCountedRead("/system identity print as-value", false);
        private static readonly string CountedTyped = CliEndMarker.InjectWithoutPaging(Counted);
        private static readonly string CountedAnswer = "name=CHR2\r\n" + CliCommandBuilder.CountMarker + "1/nil\r\n";

        private static TerminalReadFixture Fixture(TerminalCarrier carrier, RouterScript script, int timeoutMs = TimeoutMs)
            => TerminalReadFixture.Create(carrier, script, timeoutMs);

        private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();

        // ── A command's answer ────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void EchoThenPrompt_IsTheAnswer(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, Command + "\r\n"), (50, "CHR2\r\n" + Prompt))))
            {
                Assert.AreEqual("CHR2", Run(f.CommandAsync(Command)).Trim());
                Assert.AreEqual(Command + "\r", f.Inputs[0], "one line and one Enter");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void ARepaintBeforeACountedAnswer_DoesNotEndTheRead(TerminalCarrier carrier)
        {
            // A console log line makes RouterOS repaint its prompt between the echo and a slow answer. Nothing of the
            // answer's own is behind the echo yet, so the prompt is waited past — well beyond the settle window.
            using (var f = Fixture(carrier, RouterScript.Answer((0, CountedTyped + "\r\n" + Prompt), (500, "\r\n" + CountedAnswer + Prompt))))
            {
                StringAssert.Contains(Run(f.CommandAsync(Counted)), "name=CHR2");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void ACountedRead_ReturnsAtItsCountLine_WithoutTheSettleWindow(TerminalCarrier carrier)
        {
            // Text arriving 100 ms after the prompt is inside every engine's settle window (120 / 150 ms): a read
            // waiting the window out would take it, find the prompt no longer last, and run to the deadline.
            using (var f = Fixture(carrier, RouterScript.Answer((0, CountedTyped + "\r\n" + CountedAnswer + Prompt), (100, "late\r\n"))))
            {
                string answer = Run(f.CommandAsync(Counted));

                StringAssert.Contains(answer, "name=CHR2");
                Assert.IsFalse(answer.Contains("late"), answer);
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void ACloseMidRead_IsReportedAsClosed_AtOnce(TerminalCarrier carrier)
        {
            // The PTY carrier closing, MAC-Telnet's PKT_END, WinBox's Mepty.Close: no prompt will follow.
            using (var f = Fixture(carrier, RouterScript.Answer((0, Command + "\r\nCH")).ThenEnd(100)))
            {
                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => Run(f.CommandAsync(Command)));

                Assert.IsTrue(sw.ElapsedMilliseconds < TimeoutMs / 2, sw.ElapsedMilliseconds + " ms: the read waited for the deadline");
                StringAssert.Contains(ex.Message, "closed the connection");
                StringAssert.Contains(ex.Message, "may have run");
                StringAssert.EndsWith(ex.PartialResponse, "CH");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void TheDeadline_ThrowsWithWhatArrived(TerminalCarrier carrier)
        {
            const int Short = 1500;
            using (var f = Fixture(carrier, RouterScript.Answer((0, Command + "\r\nCH")), Short))
            {
                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => Run(f.CommandAsync(Command)));

                Assert.IsTrue(sw.ElapsedMilliseconds >= Short - 100, sw.ElapsedMilliseconds + " ms");
                StringAssert.Contains(ex.Message, "did not finish answering within " + Short + " ms");
                StringAssert.Contains(ex.Message, Command);
                StringAssert.EndsWith(ex.PartialResponse, "CH");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void AnInterrupt_SendsCtrlC_AndTheNextPromptEndsTheRead(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, Command + "\r\npartial\r\n"));
            script.OnCtrlC = "\r\n" + Prompt;
            using (var f = Fixture(carrier, script, 10000))
            {
                var sw = Stopwatch.StartNew();
                Task<string> read = f.CommandAsync(Command);
                while (f.Inputs.Count == 0 && sw.ElapsedMilliseconds < 2000) Thread.Sleep(5);
                Thread.Sleep(300);
                f.RequestInterrupt();
                string answer = Run(read);

                StringAssert.Contains(answer, "partial");
                Assert.AreEqual(1, f.Inputs.Count(i => i == "\x03"), "one Ctrl-C");
                Assert.IsTrue(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms: the read ran past the key");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void AnInterruptRequestedBeforeTheCommand_IsVoid(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, Command + "\r\nCHR2\r\n" + Prompt))))
            {
                f.RequestInterrupt();

                Assert.AreEqual("CHR2", Run(f.CommandAsync(Command)).Trim());
                Assert.IsFalse(f.Inputs.Contains("\x03"), "a request that came before the command was carried into it");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void ARawKey_ReadsToThePrompt_AndAMissingPromptIsNotAnError(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, "\r\n[admin@CHR2] <SAFE> ")).Then((0, "\x1b7\x1b8")), 1500))
            {
                StringAssert.Contains(Run(f.RawAsync(new byte[] { 0x18 })), "<SAFE>");
                // Ctrl-D is answered with a bare cursor save/restore: the deadline ends the read, without an exception.
                Run(f.RawAsync(new byte[] { 0x04 }));
            }
        }

        // ── A quiet read (Tab-completion) ─────────────────────────────────────

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void AQuietRead_WithData_EndsOnTheQuiet(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, "\r\nsystem  \r\n"), (50, Prompt + "/sys")), 10000))
            {
                var sw = Stopwatch.StartNew();
                string answer = Run(f.QuietAsync(Encoding.ASCII.GetBytes("/sys\t"), 150));

                StringAssert.Contains(answer, "system");
                StringAssert.EndsWith(answer, "/sys");
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
            }
        }

        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void AQuietRead_WithoutData_IsAnEmptyAnswer_NotTheDeadline(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Silent(), 10000))
            {
                var sw = Stopwatch.StartNew();
                string answer = Run(f.QuietAsync(Encoding.ASCII.GetBytes("/zz\t"), 100));

                Assert.AreEqual(string.Empty, answer);
                Assert.IsTrue(sw.ElapsedMilliseconds >= 100 * CliCompletionParser.NoReactionQuietFactor - 50, sw.ElapsedMilliseconds + " ms");
                Assert.IsTrue(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms: waited for the deadline");
            }
        }

        // ── The Safe Mode quit wait ───────────────────────────────────────────

        private const string QuitAnswered = "/quit\r\nYou are in Safe Mode. Quitting will unroll changes. Quit? [y/N]: y\r\ninterrupted\r\n";

        /// <summary>
        /// X6 (D4): after <c>/quit</c> and the <c>y</c>, the close waits for the router to end the session (the PTY
        /// carrier closing, MAC-Telnet's <c>PKT_END</c>, WinBox's <c>Mepty.Close</c>) and not longer: a close racing the
        /// <c>y</c> wedged RouterOS 6.49.13's console. RouterOS closes the WinBox terminal 60–220 ms after the keys.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X6_TheSafeModeQuitWait_EndsWhenTheRouterEndsTheSession(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, QuitAnswered)).ThenEnd(200)))
            {
                var sw = Stopwatch.StartNew();
                f.End(answerSafeModeQuestion: true);

                Assert.AreEqual("/quit\ry", string.Concat(f.Inputs), "the keys, and nothing typed after them");
                Assert.IsTrue(sw.ElapsedMilliseconds >= 190, sw.ElapsedMilliseconds + " ms: the close did not wait for the router");
                Assert.IsTrue(sw.ElapsedMilliseconds < 450, sw.ElapsedMilliseconds + " ms: the close waited past the router's end");
            }
        }

        /// <summary>X6: a router that never ends the session holds the close no longer than the bound.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X6_TheSafeModeQuitWait_IsBounded(TerminalCarrier carrier)
        {
            using (var f = Fixture(carrier, RouterScript.Answer((0, QuitAnswered))))
            {
                var sw = Stopwatch.StartNew();
                f.End(answerSafeModeQuestion: true);

                Assert.IsTrue(sw.ElapsedMilliseconds < RouterOsCliLogin.SafeModeQuitWaitMs + 500, sw.ElapsedMilliseconds + " ms");
            }
        }

        // ── The caller's token ────────────────────────────────────────────────

        /// <summary>Which read a cancel case drives.</summary>
        public enum ReadKind { Command, RawKey, Quiet }

        /// <summary>
        /// X3: the token reaches every read, on every engine. Production passes <see cref="CancellationToken.None"/>
        /// except in <see cref="TikCancellationMode.AbandonAndClose"/>, and there a cancelled read must end at the cancel,
        /// not at the end of its window. The router never answers; the read would run several seconds without the cancel.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty, ReadKind.Command)]
        [DataRow(TerminalCarrier.Pty, ReadKind.RawKey)]
        [DataRow(TerminalCarrier.Pty, ReadKind.Quiet)]
        [DataRow(TerminalCarrier.MacTelnet, ReadKind.Command)]
        [DataRow(TerminalCarrier.MacTelnet, ReadKind.RawKey)]
        [DataRow(TerminalCarrier.MacTelnet, ReadKind.Quiet)]
        [DataRow(TerminalCarrier.WinboxCli, ReadKind.Command)]
        [DataRow(TerminalCarrier.WinboxCli, ReadKind.RawKey)]
        [DataRow(TerminalCarrier.WinboxCli, ReadKind.Quiet)]
        public void X3_ACancelledRead_EndsAtTheCancel(TerminalCarrier carrier, ReadKind kind)
        {
            using (var f = Fixture(carrier, RouterScript.Silent(), 10000))
            using (var cts = new CancellationTokenSource())
            {
                Task<string> read;
                switch (kind)
                {
                    case ReadKind.Command: read = f.CommandAsync(Command, cts.Token); break;
                    case ReadKind.RawKey:  read = f.RawAsync(new byte[] { 0x18 }, cts.Token); break;
                    default:               read = f.QuietAsync(Encoding.ASCII.GetBytes("/zz\t"), 2000, cts.Token); break;
                }
                Thread.Sleep(200);
                var sw = Stopwatch.StartNew();
                cts.Cancel();

                Exception caught = null;
                try { Run(read); }
                catch (Exception ex) { caught = ex; }

                Assert.IsInstanceOfType(caught, typeof(OperationCanceledException), caught?.ToString() ?? "the read returned");
                Assert.IsTrue(sw.ElapsedMilliseconds < 1000, sw.ElapsedMilliseconds + " ms after the cancel");
            }
        }

        // ── Where the engines differ today: pinned as they are ───────────────

        private const string Residue = "\r\nRESIDUE\r\n" + Prompt;

        /// <summary>
        /// X4 (D6): residue (a late repaint left unread) is not read into a raw key's answer on any engine: the PTY and
        /// the WinBox CLI drain it first, as before a command, and MAC-Telnet discards what its pump collected.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X4_ResidueBeforeARawKey_IsNotInTheAnswer(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, "\r\n[admin@CHR2] <SAFE> "));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script))
            {
                string answer = Run(f.RawAsync(new byte[] { 0x18 }));

                StringAssert.Contains(answer, "<SAFE>");
                Assert.IsFalse(answer.Contains("RESIDUE"), answer);
            }
        }

        /// <summary>X4, the quiet read: the residue does not reach the completion parser.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X4_ResidueBeforeATab_IsNotInTheListing(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, "\r\nsystem  \r\n" + Prompt + "/sys"));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script, 10000))
            {
                string answer = Run(f.QuietAsync(Encoding.ASCII.GetBytes("/sys\t"), 150));

                StringAssert.Contains(answer, "system");
                Assert.IsFalse(answer.Contains("RESIDUE"), answer);
            }
        }

        /// <summary>
        /// X4, why it matters: a residue's prompt ends a Safe Mode key's read before the router's answer arrives, and
        /// <see cref="CliSafeModeParser"/> misses the conflict question — which then waits for its answer, and the
        /// next line typed gives it one.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X4_ResidueBeforeASafeModeKey_DoesNotHideTheConflictQuestion(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((300, "\r\nSafe Mode is taken by current user in another session. Unroll, release or abort [u/r]?"));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script, 1500))
            {
                string answer = Run(f.RawAsync(new byte[] { 0x18 }));

                Assert.IsTrue(CliSafeModeParser.IsTakeConflict(answer), Escape(answer));
            }
        }

        /// <summary>X4, why it matters: a residue starts the quiet window, and a listing slower than it is lost.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X4_ResidueBeforeATab_DoesNotEndTheReadBeforeASlowListing(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((250, "\r\nsystem  \r\n" + Prompt + "/sys"));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script, 10000))
            {
                string answer = Run(f.QuietAsync(Encoding.ASCII.GetBytes("/sys\t"), 150));

                StringAssert.Contains(answer, "system", Escape(answer));
            }
        }

        /// <summary>
        /// T6, the command prologue: an interrupt requested once the command call has begun belongs to it, also while the
        /// residue of the previous read is still being drained; only a request from before the call is void.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void AnInterruptRequestedWhileTheResidueIsDrained_ReachesTheCommand(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, Command + "\r\npartial\r\n"));
            script.Residue = Residue;
            script.OnCtrlC = "\r\n" + Prompt;
            using (var f = Fixture(carrier, script, 10000))
            {
                var sw = Stopwatch.StartNew();
                Task<string> read = f.CommandAsync(Command);
                Thread.Sleep(40);
                f.RequestInterrupt();
                string answer = Run(read);

                StringAssert.Contains(answer, "partial");
                Assert.AreEqual(1, f.Inputs.Count(i => i == "\x03"), "one Ctrl-C");
                Assert.IsTrue(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms: the request was lost");
            }
        }

        /// <summary>X4's contrast: before a command, residue is drained on all three.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void ResidueBeforeACommand_IsDrained(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, Command + "\r\nCHR2\r\n" + Prompt));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script))
            {
                Assert.AreEqual("CHR2", Run(f.CommandAsync(Command)).Trim());
            }
        }

        /// <summary>
        /// X5 (D2): the carrier ends in the same poll as a complete counted answer, and the answer wins on every engine —
        /// the router printed its own count and the prompt, so the command ran and the answer is whole.
        /// </summary>
        /// <remarks>
        /// "The same poll" per carrier: on the PTY the last chunk and the close are both there when the reader looks; on
        /// the WinBox CLI the <c>Mepty.Close</c> frame carries the last chunk; on MAC-Telnet the <c>PKT_END</c> is sent
        /// just ahead of the last datagram, the order the read can see an end before the text that came with it.
        /// </remarks>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X5_ACloseWithACompleteCountedAnswer_TheAnswerWins(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, CountedTyped + "\r\n" + CountedAnswer + Prompt)).EndingWithTheLastChunk();
            using (var f = Fixture(carrier, script))
            {
                StringAssert.Contains(Run(f.CommandAsync(Counted)), "name=CHR2");
            }
        }

        /// <summary>X5: the end just after the answer, in its own datagram, frame or read — the order the router sends them in.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X5_ACloseJustAfterACompleteCountedAnswer_TheAnswerWins(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, CountedTyped + "\r\n" + CountedAnswer + Prompt)).ThenEnd(2);
            using (var f = Fixture(carrier, script))
            {
                StringAssert.Contains(Run(f.CommandAsync(Counted)), "name=CHR2");
            }
        }

        /// <summary>X5's contrast: an answer without its count line is not whole, and the close that cuts it is reported.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X5_ACloseWithAnUncountedPrompt_IsReportedAsClosed(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, Command + "\r\nCHR2\r\n" + Prompt)).EndingWithTheLastChunk();
            using (var f = Fixture(carrier, script))
            {
                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => Run(f.CommandAsync(Command)));
                StringAssert.Contains(ex.Message, "closed the connection");
            }
        }

        private static readonly Regex CursorReport = new Regex("^\x1b\\[\\d+;\\d+R$");

        /// <summary>
        /// X7 (D3): a change-password prompt during a command read (a caller's <c>/password</c>). A cursor probe's reply
        /// is held back on every engine: typed into the prompt it would be password input.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X7_ACursorProbeAtAPasswordPromptInACommandRead_IsNotAnswered(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, "/password\r\nnew password> "), (100, "\x1b[6n"), (400, "\r\n" + Prompt));
            using (var f = Fixture(carrier, script))
            {
                Run(f.CommandAsync("/password"));

                Assert.AreEqual(0, f.Inputs.Count(i => CursorReport.IsMatch(i)), string.Join(" | ", f.Inputs.Select(Escape)));
            }
        }

        /// <summary>X7, the quiet read: the same prompt reached by a key read until quiet.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X7_ACursorProbeAtAPasswordPromptInAQuietRead_IsNotAnswered(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, "\r\nnew password> "), (50, "\x1b[6n"));
            using (var f = Fixture(carrier, script, 10000))
            {
                Run(f.QuietAsync(Encoding.ASCII.GetBytes("/password\r"), 300));

                Assert.AreEqual(0, f.Inputs.Count(i => CursorReport.IsMatch(i)), string.Join(" | ", f.Inputs.Select(Escape)));
            }
        }

        /// <summary>X7's contrast: away from the prompt, a probe in a command read is answered on every engine.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty)]
        [DataRow(TerminalCarrier.MacTelnet)]
        [DataRow(TerminalCarrier.WinboxCli)]
        public void X7_ACursorProbeInACommandRead_IsAnswered(TerminalCarrier carrier)
        {
            var script = RouterScript.Answer((0, Command + "\r\n"), (50, "\x1b[6n"), (100, "CHR2\r\n" + Prompt));
            using (var f = Fixture(carrier, script))
            {
                Assert.AreEqual("CHR2", Run(f.CommandAsync(Command)).Trim());

                Assert.AreEqual(1, f.Inputs.Count(i => CursorReport.IsMatch(i)), string.Join(" | ", f.Inputs.Select(Escape)));
            }
        }

        private static string Escape(string s) => s.Replace("\x1b", "\\e").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
