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

        // What an unpaged read of the test menu sends: the print, bound to a variable, and its record count.
        private const string CountedProbeRead =
            ":local d [/probe print as-value]; :put $d; :put (\"#n=\" . [:len $d] . \"/\" . [:typeof [:find $d [:pick $d 0]]])";

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

                CollectionAssert.AreEqual(new[] { CountedProbeRead }, conn.Sent,
                    "off means off: one command for the whole table, counted");
            }
        }

        [TestMethod]
        public void AFilteredReadIsWindowedWithItsFilterInTheFind()
        {
            // The window covers MATCHING rows only, so one row out of 50 costs one request — where an
            // unfiltered window with the filter in its print would have walked all 25 of them.
            using (var conn = new PagingCliConnection(rowCount: 50))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>(new FakeParam("name", "r3")).ToList();

                Assert.AreEqual(1, conn.Sent.Count, string.Join(" | ", conn.Sent));
                StringAssert.Contains(conn.Sent[0], ":pick [/probe find (name=r3)] 0 2");
                CollectionAssert.AreEqual(new[] { "r3" }, rows.Select(r => r.Name).ToList());
            }
        }

        [TestMethod]
        public void AWindowsPrintDoesNotCarryTheFilterAsWell()
        {
            // The filter belongs in the find alone. Left in the print too, a row that stopped matching
            // between the two would make the answer shorter than the window's own '#w=' count — refused as
            // an incomplete response, with no retry path.
            using (var conn = new PagingCliConnection(rowCount: 50))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                conn.LoadList<PagedProbe>(new FakeParam("name", "r3")).ToList();

                Assert.IsFalse(conn.Sent[0].Contains(" where "),
                    "the filter is in the find, not in the print: " + conn.Sent[0]);
            }
        }

        [TestMethod]
        public void AFilteredReadThatSpansPagesWalksOnlyTheMatchingRows()
        {
            // Two matching rows of 50 at a page size of 1: two windows plus the short one that ends the
            // loop, and every offset is an offset into the MATCHES, not into the table.
            using (var conn = new PagingCliConnection(rowCount: 50))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 1;

                var rows = conn.LoadList<PagedProbe>(
                    new FakeParam("name", "r3"), new FakeParam("name", "r7"), new FakeParam("#|", "")).ToList();

                Assert.AreEqual(3, conn.Sent.Count, string.Join(" | ", conn.Sent));
                StringAssert.Contains(conn.Sent[0], ":pick [/probe find ((name=r3 || name=r7))] 0 1");
                StringAssert.Contains(conn.Sent[2], ":pick [/probe find ((name=r3 || name=r7))] 2 3");
                CollectionAssert.AreEqual(new[] { "r3", "r7" }, rows.Select(r => r.Name).ToList());
            }
        }

        [TestMethod]
        public void TheFindClauseIsParenthesisedForEveryShapeTheBuilderEmits()
        {
            // The parentheses are what put the clause in EXPRESSION context, the same grammar 'where' uses.
            // Unparenthesised, RouterOS parses it as the verb's arguments instead: '!(…)' is a syntax error
            // and an unknown field is refused by name. So the shape is pinned, not just the presence.
            foreach (string clause in new[]
                     {
                         "name=ether1", "name!=ether1", "mtu>1000", "name~\"ether\"", "comment",
                         "comment=\"\"", "(chain=prerouting || chain=forward)", "!(chain=prerouting)",
                     })
            {
                string cli = CliCommandBuilder.BuildPagedWindow("/probe/print", ":put [x]", 0, 10, clause);

                StringAssert.Contains(cli, "[/probe find (" + clause + ")]", clause);
            }
        }

        [TestMethod]
        public void AnUnfilteredWindowStillSpellsAPlainFind()
        {
            string cli = CliCommandBuilder.BuildPagedWindow("/probe/print", ":put [x]", 0, 10, null);

            StringAssert.Contains(cli, "[/probe find]");
            Assert.IsFalse(cli.Contains("find ("), "no empty parentheses for an unfiltered read: " + cli);
        }

        [TestMethod]
        public void TheStatsHalfOfATwoQueryReadIsFilteredInItsFindToo()
        {
            // Both halves are windowed by the same code, so an IncludeCliStats entity must get the clause in
            // each find and in neither print — otherwise the two halves window different row sets and the
            // merge lines up by .id against the wrong table.
            using (var conn = new PagingCliConnection(rowCount: 50))
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                conn.LoadList<StatsProbe>(new FakeParam("name", "r3")).ToList();

                Assert.AreEqual(2, conn.Sent.Count, string.Join(" | ", conn.Sent));
                Assert.IsTrue(conn.Sent[1].Contains("stats"), "the second query is the stats half: " + conn.Sent[1]);
                foreach (string sent in conn.Sent)
                {
                    StringAssert.Contains(sent, "find (name=r3)");
                    Assert.IsFalse(sent.Contains(" where "), sent);
                }
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
                Assert.AreEqual(CountedProbeRead, conn.Sent[1], "plain single-command read");
            }
        }

        [TestMethod]
        public void AMenuWhosePrintTakesNoFromIsReadInOneCommandInstead()
        {
            // '/log print' has no 'from=' argument (7.24), so the window line is refused as a parse error. That
            // is the menu saying it cannot be windowed, not the caller's mistake: the read must still happen.
            using (var conn = new PagingCliConnection(rowCount: 5) { WindowRefusal = "bad parameter from (line 1 column 137)" })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>().ToList();

                Assert.AreEqual(5, rows.Count, "the fallback must return the whole table");
                Assert.AreEqual(2, conn.Sent.Count, string.Join(" | ", conn.Sent));
                Assert.AreEqual(CountedProbeRead, conn.Sent[1], "plain single-command read");
            }
        }

        [TestMethod]
        public void AParseErrorInTheFilterIsStillTheCallersError()
        {
            using (var conn = new PagingCliConnection(rowCount: 5) { WindowRefusal = "expected yes or no (line 1 column 62)" })
            {
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var ex = Assert.ThrowsException<TikCommandTrapException>(() => conn.LoadList<PagedProbe>().ToList());
                StringAssert.Contains(ex.Message, "expected yes or no");
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

                CollectionAssert.AreEqual(new[] { CountedProbeRead }, conn.Sent,
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

        // ── A row that vanishes under a window ────────────────────────────────
        //
        // A window is 'find' then 'print from=$w'. When an id vanishes between the two — conntrack reaps its
        // own rows constantly — RouterOS aborts the line and answers only 'interrupted': no rows, no '#w=', no
        // error text (measured on 7.24 under churn, Docs/findings-cli.md §1). A whole-table print is not
        // affected; the router snapshots it.

        [TestMethod]
        public void AnInterruptedWindowIsTakenAgain()
        {
            using (var conn = new PagingCliConnection(rowCount: 5))
            {
                conn.InterruptOffsets[2] = 1;
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var rows = conn.LoadList<PagedProbe>().ToList();

                CollectionAssert.AreEqual(new[] { "r0", "r1", "r2", "r3", "r4" }, rows.Select(r => r.Name).ToList());
                Assert.AreEqual(2, conn.Sent.Count(s => s.Contains(":pick [/probe find] 2 4")),
                    "the interrupted window is asked for again: " + string.Join(" | ", conn.Sent));
            }
        }

        [TestMethod]
        public void AnInterruptedFirstWindowDoesNotSwitchPagingOff()
        {
            // The first window is where "this menu has no find" is discovered. An interrupted one is not that,
            // and taking it for that would read this menu unpaged for the rest of the connection — on the MAC
            // layer, exactly the read paging exists to prevent.
            using (var conn = new PagingCliConnection(rowCount: 5))
            {
                conn.InterruptOffsets[0] = 1;
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                Assert.AreEqual(5, conn.LoadList<PagedProbe>().Count());
                conn.Sent.Clear();
                conn.LoadList<PagedProbe>().ToList();

                StringAssert.Contains(conn.Sent[0], ":pick [/probe find] 0 2", "the next read still pages");
            }
        }

        [TestMethod]
        public void AWindowInterruptedEveryTimeIsRefusedAndSaysWhy()
        {
            using (var conn = new PagingCliConnection(rowCount: 5))
            {
                conn.InterruptOffsets[2] = int.MaxValue;
                conn.OpenScripted();
                conn.CliReadPageSize = 2;

                var ex = Assert.ThrowsException<TikConnectionResponseIncompleteException>(
                    () => conn.LoadList<PagedProbe>().ToList());

                StringAssert.Contains(ex.Message, "interrupted");
                StringAssert.Contains(ex.Message, "CliReadPageSize");
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

        /// <summary>The same probe menu read through the two-query stats path.</summary>
        [TikEntity("/probe", IncludeCliStats = true)]
        private sealed class StatsProbe
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

            /// <summary>When set, the whole answer to every window request, as the router refuses one.</summary>
            public string? WindowRefusal;
            public int DropRecordsForOffset = -1;
            public int DropOneRecordForOffset = -1;
            public int ExtraRecordForOffset = -1;

            /// <summary>Window offset → how many more times it answers 'interrupted', as the router does.</summary>
            public readonly Dictionary<int, int> InterruptOffsets = new Dictionary<int, int>();

            public PagingCliConnection(int rowCount) => _rowCount = rowCount;

            protected override string TransportName => "Paging";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync,
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            private Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);

                // The clause is optional and lives INSIDE the find's parentheses, exactly as a filtered
                // window spells it — 'find (name=r3)'. The double honours it, so a filtered read's window
                // holds the ids that match and nothing else, like the router's own find.
                // The clause itself contains parentheses, so the trailing '] <from> <to>]' is what anchors
                // this — a non-greedy clause would stop at the first ')' and answer a WINDOW request with
                // the unpaged shape, which reads as a menu that cannot be paged.
                var pick = Regex.Match(cliText,
                    @":pick \[[^\[\]]*? find(?: \((?<clause>.*)\))?\] (?<from>\d+) (?<to>\d+)\]");
                if (!pick.Success)   // unpaged read: the whole table, counted as the router counts it
                    return Task.FromResult(Rows(0, _rowCount) + ((char)10) + "#n=" + _rowCount + "/num");

                if (WindowRefusal != null)
                    return Task.FromResult(WindowRefusal);

                int from = int.Parse(pick.Groups["from"].Value);
                int to = int.Parse(pick.Groups["to"].Value);

                if (InterruptOffsets.TryGetValue(from, out int left) && left > 0)
                {
                    InterruptOffsets[from] = left == int.MaxValue ? left : left - 1;
                    return Task.FromResult("interrupted");   // the whole answer: no rows, no marker
                }

                // What 'find (clause)' resolves to, then the slice :pick takes of it.
                var matching = Enumerable.Range(0, _rowCount)
                    .Where(r => Matches(r, pick.Groups["clause"].Value))
                    .ToList();
                var ids = matching.Skip(from).Take(Math.Max(0, to - from)).ToList();
                int window = ids.Count;

                string body = from == DropRecordsForOffset ? string.Empty
                    : from == DropOneRecordForOffset ? Rows(ids.Skip(1))
                    : from == ExtraRecordForOffset ? Rows(ids) + ";.id=*99;name=split"
                    : Rows(ids);
                string marker = OmitMarker ? string.Empty : ((char)10) + "#w=" + window;
                return Task.FromResult(body + marker);
            }

            /// <summary>
            /// The clause shapes these tests use: none, <c>name=rN</c>, and <c>(a || b)</c> of those.
            /// </summary>
            private static bool Matches(int row, string clause)
            {
                if (string.IsNullOrEmpty(clause))
                    return true;
                string bare = clause.StartsWith("(") && clause.EndsWith(")")
                    ? clause.Substring(1, clause.Length - 2)
                    : clause;
                return bare.Split(new[] { "||" }, StringSplitOptions.None)
                    .Any(alt => alt.Trim() == "name=r" + row);
            }

            private static string Rows(int first, int lastExclusive)
                => Rows(Enumerable.Range(first, Math.Max(0, lastExclusive - first)));

            private static string Rows(IEnumerable<int> rows)
                => string.Join(";", rows.Select(r => ".id=*" + r + ";name=r" + r));

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }
    }
}
