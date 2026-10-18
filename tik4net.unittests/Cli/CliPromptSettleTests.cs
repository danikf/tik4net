using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The one rule that ends a terminal command's read, on an explicit clock: each step is what has been read so far,
    /// whether anything new arrived, and the time since the read started.
    /// </summary>
    [TestClass]
    public class CliPromptSettleTests
    {
        private const string Prompt = "[admin@CHR2] > ";
        private const string Command = ":put [/system identity get name]";
        private const int SettleMs = 120;

        private static readonly string Answered = Command + "\r\nCHR2\r\n\r\r\r" + Prompt;

        private static string Counted => CliCommandBuilder.BuildCountedRead("/system identity print as-value", false);

        private static void Step(CliPromptSettle settle, string stripped, bool newData, long ms,
            CliPromptSettle.Outcome expected, bool atPrompt)
        {
            Assert.AreEqual(expected, settle.Observe(stripped, newData, ms, stripped.Length), "outcome @" + ms + "ms");
            Assert.AreEqual(atPrompt, settle.AtPrompt, "AtPrompt @" + ms + "ms");
        }

        [TestMethod]
        public void APromptBeforeTheEcho_NeverEndsTheRead()
        {
            var settle = new CliPromptSettle(Command, SettleMs);
            Step(settle, Prompt, true, 0, CliPromptSettle.Outcome.Reading, false);
            for (long t = 15; t <= 1000; t += 15)
                Step(settle, Prompt, false, t, CliPromptSettle.Outcome.Reading, false);
            Assert.IsFalse(settle.Prompted);
        }

        [TestMethod]
        public void ASettledPrompt_EndsTheReadAfterTheWindow()
        {
            var settle = new CliPromptSettle(Command, SettleMs);
            Step(settle, Answered, true, 0, CliPromptSettle.Outcome.Reading, true);   // the window opens
            Step(settle, Answered, false, 119, CliPromptSettle.Outcome.Reading, true);
            Step(settle, Answered, false, 120, CliPromptSettle.Outcome.Settled, true);
        }

        [TestMethod]
        public void NewDataInTheWindow_RestartsIt()
        {
            var settle = new CliPromptSettle(Command, SettleMs);
            string repainted = Answered + "\r\n" + Prompt;   // still ends at a prompt that starts its line
            Step(settle, Answered, true, 0, CliPromptSettle.Outcome.Reading, true);
            Step(settle, repainted, true, 100, CliPromptSettle.Outcome.Reading, true);   // the window restarts at 100
            Step(settle, repainted, false, 219, CliPromptSettle.Outcome.Reading, true);
            Step(settle, repainted, false, 220, CliPromptSettle.Outcome.Settled, true);
        }

        [TestMethod]
        public void ACountLineAndPrompt_CompletesAtOnce()
        {
            string cmd = Counted;
            var settle = new CliPromptSettle(cmd, SettleMs);
            Step(settle, cmd + "\r\nname=CHR2\r\n" + CliCommandBuilder.CountMarker + "1/nil\r\n\r\r\r" + Prompt, true, 0,
                CliPromptSettle.Outcome.Complete, false);
        }

        [TestMethod]
        public void ACountedReadWithNothingAfterTheEcho_KeepsReading()
        {
            string cmd = Counted;
            var settle = new CliPromptSettle(cmd, SettleMs);
            string logLineAndRepaint = cmd + "\r\n12:00:01 system,info user admin logged in\r\n\r\r\r" + Prompt;
            Step(settle, logLineAndRepaint, true, 0, CliPromptSettle.Outcome.Reading, false);
            for (long t = 15; t <= 1000; t += 15)
                Step(settle, logLineAndRepaint, false, t, CliPromptSettle.Outcome.Reading, false);
        }

        [TestMethod]
        public void ARepaintCutMidEcho_KeepsReading()
        {
            // 6.49's per-character echo, cut on the repaint after the last character: the echo is on screen and the
            // buffer ends at a prompt, but typed text precedes it on its line.
            var sb = new StringBuilder();
            for (int i = 1; i <= Command.Length; i++)
                sb.Append(Command[i - 1]).Append('\r').Append(Prompt).Append(Command, 0, i);
            string cut = sb.ToString(0, sb.ToString().LastIndexOf("\r" + Prompt, System.StringComparison.Ordinal) + 1 + Prompt.Length);

            var settle = new CliPromptSettle(Command, SettleMs);
            Step(settle, cut, true, 0, CliPromptSettle.Outcome.Reading, false);
            Step(settle, cut, false, 500, CliPromptSettle.Outcome.Reading, false);
        }

        [TestMethod]
        public void AControlKey_SettlesAtABarePromptOrAQuestion()
        {
            foreach (string answer in new[] { "\r[admin@CHR2] <SAFE> ", "\r\nHijacking Safe Mode from someone - unroll/release/don't take it [u/r/d]: " })
            {
                var settle = new CliPromptSettle(null, SettleMs);
                Step(settle, answer, true, 0, CliPromptSettle.Outcome.Reading, true);
                Step(settle, answer, false, 119, CliPromptSettle.Outcome.Reading, true);
                Step(settle, answer, false, 120, CliPromptSettle.Outcome.Settled, true);
            }
        }

        [TestMethod]
        public void TextThatLeavesThePrompt_ClosesTheWindow()
        {
            var settle = new CliPromptSettle(Command, SettleMs);
            Step(settle, Answered, true, 0, CliPromptSettle.Outcome.Reading, true);
            Step(settle, Answered + "late\r\n", true, 40, CliPromptSettle.Outcome.Reading, false);
            Step(settle, Answered + "late\r\n", false, 500, CliPromptSettle.Outcome.Reading, false);
            Step(settle, Answered + "late\r\nmore\r\n" + Prompt, true, 520, CliPromptSettle.Outcome.Reading, true);
            Step(settle, Answered + "late\r\nmore\r\n" + Prompt, false, 640, CliPromptSettle.Outcome.Settled, true);
        }

        [TestMethod]
        public void Notes_NameTheTransitions()
        {
            var notes = new List<string>();
            var settle = new CliPromptSettle(Command, SettleMs, notes.Add);
            settle.Observe(Answered, true, 0, 10);
            settle.Observe(Answered, false, 120, 10);
            CollectionAssert.AreEqual(new[] { "prompt seen @0ms (bytes=10)", "settled -> return @120ms" }, notes);

            notes.Clear();
            string cmd = Counted;
            new CliPromptSettle(cmd, SettleMs, notes.Add)
                .Observe(cmd + "\r\nname=CHR2\r\n" + CliCommandBuilder.CountMarker + "1/nil\r\n" + Prompt, true, 7, 99);
            CollectionAssert.AreEqual(new[] { "count line + prompt -> return @7ms (bytes=99)" }, notes);

            notes.Clear();
            var ended = new CliPromptSettle(Command, SettleMs, notes.Add);
            ended.Observe(Answered, true, 0, 10);
            ended.NoteEnd("TIMEOUT", 30000, 10);
            CollectionAssert.AreEqual(new[] { "prompt seen @0ms (bytes=10)", "TIMEOUT @30000ms (prompted=True, bytes=10)" }, notes);
        }
    }
}
