// UserActiveSessionProbeTest.cs — does closing a connection actually end the router-side session?
//
// P2.35 was filed on an observation, not a measurement: the lab router once held 164 entries in
// /user/active (109 api, 55 rest-api) dating back to the previous day, from test processes long since
// exited, and shortly afterwards it stalled and rebooted. The open question is whether our close path
// leaks sessions, or whether this is RouterOS bookkeeping we cannot influence — and per transport,
// because REST logs in per request and the CLI/WinBox family holds a terminal.
//
// Method: one long-lived monitor connection over the binary API reads /user/active. It is long-lived on
// purpose — its OWN entry then appears in every snapshot and cancels out of every delta, which a
// connect-print-disconnect measurement could not do (each measurement would add an entry of its own and
// the leak would be indistinguishable from the instrument). Sessions are compared by `.id`, never by
// count: RouterOS hands out a monotonically increasing id, so "the count is the same" can hide one
// session ending while another leaks.
//
// Each transport gets N open→trivial-command→Dispose cycles. Snapshots are taken immediately after and
// again after a settle window, because "the entry is still there 200 ms later" and "the entry is still
// there 15 s later" are completely different findings and only the second one is a leak.
//
// ⚠️ Do NOT reach for free-memory to corroborate anything here — on this lab CHR it is fiction whenever
// the VM is on Hyper-V Dynamic Memory (see WinboxMproxyMemoryProbeTest for the full trap).
//
// ── What this measured, 2026-07-28/29 on 7.23.2 (P2.35) ──────────────────────────────────────────────
//
// Clean on eight of eleven transports: Api, ApiSsl, Telnet, Ssh, WinboxCli, WinboxNative, MacTelnet,
// WinboxCliMac leave nothing behind (the two CLI-terminal ones linger a moment, gone inside 15 s).
//
// One real leak, and it was ours: WinboxNativeMac left 6/6 sessions, still there after 15 s and only
// expiring after ~90 s — UDP has no FIN, and that channel sent no PKT_END either. Fixed in
// WinboxMacM2Session.OnDisposing; verified 6/6 -> 0, net-new across the whole sweep 6 -> 0.
//
// The REST accumulation is the ROUTER'S, and no client can fix it. Every REST login creates TWO rows
// (rest-api + api at the same second — the second is the router's internal www→api backend, which is
// where the filed "109 api / 55 rest-api" 2:1 ratio comes from), and they outlive the TCP connection,
// the client process, and every timeout. Proved with a client that shares no code with tik4net: one
// `curl -u admin: http://<host>/rest/system/identity` left a rest-api+api pair still listed 90 s after
// curl had exited, while the host had no TCP connection to port 80/443 at all. Over a 33-minute watch of
// 12 rows exactly ONE expired — the api half of curl's pair, at ~10 min, while its rest-api half was
// still listed at 25 min and four other api rows outlived it by more than an hour (aged past 78 min).
// The two halves plainly do not share an expiry rule and the actual rule was NOT established: do not
// repeat "~10 minutes" as if it were one. /user/active/remove refuses every row ("action failed (6)"),
// so they cannot be cleaned up either — only a reboot clears them. The row COUNT measures nothing.
// One more trap while counting: a `via=winbox` row in the residue turned out to be the maintainer's own
// live WinBox client, i.e. a perfectly legitimate session being tallied as a leak.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc session-accounting probe against a live router — comment out to run.")]
    [TestClass]
    public class UserActiveSessionProbeTest
    {
        private const int CyclesPerTransport = 6;
        private const int SettleSeconds = 15;

        private static readonly TikConnectionType[] Transports =
        {
            TikConnectionType.Api,
            TikConnectionType.ApiSsl,
            TikConnectionType.Rest,
            TikConnectionType.RestSsl,
            TikConnectionType.Telnet,
            TikConnectionType.Ssh,
            TikConnectionType.WinboxCli,
            TikConnectionType.WinboxNative,
            TikConnectionType.MacTelnet,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.WinboxNativeMac,
        };

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        // The lifetime watch runs for the best part of an hour, and MSTest's console logger hands nothing
        // over until the test method returns — so its progress is invisible exactly while it is being
        // produced. Mirror every line to TIK4NET_PROBE_LOG when set, and it can be read as it happens.
        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        /// <summary>Active sessions keyed by <c>.id</c>, valued by a printable "via @ when" description.</summary>
        private static Dictionary<string, string> Snapshot(ITikConnection monitor)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in monitor.CreateCommandAndParameters("/user/active/print").ExecuteList())
            {
                string id = row.GetResponseFieldOrDefault(".id", null);
                if (id == null) continue;
                result[id] = row.GetResponseFieldOrDefault("via", "?") + " @ " +
                             row.GetResponseFieldOrDefault("when", "?");
            }
            return result;
        }

        private static ITikConnection OpenOne(TikConnectionType type)
        {
            var (host, user, pass) = Cfg();
            var conn = ConnectionFactory.CreateConnection(type);
            string mac = ConfigurationManager.AppSettings["routerMac"];
            if (!string.IsNullOrEmpty(mac) && conn is ITikMacLayerConnection macConn)
                macConn.RouterMac = mac;
            conn.Open(host, user, pass);
            return conn;
        }

        [TestMethod]
        public void Probe_UserActive_ReleasedOnClose_PerTransport()
        {
            tik4net.Ssh.Tik4NetSsh.Register();

            using (var monitor = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                var (host, user, pass) = Cfg();
                monitor.Open(host, user, pass);

                var start = Snapshot(monitor);
                Console.WriteLine($"baseline: {start.Count} active session(s) (incl. this monitor)");
                foreach (var kv in start.OrderBy(k => k.Key))
                    Console.WriteLine($"    {kv.Key,-8} {kv.Value}");
                Console.WriteLine();

                // Narrow the sweep while iterating on one transport: TIK4NET_PROBE_TRANSPORTS=WinboxNativeMac,MacTelnet
                string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
                var selected = string.IsNullOrEmpty(only)
                    ? Transports
                    : Transports.Where(t => only.Split(',').Any(
                        n => string.Equals(n.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

                foreach (var type in selected)
                {
                    var before = Snapshot(monitor);

                    int opened = 0;
                    string failure = null;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < CyclesPerTransport; i++)
                    {
                        try
                        {
                            using (var conn = OpenOne(type))
                            {
                                opened++;
                                conn.CallCommandSync("/system/identity/print").Count();
                            }
                        }
                        catch (Exception ex)
                        {
                            failure = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                            break;
                        }
                    }
                    sw.Stop();

                    if (opened == 0)
                    {
                        Console.WriteLine($"{type,-16}: could not connect — {failure}");
                        continue;
                    }

                    var immediate = Snapshot(monitor);
                    Thread.Sleep(SettleSeconds * 1000);
                    var settled = Snapshot(monitor);

                    var leakedNow = immediate.Keys.Except(before.Keys).ToList();
                    var leakedStill = settled.Keys.Except(before.Keys).ToList();

                    Console.WriteLine(
                        $"{type,-16}: {opened}/{CyclesPerTransport} cycles in {sw.ElapsedMilliseconds} ms — " +
                        $"survived close: {leakedNow.Count}, still there after {SettleSeconds}s: {leakedStill.Count}" +
                        (failure != null ? "   (aborted: " + failure + ")" : ""));
                    foreach (var id in leakedStill)
                        Console.WriteLine($"    LEAKED {id,-8} {settled[id]}");
                }

                Console.WriteLine();
                var end = Snapshot(monitor);
                var net = end.Keys.Except(start.Keys).ToList();
                Console.WriteLine($"net new sessions over the whole probe: {net.Count}");
                foreach (var id in net)
                    Console.WriteLine($"    {id,-8} {end[id]}");
            }
        }

        /// <summary>
        /// Watches the sessions that are already on the router and reports when each one disappears.
        /// </summary>
        /// <remarks>
        /// The per-transport probe above can only answer "was it gone within 15 s". That is enough to clear
        /// a transport but not to condemn one: a session released after 90 s is a lag, a session still there
        /// after an hour is the accumulation P2.35 was filed on. Nothing here creates load — one API
        /// connection, one print per minute — so it is safe to leave running alongside other work.
        /// </remarks>
        [TestMethod]
        public void Probe_UserActive_Lifetime()
        {
            int watchMinutes = int.Parse(
                Environment.GetEnvironmentVariable("TIK4NET_WATCH_MINUTES") ?? "45");

            using (var monitor = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                var (host, user, pass) = Cfg();
                monitor.Open(host, user, pass);

                var seen = Snapshot(monitor);
                var firstSeen = seen.Keys.ToDictionary(k => k, _ => DateTime.Now);
                Log($"watching {seen.Count} session(s) for {watchMinutes} min");
                foreach (var kv in seen.OrderBy(k => k.Value))
                    Log($"    {kv.Key,-8} {kv.Value}");

                var deadline = DateTime.Now.AddMinutes(watchMinutes);
                while (DateTime.Now < deadline)
                {
                    Thread.Sleep(60 * 1000);
                    var now = Snapshot(monitor);

                    foreach (var id in seen.Keys.Except(now.Keys).ToList())
                    {
                        Log($"{DateTime.Now:HH:mm:ss}  GONE    {id,-8} {seen[id]}  " +
                            $"(observed alive {(DateTime.Now - firstSeen[id]).TotalMinutes:F0} min)");
                        seen.Remove(id);
                    }
                    foreach (var id in now.Keys.Except(seen.Keys).ToList())
                    {
                        Log($"{DateTime.Now:HH:mm:ss}  NEW     {id,-8} {now[id]}");
                        seen[id] = now[id];
                        firstSeen[id] = DateTime.Now;
                    }
                }

                Log($"still alive after {watchMinutes} min: {seen.Count}");
                foreach (var kv in seen.OrderBy(k => k.Value))
                    Log($"    {kv.Key,-8} {kv.Value}");
            }
        }
    }
}
