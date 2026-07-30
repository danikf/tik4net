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
            Assert.IsTrue(topics.Split(',').Any(t => t.Trim() == "error"),
                $"a /log error line must carry the 'error' topic, got '{topics}'");
        }

        /// <summary>
        /// Writes the marker line over a side API connection rather than over the transport under test.
        /// This test is about how a log row DECODES, and `/log/error` is not dispatchable on every
        /// transport today — WinboxNative rejects the verb outright and REST sends it as a `GET`, even
        /// though the router accepts `POST /rest/log/error`. Filed separately; writing over the API keeps
        /// this test measuring the one thing it is named for.
        /// </summary>
        private static void WriteMarkerOverApi(string marker)
        {
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api,
                       ConfigurationManager.AppSettings["host"],
                       ConfigurationManager.AppSettings["user"],
                       ConfigurationManager.AppSettings["pass"]))
            {
                api.LogError(marker);
            }
        }
    }
}
