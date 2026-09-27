using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Torch on RouterOS 6, which refuses <c>proplist=</c>: its plain table, read as the binary API reports the same
    /// arguments. The frame is a real 6.49.13 capture (<c>interface=ether1 src-address=0.0.0.0/0 port=any
    /// ip-protocol=any</c>); only runs of blank columns the terminal width had made thousands wide are narrowed, the
    /// same columns in every line, so the alignment is the router's.
    /// </summary>
    [TestClass]
    public class CliTorchTableTests
    {
        private const string Footer = "-- [Q quit|D dump|C-z pause]";

        private static readonly string[] FlowFrame =
        {
            "MAC-PROTOCOL    IP-PROTOCOL SRC-ADDRESS     SRC-PORT    DST-PORT              TX         RX TX-PACKETS RX-PACKETS",
            "ip              tcp         192.168.4.31    61224       23 (telnet)         0bps       0bps          0          0",
            "ip              tcp         192.168.4.31    63444       8291 (winbox)    3.5kbps    1664bps          1          2",
            "ip              tcp         192.168.4.83    44040       8291 (winbox)    3.6kbps    2.1kbps          1          2",
            "ip              udp         192.168.4.40    49153       6667                0bps    1712bps          0          1",
            "ip              udp         192.168.4.57    8001        8001                0bps    1920bps          0          1",
            "                                                                         7.2kbps    7.4kbps          2          6",
        };

        private static string Frames(params string[][] frames)
            => Footer + "\r\n" + string.Join("\r\n", frames.Select(f => string.Join("\r\n", f) + "\r\n" + Footer)) + "\r\n";

        private static Dictionary<string, string> Row(TikRecordSentence r)
            => r.Words.ToDictionary(w => w.Key, w => w.Value);

        [TestMethod]
        public void AFlowRow_ReadsAsTheApiSpellsIt()
        {
            var rows = CliOutputParser.ParseTorchTable(Frames(FlowFrame));

            Assert.AreEqual(6, rows.Count);
            var winbox = Row(rows[1]);
            Assert.AreEqual("ip", winbox["mac-protocol"]);
            Assert.AreEqual("tcp", winbox["ip-protocol"]);
            Assert.AreEqual("192.168.4.31", winbox["src-address"]);
            Assert.AreEqual("63444", winbox["src-port"]);
            Assert.AreEqual("8291 (winbox)", winbox["dst-port"], "the API keeps the service name too");
            Assert.AreEqual("3500", winbox["tx"], "right-aligned: the rate ends under TX, starting well left of it");
            Assert.AreEqual("1664", winbox["rx"]);
            Assert.AreEqual("1", winbox["tx-packets"]);
            Assert.AreEqual("2", winbox["rx-packets"]);
        }

        [TestMethod]
        public void TheTotalsRow_HasOnlyTheCounters_AsOverTheApi()
        {
            var totals = Row(CliOutputParser.ParseTorchTable(Frames(FlowFrame)).Last());

            CollectionAssert.AreEquivalent(new[] { "tx", "rx", "tx-packets", "rx-packets" }, totals.Keys.ToList());
            Assert.AreEqual("7200", totals["tx"]);
            Assert.AreEqual("6", totals["rx-packets"]);
        }

        [TestMethod]
        public void InterfaceAlone_PrintsTheTotalsOnly()
        {
            // 6.49.13, 'interface=ether1' and nothing else: the API answers the same four counters.
            var rows = CliOutputParser.ParseTorchTable(Frames(new[]
            {
                "        TX         RX TX-PACKETS RX-PACKETS",
                "   7.2kbps    4.4kbps          2          5",
            }));

            Assert.AreEqual(1, rows.Count);
            CollectionAssert.AreEqual(new[] { "7200", "4400", "2", "5" },
                new[] { "tx", "rx", "tx-packets", "rx-packets" }.Select(k => Row(rows[0])[k]).ToList());
        }

        [TestMethod]
        public void OnlyTheLastFrameIsRead()
        {
            var older = new[] { "        TX         RX TX-PACKETS RX-PACKETS", "      0bps       0bps          0          0" };
            var rows = CliOutputParser.ParseTorchTable(Frames(older, FlowFrame));

            Assert.AreEqual(6, rows.Count);
        }

        [TestMethod]
        public void WithoutProplist_TheCommandCarriesNone()
        {
            var pars = new List<ITikCommandParameter> { new TikCommandParameter("interface", "ether1", TikCommandParameterFormat.NameValue) };

            string plain = CliCommandBuilder.BuildTorchSnapshot("/tool/torch", pars, 2, withProplist: false);

            Assert.AreEqual(":put [/tool torch interface=ether1 duration=4 freeze-frame-interval=2]", plain);
            StringAssert.Contains(CliCommandBuilder.BuildTorchSnapshot("/tool/torch", pars, 2), " proplist=");
        }
    }
}
