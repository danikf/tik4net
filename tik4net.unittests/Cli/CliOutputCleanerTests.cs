using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers the two CLI output-cleaning defects that surfaced while enabling the positional error
    /// detection. Both were invisible for as long as nothing read meaning into leftover text.
    /// </summary>
    [TestClass]
    public class CliOutputCleanerTests
    {
        private const string Prompt = "[admin@MikroTik] > ";

        // The terminal echoes the command exactly as sent, including the leading '/', while the comparison
        // ran against a copy that had been TrimStart('/')-ed — so for every slash-prefixed command (all of
        // set/remove/enable/disable/move) the echo was never recognised and survived as "output".
        // Measured over SSH under load (2026-10-01): the answer was complete, then the router repainted its prompt,
        // wrote a console log entry (a WinBox login failure from another session) and repainted the prompt again.
        // Only the last prompt was stripped, the log line stopped the trailing loop, and the two prompts above it
        // stayed in the output — after the count line, so the counted read reported its count missing.
        [TestMethod]
        public void CleanOutput_StripsTrailingPromptsAroundALogLineAfterTheAnswer()
        {
            const string sent = ":put \"x\"";
            string raw = sent + "\r\n\rrow1\r\n#n=1/num\r\n\r\r\r" + Prompt + "\r\n\r\r\r\r" + Prompt
                       + "\r23:30:37 echo: system,error,critical login failure for user admin from 192.0.2.31 via winbox\r\n\r"
                       + Prompt.TrimEnd();

            Assert.AreEqual("row1\n#n=1/num", CliOutputCleaner.CleanOutput(raw, sent));
        }

        [TestMethod]
        public void CleanOutput_StripsTheEchoOfASlashPrefixedCommand()
        {
            const string sent = "/interface set [find .id=*2] comment=\"My comment\"";
            string raw = sent + "\r\n" + Prompt;

            Assert.AreEqual(string.Empty, CliOutputCleaner.CleanOutput(raw, sent),
                "a successful write must clean down to nothing at all");
        }

        [TestMethod]
        public void CleanOutput_KeepsTheRouterDiagnosticOfAFailedWrite()
        {
            const string sent = "/interface set [find .id=*2] mtu=notanumber";
            string raw = sent + "\r\nvalue of mtu contains invalid trailing characters\r\n" + Prompt;

            Assert.AreEqual("value of mtu contains invalid trailing characters",
                CliOutputCleaner.CleanOutput(raw, sent));
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

            Assert.AreEqual(".id=*1;address=192.168.1.1/24", CliOutputCleaner.CleanOutput(raw, sent),
                "a log line must not be mistaken for a data record");
        }

        // The log line is written when the router emits it, which can be BEFORE the command echo has been
        // painted. That order defeated the leading-noise loop: the log line is not blank, not a prompt and
        // not a fragment of the command, so the loop stopped on it and the echo behind it was returned as
        // data. Measured live — one such line appeared in a full telnet suite run.
        [TestMethod]
        public void CleanOutput_DropsARouterLogLineThatArrivesAheadOfTheEcho()
        {
            const string sent = ":put [/ip address print detail as-value]";
            string raw = "19:54:32 echo: system,error,critical login failure for user admin from 10.0.0.1 via api\r\n"
                       + sent + "\r\n"
                       + ".id=*1;address=192.168.1.1/24\r\n"
                       + Prompt;

            Assert.AreEqual(".id=*1;address=192.168.1.1/24", CliOutputCleaner.CleanOutput(raw, sent),
                "the echo must still be recognised when a log line precedes it");
        }

        [TestMethod]
        public void CleanOutput_LogLineAheadOfASilentWriteDoesNotLookLikeAnError()
        {
            const string sent = "/interface set [find .id=*2] comment=x";
            string raw = "19:54:32 system,error,critical login failure for user admin\r\n"
                       + sent + "\r\n"
                       + Prompt;

            Assert.AreEqual(string.Empty, CliOutputCleaner.CleanOutput(raw, sent),
                "otherwise the surviving echo is read as the router rejecting the write");
        }

        [TestMethod]
        public void CleanOutput_LogLineDuringASilentWriteDoesNotLookLikeAnError()
        {
            const string sent = "/interface set [find .id=*2] comment=x";
            string raw = sent + "\r\n"
                       + "jul/25 19:54:32 system,error,critical login failure for user admin\r\n"
                       + Prompt;

            Assert.AreEqual(string.Empty, CliOutputCleaner.CleanOutput(raw, sent),
                "otherwise P2.12's positional rule reports a successful write as failed");
        }

        // The exact bytes captured off the wire (channel ssh.pty) for the read-back that
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

            Assert.AreEqual(record, CliOutputCleaner.CleanOutput(VtStripper.StripAnsi(raw), sent),
                "every trailing prompt must go, not just the last one");
        }

        [TestMethod]
        public void CleanOutput_KeepsADataLineThatMerelyEndsLikeAPrompt()
        {
            const string sent = ":put [/system/script print detail as-value]";
            const string record = ".id=*1;source=:put [$x] >";
            string raw = sent + "\r\n" + record + "\r\n" + Prompt;

            Assert.AreEqual(record, CliOutputCleaner.CleanOutput(raw, sent),
                "the repeat must stop at the first line that is not a prompt repaint");
        }

        // The previous command's tail arrives after its read has already returned on a settled
        // prompt and after the next command's pre-send drain has looked at the socket, so it becomes the
        // HEAD of the next response. Nothing used to notice: foreign output is not blank, not a prompt and
        // not a fragment of the sent command, so the head-trim loop treats it as the first data line and
        // stops there — the caller is handed someone else's answer with its own echo still inside it.
        [TestMethod]
        public void CleanOutput_DropsThePreviousCommandsTailFromTheHeadOfThisResponse()
        {
            const string sent = ":put [/interface/l2tp-client add name=t4ntest-l2tp1a2b3c4d comment=x]";
            string raw = "*8E\r\n"                                    // the tail of the PREVIOUS add
                       + Prompt + "\r\n"                              // and its prompt, arriving late
                       + sent + "\r\n"
                       + "*9F\r\n"
                       + Prompt;

            Assert.AreEqual("*9F", CliOutputCleaner.CleanOutput(raw, sent),
                "the id returned must be the one this add printed, not the one left in the socket");
        }

        // The same splice on a read: the residue is a whole record, which parses perfectly well and is
        // simply the wrong row.
        [TestMethod]
        public void CleanOutput_DropsAForeignRecordThatPrecedesTheEcho()
        {
            const string sent = ":put [/ip/address print detail as-value where .id=\"*2\"]";
            const string record = ".id=*2;address=192.168.1.1/24";
            string raw = ".id=*1;address=10.0.0.1/24\r\n"
                       + sent + "\r\n"
                       + record + "\r\n"
                       + Prompt;

            Assert.AreEqual(record, CliOutputCleaner.CleanOutput(raw, sent));
        }

        // The ordinary head must not be touched by the alignment: a blank line, a repainted prompt, an
        // asynchronous log line and a split character-echo are all normal in front of the echo.
        [TestMethod]
        public void CleanOutput_AlignmentLeavesTheOrdinaryHeadAlone()
        {
            const string sent = ":put [/ip/address print detail as-value]";
            const string record = ".id=*1;address=192.168.1.1/24";
            string raw = "\r\n"
                       + "19:54:32 system,error,critical login failure for user x\r\n"
                       + ":put [/ip/addre\r\n"                        // character-echo split by a repaint
                       + Prompt + sent + "\r\n"
                       + record + "\r\n"
                       + Prompt;

            Assert.AreEqual(record, CliOutputCleaner.CleanOutput(raw, sent));
        }

        // A data line that happens to contain the command text must not be mistaken for the echo — the
        // real echo comes first, so the first match is always the right one.
        [TestMethod]
        public void CleanOutput_KeepsARecordThatQuotesTheCommandItself()
        {
            const string sent = ":put [/system/script print detail as-value]";
            const string record = ".id=*1;source=:put [/system/script print detail as-value]";
            string raw = sent + "\r\n" + record + "\r\n" + Prompt;

            Assert.AreEqual(record, CliOutputCleaner.CleanOutput(raw, sent));
        }

        // The gate every PTY read loop gets its terminator from: a prompt only ends the read once the
        // router has started answering THIS command.
        [TestMethod]
        public void ContainsEcho_TellsOurResponseFromTheOneLeftInTheSocket()
        {
            const string sent = ":put [/ip/address print detail as-value where .id=\"*2\"]";

            Assert.IsFalse(CliOutputCleaner.ContainsEcho(".id=*1;address=10.0.0.1/24\r\n" + Prompt, sent),
                "the previous command's tail plus its prompt must not end this read");
            Assert.IsTrue(CliOutputCleaner.ContainsEcho(Prompt + sent + "\r\n", sent),
                "the prompt-prefixed repaint is the echo");
            Assert.IsTrue(CliOutputCleaner.ContainsEcho(":put  [/ip/address  print detail as-value where .id=\"*2\"]", sent),
                "the line editor repaints the echo, it does not replay the bytes — whitespace may differ");
        }

        [TestMethod]
        public void ContainsEcho_DoesNotGateWhatItCannotAnchorOn()
        {
            // A control key is not a command and RouterOS need not answer one at all.
            Assert.IsTrue(CliOutputCleaner.ContainsEcho("anything", null));
            Assert.IsTrue(CliOutputCleaner.ContainsEcho("anything", ""));
            // Too short to be distinctive among a response's data lines.
            Assert.IsTrue(CliOutputCleaner.ContainsEcho("anything", "/quit"));
        }

        [TestMethod]
        public void IsRouterLogLine_DoesNotSwallowRealOutput()
        {
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine(".id=*1;address=192.168.1.1/24"));
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine("value of mtu contains invalid trailing characters"));
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine("no such item"));
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine("*8E"));
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine(""));
            // uptime/time values are fields, never a whole line
            Assert.IsFalse(CliOutputCleaner.IsRouterLogLine("uptime=19:54:32"));

            Assert.IsTrue(CliOutputCleaner.IsRouterLogLine("19:54:32 system,info account: user admin logged in"));
            Assert.IsTrue(CliOutputCleaner.IsRouterLogLine("jul/25 19:54:32 system,error,critical login failure"));
        }

        [TestMethod]
        public void StripAnsi_AppliesTheLineEditorsColourRepaint()
        {
            // The WinBox terminal (7.24.5) echoes a mistyped command, moves the cursor back over it and writes it
            // again in syntax colours, then prints the error. The bytes as the mepty read received them:
            const string sent = "/interface prnt";
            string raw = "/interface prnt\x1b[15D\x1b[m\x1b[36m/interface\x1b[m \x1b[m\x1b[31mprnt\x1b[m\r\n"
                       + "\rbad command name prnt (line 1 column 12)\r\n"
                       + "\r\r\r\x1b[9999B\x1b[K[\x1b[m\x1b[36madmin\x1b[m@\x1b[m\x1b[32mCHR\x1b[m] > ";

            Assert.AreEqual("bad command name prnt (line 1 column 12)",
                CliOutputCleaner.CleanOutput(VtStripper.StripAnsi(raw), sent),
                "the repaint overwrites the echo; it must not be appended to it");
        }

        [TestMethod]
        public void StripAnsi_CursorBackStaysOnItsLineAndEraseCutsAtTheCursor()
        {
            Assert.AreEqual("ab\nxyz", VtStripper.StripAnsi("ab\nxyz\x1b[9D"),
                "a move back stops at the start of the line and erases nothing by itself");
            Assert.AreEqual("ab\nzz", VtStripper.StripAnsi("ab\nxyz\x1b[9Dzz\x1b[K"),
                "text overwrites from the cursor; erase to end of line cuts there once the cursor has moved back");
            Assert.AreEqual("abc", VtStripper.StripAnsi("abc\x1b[K"), "at the end of the text erase is a no-op");
        }

        [TestMethod]
        public void StripAnsi_WithoutCursorBackRemovesOnlyTheSequences()
        {
            Assert.AreEqual("[admin@CHR] > /i\r\n\x1b", VtStripper.StripAnsi(
                "\x1b[m\x1b[36m[admin@CHR]\x1b[m > \x1b[K/i\x1b]0;title\x07\x1b" + "7\r\n\x1b"),
                "CSI, OSC and ESC+char are dropped; a trailing lone ESC is kept");
            Assert.AreEqual("", VtStripper.StripAnsi(null!));
        }

        [TestMethod]
        public void StripAnsi_KeepsAStoredU009BAsText()
        {
            // Captured over Telnet on 7.25.1: the DSV row of a /system/script whose source is a U+009B b ě c Û d. The router
            // writes the stored bytes unchanged (U+009B is UTF-8 C2 9B) and its own sequences in 7-bit ESC [ only, with a
            // client answering its UTF-8 test either way. Read as an 8-bit CSI, U+009B b became a sequence ending in 'b'
            // and both characters were dropped.
            byte[] wire =
            {
                0x2A, 0x32, 0x31, 0x7E, 0x5E, 0x7E, 0x30, 0x7E, 0x5E, 0x7E,       // *21~^~0~^~
                0x61, 0xC2, 0x9B, 0x62, 0xC4, 0x9B, 0x63, 0xC3, 0x9B, 0x64,       // a U+009B b ě c Û d
                0x0D, 0x0A, 0x23, 0x6E, 0x3D, 0x31, 0x2F, 0x6E, 0x75, 0x6D, 0x0D, 0x0A, // \r\n#n=1/num\r\n
                0x0D, 0x0D, 0x0D, 0x1B, 0x5B, 0x39, 0x39, 0x39, 0x39, 0x42,       // \r\r\r ESC[9999B
                0x5B, 0x61, 0x64, 0x6D, 0x69, 0x6E, 0x40, 0x43, 0x48, 0x52, 0x5D, 0x20, 0x3E, 0x20, // [admin@CHR] >
            };

            Assert.AreEqual("*21~^~0~^~a\u009BběcÛd\r\n#n=1/num\r\n\r\r\r[admin@CHR] > ",
                VtStripper.StripAnsi(Encoding.UTF8.GetString(wire)));
            Assert.AreEqual("x\u009B32my\u009B", VtStripper.StripAnsi("x\u009B32my\u009B"),
                "U+009B is text wherever it stands, also before what would be CSI parameters");
        }
    }
}
