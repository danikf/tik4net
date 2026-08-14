using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The CLI login/prompt/nag state machine, driven against recorded RouterOS transcripts (P2.24).
    /// </summary>
    /// <remarks>
    /// These cover the assumptions that are version-dependent and, until now, only ever validated by the fact
    /// that the lab router happened to agree: the banner it has to skip, the nag it has to decline, the prompt
    /// it has to recognise, and the refusal it has to notice. Each of those fails silently when wrong — an
    /// unrecognised screen does not raise anything, it waits for the receive deadline — so "the suite is
    /// green" was never evidence that any of them matched.
    /// </remarks>
    [TestClass]
    public class CliLoginTranscriptTests
    {
        private const byte CtrlC = 0x03;

        private static FakeRouterTerminal FullLoginScript() =>
            new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .Emits(RouterOsTranscripts.V7232_BannerAndNag)
                .Emits(RouterOsTranscripts.V7232_PromptAfterNag);

        // ── the happy path, end to end ────────────────────────────────────────

        [TestMethod]
        public async Task Login_7232_SendsUserThenPasswordThenCtrlC_AndSettlesAtThePrompt()
        {
            var term = FullLoginScript();

            await term.LoginAsync(user: "admin", password: "");

            CollectionAssert.AreEqual(
                new[] { "line:admin+c", "line:", "bytes:03" },
                term.Sent.Select(s => s.ToString()).ToArray(),
                "The login is exactly: user with the +c terminal flag, the (empty) password, Ctrl-C for the nag.");
            Assert.AreEqual(0, term.DeadlineHits,
                "Every phase must be recognised from what the router sent; a phase that is not costs a full " +
                "receive deadline and still 'passes'.");
        }

        /// <summary>
        /// The invariant P2.13c paid for: while the change-password nag is on screen, <b>no byte other than
        /// Ctrl-C</b> may be sent. Anything else is typed into the new-password field — that is how the lab
        /// router's admin password got changed by a client that meant to answer a VT100 probe.
        /// </summary>
        [TestMethod]
        public async Task Login_SendsNothingButCtrlC_WhileTheNagIsOnScreen()
        {
            var term = FullLoginScript();

            await term.LoginAsync();

            foreach (var item in term.Sent.Where(s => RouterOsCliLogin.IsChangePasswordNag(s.ScreenBefore)))
            {
                Assert.IsNull(item.Line, $"A line was sent into the change-password prompt: '{item.Line}'.");
                CollectionAssert.AreEqual(new byte[] { CtrlC }, item.Bytes,
                    "Only Ctrl-C may be sent while the nag is up.");
            }
        }

        /// <summary>
        /// 7.23.2 repaints the nag, so one read carries <c>new password&gt;</c> twice. It must still be
        /// declined once — a client that counts occurrences would send a second Ctrl-C into whatever came
        /// after it.
        /// </summary>
        [TestMethod]
        public async Task Login_DoubledNagRepaint_IsDeclinedExactlyOnce()
        {
            var term = FullLoginScript();

            await term.LoginAsync();

            Assert.AreEqual(1, term.Sent.Count(s => s.Bytes != null && s.Bytes[0] == CtrlC));
        }

        // ── refusals ──────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Login_RefusedCredentials_ThrowsInsteadOfWaitingOutTheDeadline()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .Emits(RouterOsTranscripts.V7232_LoginRefused);

            await AssertLoginFails(term);

            Assert.AreEqual(0, term.DeadlineHits,
                "The refusal is on screen — recognising it is what separates 1.3 s from 30 s (P2.24).");
        }

        /// <summary>
        /// The same refusal in a wording nobody here has ever seen. This is the test the phrase list cannot
        /// pass and the positional signal can: RouterOS re-offers <c>Login:</c> after rejecting credentials,
        /// which is dialogue state rather than English, so it holds on a version we have never met.
        /// </summary>
        [TestMethod]
        public async Task Login_RefusedWithUnknownWording_IsStillDetected()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .Emits("\r\nAuthentisierung fehlgeschlagen\r\n\r\nLogin: ");

            await AssertLoginFails(term);

            Assert.AreEqual(0, term.DeadlineHits,
                "A refusal must be detected from the re-offered login prompt, without recognising the text.");
        }

        /// <summary>
        /// A nag that never clears must end the login rather than loop: the Ctrl-C rounds are bounded, and
        /// what follows is an exception, not a client sitting in a password dialogue for ever.
        /// </summary>
        [TestMethod]
        public async Task Login_NagThatNeverClears_IsBounded()
        {
            var term = new FakeRouterTerminal()
                .Emits(RouterOsTranscripts.V7232_Login)
                .Emits(RouterOsTranscripts.V7232_Password)
                .EmitsRepeatedly(RouterOsTranscripts.V7232_BannerAndNag, 12);

            await AssertLoginFails(term);

            int ctrlCs = term.Sent.Count(s => s.Bytes != null && s.Bytes[0] == CtrlC);
            Assert.IsTrue(ctrlCs <= 3, $"Ctrl-C rounds must stay bounded (MaxNagRounds); sent {ctrlCs}.");
        }

        // ── the transport-authenticated path (SSH, WinBox mepty) ──────────────

        [TestMethod]
        public async Task ResolveToPrompt_SettlesAnAlreadyAuthenticatedShell()
        {
            var term = new FakeRouterTerminal().Emits(RouterOsTranscripts.V7232_PromptOnly);

            await term.ResolveToPromptAsync();

            Assert.AreEqual(0, term.Sent.Count, "Nothing is typed at a shell that is already at its prompt.");
        }

        /// <summary>
        /// The positional refusal signal is scoped to the interactive login. A transport that authenticated
        /// below the terminal never sends credentials, so a <c>Login:</c> string reaching it (a MOTD, a
        /// scrollback line) is not evidence of anything and must not fail the session.
        /// </summary>
        [TestMethod]
        public async Task ResolveToPrompt_DoesNotReadALoginPromptAsRefusal_WhenNoCredentialsWereSent()
        {
            var term = new FakeRouterTerminal()
                .Emits("\r\nLast Login: 2026-08-14 22:15:15\r\n")
                .Emits(RouterOsTranscripts.V7232_PromptOnly);

            await term.ResolveToPromptAsync();   // must not throw
        }

        // ── prompt shapes ─────────────────────────────────────────────────────

        [TestMethod]
        public void PromptShapes_7232_AreRecognisedAsEmitted()
        {
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt(
                VtStripper.StripAnsi(RouterOsTranscripts.V7232_PromptOnly)));
            Assert.IsTrue(RouterOsCliLogin.IsShellPrompt(
                VtStripper.StripAnsi(RouterOsTranscripts.V7232_SafeModePrompt)),
                "Safe Mode replaces the '>' with the token — the arrow-bearing form is not what 7.23.2 sends (P2.31).");
        }

        [TestMethod]
        public void BannerIsNotMistakenForAPrompt()
        {
            string banner = VtStripper.StripAnsi(RouterOsTranscripts.V7232_BannerAndNag);

            Assert.IsFalse(RouterOsCliLogin.IsShellPrompt(banner));
            Assert.IsTrue(RouterOsCliLogin.IsChangePasswordNag(banner));
            Assert.IsFalse(RouterOsCliLogin.IsLoginFailure(banner),
                "The banner mentions neither a failure nor anything that should read as one.");
        }

        private static async Task AssertLoginFails(FakeRouterTerminal term)
        {
            try
            {
                await term.LoginAsync(user: "admin", password: "wrong");
                Assert.Fail("A refused login must throw TikConnectionLoginException.");
            }
            catch (TikConnectionLoginException)
            {
                // expected
            }
        }
    }
}
