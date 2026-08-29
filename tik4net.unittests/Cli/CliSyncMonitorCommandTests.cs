using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers how a monitor command reached through a READ method (<c>ExecuteList</c>/<c>LoadList</c> of
    /// <c>/ping</c>, <c>/tool/traceroute</c>, <c>/interface/monitor-traffic</c>) is turned into CLI text.
    /// </summary>
    /// <remarks>
    /// P2.51: it used to go to <c>BuildPrint</c>, which understands print modifiers and a <c>where</c> clause
    /// and nothing else — so every input was dropped. Measured on 7.23.2 over Telnet, <c>/ping</c> with
    /// <c>=address=127.0.0.1 =count=2</c> went out as <c>:put [/ping as-value]</c> and the router answered
    /// <c>failure: resolve failed</c>; <c>/interface/monitor-traffic</c> with <c>=interface=ether1</c> went out
    /// as <c>:put [/interface monitor-traffic once as-value]</c>, was rejected with <c>input does not match any
    /// value of interface</c>, and the call still returned success.
    /// </remarks>
    [TestClass]
    public class CliSyncMonitorCommandTests
    {
        private static IList<ITikCommandParameter> Params(TikCommandParameterFormat format,
            params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, format))
                .ToList();

        // ── Which verbs take the monitor shape ─────────────────────────────────

        [TestMethod]
        public void MonitorVerbs_AreRecognisedOnTheReadPath()
        {
            foreach (string verb in new[] { "ping", "traceroute", "monitor", "monitor-traffic", "profile" })
                Assert.IsTrue(CliMonitorVerbs.IsSyncMonitorVerb(verb), verb);
        }

        [TestMethod]
        public void PrintAndFriendsAreNotMonitorVerbs()
        {
            // The read path must keep building an ordinary print for everything else — the two shapes disagree
            // about what the parameters mean (inputs vs. a where-clause), so a false positive is silent damage.
            foreach (string verb in new[] { "print", "getall", "listen", "add", "set", "remove", "wol", "run" })
                Assert.IsFalse(CliMonitorVerbs.IsSyncMonitorVerb(verb), verb);
        }

        [TestMethod]
        public void TorchIsNotDrivenAsAOneShotMonitor()
        {
            // Measured on 7.23.2: ':put [/tool torch interface=ether1 once as-value]' answers
            // "bad parameter once (line 1 column 40)", and torch's as-value form prints nothing even without
            // the modifier. It has no working one-shot CLI form to route a synchronous read to.
            Assert.IsFalse(CliMonitorVerbs.IsSyncMonitorVerb("torch"));
        }

        // ── Snapshot modifiers ─────────────────────────────────────────────────

        [TestMethod]
        public void TracerouteAsksForACountAndNotForOnce()
        {
            // 'once' is the default modifier and traceroute REFUSES it: measured on 7.23.2,
            // ':put [/tool traceroute address=127.0.0.1 count=1 once as-value]' answers
            // "bad parameter once (line 1 column 54)". Dropping the default here is the whole fix.
            Assert.AreEqual("count=1", CliMonitorVerbs.SnapshotModifier("traceroute"));
            Assert.AreEqual("count=1", CliMonitorVerbs.SnapshotModifier("ping"));
            Assert.AreEqual("duration=1", CliMonitorVerbs.SnapshotModifier("profile"));
            Assert.AreEqual("once", CliMonitorVerbs.SnapshotModifier("monitor-traffic"));
        }

        [TestMethod]
        public void TheCallersOwnModifierIsNotDuplicated()
        {
            var cli = CliCommandBuilder.BuildMonitorSnapshot("/ping",
                Params(TikCommandParameterFormat.NameValue, ("address", "127.0.0.1"), ("count", "5")),
                CliMonitorVerbs.SnapshotModifier("ping"));

            Assert.AreEqual(":put [/ping address=127.0.0.1 count=5 as-value]", cli);
        }

        // ── The read path's Filter-format inputs ───────────────────────────────

        [TestMethod]
        public void ReadPathInputsSurviveAsCommandInputs()
        {
            // TikGenericCommand.ResolveParamsForRead rewrites the caller's parameters to Filter format before
            // RunPrint sees them. A monitor has no query semantics, so without includeFilters they all
            // vanished and the command went out bare.
            var cli = CliCommandBuilder.BuildMonitorSnapshot("/ping",
                Params(TikCommandParameterFormat.Filter, ("address", "127.0.0.1"), ("count", "2")),
                CliMonitorVerbs.SnapshotModifier("ping"), includeFilters: true);

            Assert.AreEqual(":put [/ping address=127.0.0.1 count=2 as-value]", cli);
        }

        [TestMethod]
        public void WithoutIncludeFilters_TheAsyncPathIsUnchanged()
        {
            // ExecuteWithCallback passes the caller's parameters through untouched (Default format, never rewritten),
            // so the async path must keep ignoring Filter parameters exactly as before.
            var cli = CliCommandBuilder.BuildMonitorSnapshot("/ping",
                Params(TikCommandParameterFormat.Filter, ("address", "127.0.0.1")),
                CliMonitorVerbs.SnapshotModifier("ping"));

            Assert.AreEqual(":put [/ping count=1 as-value]", cli);
        }

        [TestMethod]
        public void MonitorTrafficCarriesItsInterfaceAndTheOnceModifier()
        {
            var cli = CliCommandBuilder.BuildMonitorSnapshot("/interface/monitor-traffic",
                Params(TikCommandParameterFormat.Filter, ("interface", "ether1")),
                CliMonitorVerbs.SnapshotModifier("monitor-traffic"), includeFilters: true);

            Assert.AreEqual(":put [/interface monitor-traffic interface=ether1 once as-value]", cli);
        }

        [TestMethod]
        public void EthernetMonitorKeepsTheNumbersAndOnceFormItAlwaysHad()
        {
            // /interface/ethernet/monitor moved from BuildPrint (which special-cased 'numbers' and 'once') to
            // the monitor builder; the emitted text must not change.
            var cli = CliCommandBuilder.BuildMonitorSnapshot("/interface/ethernet/monitor",
                Params(TikCommandParameterFormat.Filter, ("numbers", "ether1"), ("once", "")),
                CliMonitorVerbs.SnapshotModifier("monitor"), includeFilters: true);

            Assert.AreEqual(":put [/interface ethernet monitor numbers=ether1 once as-value]", cli);
        }

        // ── The traceroute table's row ordinal ─────────────────────────────────

        [TestMethod]
        public void TracerouteRowOrdinalIsNotAField()
        {
            // Verbatim from a live 7.23.2: traceroute's interactive table leads with a '#' column that is the
            // terminal's row number. The binary API returns '.id' in its place and no field called '#'.
            var table = new CliTableParser();
            Assert.IsNull(table.Feed("Columns: ADDRESS, LOSS, SENT, LAST, AVG, BEST, WORST, STD-DEV"));
            Assert.IsNull(table.Feed("#  ADDRESS    LOSS  SENT  LAST  AVG  BEST  WORST  STD-DEV"));

            var row = table.Feed("0  127.0.0.1  0%       1  0ms     0     0      0        0");

            Assert.IsNotNull(row);
            Assert.IsFalse(row.Words.ContainsKey("#"));
            Assert.AreEqual("127.0.0.1", row.GetResponseField("address"));
            Assert.AreEqual("0%", row.GetResponseField("loss"));
            Assert.AreEqual("1", row.GetResponseField("sent"));
            Assert.AreEqual("0", row.GetResponseField("std-dev"));
        }
    }
}
