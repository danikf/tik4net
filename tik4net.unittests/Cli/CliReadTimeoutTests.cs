using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The message a truncated CLI read produces. This used to be no message at all — the partial text was
    /// returned as a successful result — so what the exception actually says is the whole point of P2.32.
    /// </summary>
    [TestClass]
    public class CliReadTimeoutTests
    {
        [TestMethod]
        public void Create_NamesTheCommandTheTimeoutAndTheTail()
        {
            var ex = CliReadTimeout.Create("Telnet", 30000, ":put [/log print as-value]",
                ".id=*1;message=first\n.id=*2;message=second");

            StringAssert.Contains(ex.Message, "Telnet");
            StringAssert.Contains(ex.Message, "30000 ms");
            StringAssert.Contains(ex.Message, ":put [/log print as-value]");
            StringAssert.Contains(ex.Message, "incomplete");
            StringAssert.Contains(ex.Message, "second", "the tail is what shows where the read stopped");
            Assert.AreEqual(30000, ex.TimeoutMilliseconds);
        }

        // The partial text is kept reachable rather than thrown away: a caller that genuinely wants it can
        // still have it, but has to ask — which is the difference between an error and a silent wrong answer.
        [TestMethod]
        public void Create_KeepsThePartialResponseOnTheException()
        {
            var ex = CliReadTimeout.Create("SSH", 5000, "/interface print", "half a table");

            Assert.AreEqual("half a table", ex.PartialResponse);
        }

        [TestMethod]
        public void Create_SaysSoWhenNothingArrivedAtAll()
        {
            var ex = CliReadTimeout.Create("MAC-Telnet", 1000, "/system/identity/print", "");

            StringAssert.Contains(ex.Message, "nothing was received");
            Assert.IsNull(ex.PartialResponse, "no partial data means there is nothing to hand back");
        }

        // Line breaks are escaped so the quoted tail cannot smear the diagnostic across a log.
        [TestMethod]
        public void Create_KeepsTheQuotedTailOnOneLine()
        {
            var ex = CliReadTimeout.Create("Telnet", 30000, "/x", "a\r\nb");

            StringAssert.Contains(ex.Message, "a\\r\\nb");
        }
    }
}
