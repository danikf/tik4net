using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The RoMON SSH relay (<see cref="RouterOsCliLogin.RomonSshLoginAsync"/>) against transcripts recorded on
    /// a 7.24.4 agent relaying to a 7.17rc3 target, 2026-09-19. Ids and identities are placeholders.
    /// </summary>
    /// <remarks>
    /// The failure modes here are silent on the wire: an unreachable target and RoMON switched off on the
    /// agent both answer with nothing but the AGENT's prompt, which a prompt-shape check cannot tell from the
    /// target's. Each test pins which screen means what, and what the client must never send.
    /// </remarks>
    [TestClass]
    public class RomonSshRelayTranscriptTests
    {
        private const string Target = "AA:BB:CC:DD:EE:FF";
        private const string AgentId = "AA:BB:CC:00:00:01";
        private const string Password = "pw";

        // The login name carries the +c flag, and '+' is quoted like any unsafe character.
        private const string SshLine = "line:/tool romon ssh address=" + Target + " user=\"test+c\"";

        // The agent echoes the command, saves the cursor, and the target's ssh server asks for the password.
        private const string PasswordPrompt =
            "/tool romon ssh address=" + Target + " user=test+c\r\n\r\u001b7\u001b8password: ";

        // Nothing but the agent's own prompt: an unknown RoMON id and RoMON disabled on the agent look the same.
        private const string WelcomeBack =
            "/tool romon ssh address=" + Target + " user=test+c\r\n\r\u001b7\u001b8\r\u001b[9999B\r\u001b[9999B" +
            "\r\nWelcome back!\r\n\r\r\r\u001b[9999B[admin@Agent] > ";

        // The target's banner carries its recent critical log lines — here a login FAILURE, on a login that
        // succeeds. Then the prompt (no colour: +c went through the relay).
        private const string TargetBannerAndPrompt =
            "\r\n\r\u001b[9999B\u001b[6n\r\n  MikroTik RouterOS 7.17rc3 (c) 1999-2024       https://www.mikrotik.com/\r\n" +
            "\r\nPress F1 for help\r\n\r\n2026-09-19 12:52:57 system,error,critical login failure for user test from " +
            AgentId + " by romon " + AgentId + " via ssh\r\n\r\r\r\u001b[9999B[test@Target] > ";

        // RouterOS repaints the prompt once more after a login; it arrives before the next command's echo.
        private const string StalePrompt = "\r\r\r\u001b[9999B[test@Target] > ";

        private static string IdAnswer(string id, string prompt = "[test@Target] > ")
            => ":put [/tool romon get current-id]\r\n\r" + id + "\r\n\r\r\r\u001b[9999B" + prompt;

        private static string EnabledAnswer(string value)
            => ":put [/tool romon get enabled]\r\n\r" + value + "\r\n\r\r\r\u001b[9999B[admin@Agent] > ";

        private static string[] Lines(FakeRouterTerminal term)
            => term.Sent.Select(s => s.ToString()).ToArray();

        // ── the happy path ────────────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_SendsCommandPasswordAndConfirmation_AndSettlesOnTheTarget()
        {
            var term = new FakeRouterTerminal()
                .Emits(PasswordPrompt)
                .Emits(TargetBannerAndPrompt)
                .Emits(StalePrompt)
                .Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);

            CollectionAssert.AreEqual(
                new[] { SshLine, "line:" + Password,
                        "line::put [/tool romon get current-id]" },
                Lines(term));
            Assert.AreEqual(0, term.DeadlineHits, "every phase must be recognised, none may wait for the deadline");
        }

        [TestMethod]
        public async Task Relay_ALoggedLoginFailureInTheBanner_IsNotARefusal()
        {
            // The positional rule: only a second password prompt is a refusal. The banner above contains the
            // literal text 'login failure', which the ordinary login's phrase table would stop on.
            var term = new FakeRouterTerminal()
                .Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);
        }

        [TestMethod]
        public async Task Relay_AcceptsTheIdInAnyCaseAndWithDashes()
        {
            var term = new FakeRouterTerminal()
                .Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync("aa-bb-cc-dd-ee-ff");

            Assert.AreEqual(SshLine, Lines(term)[0]);
        }

        [TestMethod]
        public async Task Relay_DeclinesTheTargetsChangePasswordNagWithCtrlC()
        {
            var term = new FakeRouterTerminal()
                .Emits(PasswordPrompt)
                .Emits("\r\nPress F1 for help\r\n\r\nChange your password\r\n\r\nnew password> ")
                .Emits("\r\n\r\r\r\u001b[9999B[test@Target] > ")
                .Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);

            CollectionAssert.Contains(Lines(term), "bytes:03");
        }

        // ── the relay never starts ────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_AgentPromptBeforePassword_WithRomonEnabled_ReportsTheTargetUnreachable()
        {
            var term = new FakeRouterTerminal().Emits(WelcomeBack).Emits(EnabledAnswer("true"));

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.RomonSshLoginAsync(Target));

            StringAssert.Contains(ex.Message, "could not reach RoMON id " + Target);
            Assert.IsFalse(Lines(term).Contains("line:" + Password), "the password must never reach the agent's shell");
        }

        [TestMethod]
        public async Task Relay_AgentPromptBeforePassword_WithRomonDisabled_SaysSo()
        {
            var term = new FakeRouterTerminal().Emits(WelcomeBack).Emits(EnabledAnswer("false"));

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.RomonSshLoginAsync(Target));

            StringAssert.Contains(ex.Message, "RoMON is not enabled on the agent");
        }

        // ── the target refuses ────────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_SecondPasswordPrompt_IsARefusal_LeftWithCtrlC_AndNothingElseIsTyped()
        {
            var term = new FakeRouterTerminal().Emits(PasswordPrompt).Emits("\r\npassword: ");

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.RomonSshLoginAsync(Target));

            StringAssert.Contains(ex.Message, "'ssh' policy");
            CollectionAssert.AreEqual(
                new[] { SshLine, "line:" + Password, "bytes:03" },
                Lines(term),
                "one password, then Ctrl-C: every further line typed into that prompt is another failed login on the target");
        }

        // ── the prompt reached is not the target ──────────────────────────────

        [TestMethod]
        public async Task Relay_ThePromptAnswersTheAgentsId_IsNotAcceptedAsTheTarget()
        {
            var term = new FakeRouterTerminal()
                .Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(AgentId, "[admin@Agent] > "));

            var ex = await Assert.ThrowsExceptionAsync<TikConnectionLoginException>(() => term.RomonSshLoginAsync(Target));

            StringAssert.Contains(ex.Message, AgentId);
        }

        // ── the id is spliced into a command line ─────────────────────────────

        [DataTestMethod]
        [DataRow("AA:BB:CC:DD:EE:FF user=admin")]
        [DataRow("AA:BB:CC:DD:EE")]
        [DataRow("AA:BB:CC:DD:EE:GG")]
        [DataRow("")]
        public void NormalizeRomonId_RefusesAnythingButSixHexOctets(string id)
            => Assert.ThrowsException<ArgumentException>(() => RouterOsCliLogin.NormalizeRomonId(id));

        [TestMethod]
        public void AnswerAfterEcho_IgnoresAStalePromptThatArrivesBeforeTheEcho()
        {
            string query = ":put [/tool romon get current-id]";
            Assert.IsNull(RouterOsCliLogin.AnswerAfterEcho("[test@Target] > ", query));
            Assert.IsNull(RouterOsCliLogin.AnswerAfterEcho("[test@Target] > " + query + "\r\n" + Target, query));
            Assert.AreEqual(Target, RouterOsCliLogin.AnswerAfterEcho(
                "[test@Target] > " + query + "\r\n" + Target + "\r\n[test@Target] > ", query));
        }
    }
}
