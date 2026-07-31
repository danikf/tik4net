using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>Log.Debug/Info/Warning/WriteErrorMessage</c> (i.e. <c>/log/&lt;level&gt;</c>) must actually write a
    /// line to the router log on every transport that can reach the command.
    /// </summary>
    /// <remarks>
    /// These four public helpers had <b>no</b> integration coverage on any transport, which is why P2.48 stayed
    /// invisible: over REST the level was not on the request builder's write-verb allow-list, so it became part
    /// of the path with an implicit <c>print</c> and went out as <c>GET /rest/log/error</c> — answered
    /// <c>400 no such command</c>. The router was never the limit: <c>POST /rest/log/error</c> returns
    /// <c>200</c> and the line appears in <c>/log</c> (measured on 7.23.2).
    /// </remarks>
    [TestClass]
    public class LogWriteTest : TestBase
    {
        [TestMethod]
        public void LogErrorWritesALineReadableBack()
        {
            string marker = NewMarker();
            WriteOrSkip(() => Connection.LogError(marker));

            var rows = ReadMarkerRows(marker);
            Assert.AreEqual(1, rows.Length, "the line written by LogError is not in the router log");
            Assert.IsTrue(rows[0].Split(',').Any(t => t.Trim() == "error"),
                $"a /log/error line must carry the 'error' topic, got '{rows[0]}'");
        }

        [TestMethod]
        public void LogInfoAndWarningWriteTheirOwnLines()
        {
            // Each level is a separate router command, not one command with a level argument — a builder that
            // collapses them (or drops the last path segment) would still make this pass for one of them.
            string info = NewMarker();
            string warning = NewMarker();

            WriteOrSkip(() => Connection.LogInfo(info));
            Connection.LogWarning(warning);

            Assert.AreEqual(1, ReadMarkerRows(info).Length, "LogInfo wrote no line");
            var warningRows = ReadMarkerRows(warning);
            Assert.AreEqual(1, warningRows.Length, "LogWarning wrote no line");
            Assert.IsTrue(warningRows[0].Split(',').Any(t => t.Trim() == "warning"),
                $"a /log/warning line must carry the 'warning' topic, got '{warningRows[0]}'");
        }

        [TestMethod]
        public void LogDebugIsAcceptedEvenWhenTheRouterDiscardsIt()
        {
            // /log/debug succeeds but the line only reaches the memory log when the logging configuration
            // enables the debug topics — on a default router it does not (measured: POST /rest/log/debug
            // returns 200 and nothing appears in /log). So this asserts the CALL, not the line: it fails only
            // if the transport rejects the command, which is exactly what P2.48 did.
            WriteOrSkip(() => Connection.LogDebug(NewMarker()));
        }

        private static string NewMarker()
            => "T4NLOGWRITE_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>
        /// Runs the write, converting the native transport's stated refusal into a skip.
        /// </summary>
        /// <remarks>
        /// This is the one legitimate gap: WinBox invokes actions declared by the router's own <c>.jg</c>
        /// catalog, and no window in it — on <c>/log</c>'s handler <c>[3,4]</c> or anywhere else — declares a
        /// log-writing action. WinBox itself cannot write a log line, so there is no correctly-formed request
        /// being refused (P2.48). Caught by the exception rather than by transport name, so the test starts
        /// passing on its own if a future catalog gains the action.
        /// </remarks>
        private static void WriteOrSkip(Action write)
        {
            try
            {
                write();
            }
            catch (NotSupportedException ex)
            {
                Assert.Inconclusive("/log/<level> is not in the WinBox .jg catalog, so the native WinBox "
                    + "transport cannot invoke it: " + ex.Message);
            }
        }

        /// <summary>Reads back the marker's <c>topics</c>, one string per matching log row.</summary>
        /// <remarks>
        /// Filtered on the router deliberately: an unfiltered <c>/log/print</c> dumps the whole memory log
        /// (~139 000 characters), which does not fit a MAC transport's read budget — the same reason
        /// <see cref="LogTopicsTest"/> avoids <c>EnsureCommandAvailable</c> here.
        /// </remarks>
        private string[] ReadMarkerRows(string marker)
            => Connection.CreateCommandAndParameters("/log/print", "message", marker)
                         .ExecuteList()
                         .Select(r => r.GetResponseFieldOrDefault("topics", string.Empty))
                         .ToArray();
    }
}
