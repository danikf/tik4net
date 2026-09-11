// MacLayerLargeReadProbe — can the MAC-layer CLI carriers read a large table inside the receive deadline?
//
// Measured before paged reads existed, a single-command read of /queue/tree (681 rows, ~316 KB over two
// queries) and /ip/firewall/mangle (1672 rows) moved 7-15 KB/s over MacTelnet and WinboxCliMac and hit the 30 s
// ReceiveTimeout part-way through. With paging each window is its own command; this times the same reads.
//
// Env:  TIK_PROBE=1                    required, or the test reports Inconclusive
//       TIK4NET_PROBE_TRANSPORTS       comma-separated (default MacTelnet,WinboxCliMac)
//       TIK4NET_LARGE_READS            reads per table per transport (default 3)
//       TIK4NET_PROBE_LOG              also append the output to this file

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Queue;

namespace tik4net.integrationtests
{
    // Gated on TIK_PROBE=1 rather than [Ignore], so it can be run from the command line without editing the
    // file (MSTest skips [Ignore] even under --filter) and a normal suite run still never pays for it.
    [TestClass]
    public class MacLayerLargeReadProbe
    {
        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        [TestMethod]
        public void Probe_LargeReadsOverTheMacLayer()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 against a router carrying large tables.");

            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS") ?? "MacTelnet,WinboxCliMac";
            int reads = int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_LARGE_READS"), out int n) && n > 0 ? n : 3;

            foreach (string name in only.Split(','))
            {
                var transport = (TikConnectionType)Enum.Parse(typeof(TikConnectionType), name.Trim(), ignoreCase: true);
                Log("");
                Log($"══ {transport} — {reads} read(s) of each table ══");
                using (ITikConnection connection = ConnectionFactory.CreateConnection(transport))
                {
                    string mac = ConfigurationManager.AppSettings["routerMac"];
                    if (!string.IsNullOrEmpty(mac) && connection is ITikMacLayerConnection macConn)
                        macConn.RouterMac = mac;
                    connection.Open(ConfigurationManager.AppSettings["host"],
                                    ConfigurationManager.AppSettings["user"],
                                    ConfigurationManager.AppSettings["pass"] ?? "");
                    Log($"  page size {((ITikCliPagedReadConnection)connection).CliReadPageSize}");

                    for (int i = 1; i <= reads; i++)
                    {
                        Measure($"queue tree  #{i}", () => connection.LoadList<QueueTree>().Count());
                        Measure($"mangle      #{i}", () => connection.LoadAll<FirewallMangle>().Count());
                    }
                }
            }
        }

        private static void Measure(string label, Func<int> read)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                int rows = read();
                Log($"  {label}: {rows,5} rows in {sw.ElapsedMilliseconds,6} ms");
            }
            catch (Exception ex)
            {
                Log($"  {label}: {ex.GetType().Name} after {sw.ElapsedMilliseconds} ms — {ex.Message}");
            }
        }
    }
}
