// Nullable-enabled on its own: the test project as a whole is not (see the note in
// Directory.Build.props), but this file implements ITikCommandParameter, whose signature is annotated.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Objects;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Reading a table in slices instead of one command — the commands that go out, and the rows that come
    /// back. See <see cref="ITikCliPagedReadConnection"/> for why the MAC-layer transports do this by default
    /// and Docs/findings-cli.md §1 for the RouterOS behaviour the command shape has to respect.
    /// </summary>
    [TestClass]
    public class CliPagedReadTests
    {
        // ── The command shape ─────────────────────────────────────────────────
        //
        // These pin argument ORDER, and the reason is that getting it wrong fails silently: RouterOS lets
        // 'where' swallow everything after it, so a 'from=' placed behind the filter is absorbed into the
        // expression and the router answers with the WHOLE table. Measured at 1672 rows where 2 were asked for.

        private static IList<ITikCommandParameter> NoParams() => new List<ITikCommandParameter>();

        private static IList<ITikCommandParameter> WithFilter()
            => new List<ITikCommandParameter> { new FakeParam("chain", "prerouting") };

        [TestMethod]
        public void TheRowSelectorFollowsAsValue()
        {
            string cli = CliCommandBuilder.BuildPrint("/ip/firewall/mangle/print", NoParams(), false, "0,1,2");

            Assert.AreEqual(":put [/ip firewall mangle print as-value from=0,1,2]", cli);
        }

        [TestMethod]
        public void TheRowSelectorComesBeforeTheWhereClause()
        {
            string cli = CliCommandBuilder.BuildPrint("/ip/firewall/mangle/print", WithFilter(), false, "0,1");

            int from = cli.IndexOf("from=", StringComparison.Ordinal);
            int where = cli.IndexOf(" where ", StringComparison.Ordinal);
            Assert.IsTrue(from > 0 && where > 0, cli);
            Assert.IsTrue(from < where,
                "'where' consumes what follows it, so a from= behind the filter is silently swallowed and the "
                + "router answers with the whole table: " + cli);
        }

        [TestMethod]
        public void NoSelectorLeavesTheCommandExactlyAsItWas()
        {
            Assert.AreEqual(CliCommandBuilder.BuildPrint("/ip/firewall/mangle/print", NoParams()),
                            CliCommandBuilder.BuildPrint("/ip/firewall/mangle/print", NoParams(), false, null));
        }

        [TestMethod]
        public void TheStatsQueryTakesTheSameSelector()
        {
            string cli = CliCommandBuilder.BuildPrintStats("/ip/firewall/mangle/print", NoParams(), false, "0,1");

            Assert.AreEqual(":put [/ip firewall mangle print stats as-value from=0,1]", cli);
        }

        [TestMethod]
        public void AWindowPicksIdsGuardsTheEmptyCaseAndStatesItsOwnSize()
        {
            string inner = CliCommandBuilder.BuildPrint("/ip/firewall/mangle/print", NoParams(), false,
                CliCommandBuilder.WindowVariable);

            string cli = CliCommandBuilder.BuildPagedWindow("/ip/firewall/mangle/print", inner, 200, 100);

            Assert.AreEqual(
                ":local w [:pick [/ip firewall mangle find] 200 300]; "
                + ":if ([:len $w] > 0) do={ :put [/ip firewall mangle print as-value from=$w] }; "
                + ":put (\"#w=\" . [:len $w])",
                cli);
        }

        [TestMethod]
        public void TheWindowIsTakenFromTheMenuNotTheVerb()
        {
            string cli = CliCommandBuilder.BuildPagedWindow("/ip/firewall/mangle/print", ":put [x]", 0, 10);

            StringAssert.Contains(cli, "[/ip firewall mangle find]");
            Assert.IsFalse(cli.Contains("mangle print find"), cli);
        }

        // ── The loop ──────────────────────────────────────────────────────────

        [TestMethod]
        public void ATableLargerThanThePageIsReadInSlicesAndConcatenatedInOrder()
        {
            using (var conn = new PagingCliConnection(rowCount: 5))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>().ToList();

                Assert.AreEqual(3, conn.Sent.Count, string.Join(" | ", conn.Sent));
                StringAssert.Contains(conn.Sent[0], ":pick [/probe find] 0 2");
                StringAssert.Contains(conn.Sent[2], ":pick [/probe find] 4 6");
                StringAssert.Contains(conn.Sent[0], "from=$w");
                CollectionAssert.AreEqual(new[] { "r0", "r1", "r2", "r3", "r4" },
                    rows.Select(r => r.Name).ToList(), "slices concatenate in table order");
            }
        }

        [TestMethod]
        public void ATableThatFitsInOnePageIsStillReadWithASingleCommand()
        {
            using (var conn = new PagingCliConnection(rowCount: 2))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 5;

                var rows = conn.LoadList<PagedProbe>().ToList();

                Assert.AreEqual(1, conn.Sent.Count,
                    "a table smaller than a page must cost ONE request: " + string.Join(" | ", conn.Sent));
                Assert.AreEqual(2, rows.Count);
            }
        }

        [TestMethod]
        public void PagingOffSendsTheOneCommandItAlwaysDid()
        {
            using (var conn = new PagingCliConnection(rowCount: 5))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 0;

                conn.LoadList<PagedProbe>().ToList();

                CollectionAssert.AreEqual(new[] { ":put [/probe print as-value]" }, conn.Sent,
                    "off means off: the plain single-command read, unwrapped");
            }
        }

        [TestMethod]
        public void AFilteredReadIsNotPagedAtAll()
        {
            // Windows are taken over the unfiltered table, so filtering does not shrink the work — measured
            // over MAC-Telnet at 9.6 s for 49 of 1672 rows. Until the filter can be pushed into 'find', the
            // single command is the faster answer and the datagram-loss check covers it.
            using (var conn = new PagingCliConnection(rowCount: 50))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                conn.LoadList<PagedProbe>(new FakeParam("name", "r3")).ToList();

                Assert.AreEqual(1, conn.Sent.Count, string.Join(" | ", conn.Sent));
                StringAssert.Contains(conn.Sent[0], "where name=r3");
                Assert.IsFalse(conn.Sent[0].Contains(":pick"), conn.Sent[0]);
            }
        }

        [TestMethod]
        public void AMenuThatCannotReportAWindowSizeIsReadInOneCommandInstead()
        {
            // Not every menu can be windowed — a singleton has no 'find' to pick from. Discovering that on
            // the FIRST window means nothing has been returned yet, so the read simply happens the way every
            // read happened before paging existed.
            using (var conn = new PagingCliConnection(rowCount: 5) { OmitMarker = true })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>().ToList();

                Assert.AreEqual(5, rows.Count, "the fallback must return the whole table");
                Assert.AreEqual(2, conn.Sent.Count, string.Join(" | ", conn.Sent));
                Assert.AreEqual(":put [/probe print as-value]", conn.Sent[1], "plain single-command read");
            }
        }

        [TestMethod]
        public void AMenuFoundUnpageableIsNotProbedAgainOnTheSameConnection()
        {
            using (var conn = new PagingCliConnection(rowCount: 5) { OmitMarker = true })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                conn.LoadList<PagedProbe>().ToList();
                conn.Sent.Clear();
                conn.LoadList<PagedProbe>().ToList();

                CollectionAssert.AreEqual(new[] { ":put [/probe print as-value]" }, conn.Sent,
                    "the second read must not pay for the discovery again");
            }
        }

        [TestMethod]
        public void ATableThatIsAnExactMultipleOfThePageEndsOnAnEmptyWindow()
        {
            using (var conn = new PagingCliConnection(rowCount: 6))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>().ToList();

                // Four, not three: the loop only learns it is done from a fourth window that comes back
                // empty. That is the one request a row count up front would save, and the price of not
                // paying for one on every small read.
                Assert.AreEqual(4, conn.Sent.Count, string.Join(" | ", conn.Sent));
                Assert.AreEqual(6, rows.Count);
            }
        }

        // ── The count the router states ───────────────────────────────────────
        //
        // A window is never filtered, so '#w=' is the number of records its answer must carry. These are the
        // cases where it does not — rows lost on the way, or a parser that split one — and the read must
        // refuse rather than hand back something that is not the table.

        [TestMethod]
        public void AWindowThatAnswersNoRowsForTheIdsItHeldIsRefused()
        {
            using (var conn = new PagingCliConnection(rowCount: 6) { DropRecordsForOffset = 2 })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.LoadList<PagedProbe>().ToList());

                StringAssert.Contains(ex.Message, "offset 2");
                StringAssert.Contains(ex.Message, "held 2 row(s)");
                StringAssert.Contains(ex.Message, "0 record(s)");
            }
        }

        [TestMethod]
        public void AWindowMissingOneRowIsRefused()
        {
            // The realistic shape of the loss: one row gone from the middle, everything else intact, the
            // answer still well-formed and still ending with the marker.
            using (var conn = new PagingCliConnection(rowCount: 5) { DropOneRecordForOffset = 2 })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.LoadList<PagedProbe>().ToList());

                StringAssert.Contains(ex.Message, "held 2 row(s)");
                StringAssert.Contains(ex.Message, "1 record(s)");
                Assert.IsNotNull(ex.PartialResponse, "the answer that was refused is kept for diagnosis");
            }
        }

        [TestMethod]
        public void AWindowAnsweringMoreRowsThanItHeldIsRefusedToo()
        {
            // Not a loss but no more trustworthy: a record the parser split in two is a row that does not exist.
            using (var conn = new PagingCliConnection(rowCount: 5) { ExtraRecordForOffset = 0 })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.LoadList<PagedProbe>().ToList());

                StringAssert.Contains(ex.Message, "3 record(s)");
            }
        }

        [TestMethod]
        public void ANegativePageSizeIsRefused()
        {
            using (var conn = new PagingCliConnection(rowCount: 1))
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => conn.CliReadPageSize = -1);
            }
        }

        private sealed class FakeParam : ITikCommandParameter
        {
            public FakeParam(string name, string value)
            {
                Name = name;
                Value = value;
                ParameterFormat = TikCommandParameterFormat.Filter;
            }

            public string Name { get; set; }
            public string? Value { get; set; }
            public TikCommandParameterFormat ParameterFormat { get; set; }
        }

        // ── The double ────────────────────────────────────────────────────────

        [TikEntity("/probe")]
        private sealed class PagedProbe
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string? Id { get; set; }

            [TikProperty("name")]
            public string? Name { get; set; }
        }

        /// <summary>
        /// Answers <c>:len</c> with a row count and every <c>print</c> with the rows its <c>from=</c> names,
        /// so the test can assert on what was asked for as well as on what came back.
        /// </summary>
        private sealed class PagingCliConnection : CliConnectionBase
        {
            private readonly int _rowCount;
            public readonly List<string> Sent = new List<string>();
            public bool OmitMarker;
            public int DropRecordsForOffset = -1;
            public int DropOneRecordForOffset = -1;
            public int ExtraRecordForOffset = -1;

            public PagingCliConnection(int rowCount) => _rowCount = rowCount;

            protected override string TransportName => "Paging";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync,
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);

                var pick = Regex.Match(cliText, @":pick \[[^\]]*find\] (\d+) (\d+)\]");
                if (!pick.Success)
                    return Task.FromResult(Rows(0, _rowCount));   // unpaged read: the whole table

                int from = int.Parse(pick.Groups[1].Value);
                int to = int.Parse(pick.Groups[2].Value);
                int window = Math.Max(0, Math.Min(to, _rowCount) - Math.Min(from, _rowCount));

                string body = from == DropRecordsForOffset ? string.Empty
                    : from == DropOneRecordForOffset ? Rows(from + 1, from + window)
                    : from == ExtraRecordForOffset ? Rows(from, from + window) + ";.id=*99;name=split"
                    : Rows(from, from + window);
                string marker = OmitMarker ? string.Empty : ((char)10) + "#w=" + window;
                return Task.FromResult(body + marker);
            }

            private static string Rows(int first, int lastExclusive)
                => string.Join(";", Enumerable.Range(first, Math.Max(0, lastExclusive - first))
                    .Select(r => ".id=*" + r + ";name=r" + r));

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
