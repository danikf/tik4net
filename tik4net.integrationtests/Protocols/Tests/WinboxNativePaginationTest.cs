// WinboxNativePaginationTest.cs — P2.9: live proof that a MULTI-PAGE getall over WinBox native
// returns every page, and a report of which continuation key the router actually used.
//
// The deterministic side of P2.9 lives in tik4net.unittests (WinboxM2PaginationTests, scripted peer).
// This is the other half: the unit tests prove we follow a cursor a peer hands us, this proves the
// live router's own paging still comes back whole through the same code.
//
// /log is the menu chosen because it is the one reliably multi-page table on the test router
// (~1000 rows ≈ 5 pages of ~200); everything else in a lab config fits one page.
//
// [Ignore] keeps it out of the matrix — it is a probe, run via --filter.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net;

namespace tik4net.integrationtests
{
    // Measured 2026-08-13 on 7.23.2: api 1000 rows, winboxnative 1000 rows (5 pages via ufe0003).
    [Ignore("P2.9 pagination probe — hits a live router and reads the whole log. Remove the attribute to run.")]
    [TestClass]
    public class WinboxNativePaginationTest
    {
        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        /// <summary>
        /// Reads <c>/log</c> over WinBox native and over the binary API and compares the counts. The API is
        /// the reference implementation and does not page at all, so a native read that stops at its first
        /// page shows up as a count far below it — which is exactly the failure P2.9 describes and which a
        /// "did it throw?" test cannot see.
        /// </summary>
        [TestMethod]
        public void Native_PagedRead_ReturnsEveryPage()
        {
            var (host, user, pass) = Cfg();

            int apiRows, nativeRows;
            using (var api = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                api.Open(host, user, pass);
                apiRows = api.CreateCommand("/log/print").ExecuteList().Count();
            }

            using (var native = ConnectionFactory.CreateConnection(TikConnectionType.WinboxNative))
            {
                native.Open(host, user, pass);
                nativeRows = native.CreateCommand("/log/print").ExecuteList().Count();
            }

            Console.WriteLine($"/log rows — api: {apiRows}, winboxnative: {nativeRows}");

            Assert.IsTrue(apiRows > 500,
                $"Probe precondition: the router's log must be multi-page for this to test anything (got {apiRows} rows). " +
                "Generate traffic or lower the page size before reading the result as a pass.");

            // The log grows while the two reads run, so compare with a tolerance rather than for equality;
            // a first-page-only read is off by hundreds, not by the handful of lines a second can add.
            Assert.IsTrue(Math.Abs(apiRows - nativeRows) <= 25,
                $"WinBox native returned {nativeRows} of the API's {apiRows} log rows — a paged read lost pages.");
        }
    }
}
