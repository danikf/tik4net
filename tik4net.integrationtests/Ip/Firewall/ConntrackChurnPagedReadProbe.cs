// ConntrackChurnPagedReadProbe — does a paged CLI read of a table that changes under it fail?
//
// A window is ':local w [:pick [/path find] a b]; … print from=$w'. An id that vanishes between the find and
// the print answers 'no such item' and aborts the line (measured, Docs/findings-cli.md §1), so a paged read of
// a table that reaps its own rows — conntrack — can fail even though nothing is wrong. This measures how often.
//
// Churn: UDP datagrams from this machine to random router ports. Each one is a conntrack row that expires
// after the router's udp-timeout (10 s by default), so the table turns over continuously.
//
// Env:  TIK_PROBE=1                    required, or the test reports Inconclusive
//       TIK4NET_PROBE_TRANSPORTS       transport (default Telnet — the carrier does not matter to the race)
//       TIK4NET_CHURN_PAGE             rows per window (default 100)
//       TIK4NET_CHURN_READS            reads to take (default 20)
//       TIK4NET_CHURN_PPS              datagrams per second (default 200)
//       TIK4NET_PROBE_LOG              also append the output to this file

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    // Gated on TIK_PROBE=1 rather than [Ignore], so it can be run from the command line without editing the
    // file (MSTest skips [Ignore] even under --filter) and a normal suite run still never pays for it.
    [TestClass]
    public class ConntrackChurnPagedReadProbe
    {
        private static int EnvInt(string name, int fallback)
            => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) && v > 0 ? v : fallback;

        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        [TestMethod]
        public void Probe_ConntrackPagedReadUnderChurn()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1.");

            string host = ConfigurationManager.AppSettings["host"];
            var transport = (TikConnectionType)Enum.Parse(typeof(TikConnectionType),
                Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS") ?? "Telnet", ignoreCase: true);
            int page = EnvInt("TIK4NET_CHURN_PAGE", 100);
            int reads = EnvInt("TIK4NET_CHURN_READS", 20);
            int pps = EnvInt("TIK4NET_CHURN_PPS", 200);

            using (var stop = new CancellationTokenSource())
            {
                var churn = new Thread(() => Churn(host, pps, stop.Token)) { IsBackground = true };
                churn.Start();
                try
                {
                    Log("");
                    Log($"══ conntrack paged read under churn — {transport}, page {page}, {pps} datagrams/s ══");
                    Thread.Sleep(12000);   // past one udp-timeout, so rows are expiring as fast as they arrive

                    using (ITikConnection connection = ConnectionFactory.CreateConnection(transport))
                    {
                        string mac = ConfigurationManager.AppSettings["routerMac"];
                        if (!string.IsNullOrEmpty(mac) && connection is ITikMacLayerConnection macConn)
                            macConn.RouterMac = mac;
                        ((ITikCliPagedReadConnection)connection).CliReadPageSize = page;
                        connection.Open(host, ConfigurationManager.AppSettings["user"],
                                        ConfigurationManager.AppSettings["pass"] ?? "");

                        int ok = 0, vanished = 0, other = 0;
                        for (int i = 1; i <= reads; i++)
                        {
                            var sw = Stopwatch.StartNew();
                            try
                            {
                                int rows = connection.LoadAll<FirewallConnection>().Count();
                                ok++;
                                Log($"  read {i,2}: {rows,5} rows in {sw.ElapsedMilliseconds,6} ms");
                            }
                            catch (TikNoSuchItemException ex)
                            {
                                vanished++;
                                Log($"  read {i,2}: NO SUCH ITEM after {sw.ElapsedMilliseconds} ms — {ex.Message}");
                            }
                            catch (Exception ex)
                            {
                                other++;
                                Log($"  read {i,2}: {ex.GetType().Name} after {sw.ElapsedMilliseconds} ms — {ex.Message}");
                            }
                        }

                        Log($"  → {ok} complete, {vanished} failed on a vanished row, {other} other failure(s)");
                    }
                }
                finally
                {
                    stop.Cancel();
                    churn.Join(2000);
                }
            }
        }

        private static void Churn(string host, int pps, CancellationToken stop)
        {
            var rnd = new Random();
            var payload = new byte[] { 0x74, 0x34, 0x6E };   // "t4n"
            using (var udp = new UdpClient())
            {
                var sw = Stopwatch.StartNew();
                long sent = 0;
                while (!stop.IsCancellationRequested)
                {
                    try { udp.Send(payload, payload.Length, host, rnd.Next(40000, 60000)); } catch { }
                    sent++;
                    long due = sent * 1000 / pps;
                    long ahead = due - sw.ElapsedMilliseconds;
                    if (ahead > 0)
                        Thread.Sleep((int)ahead);
                }
            }
        }
    }
}
