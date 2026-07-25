using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers the two CLI output-cleaning defects that surfaced while enabling P2.12's positional error
    /// detection. Both were invisible for as long as nothing read meaning into leftover text.
    /// </summary>
    [TestClass]
    public class CliOutputHelperTests
    {
        private const string Prompt = "[admin@MikroTik] > ";

        // The terminal echoes the command exactly as sent, including the leading '/', while the comparison
        // ran against a copy that had been TrimStart('/')-ed — so for every slash-prefixed command (all of
        // set/remove/enable/disable/move) the echo was never recognised and survived as "output".
        [TestMethod]
        public void CleanOutput_StripsTheEchoOfASlashPrefixedCommand()
        {
            const string sent = "/interface set [find .id=*2] comment=\"My comment\"";
            string raw = sent + "\r\n" + Prompt;

            Assert.AreEqual(string.Empty, CliOutputHelper.CleanOutput(raw, sent),
                "a successful write must clean down to nothing at all");
        }

        [TestMethod]
        public void CleanOutput_KeepsTheRouterDiagnosticOfAFailedWrite()
        {
            const string sent = "/interface set [find .id=*2] mtu=notanumber";
            string raw = sent + "\r\nvalue of mtu contains invalid trailing characters\r\n" + Prompt;

            Assert.AreEqual("value of mtu contains invalid trailing characters",
                CliOutputHelper.CleanOutput(raw, sent));
        }

        // RouterOS ships a logging rule that sends `critical` topics to the console, so an unrelated log
        // entry can land in the middle of any command's output on a stock router.
        [TestMethod]
        public void CleanOutput_DropsAsynchronousRouterLogLines()
        {
            const string sent = ":put [/ip address print detail as-value]";
            string raw = sent + "\r\n"
                       + "19:54:32 echo: system,error,critical login failure for user admin from 10.0.0.1 via api\r\n"
                       + ".id=*1;address=192.168.1.1/24\r\n"
                       + Prompt;

            Assert.AreEqual(".id=*1;address=192.168.1.1/24", CliOutputHelper.CleanOutput(raw, sent),
                "a log line must not be mistaken for a data record");
        }

        [TestMethod]
        public void CleanOutput_LogLineDuringASilentWriteDoesNotLookLikeAnError()
        {
            const string sent = "/interface set [find .id=*2] comment=x";
            string raw = sent + "\r\n"
                       + "jul/25 19:54:32 system,error,critical login failure for user admin\r\n"
                       + Prompt;

            Assert.AreEqual(string.Empty, CliOutputHelper.CleanOutput(raw, sent),
                "otherwise P2.12's positional rule reports a successful write as failed");
        }

        [TestMethod]
        public void IsRouterLogLine_DoesNotSwallowRealOutput()
        {
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine(".id=*1;address=192.168.1.1/24"));
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine("value of mtu contains invalid trailing characters"));
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine("no such item"));
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine("*8E"));
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine(""));
            // uptime/time values are fields, never a whole line
            Assert.IsFalse(CliOutputHelper.IsRouterLogLine("uptime=19:54:32"));

            Assert.IsTrue(CliOutputHelper.IsRouterLogLine("19:54:32 system,info account: user admin logged in"));
            Assert.IsTrue(CliOutputHelper.IsRouterLogLine("jul/25 19:54:32 system,error,critical login failure"));
        }
    }
}
