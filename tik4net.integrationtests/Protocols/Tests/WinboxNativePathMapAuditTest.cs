// WinboxNativePathMapAuditTest.cs — diagnostic: does the WinBox-native path map reach everything the
// binary API reaches, and does it come back with the SAME table?
//
// The native transport addresses a path by resolving it to an M2 handler (the .jg menu catalog plus the
// apiPath → menu-label alias table in WinboxHandlerMap). A path it cannot resolve is a gap in tik4net, not
// a statement about the router — and a path it resolves to the WRONG window is worse: it answers, with
// somebody else's records. Neither shows up in a normal suite run, because a test that cannot reach a path
// skips and a test that reads plausible rows passes.
//
// So this compares, per API path, what the binary API returns against what WinBox-native returns:
// row count and the set of field names. It is [Ignore]d — run it by hand from Test Explorer (or
// --filter AuditPathMapAgainstApi) after touching the alias table, the .jg harvest, or on a new RouterOS
// version. It writes a full report next to the other catalog dumps (App.config catalogDumpDir).

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WinboxNativePathMapAuditTest
    {
        // Paths whose `print` is not a plain table read, and would measure the harness rather than the map:
        // action/monitor windows that only produce rows inside a monitor cycle, and reads big enough to
        // dominate the run (the whole memory log, file contents).
        private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/log", "/ping", "/tool/ping", "/tool/torch", "/tool/profile", "/tool/traceroute",
            "/tool/bandwidth-test", "/tool/flood-ping", "/tool/ip-scan", "/tool/wol",
            "/interface/monitor-traffic", "/interface/ethernet/monitor", "/interface/pppoe-client/monitor",
            "/system/reboot", "/system/shutdown", "/system/reset-configuration", "/system/script/run",
        };

        // Paths that reach the RIGHT window but whose FIELD vocabulary still differs from the API's. These
        // are decode-layer gaps (label ↔ api-name), not path-map gaps, and each is diagnosed here so a new
        // one cannot hide among them. Measured on RouterOS 7.23.2, 2026-08-15.
        private static readonly Dictionary<string, string> KnownFieldGaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // /ip/route: the API's print hides the routes WinBox lists (6 vs 2), and the distance/scope/vrf
            // block is not decoded. Pre-existing, unrelated to the path map.
            ["/ip/route"] = "native lists routes the API's print filters out; distance/scope/vrf not decoded",
            // The wireless sniffer singleton answers with its running counters, not its settings.
            ["/interface/wireless/sniffer"] = "handler [88,9] returns sniffer statistics, API returns settings",
            // Known and documented: /system/health on a CHR has no hardware sensors, and state/
            // state-after-reboot are API-only fields with no WinBox equivalent.
            ["/system/health"] = "board-gated singleton; state/state-after-reboot are API-only",
        };

        // Paths WinBox genuinely does not expose as a readable window, verified against the router's own .jg
        // catalog rather than assumed. These stay unmapped by design — the alternative would be pointing an
        // alias at some other window, i.e. answering with the wrong table.
        private static readonly Dictionary<string, string> NoWinboxWindow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // WinBox shows BGP advertisements through the 'Dump Advertisements' ACTION on the session window
            // ([44,33] dump-adv), not as a table of its own — there is nothing to getall.
            ["/routing/bgp/advertisements"] = "WinBox exposes it as the session window's dump-adv action, not a table",
        };

        private static IEnumerable<string> EntityPaths()
        {
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Type t in typeof(TikEntityAttribute).Assembly.GetTypes())
            {
                var ea = t.GetCustomAttribute<TikEntityAttribute>();
                if (ea == null || string.IsNullOrEmpty(ea.EntityPath)) continue;
                string p = ea.EntityPath.StartsWith("/") ? ea.EntityPath : "/" + ea.EntityPath;
                if (!Skip.Contains(p)) paths.Add(p);
            }
            return paths;
        }

        private static ITikConnection Open(TikConnectionType type)
        {
            var conn = ConnectionFactory.CreateConnection(type);
            conn.Open(ConfigurationManager.AppSettings["host"],
                      ConfigurationManager.AppSettings["user"],
                      ConfigurationManager.AppSettings["pass"] ?? "");
            return conn;
        }

        // (rowCount, field names, error) for one path on one transport.
        private static Tuple<int, HashSet<string>, string> Read(ITikConnection conn, string path)
        {
            try
            {
                var rows = conn.CreateCommand(path + "/print").ExecuteList().ToList();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                    foreach (var w in row.Words)
                        names.Add(w.Key);
                return Tuple.Create<int, HashSet<string>, string>(rows.Count, names, null);
            }
            catch (TikPathNotMappedException ex) { return Tuple.Create(-1, new HashSet<string>(), "UNMAPPED: " + ex.Message); }
            catch (Exception ex) { return Tuple.Create(-1, new HashSet<string>(), ex.GetType().Name + ": " + ex.Message); }
        }

        [Ignore]
        [TestMethod]
        public void AuditPathMapAgainstApi()
        {
            string dumpDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["catalogDumpDir"] ?? @".\.tik4net"));
            Directory.CreateDirectory(dumpDir);
            string reportPath = Path.Combine(dumpDir, "winbox-native-path-audit.txt");

            var report = new List<string>();
            int unmapped = 0, mismatched = 0, agreed = 0, apiRefused = 0, known = 0;

            using (var api = Open(TikConnectionType.Api))
            using (var native = Open(TikConnectionType.WinboxNative))
            {
                foreach (string path in EntityPaths())
                {
                    var a = Read(api, path);
                    var n = Read(native, path);

                    if (a.Item3 != null)
                    {
                        // The router itself refuses it (package not installed, path gone in this version) —
                        // nothing for the native map to be measured against.
                        apiRefused++;
                        report.Add($"ROUTER-N/A  {path}\tapi: {a.Item3}");
                        continue;
                    }
                    if (n.Item3 != null)
                    {
                        if (n.Item3.StartsWith("UNMAPPED") && NoWinboxWindow.TryGetValue(path, out string reason))
                        {
                            known++;
                            report.Add($"NO-WINDOW  {path}\t{reason}");
                            continue;
                        }
                        if (n.Item3.StartsWith("UNMAPPED")) unmapped++; else mismatched++;
                        report.Add($"{(n.Item3.StartsWith("UNMAPPED") ? "UNMAPPED   " : "NATIVE-ERR ")}{path}"
                                   + $"\tapi rows={a.Item1}\t{n.Item3}");
                        continue;
                    }

                    // Field names are the strongest cheap signal that both transports read the SAME window:
                    // a wrong handler answers with a different vocabulary long before the row count matches.
                    var onlyApi = a.Item2.Where(f => !n.Item2.Contains(f)).OrderBy(f => f).ToList();
                    var shared = a.Item2.Count(f => n.Item2.Contains(f));
                    bool rowsAgree = a.Item1 == n.Item1;
                    bool fieldsAgree = a.Item2.Count == 0 || shared * 2 >= a.Item2.Count;

                    if (rowsAgree && fieldsAgree) { agreed++; report.Add($"OK         {path}\trows={a.Item1}\tshared fields={shared}/{a.Item2.Count}"); }
                    else if (KnownFieldGaps.TryGetValue(path, out string why))
                    {
                        known++;
                        report.Add($"KNOWN-GAP  {path}\tapi rows={a.Item1} native rows={n.Item1}"
                                   + $"\tshared fields={shared}/{a.Item2.Count}\t{why}");
                    }
                    else
                    {
                        mismatched++;
                        var onlyNative = n.Item2.Where(f => !a.Item2.Contains(f)).OrderBy(f => f).ToList();
                        report.Add($"MISMATCH   {path}\tapi rows={a.Item1} native rows={n.Item1}"
                                   + $"\tshared fields={shared}/{a.Item2.Count}"
                                   + (onlyApi.Count > 0 ? "\tapi-only: " + string.Join(",", onlyApi.Take(12)) : "")
                                   + (onlyNative.Count > 0 ? "\tnative-only: " + string.Join(",", onlyNative.Take(12)) : ""));
                    }
                }
            }

            File.WriteAllLines(reportPath, report);
            foreach (string line in report) Console.WriteLine(line);
            Console.WriteLine();
            Console.WriteLine($"OK={agreed}  KNOWN-GAP={known}  MISMATCH={mismatched}  UNMAPPED={unmapped}  ROUTER-N/A={apiRefused}");
            Console.WriteLine($"report: {reportPath}");

            Assert.AreEqual(0, unmapped, "paths the WinBox-native transport cannot address but the API can — see the report");
            Assert.AreEqual(0, mismatched, "paths where WinBox-native disagrees with the API — see the report");
        }
    }
}
