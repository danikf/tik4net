// TerminalFrameBoundaryTest.cs — terminal answers sized across the WinBox chunk boundary.
//
// RouterOS writes an encrypted WinBox frame in 255-byte chunks and sends no empty chunk after a frame that
// ends on a full one — the one reachable size is 3570 bytes, and a terminal answer of ~3.4 KB lands on it.
// A reader waiting for a short chunk there swallowed the next frame: WinboxCli died with an undecryptable
// frame and WinboxCliMac stalled until the receive timeout (a whole-table mangle read hung in its last
// window). The unit tests pin the reader (WinboxChunkReassemblyTests); this checks the router still frames
// that way, and that every length around it comes back whole.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class TerminalFrameBoundaryTest : TestBase
    {
        [TestMethod]
        public void AnswersAroundTheFullChunkFrameSizeComeBackWhole()
        {
            EnsureRawDialectIsCliText("CallCommandSync with CLI text");

            // Which output length produces the 3570-byte frame depends on what shares the frame with it (the
            // echo, the prompt, the identity's length), so the sweep is wide; one AES block is 16 bytes, so a
            // step of 4 visits every frame size in the range several times.
            for (int length = 3300; length <= 3600; length += 4)
            {
                var sentences = RawConnection.CallCommandSync(
                    ":local s \"\"; :for i from=1 to=" + length + " do={:set s ($s . \"x\")}; :put $s").ToList();

                string answer = sentences.OfType<ITikDoneSentence>().Single().GetResponseWord();
                Assert.AreEqual(new string('x', length), answer, "an answer of " + length + " characters");
            }
        }
    }
}
