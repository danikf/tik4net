using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A paused-screen table — RoMON discover on RouterOS 6.49.13, read as its plain table because 6.x has no
    /// as-value on monitors. The layout is the router's (column positions as measured); the MAC addresses and the
    /// neighbour's identity are placeholders.
    /// </summary>
    [TestClass]
    public class CliTableParserScreenTests
    {
        private static readonly string[] Screen =
        {
            "Flags: A - active ",
            "  ADDRESS              COST  HOPS PATH                L2MTU IDENTITY    VERSION    BOARD",
            "-- [Q quit|D dump|C-z pause]",
            "Flags: A - active ",
            "  ADDRESS              COST  HOPS PATH                L2MTU IDENTITY    VERSION    BOARD",
            "A AA:BB:CC:DD:EE:FF     200     1 AA:BB:CC:DD:EE:FF    1500 CHR         7.24.4     CHR",
            "A AA:BB:CC:DD:EE:01     200     1 AA:BB:CC:DD:EE:01    1500 home-router 7.17rc3    RB4011iGS+5HacQ2HnD",
            "-- [Q quit|D dump|C-z pause]",
        };

        private static List<Dictionary<string, string>> Parse()
        {
            var table = new CliTableParser();
            return Screen.Select(table.Feed).Where(r => r != null)
                .Select(r => r!.Words.ToDictionary(w => w.Key, w => w.Value)).ToList();
        }

        [TestMethod]
        public void TheFooterIsNoRow()
        {
            Assert.IsFalse(Parse().Any(r => r.Values.Any(v => v.Contains("Q quit"))));
        }

        [TestMethod]
        public void AnAllCapitalsRow_IsARow_NotANewHeader()
        {
            // The CHR row has no lower-case letter anywhere; it was taken for a header, and the next row was then read
            // against it with its values for field names.
            var rows = Parse();

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", rows[0]["address"]);
            Assert.AreEqual("CHR", rows[0]["identity"]);
            Assert.AreEqual("7.24.4", rows[0]["version"]);
            Assert.AreEqual("home-router", rows[1]["identity"]);
            Assert.AreEqual("200", rows[1]["cost"]);
            Assert.AreEqual("1", rows[1]["hops"]);
        }

        [TestMethod]
        public void ARepaintedHeader_StillStartsANewFrame()
        {
            // traceroute repaints its header every round; a real header line is still one.
            var table = new CliTableParser();
            Assert.IsNull(table.Feed("  # ADDRESS                          LOSS SENT    LAST     AVG    BEST   WORST STD-DEV STATUS"));
            Assert.IsTrue(table.HasHeader);
        }
    }
}
