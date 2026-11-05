using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The PTY console's quiet read (Tab-completion) over a timed byte stream: the cases <c>CliLoginScreenTests</c> gives
    /// <see cref="CliQuietWindow"/>, plus the two only a carrier has — it closes, or data never stops before the deadline.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // timed fakes
    public class CliPtySessionQuietReadTests
    {
        private static readonly byte[] Tab = Encoding.ASCII.GetBytes("/sys\t");

        private static (string Answer, long Ms) Quiet(RouterScript script, int quietMs, int receiveTimeoutMs = 10000)
        {
            using (var f = TerminalReadFixture.Create(TerminalCarrier.Pty, script, receiveTimeoutMs))
            {
                var sw = Stopwatch.StartNew();
                string answer = f.QuietAsync(Tab, quietMs).GetAwaiter().GetResult();
                return (answer, sw.ElapsedMilliseconds);
            }
        }

        [TestMethod]
        public void DataWithinTheWindow_KeepsTheReadOpen()
        {
            var (answer, ms) = Quiet(RouterScript.Answer((0, "one "), (60, "two")), 150);

            Assert.AreEqual("one two", answer);
            Assert.IsTrue(ms >= 60 + 150 - 20, ms + " ms: settled before the window after the last data");
        }

        [TestMethod]
        public void AGapLongerThanTheWindow_AfterData_EndsTheRead()
        {
            var (answer, _) = Quiet(RouterScript.Answer((0, "one "), (600, "two")), 100);

            Assert.AreEqual("one ", answer);
        }

        [TestMethod]
        public void OneSilentWindow_IsNotYetNoReaction()
        {
            var (answer, _) = Quiet(RouterScript.Answer((200, "late")), 100);

            Assert.AreEqual("late", answer);
        }

        [TestMethod]
        public void NothingAtAll_IsAnEmptyAnswer_AfterTheNoReactionWindows()
        {
            var (answer, ms) = Quiet(RouterScript.Silent(), 100);

            Assert.AreEqual(string.Empty, answer);
            Assert.IsTrue(ms >= 100 * CliCompletionParser.NoReactionQuietFactor - 20, ms + " ms");
            Assert.IsTrue(ms < 3000, ms + " ms: waited for the deadline");
        }

        [TestMethod]
        public void ACarrierThatCloses_AfterData_EndsTheReadBeforeTheWindow()
        {
            var (answer, ms) = Quiet(RouterScript.Answer((0, "one")).ThenEnd(20), 1000);

            Assert.AreEqual("one", answer);
            Assert.IsTrue(ms < 800, ms + " ms: waited for the quiet window on a closed carrier");
        }

        [TestMethod]
        public void ACarrierThatCloses_WithoutData_IsAnEmptyAnswer_BeforeTheNoReactionWindows()
        {
            var (answer, ms) = Quiet(RouterScript.Answer().ThenEnd(20), 1000);

            Assert.AreEqual(string.Empty, answer);
            Assert.IsTrue(ms < 2000, ms + " ms: waited for the no-reaction windows on a closed carrier");
        }

        [TestMethod]
        public void DataThatNeverStops_EndsAtTheDeadline_WithWhatArrived()
        {
            var chunks = Enumerable.Range(0, 30).Select(i => (i * 50, "x")).ToArray();
            var (answer, ms) = Quiet(RouterScript.Answer(chunks), 150, receiveTimeoutMs: 600);

            StringAssert.StartsWith(answer, "xxxx");
            Assert.IsTrue(answer.Length < 30, answer.Length + " chunks: read past the deadline");
            Assert.IsTrue(ms >= 600 - 20 && ms < 1300, ms + " ms");
        }
    }
}
