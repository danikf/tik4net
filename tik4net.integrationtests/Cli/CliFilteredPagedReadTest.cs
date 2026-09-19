// CliFilteredPagedReadTest — a filtered CLI read is windowed, and its windows cover matching rows only.
//
// The filter goes into the window's own 'find' (CliCommandBuilder.BuildPagedWindow), which is a different
// RouterOS parser from the 'where' it replaces: a bare clause is parsed as the verb's arguments, a
// parenthesised one as an expression. The risk that matters is the direction of a mismatch — a 'find' that
// matched FEWER rows than 'where' would drop rows silently — so every test here compares the paged read
// against the SAME read unpaged on the SAME connection, which is the path that predates this feature.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests.Cli
{
    [TestClass]
    public class CliFilteredPagedReadTest : TestBase
    {
        /// <summary>
        /// The connection as an <see cref="ITikCliPagedReadConnection"/>, or Inconclusive: windowing a read
        /// is a CLI-layer notion, and the binary API, REST and WinBox-native transports have no such thing.
        /// </summary>
        private ITikCliPagedReadConnection Paged
        {
            get
            {
                if (Connection is ITikCliPagedReadConnection paged)
                    return paged;
                Assert.Inconclusive($"Transport '{ResolveConnectionType()}' does not read in windows "
                                    + "(ITikCliPagedReadConnection) — test skipped.");
                return null;   // unreachable
            }
        }

        /// <summary>
        /// Reads the same thing twice on one connection — once windowed at <paramref name="pageSize"/>, once
        /// in a single command — and returns the two id sequences. The unpaged read is the reference: it is
        /// the path every filtered read took before the filter could go into a <c>find</c>.
        /// </summary>
        private void CompareWindowedWithUnpaged(int pageSize, System.Func<IEnumerable<string>> readIds,
                                                out IList<string> paged, out IList<string> unpaged)
        {
            var conn = Paged;
            int original = conn.CliReadPageSize;
            try
            {
                conn.CliReadPageSize = pageSize;
                paged = readIds().ToList();
                conn.CliReadPageSize = 0;
                unpaged = readIds().ToList();
            }
            finally
            {
                conn.CliReadPageSize = original;
            }
        }

        [TestMethod]
        public void AFilteredReadSpanningWindowsReturnsWhatTheSingleCommandDoes()
        {
            // The page is deliberately smaller than the match count, so the read really does loop: the whole
            // point is that the offsets walk the MATCHES and not the table.
            CompareWindowedWithUnpaged(10,
                () => Connection.LoadList<FirewallMangle>(Connection.CreateParameter("chain", "prerouting",
                          TikCommandParameterFormat.Filter)).Select(r => r.Id),
                out var paged, out var unpaged);

            CollectionAssert.AreEqual(unpaged.ToList(), paged.ToList(),
                "a windowed filtered read must return the same rows in the same order as the single command: "
                + $"{paged.Count} row(s) windowed, {unpaged.Count} unpaged");
        }

        [TestMethod]
        public void ANegatedFilterIsWindowedToo()
        {
            // '!(…)' is the clause shape that fails when it is handed to 'find' unparenthesised — a plain
            // syntax error, so this test is the one that would have caught the parentheses going missing.
            // Deliberately on a SMALL menu: the reference here is a single-command read, and a negation over
            // a large table is the one read that a MAC-layer transport cannot do that way (findings-mactelnet
            // §9.6). The large case is AFilterAndItsNegationPartitionTheTable, which needs no unpaged read.
            CompareWindowedWithUnpaged(2,
                () => Connection.LoadList<Objects.Interface.Interface>(
                          Connection.CreateParameter("name", TestConstants.Interface,
                              TikCommandParameterFormat.Filter),
                          Connection.CreateParameter("#!", "", TikCommandParameterFormat.Filter))
                      .Select(r => r.Id),
                out var paged, out var unpaged);

            if (unpaged.Count <= 1)
                Assert.Inconclusive("the negated filter must span more than one window for this to measure "
                    + $"anything, and it matched {unpaged.Count} row(s) — this router has too few interfaces.");
            CollectionAssert.AreEqual(unpaged.ToList(), paged.ToList(),
                $"negated filter: {paged.Count} row(s) windowed, {unpaged.Count} unpaged");
        }

        [TestMethod]
        public void AFilterAndItsNegationPartitionTheTable()
        {
            // The large-table case, with an oracle that needs no single-command read: a filter and its
            // negation must between them account for every row, and all three reads are windowed. A 'find'
            // that quietly matched fewer rows than the 'where' it replaced shows up here as a shortfall,
            // which is the failure mode this whole area exists to prevent.
            //
            // Reading the same 1623 rows in ONE command is what a MAC-layer transport cannot do — the router
            // counts them and sends fewer (findings-mactelnet §9.6) — so the bounded windowed read is not
            // merely faster here, it is the only shape that completes. That is why the reference is a
            // partition rather than an unpaged read.
            var conn = Paged;
            int original = conn.CliReadPageSize;
            try
            {
                conn.CliReadPageSize = 100;

                int matching = Connection.LoadList<FirewallMangle>(
                    Connection.CreateParameter("chain", "prerouting", TikCommandParameterFormat.Filter)).Count();
                int negated = Connection.LoadList<FirewallMangle>(
                    Connection.CreateParameter("chain", "prerouting", TikCommandParameterFormat.Filter),
                    Connection.CreateParameter("#!", "", TikCommandParameterFormat.Filter)).Count();
                int all = Connection.LoadAll<FirewallMangle>().Count();

                Assert.AreEqual(all, matching + negated,
                    $"a filter and its negation must partition the table: {matching} + {negated} against {all}");
                if (negated <= 100)
                    Assert.Inconclusive("the negated half must span several windows for this to measure anything "
                        + $"({negated} rows) — this router's mangle table is too small.");
            }
            finally
            {
                conn.CliReadPageSize = original;
            }
        }

        [TestMethod]
        public void AnAlternativeFilterIsWindowedToo()
        {
            // '(a || b)' — the other compound shape BuildWhereClause emits.
            CompareWindowedWithUnpaged(10,
                () => Connection.LoadList<FirewallMangle>(
                          Connection.CreateParameter("chain", "prerouting", TikCommandParameterFormat.Filter),
                          Connection.CreateParameter("chain", "forward", TikCommandParameterFormat.Filter),
                          Connection.CreateParameter("#|", "", TikCommandParameterFormat.Filter))
                      .Select(r => r.Id),
                out var paged, out var unpaged);

            CollectionAssert.AreEqual(unpaged.ToList(), paged.ToList(),
                $"alternative filter: {paged.Count} row(s) windowed, {unpaged.Count} unpaged");
        }

        [TestMethod]
        public void TheLoadByNameShapeFindsItsRowInOneWindow()
        {
            // The shape this feature exists for. A page of one proves the row is selected by the find rather
            // than found by walking the table: an unfiltered window of size one would need as many requests
            // as there are interfaces before the row's position came up.
            var conn = Paged;
            int original = conn.CliReadPageSize;
            try
            {
                conn.CliReadPageSize = 1;

                var eth = Connection.LoadByName<Objects.Interface.Interface>(TestConstants.Interface);

                Assert.IsNotNull(eth);
                Assert.AreEqual(TestConstants.Interface, eth.Name);
            }
            finally
            {
                conn.CliReadPageSize = original;
            }
        }

        [TestMethod]
        public void AFilterThatMatchesNothingIsAnEmptyWindowedRead()
        {
            // A 'find' matching nothing yields an empty array, ':pick' clamps it to empty, and the window's
            // ':if' guard keeps it away from 'from=' — which refuses an empty array outright. So this must be
            // an empty result, not an error.
            var conn = Paged;
            int original = conn.CliReadPageSize;
            try
            {
                conn.CliReadPageSize = 10;

                var rows = Connection.LoadList<Objects.Interface.Interface>(
                    Connection.CreateParameter("name", "tik4net-no-such-interface",
                        TikCommandParameterFormat.Filter)).ToList();

                Assert.AreEqual(0, rows.Count);
            }
            finally
            {
                conn.CliReadPageSize = original;
            }
        }

        [TestMethod]
        public void AFilteredStatsMergeIsWindowedInBothHalves()
        {
            // FirewallFilter carries IncludeCliStats, so a read of it is two windowed queries merged by .id —
            // the clause has to reach both finds. This pins that the two-query windowed path runs and agrees
            // with the single-command one; that each half carries the clause is pinned by
            // CliPagedReadTests.TheStatsHalfOfATwoQueryReadIsFilteredInItsFindToo, because the counters the
            // merge brings in are volatile and cannot be compared across two reads.
            CompareWindowedWithUnpaged(10,
                () => Connection.LoadList<FirewallFilter>(Connection.CreateParameter("chain", "forward",
                          TikCommandParameterFormat.Filter)).Select(r => r.Id),
                out var paged, out var unpaged);

            CollectionAssert.AreEqual(unpaged.ToList(), paged.ToList(),
                "the stats half must cover the same rows as the config half");
        }
    }
}
