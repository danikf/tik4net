using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    /// agent both answer with nothing but <c>Welcome back!</c> — followed, without the trailing <c>/quit</c>, by
    /// the AGENT's prompt, which a prompt-shape check cannot tell from the target's. Each test pins which screen
    /// means what, and what the client must never send.
    /// </remarks>
    [TestClass]
    public class RomonSshRelayTranscriptTests
    {
        private const string Target = "AA:BB:CC:DD:EE:FF";
        private const string AgentId = "AA:BB:CC:00:00:01";
        private const string Password = "pw";

        private const string IdQuery = ":put [/tool romon get current-id]";
        private const string IdQueryLine = "line:" + IdQuery;

        private const string EnabledQueryLine = "line::put [/tool romon get enabled]";

        // The login name carries the +c flag, and '+' is quoted like any unsafe character. The trailing /quit
        // ends the agent's session when the relay ends, so no later command can run on the agent.
        private const string SshCommand = "/tool romon ssh address=" + Target + " user=\"test+c\"; /quit";
        private const string SshLine = "line:" + SshCommand;

        // The agent echoes the command, saves the cursor, and the target's ssh server asks for the password.
        private const string PasswordPrompt = SshCommand + "\r\n\r\u001b7\u001b8password: ";

        // An unknown RoMON id: a pause, then 'Welcome back!' — and the /quit ends the agent's session, so no
        // prompt follows.
        private const string WelcomeBack =
            SshCommand + "\r\n\r\u001b7\u001b8\r\u001b[9999B\r\u001b[9999B\r\nWelcome back!\r\ninterrupted\r\n\u001b7\u001b8";

        // The same screen had the /quit not run: the agent's own prompt returns.
        private const string WelcomeBackToTheAgentsPrompt =
            SshCommand + "\r\n\r\u001b7\u001b8\r\u001b[9999B\r\u001b[9999B" +
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
            => IdQuery + "\r\n\r" + id + "\r\n\r\r\r\u001b[9999B" + prompt;

        private static string EnabledAnswer(string value)
            => ":put [/tool romon get enabled]\r\n\r" + value + "\r\n\r\r\r\u001b[9999B[admin@Agent] > ";

        // Every relay starts by asking the agent, on its own shell, for its RoMON id and whether RoMON is on.
        private static FakeRouterTerminal AgentTerminal(string romonEnabled = "true")
            => new FakeRouterTerminal().Emits(IdAnswer(AgentId, "[admin@Agent] > ")).Emits(EnabledAnswer(romonEnabled));

        private static string[] Lines(FakeRouterTerminal term)
            => term.Sent.Select(s => s.ToString()).ToArray();

        // ── the happy path ────────────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_AsksTheAgentForItsId_ThenRelays_ConfirmsTheTarget_AndReturnsTheAgentsId()
        {
            var term = AgentTerminal()
                .Emits(PasswordPrompt)
                .Emits(TargetBannerAndPrompt)
                .Emits(StalePrompt)
                .Emits(IdAnswer(Target));

            string agentId = await term.RomonSshLoginAsync(Target);

            Assert.AreEqual(AgentId, agentId);
            CollectionAssert.AreEqual(
                new[] { IdQueryLine, EnabledQueryLine, SshLine, "line:" + Password, IdQueryLine }, Lines(term));
            Assert.AreEqual(0, term.DeadlineHits, "every phase must be recognised, none may wait for the deadline");
        }

        [TestMethod]
        public async Task Relay_ALoggedLoginFailureInTheBanner_IsNotARefusal()
        {
            // The positional rule: only a second password prompt is a refusal. The banner above contains the
            // literal text 'login failure', which the ordinary login's phrase table would stop on.
            var term = AgentTerminal().Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);
        }

        [TestMethod]
        public async Task Relay_AcceptsTheIdInAnyCaseAndWithDashes()
        {
            var term = AgentTerminal().Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync("aa-bb-cc-dd-ee-ff");

            Assert.AreEqual(SshLine, Lines(term)[2]);
        }

        [TestMethod]
        public async Task Relay_DeclinesTheTargetsChangePasswordNagWithCtrlC()
        {
            var term = AgentTerminal()
                .Emits(PasswordPrompt)
                .Emits("\r\nPress F1 for help\r\n\r\nChange your password\r\n\r\nnew password> ")
                .Emits("\r\n\r\r\r\u001b[9999B[test@Target] > ")
                .Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);

            CollectionAssert.Contains(Lines(term), "bytes:03");
        }

        [TestMethod]
        public async Task Relay_ATargetUserWithAnEmptyPassword_GetsNoPasswordPrompt_OnlyTheNag_WhichIsDeclined()
        {
            // 7.24.4 target, user with an empty password: the ssh client logs straight in, and the first thing on
            // screen after the echo is the banner and the change-password nag — never a 'password:'. Typing the
            // password there would be typing a NEW password for the target's account.
            var term = AgentTerminal()
                .Emits(SshCommand + "\r\n\r78\r[9999B[6n\r\n  MikroTik RouterOS 7.24.4 (c) 1999-2026" +
                       "       https://www.mikrotik.com/\r\n\r\n\r\nPress F1 for help\r\n\r\n\r\n\r\n" +
                       "Change your password (Ctrl-C to skip)\r\n\r\r\r[9999Bnew password> ")
                .Emits("\r\n\r\r\r[9999B[test@Target] > ")
                .Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target, password: "");

            CollectionAssert.AreEqual(new[] { IdQueryLine, EnabledQueryLine, SshLine, "bytes:03", IdQueryLine }, Lines(term),
                "only Ctrl-C may reach the nag; no line may be typed into it");
            Assert.AreEqual(0, term.DeadlineHits, "the nag must be recognised, not waited out");
        }

        // ── the relay never starts ────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_WelcomeBackBeforePassword_ReportsTheTargetUnreachable_WithoutWaitingForAPrompt()
        {
            var term = AgentTerminal().Emits(WelcomeBack);

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.TargetUnreachable, ex.Reason);
            StringAssert.Contains(ex.Message, "could not reach RoMON id " + Target);
            Assert.IsFalse(Lines(term).Contains("line:" + Password), "the password must never reach the agent's shell");
            Assert.AreEqual(0, term.DeadlineHits, "after the /quit the agent's session is gone; no prompt will come");
        }

        [TestMethod]
        public async Task Relay_ALogLineAndTheAgentsRepaintBeforeThePasswordPrompt_IsNotTheEnd()
        {
            // Under load (4 legs, 2026-10-02) another session's login failure is written into the agent's session
            // between the echo and the target's password prompt, and RouterOS repaints the agent's prompt after it.
            // Taken for the relay's answer, it read as "could not reach" — while the agent's discover listed the
            // target at one hop.
            var term = AgentTerminal()
                .Emits(SshCommand + "\r\n\r23:30:37 echo: system,error,critical login failure for user admin from "
                       + "192.0.2.31 via winbox\u001b[K\r\n\r\u001b[9999B[admin@Agent] > ")
                .Emits("\r\u001b7\u001b8password: ")
                .Emits(TargetBannerAndPrompt)
                .Emits(IdAnswer(Target));

            await term.RomonSshLoginAsync(Target);

            CollectionAssert.Contains(Lines(term), "line:" + Password);
            Assert.AreEqual(0, term.DeadlineHits);
        }

        [TestMethod]
        public async Task Relay_AnUnreachableTarget_SaysWhatTheAgentPrinted()
        {
            var term = AgentTerminal().Emits(WelcomeBack);

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            StringAssert.Contains(ex.Message, "Welcome back!");
        }

        [TestMethod]
        public async Task Relay_TheAgentsPromptBeforePassword_ReportsTheTargetUnreachable()
        {
            var term = AgentTerminal().Emits(WelcomeBackToTheAgentsPrompt);

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.TargetUnreachable, ex.Reason);
        }

        [TestMethod]
        public async Task Relay_RomonDisabledOnTheAgent_SaysSo_AndNeverStartsTheRelay()
        {
            var term = AgentTerminal(romonEnabled: "false");

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.RomonNotEnabledOnAgent, ex.Reason);
            CollectionAssert.AreEqual(new[] { IdQueryLine, EnabledQueryLine }, Lines(term));
        }

        [TestMethod]
        public async Task Relay_EndingAfterThePassword_IsNotMistakenForTheTarget()
        {
            // Had the /quit not run, the agent's prompt would follow 'Welcome back!' — a prompt, not the target's.
            var term = AgentTerminal().Emits(PasswordPrompt)
                .Emits("\r\n\r\u001b[9999B\r\nWelcome back!\r\n\r\r\r\u001b[9999B[admin@Agent] > ");

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.TargetDidNotRespond, ex.Reason);
            StringAssert.Contains(ex.Message, "relay ended");
        }

        [TestMethod]
        public void AnUnreachableTargetIsNotALoginFailure()
        {
            var ex = new TikRomonRelayException(TikRomonRelayFailure.TargetUnreachable, "x");

            Assert.IsNotInstanceOfType(ex, typeof(TikConnectionLoginException), "a catch for a wrong password would take it");
            Assert.IsInstanceOfType(ex, typeof(TikConnectionException));
        }

        [TestMethod]
        public void ARelayFailureLeavesTheOpenAsItself_NotWrappedAsALoginFailure()
        {
            // Open wraps whatever the login throws in a login exception, except what already says why.
            using (var conn = new RelayFailingConnection())
            {
                var ex = Assert.ThrowsException<TikRomonRelayException>(() => conn.Open("agent", "u", "p"));

                Assert.AreEqual(TikRomonRelayFailure.TargetUnreachable, ex.Reason);
                Assert.IsTrue(conn.Closed, "the agent's session is closed");
            }
        }

        private sealed class RelayFailingConnection : CliConnectionBase
        {
            public bool Closed;

            protected override string TransportName => "RelayFailing";

            public override void Open(string host, string user, string password)
                => OpenWith(_ => throw new TikRomonRelayException(TikRomonRelayFailure.TargetUnreachable, "unreachable"),
                    (text, ct) => Task.FromResult(string.Empty), (raw, ct) => Task.FromResult(string.Empty),
                    () => Closed = true);

            public override void Open(string host, int port, string user, string password) => Open(host, user, password);
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        // ── the target refuses ────────────────────────────────────────────────

        [TestMethod]
        public async Task Relay_SecondPasswordPrompt_IsARefusal_LeftWithCtrlC_AndNothingElseIsTyped()
        {
            var term = AgentTerminal().Emits(PasswordPrompt).Emits("\r\npassword: ");

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.TargetRefusedLogin, ex.Reason);
            StringAssert.Contains(ex.Message, "'ssh' policy");
            CollectionAssert.AreEqual(
                new[] { IdQueryLine, EnabledQueryLine, SshLine, "line:" + Password, "bytes:03" },
                Lines(term),
                "one password, then Ctrl-C: every further line typed into that prompt is another failed login on the target");
        }

        // ── the prompt reached is not the target ──────────────────────────────

        [TestMethod]
        public async Task Relay_ThePromptAnswersTheAgentsId_IsNotAcceptedAsTheTarget_AndSaysItFellBack()
        {
            var term = AgentTerminal()
                .Emits(PasswordPrompt).Emits(TargetBannerAndPrompt).Emits(IdAnswer(AgentId, "[admin@Agent] > "));

            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() => term.RomonSshLoginAsync(Target));

            Assert.AreEqual(TikRomonRelayFailure.NotTheTarget, ex.Reason);
            StringAssert.Contains(ex.Message, "fell back to the agent");
        }

        // ── the connection breaks mid-relay ───────────────────────────────────

        [TestMethod]
        public async Task Relay_ATransportFailureIsReportedAsSuch_WithTheConnectionsExceptionInside()
        {
            var broken = new IOException("socket closed");
            var ex = await Assert.ThrowsExceptionAsync<TikRomonRelayException>(() =>
                RouterOsCliLogin.RomonSshLoginAsync(Target, "test", Password, true,
                    (predicate, ct) => Task.FromException<string>(broken),
                    (line, ct) => Task.FromResult(0),
                    (bytes, ct) => Task.FromResult(0),
                    CancellationToken.None));

            Assert.AreEqual(TikRomonRelayFailure.TransportFailed, ex.Reason);
            Assert.AreSame(broken, ex.InnerException);
        }

        [TestMethod]
        public async Task Relay_CancellationIsNotDisguisedAsARelayFailure()
            => await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                RouterOsCliLogin.RomonSshLoginAsync(Target, "test", Password, true,
                    (predicate, ct) => Task.FromException<string>(new OperationCanceledException()),
                    (line, ct) => Task.FromResult(0),
                    (bytes, ct) => Task.FromResult(0),
                    CancellationToken.None));

        // ── the id is spliced into a command line ─────────────────────────────

        [DataTestMethod]
        [DataRow("AA:BB:CC:DD:EE:FF user=admin")]
        [DataRow("AA:BB:CC:DD:EE")]
        [DataRow("AA:BB:CC:DD:EE:GG")]
        [DataRow("")]
        public void NormalizeRomonId_RefusesAnythingButSixHexOctets(string id)
            => Assert.ThrowsException<ArgumentException>(() => RouterOsCliLogin.NormalizeRomonId(id));

        // ── the relay ends while the connection is open ───────────────────────

        [TestMethod]
        public void RelayEnd_IsWelcomeBackOnALineOfItsOwn_NotTheWordsQuotedInOutput()
        {
            Assert.IsTrue(RouterOsCliLogin.IsRomonRelayEnd("/quit\r\n\rinterrupted\r\n\r\r\r\nWelcome back!\r\ninterrupted\r\n"));
            Assert.IsTrue(RouterOsCliLogin.IsRomonRelayEnd("Welcome back!"));
            Assert.IsFalse(RouterOsCliLogin.IsRomonRelayEnd(".id=*1;comment=Welcome back!;name=x"));
            Assert.IsFalse(RouterOsCliLogin.IsRomonRelayEnd("say \"Welcome back!\" to the user"));
        }

        // ── the relay's status line spliced into the target's output ───────────

        // Two rows of a 6.49.13 target's `print detail as-value`, as the relay delivered them on 2026-09-27: the
        // status line lands at any byte — inside a value and inside a field name — ended by a bare LF.
        private const string CleanRows =
            ".id=*22c6;address=198.18.1.81;comment=c #330;list=l;.id=*22c7;address=198.18.1.82;comment=c #331;list=l";

        [DataTestMethod]
        [DataRow(".id=*22c6;address=1waiting for head\n98.18.1.81;comment=c #330;list=l;.id=*22c7;address=198.18.1.82;comment=c #331;list=l")]
        [DataRow(".id=*22c6;address=198.18.1.81;comment=c #330;list=l;.id=*22c7;addrwaiting for head\ness=198.18.1.82;comment=c #331;list=l")]
        public void RelayNoise_IsRemovedFromARelayedRead_GivingBackTheTargetsExactRows(string received)
        {
            var got = CliOutputParser.ParseAsValue(VtStripper.StripAnsi(
                RouterOsCliLogin.WithoutRomonRelayNoise(Target, received)));
            var expected = CliOutputParser.ParseAsValue(CleanRows);

            Assert.AreEqual(2, got.Count);
            for (int i = 0; i < expected.Count; i++)
                CollectionAssert.AreEquivalent(expected[i].Words.ToList(), got[i].Words.ToList(), "row " + i);
        }

        [TestMethod]
        public void RelayNoise_LeftIn_CorruptsTheRow_SoADirectSessionIsTheOnlyOneLeftAlone()
        {
            const string received = ".id=*22c6;address=1waiting for head\n98.18.1.81;list=l";

            // What the parser makes of it unaided: a value that is neither the address nor an error.
            var rows = CliOutputParser.ParseAsValue(received);
            Assert.AreEqual("1waiting for head,98.18.1.81", rows[0].Words["address"]);

            // A direct session has no relay to write the line, so its text is never touched.
            Assert.AreSame(received, RouterOsCliLogin.WithoutRomonRelayNoise(null, received));
        }

        [TestMethod]
        public void RelayEnded_MidCommand_SaysTheCommandMayHaveRun_AndNeverThrowsForADirectSession()
        {
            const string screen = "/system reboot\r\n\r\nWelcome back!\r\ninterrupted\r\n";

            RouterOsCliLogin.ThrowIfRomonRelayEnded("Telnet", null, "/system reboot", screen, commandSent: true);

            var ex = Assert.ThrowsException<TikRomonRelayEndedException>(() =>
                RouterOsCliLogin.ThrowIfRomonRelayEnded("Telnet", Target, "/system reboot", screen, commandSent: true));
            Assert.IsTrue(ex.CommandMayHaveRun);
            Assert.AreEqual(screen, ex.PartialResponse);
            StringAssert.Contains(ex.Message, "may have taken effect on the target");
            StringAssert.Contains(ex.Message, "Nothing ran on the agent");
        }

        [TestMethod]
        public void RelayEnded_WhileIdle_SaysTheCommandWasNotSent()
        {
            var ex = Assert.ThrowsException<TikRomonRelayEndedException>(() =>
                RouterOsCliLogin.ThrowIfRomonRelayEnded("SSH", Target, "/ip address print",
                    "\r\nWelcome back!\r\ninterrupted\r\n", commandSent: false));
            Assert.IsFalse(ex.CommandMayHaveRun);
            StringAssert.Contains(ex.Message, "did not run");
        }

        [TestMethod]
        public void AnswerAfterEcho_IgnoresAStalePromptThatArrivesBeforeTheEcho()
        {
            Assert.IsNull(RouterOsCliLogin.AnswerAfterEcho("[test@Target] > ", IdQuery));
            Assert.IsNull(RouterOsCliLogin.AnswerAfterEcho("[test@Target] > " + IdQuery + "\r\n" + Target, IdQuery));
            Assert.AreEqual(Target, RouterOsCliLogin.AnswerAfterEcho(
                "[test@Target] > " + IdQuery + "\r\n" + Target + "\r\n[test@Target] > ", IdQuery));
        }
    }
}
