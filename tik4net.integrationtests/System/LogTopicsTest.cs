using System;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// The <c>/log</c> <c>topics</c> field must arrive as topic NAMES on every transport.
    /// </summary>
    /// <remarks>
    /// Over WinBox native it used to decode as the raw handle list <c>"[9,3]"</c>: topics is a
    /// <c>multinumber</c> whose element type is a dynamic reference, and the catalog only looked for the
    /// reference on the field itself, never on its element child. Nothing failed — the value was simply
    /// wrong, which is why a field nothing asserted on could stay wrong indefinitely.
    /// </remarks>
    [TestClass]
    public class LogTopicsTest : TestBase
    {
        // "[9,3]" / "[]" — an undecoded WinBox reference list rather than topic names.
        private static readonly Regex RawHandleList = new Regex(@"^\[[\d,\s]*\]$");

        [TestMethod]
        public void LogTopicsAreNamesNotRawHandles()
        {
            // No EnsureCommandAvailable here on purpose: its probe is an unfiltered print, and on /log that
            // is the whole memory log — 139 358 characters, which a MAC transport cannot read inside its
            // budget. Every RouterOS has /log, so there is nothing to check.
            string marker = "T4NTOPICS_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            WriteMarkerOverApi(marker);

            // Filter on the router: an unfiltered /log/print dumps the whole memory log, which does not fit
            // in the read budget of a MAC transport (see the RunScript_Issue53 note in P2.43).
            var rows = Connection.CreateCommandAndParameters("/log/print", "message", marker)
                                 .ExecuteList().ToList();
            Assert.AreEqual(1, rows.Count, "the marker line was not found in the router log");

            string topics = rows[0].GetResponseField("topics");
            Assert.IsFalse(string.IsNullOrEmpty(topics), "the log row carries no topics field");
            Assert.IsFalse(RawHandleList.IsMatch(topics),
                $"topics came back as an undecoded reference list: '{topics}'");
            Assert.IsTrue(topics.Split(',').Any(t => t.Trim() == "info"),
                $"a /log info line must carry the 'info' topic, got '{topics}'");
        }

        /// <summary>
        /// Writes the marker line over a side API connection rather than over the transport under test.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written at <b>info</b> severity on purpose. Any level decodes its topics the same way, and an
        /// error-severity marker left a red line in the router log for every run of every transport —
        /// alarming for anyone who inspects the box afterwards, for no test value.
        /// </para>
        /// This stays even though P2.48 fixed <c>/log/&lt;level&gt;</c> dispatch, and the reason has changed:
        /// REST now posts it correctly, but the native WinBox transports still cannot write a log line at all
        /// — the router's own <c>.jg</c> catalog declares no log-writing action on any handler. Writing over
        /// the transport under test would therefore make this test SKIP on WinBox native, which is the one
        /// transport it exists for (the raw-handle-list decode bug in its summary was a native bug). The
        /// write path itself is covered per transport by <see cref="LogWriteTest"/>.
        /// </remarks>
        private static void WriteMarkerOverApi(string marker)
        {
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api,
                       ConfigurationManager.AppSettings["host"],
                       ConfigurationManager.AppSettings["user"],
                       ConfigurationManager.AppSettings["pass"]))
            {
                api.LogInfo(marker);
            }
        }
    }
}
