using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary><see cref="CliPromptText.JudgePrompt"/>: whether a settled-looking prompt ends a counted read.</summary>
    [TestClass]
    public class CliPromptTextTests
    {
        private const string Prompt = "[admin@MikroTik] > ";

        // ── Ending a read on the expected pattern (JudgePrompt) ────────────────

        private const string Counted = ":local d [/ip address print as-value]; :put [:len $d]; :put (\"#n=\" . [:len $d] . \"/\" . [:typeof [:find $d [:pick $d 0]]])";
        private const string Windowed = ":local w [:pick [/ip address find] 0 20]; :put (\"#w=\" . [:len $w])";

        [TestMethod]
        public void JudgePrompt_ACountedReadIsCompleteAtItsCountLineAndThePrompt()
        {
            Assert.AreEqual(CliPromptText.PromptVerdict.Complete,
                CliPromptText.JudgePrompt(Counted + "\r\n\rrow\r\n#n=1/num\r\n\r\r\r" + Prompt, Counted));
            Assert.AreEqual(CliPromptText.PromptVerdict.Complete,
                CliPromptText.JudgePrompt(Windowed + "\r\n\rrow\r\n#w=1\r\n\r\r\r" + Prompt, Windowed));
        }

        [TestMethod]
        public void JudgePrompt_ACountLineFollowedByALogLineIsStillComplete()
        {
            string raw = Counted + "\r\n\r#n=0/nil\r\n\r\r\r" + Prompt
                       + "\r23:30:37 echo: system,error,critical login failure for user admin from 192.0.2.31 via winbox\r\n\r" + Prompt;
            Assert.AreEqual(CliPromptText.PromptVerdict.Complete, CliPromptText.JudgePrompt(raw, Counted));
        }

        // A console log line between the echo and the answer, and the prompt RouterOS repaints after it. The router holds
        // such a line while a command runs (Docs/findings-cli.md §7); the rule guards against it all the same.
        [TestMethod]
        public void JudgePrompt_ARepaintBeforeTheAnswerIsNotItsEnd()
        {
            string raw = Counted + "\r\n\r23:30:37 echo: system,error,critical login failure for user admin from 192.0.2.31 via winbox\r\n\r" + Prompt;
            Assert.AreEqual(CliPromptText.PromptVerdict.NotYet, CliPromptText.JudgePrompt(raw, Counted));
            Assert.AreEqual(CliPromptText.PromptVerdict.NotYet, CliPromptText.JudgePrompt(Counted + "\r\n\r\r\r" + Prompt, Counted),
                "the echo carries the marker in quotes; it must not be taken for the count line");
        }

        [TestMethod]
        public void JudgePrompt_ARefusedCountedReadSettlesAsBefore()
        {
            string raw = Counted + "\r\n\rsyntax error (line 1 column 12)\r\n\r\r\r" + Prompt;
            Assert.AreEqual(CliPromptText.PromptVerdict.Settle, CliPromptText.JudgePrompt(raw, Counted));
        }

        [TestMethod]
        public void JudgePrompt_AnyOtherCommandSettlesAsBefore()
        {
            const string write = "/ip address remove numbers=*5";
            Assert.AreEqual(CliPromptText.PromptVerdict.Settle, CliPromptText.JudgePrompt(write + "\r\n" + Prompt, write));
            Assert.AreEqual(CliPromptText.PromptVerdict.Settle, CliPromptText.JudgePrompt(Prompt, null));
        }
    }
}
