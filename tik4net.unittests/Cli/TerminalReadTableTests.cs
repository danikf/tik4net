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

        // ── Where the engines differ today: pinned as they are ───────────────

        private const string Residue = "\r\nRESIDUE\r\n" + Prompt;

        /// <summary>
        /// X4: a raw key reads residue (a late repaint left unread) into its answer on the PTY and the WinBox CLI;
        /// MAC-Telnet discards it before the key goes out. A command drains residue on all three. Decision D6.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty, true)]
        [DataRow(TerminalCarrier.MacTelnet, false)]
        [DataRow(TerminalCarrier.WinboxCli, true)]
        public void X4_ResidueBeforeARawKey_IsReadIntoTheAnswer_ExceptOnMacTelnet(TerminalCarrier carrier, bool residueInAnswer)
        {
            var script = RouterScript.Answer((0, "\r\n[admin@CHR2] <SAFE> "));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script))
            {
                string answer = Run(f.RawAsync(new byte[] { 0x18 }));

                StringAssert.Contains(answer, "<SAFE>");
                Assert.AreEqual(residueInAnswer, answer.Contains("RESIDUE"), answer);
            }
        }

        /// <summary>X4, the quiet read: the residue reaches the completion parser on the PTY and the WinBox CLI.</summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty, true)]
        [DataRow(TerminalCarrier.MacTelnet, false)]
        [DataRow(TerminalCarrier.WinboxCli, true)]
        public void X4_ResidueBeforeATab_IsReadIntoTheListing_ExceptOnMacTelnet(TerminalCarrier carrier, bool residueInAnswer)
        {
            var script = RouterScript.Answer((0, "\r\nsystem  \r\n" + Prompt + "/sys"));
            script.Residue = Residue;
            using (var f = Fixture(carrier, script, 10000))
            {
                string answer = Run(f.QuietAsync(Encoding.ASCII.GetBytes("/sys\t"), 150));

                StringAssert.Contains(answer, "system");
                Assert.AreEqual(residueInAnswer, answer.Contains("RESIDUE"), answer);
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
        /// X5: the carrier ends in the same poll as a complete counted answer. The PTY returns the answer (it asks the
        /// settle first); MAC-Telnet and the WinBox CLI report the close (they check the end first). Decision D2.
        /// </summary>
        /// <remarks>
        /// "The same poll" per carrier: on the PTY the last chunk and the close are both there when the reader looks; on
        /// the WinBox CLI the <c>Mepty.Close</c> frame carries the last chunk; on MAC-Telnet the <c>PKT_END</c> is sent
        /// just ahead of the last datagram, so any poll that sees the chunk has seen the end too (the pump keeps
        /// appending after a <c>PKT_END</c>) and the pin does not depend on when the reader wakes.
        /// </remarks>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty, true)]
        [DataRow(TerminalCarrier.MacTelnet, false)]
        [DataRow(TerminalCarrier.WinboxCli, false)]
        public void X5_ACloseWithACompleteCountedAnswer_WinsOnlyOffThePty(TerminalCarrier carrier, bool answerWins)
        {
            var script = RouterScript.Answer((0, CountedTyped + "\r\n" + CountedAnswer + Prompt)).EndingWithTheLastChunk();
            using (var f = Fixture(carrier, script))
            {
                if (answerWins)
                {
                    StringAssert.Contains(Run(f.CommandAsync(Counted)), "name=CHR2");
                }
                else
                {
                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(() => Run(f.CommandAsync(Counted)));
                    StringAssert.Contains(ex.Message, "closed the connection");
                }
            }
        }

        /// <summary>
        /// X7: a change-password prompt during a command read (a caller's <c>/password</c>). The PTY holds a cursor
        /// probe's reply back on every read; MAC-Telnet and the WinBox CLI hold it back during the login and its drain
        /// only, so here they type the reply into the prompt. Decision D3.
        /// </summary>
        [DataTestMethod]
        [DataRow(TerminalCarrier.Pty, 0)]
        [DataRow(TerminalCarrier.MacTelnet, 1)]
        [DataRow(TerminalCarrier.WinboxCli, 1)]
        public void X7_ACursorProbeAtAPasswordPromptInACommandRead_IsAnsweredOffThePty(TerminalCarrier carrier, int replies)
        {
            var script = RouterScript.Answer((0, "/password\r\nnew password> "), (100, "\x1b[6n"), (400, "\r\n" + Prompt));
            using (var f = Fixture(carrier, script))
            {
                Run(f.CommandAsync("/password"));

                var cursorReport = new Regex("^\x1b\\[\\d+;\\d+R$");
                Assert.AreEqual(replies, f.Inputs.Count(i => cursorReport.IsMatch(i)), string.Join(" | ", f.Inputs.Select(Escape)));
            }
        }

        private static string Escape(string s) => s.Replace("\x1b", "\\e").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
