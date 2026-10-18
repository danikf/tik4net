using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Ssh;

namespace tik4net.unittests.Ssh
{
    /// <summary>
    /// Which stripped buffer ends an SSH command's read. RouterOS 6.49 echoes typed input by repainting the whole
    /// line, prompt included, after every character, so a chunk boundary can leave the buffer ending in a prompt in
    /// the middle of the echo (24 of 700 boundaries on 6.49.13). Telnet, MAC-Telnet and WinBox CLI reject those cuts;
    /// SSH shares their rule.
    /// </summary>
    [TestClass]
    public class SshPromptRuleTests
    {
        private const string Command = ":put [/ip address print as-value]";
        private const string CountedCommand = ":local d [/ip address print as-value]; :if ([:len $d] > 0) do={:put $d}";

        [TestMethod]
        public void ARepaintCutMidEcho_DoesNotEndTheRead()
        {
            Assert.IsFalse(SshShellClient.EndsResponse(
                ":\r[admin@CHR2] > :p\r[admin@CHR2] > :pu\r[admin@CHR2] > ", Command));
        }

        [TestMethod]
        public void ACutInsideTheCommandText_DoesNotEndTheRead()
        {
            Assert.IsFalse(SshShellClient.EndsResponse(
                ":\r[admin@CHR2] > :l\r[admin@CHR2] > :local d [/ip address print as-value]; :if ([:len $d] >",
                CountedCommand));
        }

        [TestMethod]
        public void ThePromptAfterTheOutput_EndsTheRead()
        {
            Assert.IsTrue(SshShellClient.EndsResponse(
                ":\r[admin@CHR2] > :p\r[admin@CHR2] > " + Command + "\r\n"
                + ".id=*2;address=192.168.4.2/24\r\n\r\r\r[admin@CHR2] > ", Command));
        }

        // What a second Enter after the command (CR LF over a PTY) made RouterOS 6.49 write after the real end of the
        // response: the prompt repainted in place. It is the mid-echo signature, so the shared rule rejects it; SSH
        // therefore sends one Enter per line, and this ending no longer occurs.
        [TestMethod]
        public void AnInPlaceRepaintAfterTheOutput_DoesNotEndTheRead()
        {
            Assert.IsFalse(SshShellClient.EndsResponse(
                ":\r[admin@CHR2] > :p\r[admin@CHR2] > " + Command + "\r\n"
                + ".id=*2;address=192.168.4.2/24|124\r\n\r\r\r[admin@CHR2] > \r[admin@CHR2] > ", Command));
        }

        [TestMethod]
        public void AControlKeyAtABarePrompt_EndsTheRead()
        {
            Assert.IsTrue(SshShellClient.EndsResponse("\r\n[admin@CHR2] > ", null));
        }

        [TestMethod]
        public void AControlKeyAnsweredByAQuestion_EndsTheRead()
        {
            Assert.IsTrue(SshShellClient.EndsResponse(
                "Hijacking Safe Mode from someone - unroll/release/don't take it [u/r/d]: ", null));
        }
    }
}
