using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Prompt detection. A prompt shape we fail to recognise does not throw — the read loop simply never
    /// terminates early and returns whatever accumulated by the receive deadline — so this is exactly the
    /// kind of defect that needs asserting rather than observing (P2.31).
    /// </summary>
    [TestClass]
    public class RouterOsCliLoginTests
    {
        // Captured off the wire from RouterOS 7.23.2 (channel ssh.pty, ANSI stripped):
        //   "...<ESC>[9999B[admin@CHR] <SAFE> "
        // The '>' is REPLACED by the token, it does not follow it — which is what the shipped constant
        // assumed. Every command inside safe mode therefore ran to the full 30 s receive deadline.
        [TestMethod]
        public void IsShellPrompt_RecognisesTheSafeModePromptAsRouterOsEmitsIt()
        {
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt("[admin@CHR] <SAFE> "));
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt("name=CHR\r\n[admin@CHR] <SAFE> "));
        }

        [TestMethod]
        public void IsShellPrompt_StillRecognisesTheArrowBearingSafeFormAndTheNormalPrompt()
        {
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt("[admin@CHR] <SAFE> >"));
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt("[admin@CHR] <SAFE> > "));
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt("[admin@CHR] > "));
        }

        [TestMethod]
        public void IsShellPrompt_DoesNotFireOnOutputThatMerelyMentionsSafeMode()
        {
            Assert.IsFalse(RouterOsCliLogin.IsShellPrompt("Taking Safe Mode session... Success!"));
            Assert.IsFalse(RouterOsCliLogin.IsShellPrompt("name=CHR"));
            Assert.IsFalse(RouterOsCliLogin.IsShellPrompt(""));
            Assert.IsFalse(RouterOsCliLogin.IsShellPrompt(null));
        }

        // The response to a command run inside safe mode must clean down to the data alone — the whole
        // point being that the safe-mode prompt is recognised as a prompt on both the read and clean paths.
        [TestMethod]
        public void CleanOutput_StripsTheSafeModePromptRepaints()
        {
            const string sent = ":put [/system identity print as-value]";
            string raw = sent + "\r\n"
                       + "\rname=CHR\r\n"
                       + "\r\r\r[9999B[admin@CHR] <SAFE> \r\n"
                       + "\r\r\r\r[9999B[admin@CHR] <SAFE> ";

            Assert.AreEqual("name=CHR", CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), sent));
        }
    }
}
