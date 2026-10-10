using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A control key RouterOS answers with a question ends its read at the question: no prompt follows until the
    /// question is answered.
    /// </summary>
    /// <remarks>
    /// The Safe Mode key meets the question when another session holds Safe Mode. Read up to a prompt, the take
    /// waited out the whole receive deadline before declining — and an idle MAC-Telnet console is logged out after
    /// about 30 s, so the decline never reached the router (6.49.13, 10 sessions in 34).
    /// </remarks>
    [TestClass]
    public class CliControlKeyQuestionTests
    {
        [TestMethod]
        public void TheRouterOs6HijackQuestion_EndsAControlKeyRead()
            => Assert.IsTrue(CliOutputHelper.EndsWithCompletionPrompt(
                "\r\nHijacking Safe Mode from someone - unroll/release/don't take it [u/r/d]: ", null));

        [TestMethod]
        public void TheRouterOs7ConflictQuestion_EndsAControlKeyRead()
            => Assert.IsTrue(CliOutputHelper.EndsWithCompletionPrompt(
                "Safe Mode is taken by current user in another session.\r\nUnroll, release or abort [u/r]? ", null));

        [TestMethod]
        public void TheQuitQuestion_EndsAControlKeyRead()
            => Assert.IsTrue(CliOutputHelper.EndsWithCompletionPrompt(
                "You are in Safe Mode. Quitting will unroll changes. Quit? [y/N]", null));

        [TestMethod]
        public void TheSuccessMessage_DoesNotEndTheRead()
            => Assert.IsFalse(CliOutputHelper.EndsWithCompletionPrompt("\r\n[Safe Mode taken]\r\n", null));

        [TestMethod]
        public void AnAnsweredQuestion_IsNotTheEnd()
            => Assert.IsFalse(CliOutputHelper.EndsWithCompletionPrompt(
                "Unroll, release or abort [u/r]? Action aborted.\r\n", null));

        [TestMethod]
        public void ATypedCommandsOutput_IsNotEndedByAQuestion()
            => Assert.IsFalse(CliOutputHelper.EndsWithCompletionPrompt(
                "/system/identity/print\r\nname: [a/b]", "/system/identity/print"));
    }
}
