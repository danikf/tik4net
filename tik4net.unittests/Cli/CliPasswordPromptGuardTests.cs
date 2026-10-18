using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>The line tracking behind "no VT100 reply while a change-password prompt is on screen".</summary>
    [TestClass]
    public class CliPasswordPromptGuardTests
    {
        [TestMethod]
        public void AtAShellPrompt_RepliesMayBeSent()
        {
            var guard = new CliPasswordPromptGuard();
            Assert.IsTrue(guard.MayAnswer("\r\n\x1b[9999B[admin@CHR] > "));
        }

        [TestMethod]
        public void WhileTheNagIsTheCurrentLine_NothingMayBeSent()
        {
            var guard = new CliPasswordPromptGuard();
            Assert.IsFalse(guard.MayAnswer("\r\nChange your password\r\n\r\nnew password> "));
            Assert.IsFalse(guard.MayAnswer("\x1b[6n"), "a probe arriving in a later chunk is still on the nag's line");
        }

        [TestMethod]
        public void TheNagSplitAcrossChunks_IsStillSeen()
        {
            var guard = new CliPasswordPromptGuard();
            Assert.IsTrue(guard.MayAnswer("\r\nnew pass"));
            Assert.IsFalse(guard.MayAnswer("word> "));
        }

        [TestMethod]
        public void ALineBreakAndAPromptAfterTheNag_AnswerAgain()
        {
            var guard = new CliPasswordPromptGuard();
            Assert.IsFalse(guard.MayAnswer("repeat new password> "));
            Assert.IsTrue(guard.MayAnswer("^C\r\n\r\n[admin@CHR] > "));
        }

        [TestMethod]
        public void ColouredNag_IsSeenThroughItsEscapes()
        {
            var guard = new CliPasswordPromptGuard();
            Assert.IsFalse(guard.MayAnswer("\x1b[m\x1b[1mnew\x1b[m pass\x1b[32mword\x1b[m> "));
        }
    }
}
