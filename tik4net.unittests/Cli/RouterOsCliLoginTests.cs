using System.Threading.Tasks;
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

        // ── a login that ran out of time is not a refused one (X26, D28) ──────

        // What RouterOS sends right after the password, before the banner: the UTF-8 cursor test
        // (Docs/findings-mactelnet.md, the VT100 probe table). Stripped, it leaves "ěH".
        private const string Utf8CursorProbe = "\u001b[H\u011bH\u001b[6n";

        /// <summary>
        /// The password went out, the router answered with its first VT100 probe, and the banner never came before
        /// the deadline. That is a login that ran out of time, not a refusal, and the message must say so.
        /// </summary>
        [TestMethod]
        public async Task Login_WhosePromptNeverCame_SaysItTimedOut_NotThatItFailed()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .Emits(Utf8CursorProbe);

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.LoginAsync());

            StringAssert.Contains(ex.Message, "shell prompt did not arrive");
            StringAssert.Contains(ex.Message, "the router had sent: '\u011bH'");
            Assert.IsFalse(ex.Message.Contains("login failed"), ex.Message);
        }

        /// <summary>The settle-only path (SSH): a shell that never draws its prompt timed out too.</summary>
        [TestMethod]
        public async Task ResolveToPrompt_WhosePromptNeverCame_SaysItTimedOut()
        {
            var term = new FakeRouterTerminal().Emits(Utf8CursorProbe);

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.ResolveToPromptAsync());

            StringAssert.Contains(ex.Message, "shell prompt did not arrive");
            Assert.IsFalse(ex.Message.Contains("login failed"), ex.Message);
        }

        /// <summary>A re-offered <c>Login:</c> after the password is a refusal, and keeps saying so.</summary>
        [TestMethod]
        public async Task Login_ReofferedAfterThePassword_StillSaysLoginFailed()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .Emits("\r\n\r\nLogin: ");

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(
                () => term.LoginAsync(password: "wrong"));

            StringAssert.Contains(ex.Message, "RouterOS CLI login failed. Server response: ");
        }

        /// <summary>A refusal phrase before the password keeps its "login failed" wording.</summary>
        [TestMethod]
        public async Task Login_RefusalPhraseBeforeThePassword_StillSaysLoginFailed()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits("admin+ct\r\nLogin failed, incorrect username or password\r\n");

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.LoginAsync());

            StringAssert.Contains(ex.Message, "RouterOS CLI login failed. Server response: ");
            StringAssert.Contains(ex.Message, "Login failed, incorrect username or password");
        }
    }
}
