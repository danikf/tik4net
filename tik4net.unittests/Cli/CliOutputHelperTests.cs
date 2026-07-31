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

        // The log line is written when the router emits it, which can be BEFORE the command echo has been
        // painted. That order defeated the leading-noise loop: the log line is not blank, not a prompt and
        // not a fragment of the command, so the loop stopped on it and the echo behind it was returned as
        // data. Measured live — one such line appeared in a full telnet suite run (P2.47).
        [TestMethod]
        public void CleanOutput_DropsARouterLogLineThatArrivesAheadOfTheEcho()
        {
            const string sent = ":put [/ip address print detail as-value]";
            string raw = "19:54:32 echo: system,error,critical login failure for user admin from 10.0.0.1 via api\r\n"
                       + sent + "\r\n"
                       + ".id=*1;address=192.168.1.1/24\r\n"
                       + Prompt;

            Assert.AreEqual(".id=*1;address=192.168.1.1/24", CliOutputHelper.CleanOutput(raw, sent),
                "the echo must still be recognised when a log line precedes it");
        }

        [TestMethod]
        public void CleanOutput_LogLineAheadOfASilentWriteDoesNotLookLikeAnError()
        {
            const string sent = "/interface set [find .id=*2] comment=x";
            string raw = "19:54:32 system,error,critical login failure for user admin\r\n"
                       + sent + "\r\n"
                       + Prompt;

            Assert.AreEqual(string.Empty, CliOutputHelper.CleanOutput(raw, sent),
                "otherwise the surviving echo is read as the router rejecting the write");
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

        // P2.29. The exact bytes captured off the wire (channel ssh.pty) for the read-back that
        // AddInterfaceListMemberWillNotFail failed on: RouterOS repaints the prompt line, so the response
        // ends with the prompt TWICE, separated by bare CRs. Removing only the last one left the first
        // inside the data, where CliOutputParser turned it into a multi-value continuation of the final
        // field ("list=t4n-test-926b98b7,[admin@CHR] >").
        [TestMethod]
        public void CleanOutput_StripsARepaintedPromptEmittedTwice()
        {
            const string sent = ":put [/interface list member print detail as-value where .id=\"*2E\"]";
            const string record = ".id=*2E;comment=df90f741;disabled=false;interface=ether1;list=t4n-test-926b98b7";
            string raw = sent + "\r\n"
                       + "\r" + record + "\r\n"
                       + "\r\r\r[9999B[admin@CHR] > \r\n"
                       + "\r\r\r\r[9999B[admin@CHR] > ";

            Assert.AreEqual(record, CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), sent),
                "every trailing prompt must go, not just the last one");
        }

        [TestMethod]
        public void CleanOutput_KeepsADataLineThatMerelyEndsLikeAPrompt()
        {
            const string sent = ":put [/system/script print detail as-value]";
            const string record = ".id=*1;source=:put [$x] >";
            string raw = sent + "\r\n" + record + "\r\n" + Prompt;

            Assert.AreEqual(record, CliOutputHelper.CleanOutput(raw, sent),
                "the repeat must stop at the first line that is not a prompt repaint");
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
