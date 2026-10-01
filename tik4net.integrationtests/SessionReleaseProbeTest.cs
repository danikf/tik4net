// SessionReleaseProbeTest.cs — does every connection type end its router-side session when it is disposed?
//
// Filed 2026-09-30: during a full matrix, CHR2 (6.49.13, the RoMON target) stopped answering every login — API 0
// bytes, Telnet an empty reply, MAC-Telnet no packets — while still answering ping; its native console took ~20 s
// to log in and then stalled, with the CPU busy. The suspicion is sessions the library never released, piling up
// until the router's login path starved. UserActiveSessionProbeTest (P2.35) cleared eight direct transports on
// 7.23.2 by /user/active alone; it never covered a RoMON relay, RouterOS 6, the log, or what keeps running.
//
// Three witnesses per transport, because each can miss what the others see:
//  * /user/active, compared by .id (a count hides one session ending while another leaks);
//  * the router's own log — every "logged in" needs its "logged out" (the log keeps ~1000 lines in memory, so it is
//    read right after each transport, and rows are compared by .id);
//  * /tool/profile at the end: a relay or terminal that outlived its session shows up as CPU, not as a row.
// Through a relay the agent and the target are each checked: the agent runs /tool romon ssh on a terminal of its
// own, the target sees an ssh login marked by-romon.
//
// REST is expected to leave rest-api (+api) rows and "logged in" lines without a logout: that is RouterOS's own
// accounting, proved with curl in P2.35, and is reported but not counted as ours.
//
// Run against a router profile like the suite: TIK4NET_ROUTER=chr2|chr3 (run-integration-tests.ps1 -Router …).
// TIK4NET_PROBE_TRANSPORTS narrows the direct sweep (e.g. "Telnet,Ssh"), TIK4NET_PROBE_CYCLES sets the cycles.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Session-release probe against a live router — comment out to run.")]
    [TestClass]
    [TestLock(TestLockScope.Router)]
    [TestCategory(TestCategories.LegIndependent)]
    public class SessionReleaseProbeTest : LockedTestBase
    {
        private const int SettleSeconds = 15;

        private static readonly TikConnectionType[] DirectTransports =
        {
            TikConnectionType.Api, TikConnectionType.ApiSsl, TikConnectionType.Rest, TikConnectionType.RestSsl,
            TikConnectionType.Telnet, TikConnectionType.Ssh, TikConnectionType.WinboxCli, TikConnectionType.WinboxNative,
            TikConnectionType.MacTelnet, TikConnectionType.WinboxCliMac, TikConnectionType.WinboxNativeMac,
        };

        private static readonly TikConnectionType[] RelayAgentTransports =
            { TikConnectionType.Telnet, TikConnectionType.Ssh, TikConnectionType.MacTelnet };

        private static readonly Regex LoginLine =
            new Regex(@"^user (?<user>\S+) logged (?<dir>in|out)(?: from (?<from>\S+))? via (?<via>\S+)", RegexOptions.Compiled);

        private static int Cycles => int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_PROBE_CYCLES"), out int n) ? n : 5;

        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<string> _findings = new List<string>();

        private void Log(string line)
        {
            _report.AppendLine(line);
            Console.WriteLine(line);
        }

        // ── Witnesses ─────────────────────────────────────────────────────────

        private static Dictionary<string, string> ActiveSessions(ITikConnection monitor)
            => monitor.CreateCommand("/user/active/print").ExecuteList()
                .Where(r => r.GetResponseFieldOrDefault(".id", null) != null)
                .ToDictionary(r => r.GetResponseField(".id"),
                              r => r.GetResponseFieldOrDefault("via", "?")
                                   + (string.IsNullOrEmpty(r.GetResponseFieldOrDefault("by-romon", "")) ? "" : " by-romon")
                                   + " @ " + r.GetResponseFieldOrDefault("when", "?"));

        private static HashSet<string> LogIds(ITikConnection monitor)
            => new HashSet<string>(monitor.CreateCommand("/log/print").ExecuteList()
                .Select(r => r.GetResponseFieldOrDefault(".id", "")), StringComparer.Ordinal);

        /// <summary>Login/logout lines logged since <paramref name="before"/>, per "via", as (in, out).</summary>
        private static Dictionary<string, (int In, int Out)> LoginsSince(ITikConnection monitor, HashSet<string> before)
        {
            var result = new Dictionary<string, (int In, int Out)>(StringComparer.Ordinal);
            foreach (var row in monitor.CreateCommand("/log/print").ExecuteList())
            {
                if (before.Contains(row.GetResponseFieldOrDefault(".id", ""))) continue;
                var m = LoginLine.Match(row.GetResponseFieldOrDefault("message", ""));
                if (!m.Success) continue;
                string via = m.Groups["via"].Value;
                result.TryGetValue(via, out var c);
                result[via] = m.Groups["dir"].Value == "in" ? (c.In + 1, c.Out) : (c.In, c.Out + 1);
            }
            return result;
        }

        /// <summary>The last whole second of <c>/tool/profile</c>: processes above 1 %, busiest first.</summary>
        private static string Profile(ITikConnection monitor)
        {
            var rows = monitor.CreateCommand("/tool/profile",
                    monitor.CreateParameter("duration", "3s", TikCommandParameterFormat.NameValue)).ExecuteList().ToList();
            string last = rows.Select(r => r.GetResponseFieldOrDefault(".section", "")).LastOrDefault();
            var busy = rows.Where(r => r.GetResponseFieldOrDefault(".section", "") == last)
                .Select(r => (Name: r.GetResponseFieldOrDefault("name", "?"),
                              Usage: double.TryParse(r.GetResponseFieldOrDefault("usage", "0").TrimEnd('%'),
                                  System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double u) ? u : 0))
                .Where(p => p.Usage >= 1 || p.Name == "total")
                .OrderByDescending(p => p.Usage)
                .Select(p => $"{p.Name}={p.Usage:0.#}%");
            return string.Join(" ", busy);
        }

        private sealed class Watch
        {
            public string Name;
            public ITikConnection Monitor;
            public Dictionary<string, string> Active;
            public HashSet<string> LogIdsBefore;
        }

        private static Watch StartWatch(string name, ITikConnection monitor)
            => new Watch { Name = name, Monitor = monitor, Active = ActiveSessions(monitor), LogIdsBefore = LogIds(monitor) };

        /// <summary>Reports what one router shows after the cycles and a settle window; adds findings for leaks.</summary>
        private void Verdict(string what, Watch w, bool restAccounting)
        {
            var nowActive = ActiveSessions(w.Monitor);
            var leftover = nowActive.Where(kv => !w.Active.ContainsKey(kv.Key)).ToList();
            var logins = LoginsSince(w.Monitor, w.LogIdsBefore);

            string loginText = logins.Count == 0 ? "no login lines"
                : string.Join(", ", logins.OrderBy(k => k.Key).Select(k => $"{k.Key} in {k.Value.In}/out {k.Value.Out}"));
            Log($"    {w.Name,-7} active +{leftover.Count}; log: {loginText}");
            foreach (var kv in leftover)
                Log($"        STILL ACTIVE {kv.Key,-8} {kv.Value}");

            bool restOnly(string via) => via == "rest-api" || via == "api";
            var ours = leftover.Where(kv => !(restAccounting && restOnly(kv.Value.Split(' ')[0]))).ToList();
            var unpaired = logins.Where(k => k.Value.In != k.Value.Out && !(restAccounting && restOnly(k.Key))).ToList();
            if (ours.Count > 0)
                _findings.Add($"{what}: {ours.Count} session(s) still active on {w.Name} after {SettleSeconds} s "
                              + $"({string.Join("; ", ours.Select(kv => kv.Value))})");
            if (unpaired.Count > 0)
                _findings.Add($"{what}: login/logout unpaired on {w.Name}: "
                              + string.Join(", ", unpaired.Select(k => $"{k.Key} in {k.Value.In}/out {k.Value.Out}")));
        }

        private int RunCycles(Func<ITikConnection> open, out string failure)
        {
            failure = null;
            int done = 0;
            for (int i = 0; i < Cycles; i++)
            {
                try
                {
                    using (var connection = open())
                        connection.CreateCommand("/system/identity/print").ExecuteScalar();
                    done++;
                }
                catch (Exception ex)
                {
                    failure = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                    break;
                }
            }
            return done;
        }

        private static ITikConnection OpenApi(string host, string user, string pass)
            => new TikConnectionSetup(host, user, pass).Create(TikConnectionType.Api);

        // ── The sweeps ────────────────────────────────────────────────────────

        [TestMethod]
        public void EveryConnectionTypeEndsItsSessionOnDispose()
        {
            tik4net.Ssh.Tik4NetSsh.Register();
            string host = LabConfig.Get("host"), user = LabConfig.Get("user"), pass = LabConfig.Get("pass") ?? "";
            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
            var direct = string.IsNullOrEmpty(only)
                ? DirectTransports
                : DirectTransports.Where(t => only.Split(',').Any(n => string.Equals(n.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

            using (var monitor = OpenApi(host, user, pass))
            {
                Log($"router {LabConfig.Profile ?? "default"} ({host}), {Cycles} cycles per transport, settle {SettleSeconds} s");
                Log($"  profile at start: {Profile(monitor)}");

                foreach (var type in direct)
                {
                    var w = StartWatch("router", monitor);
                    int done = RunCycles(() => TestBase.LabSetup(type).Create(type), out string failure);
                    Log($"{type,-16} {done}/{Cycles} cycles" + (failure != null ? $"  (stopped: {failure})" : ""));
                    Thread.Sleep(SettleSeconds * 1000);
                    Verdict(type.ToString(), w, restAccounting: type == TikConnectionType.Rest || type == TikConnectionType.RestSsl);
                }

                string targetId = LabConfig.Get("romonTargetId"), targetHost = LabConfig.Get("romonTargetHost");
                if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(targetHost))
                {
                    string targetUser = LabConfig.Get("romonTargetUser"), targetPass = LabConfig.Get("romonTargetPass") ?? "";
                    string agentMac = LabConfig.Get("routerMac");
                    using (var target = OpenApi(targetHost, targetUser, targetPass))
                    {
                        Log($"RoMON relay to {targetHost}; target profile at start: {Profile(target)}");
                        foreach (var agentType in RelayAgentTransports)
                        {
                            var agentWatch = StartWatch("agent", monitor);
                            var targetWatch = StartWatch("target", target);
                            var agentAddress = agentType == TikConnectionType.MacTelnet && !string.IsNullOrEmpty(agentMac)
                                ? TikRouterAddress.FromHostAndMac(host, agentMac) : TikRouterAddress.FromHost(host);
                            var setup = new TikConnectionSetup(TikRouterAddress.FromRomonId(targetId), targetUser, targetPass)
                            {
                                RomonAgentSetup = new TikRomonAgentSetup(agentAddress, user, pass),
                            };
                            int done = RunCycles(() => setup.Create(agentType), out string failure);
                            Log($"relay via {agentType,-9} {done}/{Cycles} cycles" + (failure != null ? $"  (stopped: {failure})" : ""));
                            Thread.Sleep(SettleSeconds * 1000);
                            Verdict("relay via " + agentType, agentWatch, restAccounting: false);
                            Verdict("relay via " + agentType, targetWatch, restAccounting: false);
                        }
                        Log($"  target profile at end: {Profile(target)}");
                    }
                }
                else
                    Log("no RoMON target in this profile — relay sweep skipped");

                Log($"  profile at end: {Profile(monitor)}");
            }

            Log(_findings.Count == 0 ? "RESULT: every session ended" : "RESULT: " + _findings.Count + " finding(s)");
            foreach (var f in _findings)
                Log("  " + f);
            Assert.AreEqual(0, _findings.Count, string.Join("\n", _findings));
        }
    }
}
