using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>The one login-screen rule (review D2) and the quiet rule (C25), on their own.</summary>
    [TestClass]
    public class CliLoginScreenTests
    {
        private const string Nag = "Change your password\r\nnew password> ";

        [TestMethod]
        public void TheNag_IsAnsweredThreeTimes_ThenItPersists()
        {
            var screen = new CliLoginScreen();
            Assert.AreEqual(CliLoginScreen.Step.SendCtrlC, screen.Feed(Nag));
            Assert.AreEqual(CliLoginScreen.Step.SendCtrlC, screen.Feed(Nag));
            Assert.AreEqual(CliLoginScreen.Step.SendCtrlC, screen.Feed(Nag));
            Assert.AreEqual(CliLoginScreen.Step.NagPersists, screen.Feed(Nag));
            Assert.AreEqual(3, screen.NagRounds);
        }

        [TestMethod]
        public void TheRepeatPrompt_IsTheNagToo()
            => Assert.AreEqual(CliLoginScreen.Step.SendCtrlC, new CliLoginScreen().Feed("repeat new password> "));

        [TestMethod]
        public void TheRefusal_IsRefused_WithItsLine()
        {
            var screen = new CliLoginScreen();
            Assert.AreEqual(CliLoginScreen.Step.Refused, screen.Feed("\r\nLogin failed, incorrect username or password\r\n"));
            Assert.AreEqual("Login failed, incorrect username or password", screen.Refusal("MAC-Telnet").RouterMessage);
        }

        /// <summary>The router's own log wording, which the shipped logging rules echo onto any console.</summary>
        [TestMethod]
        public void ALogLineAboutAFailedLogin_IsNotARefusal()
            => Assert.AreEqual(CliLoginScreen.Step.Prompt,
                new CliLoginScreen().Feed("login failure for user x from 10.0.0.1 via api\r\n[admin@MikroTik] > "));

        [TestMethod]
        public void ThePrompt_EndsTheLogin()
            => Assert.AreEqual(CliLoginScreen.Step.Prompt, new CliLoginScreen().Feed("MikroTik RouterOS\r\n[admin@MikroTik] > "));

        [TestMethod]
        public void AnythingElse_ReadsOn()
        {
            Assert.AreEqual(CliLoginScreen.Step.Continue, new CliLoginScreen().Feed(""));
            Assert.AreEqual(CliLoginScreen.Step.Continue, new CliLoginScreen().Feed("  MMM      MMM       KKK"));
        }

        [TestMethod]
        public void QuietWindow_SettlesAfterData_AndNotBefore()
        {
            var quiet = new CliQuietWindow(50);
            Assert.AreEqual(CliQuietWindow.Outcome.Continue, quiet.Observe(10));
            Assert.AreEqual(CliQuietWindow.Outcome.Continue, quiet.Observe(10));
            Thread.Sleep(80);
            Assert.AreEqual(CliQuietWindow.Outcome.Settled, quiet.Observe(10));
        }

        [TestMethod]
        public void QuietWindow_NothingAtAll_IsNoReaction_AfterFourWindows()
        {
            var quiet = new CliQuietWindow(30);
            Thread.Sleep(60);
            Assert.AreEqual(CliQuietWindow.Outcome.Continue, quiet.Observe(0), "one window of silence is not yet no reaction");
            Thread.Sleep(100);
            Assert.AreEqual(CliQuietWindow.Outcome.NoReaction, quiet.Observe(0));
        }

        [TestMethod]
        public void QuietWindow_WithoutRequiredData_SettlesOnSilence()
        {
            var quiet = new CliQuietWindow(30, requireData: false);
            Thread.Sleep(50);
            Assert.AreEqual(CliQuietWindow.Outcome.Settled, quiet.Observe(0));
        }
    }
}
