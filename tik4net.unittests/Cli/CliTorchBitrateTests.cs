using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A torch <c>tx</c>/<c>rx</c> display rate reads as the plain bits per second the binary API reports, and anything
    /// that is not a torch rate token is passed through for the mapper to refuse.
    /// </summary>
    [TestClass]
    public class CliTorchBitrateTests
    {
        private const string Footer = "-- [Q quit|D dump|C-z pause]";

        private static string TxOf(string token)
        {
            string output = Footer + "\r\n"
                + "TX                      RX\r\n"
                + token.PadRight(24) + "0bps\r\n"
                + Footer + "\r\n";
            return CliTorchParser.ParseTorchTable(output).Single().Words.Single(w => w.Key == "tx").Value;
        }

        [DataTestMethod]
        [DataRow("0bps", "0")]
        [DataRow("1664bps", "1664")]
        [DataRow("3.7kbps", "3700")]
        [DataRow("0.05kbps", "50")]
        [DataRow("599.9Mbps", "599900000")]
        [DataRow("1.25Gbps", "1250000000")]
        [DataRow("1Gbps", "1000000000")]
        public void ARateToken_ReadsAsBitsPerSecond(string token, string expected)
        {
            Assert.AreEqual(expected, TxOf(token));
        }

        [DataTestMethod]
        [DataRow("1.5bps", "1.5bps")]      // was "2": a fraction of a bit is no rate (TikDataRate), and torch never prints one
        public void AFractionOfABit_PassesThrough(string token, string expected)
        {
            Assert.AreEqual(expected, TxOf(token));
        }

        [DataTestMethod]
        [DataRow("15kBps")]                // the gate is case-sensitive: torch spells its units one way
        [DataRow("1.2Tbps")]
        [DataRow("unlimited")]
        [DataRow("12")]
        public void AnythingElse_PassesThrough(string token)
        {
            Assert.AreEqual(token, TxOf(token));
        }
    }
}
