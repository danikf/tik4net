using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers the two pieces that let a CLI transport deliver a long-running monitor while it is still
    /// running (P2.50): the line streamer that carves completed lines out of a growing read buffer, and the
    /// column parser that reads RouterOS's interactive table.
    /// </summary>
    /// <remarks>
    /// The table samples below are verbatim from a live 7.23.2 over Telnet — including the column offsets,
    /// which are the whole point of the parser. See <c>Docs/findings-cli.md</c> §9.
    /// </remarks>
    [TestClass]
    public class CliStreamingMonitorTests
    {
        // ── CliLineStreamer ────────────────────────────────────────────────────

        private static List<string> FeedAll(params string[] buffers)
        {
            var got = new List<string>();
            var streamer = new CliLineStreamer(got.Add);
            foreach (var b in buffers)
                streamer.Feed(b);
            return got;
        }

        [TestMethod]
        public void Streamer_DeliversEachLineOnceAsTheBufferGrows()
        {
            // The buffer is the WHOLE accumulated text every time, not the delta — that is how the read
            // loops call it (they re-strip the accumulator on each pass).
            var got = FeedAll("one\n", "one\ntwo\n", "one\ntwo\nthree\n");

            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, got);
        }

        [TestMethod]
        public void Streamer_WithholdsThePartialTrailingLine()
        {
            // A half-arrived row must not be parsed — it would produce a record with truncated values.
            var got = FeedAll("complete\npart");

            CollectionAssert.AreEqual(new[] { "complete" }, got);
        }

        [TestMethod]
        public void Streamer_DeliversTheTrailingLineOnceItsNewlineArrives()
        {
            var got = FeedAll("complete\npart", "complete\npartial line\n");

            CollectionAssert.AreEqual(new[] { "complete", "partial line" }, got);
        }

        [TestMethod]
        public void Streamer_KeepsLeadingWhitespaceAndDropsCarriageReturns()
        {
            // Leading spaces are DATA: the column parser locates values by character offset. RouterOS wraps
            // its lines in CR, which is not.
            var got = FeedAll("\r    0 127.0.0.1\r\n");

            CollectionAssert.AreEqual(new[] { "    0 127.0.0.1" }, got);
        }

        [TestMethod]
        public void Streamer_DoesNotRedeliverALineWhenAnEarlierOneShrinks()
        {
            // The hazard the streamer is built around: a chunk boundary falling inside an escape sequence
            // leaves it un-stripped on one pass and stripped on the next, so the text BEFORE the current
            // position gets shorter. A character-offset implementation would resume in the wrong place;
            // counting delivered lines cannot.
            var got = FeedAll("ESCAPE-LEFTOVERheader\nrow one\n", "header\nrow one\nrow two\n");

            CollectionAssert.AreEqual(new[] { "ESCAPE-LEFTOVERheader", "row one", "row two" }, got);
        }

        [TestMethod]
        public void Streamer_WithoutACallbackIsInert()
        {
            var streamer = new CliLineStreamer(null);
            streamer.Feed("anything\n");   // must not throw

            Assert.IsFalse(streamer.IsActive);
        }

        // ── CliTableParser ─────────────────────────────────────────────────────

        // Live capture, offsets preserved: SEQ[2..4] HOST[6..9] SIZE[47..50] TTL[52..54] TIME[56..59]
        // STATUS[67..72] (measured with a leading CR stripped).
        private const string PingHeader =
            "  SEQ HOST                                     SIZE TTL TIME       STATUS      ";
        private const string PingRowOk =
            "    0 127.0.0.1                                  56  64 107us     ";
        private const string PingRowTimeout =
            "    1 192.168.4.99                                                 timeout     ";
        private const string PingSummary =
            "    sent=2 received=2 packet-loss=0% min-rtt=107us avg-rtt=136us max-rtt=165us ";

        private static TikRecordSentence Row(CliTableParser p, string line)
        {
            var rec = p.Feed(line);
            Assert.IsNotNull(rec, "expected '" + line + "' to parse as a data row");
            return rec;
        }

        [TestMethod]
        public void Table_ReadsASuccessfulPingRow()
        {
            var p = new CliTableParser();
            Assert.IsNull(p.Feed("/ping address=127.0.0.1 count=2"), "the command echo is not a row");
            Assert.IsNull(p.Feed(PingHeader), "the header is not a row");

            var rec = Row(p, PingRowOk);

            // Field names are the header lower-cased, which is what the binary API calls them.
            Assert.AreEqual("0", rec.GetResponseField("seq"));
            Assert.AreEqual("127.0.0.1", rec.GetResponseField("host"));
            Assert.AreEqual("56", rec.GetResponseField("size"));
            Assert.AreEqual("64", rec.GetResponseField("ttl"));
            Assert.AreEqual("107us", rec.GetResponseField("time"));
            Assert.AreEqual(string.Empty, rec.GetResponseFieldOrDefault("status", string.Empty));
        }

        [TestMethod]
        public void Table_PutsATimeoutInStatusAndNotInSize()
        {
            // The reason the parser is positional. Splitting this row on whitespace yields
            // [1, 192.168.4.99, timeout] and maps 'timeout' onto 'size'.
            var p = new CliTableParser();
            p.Feed(PingHeader);

            var rec = Row(p, PingRowTimeout);

            Assert.AreEqual("timeout", rec.GetResponseField("status"));
            Assert.AreEqual("1", rec.GetResponseField("seq"));
            Assert.AreEqual("192.168.4.99", rec.GetResponseField("host"));
            Assert.AreEqual(string.Empty, rec.GetResponseFieldOrDefault("size", string.Empty));
            Assert.AreEqual(string.Empty, rec.GetResponseFieldOrDefault("time", string.Empty));
        }

        [TestMethod]
        public void Table_DropsTheAggregateSummaryLine()
        {
            // 'sent=…/received=…/min-rtt=…' summarises the table; emitting it as a record would add a
            // phantom row with no seq.
            var p = new CliTableParser();
            p.Feed(PingHeader);

            Assert.IsNull(p.Feed(PingSummary));
        }

        [TestMethod]
        public void Table_DropsThePromptAndBlankLines()
        {
            var p = new CliTableParser();
            p.Feed(PingHeader);

            Assert.IsNull(p.Feed("[admin@CHR] > "));
            Assert.IsNull(p.Feed(string.Empty));
            Assert.IsNull(p.Feed("   "));
        }

        [TestMethod]
        public void Table_IgnoresEverythingBeforeTheHeader()
        {
            // Streamed lines are raw — the echo and any login/banner leftovers reach the parser too.
            var p = new CliTableParser();

            Assert.IsFalse(p.HasHeader);
            Assert.IsNull(p.Feed("/ping address=127.0.0.1 count=2"));
            Assert.IsNull(p.Feed("    0 127.0.0.1                                  56  64 107us"));
            Assert.IsFalse(p.HasHeader, "no header seen yet, so nothing may be parsed as a row");
        }

        [TestMethod]
        public void Table_RelearnsTheLayoutWhenTheHeaderIsRepainted()
        {
            // Traceroute repaints its whole table every round, header included, and the column widths can
            // change with the data. A second header means "new frame", not "bad input".
            var p = new CliTableParser();
            p.Feed(PingHeader);
            Row(p, PingRowOk);

            const string narrowHeader = " SEQ HOST      TIME  ";
            const string narrowRow    = "   7 10.0.0.1  93us ";
            Assert.IsNull(p.Feed(narrowHeader));

            var rec = Row(p, narrowRow);
            Assert.AreEqual("7", rec.GetResponseField("seq"));
            Assert.AreEqual("10.0.0.1", rec.GetResponseField("host"));
            Assert.AreEqual("93us", rec.GetResponseField("time"));
        }

        [TestMethod]
        public void Table_ParsesAWholeCaptureIntoOneRecordPerRow()
        {
            var p = new CliTableParser();
            var rows = new[]
                {
                    "/ping address=127.0.0.1 count=2", PingHeader, PingRowOk, PingRowTimeout, PingSummary,
                    "[admin@CHR] > ",
                }
                .Select(p.Feed).Where(r => r != null).ToList();

            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "0", "1" },
                rows.Select(r => r.GetResponseField("seq")).ToArray());
        }

        // ── CliCommandBuilder ──────────────────────────────────────────────────

        private static ITikCommandParameter NameValue(string name, string value)
            => new TikCommandParameter(name, value, TikCommandParameterFormat.NameValue);

        [TestMethod]
        public void Builder_InteractiveMonitorSendsTheBareCommand()
        {
            // No ':put [' wrapper and no 'as-value' — that wrapper is exactly what stops the router
            // emitting anything until the command has finished.
            var parameters = new List<ITikCommandParameter>
            {
                NameValue("address", "127.0.0.1"),
                NameValue("count", "20"),
            };

            string cli = CliCommandBuilder.BuildInteractiveMonitor("/ping", parameters, "count=1");

            Assert.AreEqual("/ping address=127.0.0.1 count=20", cli);
        }

        [TestMethod]
        public void Builder_InteractiveMonitorAppliesTheSnapshotModifierWhenTheCallerGaveNone()
        {
            var parameters = new List<ITikCommandParameter> { NameValue("address", "127.0.0.1") };

            string cli = CliCommandBuilder.BuildInteractiveMonitor("/ping", parameters, "count=1");

            Assert.AreEqual("/ping address=127.0.0.1 count=1", cli);
        }

        [TestMethod]
        public void Builder_InteractiveAndAsValueFormsCarryTheSameInputs()
        {
            // The two builders must differ ONLY in the wrapper: a divergence in how inputs are emitted
            // would mean the streaming path silently runs a different command.
            var parameters = new List<ITikCommandParameter>
            {
                NameValue("interface", "ether1"),
                NameValue("duration", "5"),
            };

            string bare = CliCommandBuilder.BuildInteractiveMonitor("/interface/monitor-traffic", parameters, "once");
            string wrapped = CliCommandBuilder.BuildMonitorSnapshot("/interface/monitor-traffic", parameters, "once");

            Assert.AreEqual(":put [" + bare + " as-value]", wrapped);
        }
    }
}
