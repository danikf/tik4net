using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// What a terminal prints after the Safe Mode key, read as granted or refused. The texts are measured, not
    /// composed: RouterOS 6.49.13 prints the success message into the captured output, and reading it as a refusal
    /// made the caller abandon a session that held Safe Mode.
    /// </summary>
    [TestClass]
    public class CliSafeModeParserTests
    {
        private static void Take(string output) => CliSafeModeParser.ThrowIfTakeFailed(output, new TikGenericCommand());

        [TestMethod]
        public void TheSuccessMessage_IsNotARefusal()
        {
            Take("[Safe Mode taken]");                                   // 6.49.13, as captured
            Take("\r\n[Safe Mode taken]\r\n[admin@CHR2] <SAFE> ");
            Take(string.Empty);                                          // 7.x: nothing captured before the prompt
        }

        [TestMethod]
        public void AnotherSessionHoldingSafeMode_IsARefusal()
        {
            // 7.24.4, measured with a second session holding Safe Mode.
            Assert.ThrowsException<TikCommandTrapException>(() => Take(
                "Safe Mode is taken by current user in another session.\r\nUnroll, release or abort [u/r]? Action aborted.\r\n[admin@CHR] >"));
        }

        [TestMethod]
        public void TheOlderConflictWording_IsStillARefusal()
        {
            Assert.ThrowsException<TikCommandTrapException>(() => Take(
                "safe mode is taken by someone else, [u]ndo,[r]elease,[d]on't take - which one?"));
        }
    }
}
