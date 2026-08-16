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
// row count, the set of field names, and — on rows paired by .id — the VALUES of the fields both report.
// The values matter as much as the names: /system/logging read `topics` as the raw handle list "[1]" where
// the API says "info", and an audit that only counted field names called the path OK for a release. Run it after touching the alias tables, the .jg harvest, or on a
// new RouterOS version. It writes a full report next to the other catalog dumps (App.config catalogDumpDir).
//
// It is [Ignore]d, and --filter will NOT run it: MSTest applies [Ignore] before the filter, so naming the
// test reports it skipped and the run passes green having measured nothing. Comment the attribute out, run,
// then put it back — or run it from Test Explorer.

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

        /// <summary>What one path read as, on one transport.</summary>
        private sealed class Reading
        {
            public int RowCount = -1;
            public HashSet<string> FieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>row <c>.id</c> → field → value, for the value comparison. Rows without an
            /// <c>.id</c> (a singleton) are keyed by their ordinal.</summary>
            public Dictionary<string, Dictionary<string, string>> Rows =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            public string Error;
        }

        private static Reading Read(ITikConnection conn, string path)
        {
            var r = new Reading();
            try
            {
                var rows = conn.CreateCommand(path + "/print").ExecuteList().ToList();
                r.RowCount = rows.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var w in rows[i].Words)
                    {
                        r.FieldNames.Add(w.Key);
                        fields[w.Key] = w.Value;
                    }
                    string key = fields.TryGetValue(".id", out var id) && !string.IsNullOrEmpty(id)
                        ? id : "#" + i;
                    r.Rows[key] = fields;
                }
                return r;
            }
            catch (TikPathNotMappedException ex) { r.Error = "UNMAPPED: " + ex.Message; return r; }
            catch (Exception ex) { r.Error = ex.GetType().Name + ": " + ex.Message; return r; }
        }

        // Fields whose value legitimately differs between two reads taken seconds apart — counters, rates,
        // clocks, and the live status a monitor-ish window computes. Matched as substrings of the field
        // name, so 'rx-byte'/'tx-byte'/'fp-rx-byte' are all covered by "byte".
        private static readonly string[] VolatileFieldParts =
        {
            "byte", "packet", "bits-per-second", "rate", "count", "error", "drop", "uptime", "time",
            // 'expires-after' is a live countdown: once the age decoder landed, /ip/dhcp-client's read the
            // same value on both transports to within ONE SECOND — the gap between the two reads, not a
            // decode difference. It belongs with the counters rather than in the gap table.
            "expires-after",
            "age", "last-", "active", "current", "usage", "free", "used", "load", "temperature", "voltage",
            "signal", "noise", "cpu", "memory", "sent", "received", "requests", "hits", "misses", "status",
        };

        private static bool IsVolatile(string field)
        {
            foreach (string part in VolatileFieldParts)
                if (field.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Paths whose values are known to differ for a reason already diagnosed — same rule as
        /// <see cref="KnownFieldGaps"/>, one level deeper. Keep each entry's reason specific enough that a
        /// NEW disagreement on the same path cannot hide behind it.
        /// </summary>
        /// <remarks>
        /// <para>Every entry below was measured on 7.23.2 the first time this audit compared values at all,
        /// and they are all the SAME handful of missing decoders rather than 26 separate defects — see the
        /// class names in the reasons. They are recorded per path so that a path picking up a NEW
        /// disagreement still fails: the reason names which fields are excused.</para>
        /// <para>The classes, in rough order of how many paths each costs:</para>
        /// <list type="bullet">
        /// <item><b>interval/duration</b> — the <c>interval</c> UI type has no decoder, so a duration reaches
        /// the caller as raw seconds (or milliseconds) where the API prints <c>0s</c>, <c>5m</c>, <c>1w</c>.</item>
        /// <item><b>raw MAC</b> — a 6-byte field whose window does not type it <c>macaddr</c> renders as
        /// undelimited hex.</item>
        /// <item><b>sentinel</b> — <c>0</c> and <c>0xFFFFFFFF</c> that the API prints as a WORD
        /// (<c>disabled</c>, <c>unlimited</c>, <c>none</c>, <c>all</c>, <c>passthrough</c>).</item>
        /// <item><b>empty list</b> — an empty array renders <c>[]</c> where the API prints nothing.</item>
        /// <item><b>set order</b> — a bitmask set decodes by ascending bit index; RouterOS prints its own
        /// order (<c>pap,chap,mschap1,mschap2</c>).</item>
        /// <item><b>date/epoch</b> — a <c>time</c>/<c>date</c> field reaches the caller as a unix stamp.</item>
        /// </list>
        /// </remarks>
        private static readonly Dictionary<string, string> KnownValueGaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/certificate"]                          = "subject-alt-name is a tuple (type enm + ':' + a union whose FIRST member is ip6addr, while the value rides on the ipaddr key)",
            ["/interface/ethernet"]                   = "auto-negotiation reads the link's live state, not the setting",
            ["/ip/dhcp-client"]                       = "undecoded list (add-default-route)",
            ["/ip/hotspot/profile"]                    = "http-proxy is an ipaddr with the port on a sibling key; the API prints one 'addr:port'",
            ["/ip/ipsec/profile"]                      = "dpd-interval is an enm with postfix 's', not an interval",
            ["/ip/neighbor"]                          = "system-caps reads a value the API leaves empty",
            ["/routing/table"]                        = "fib reads the flag's own bool where the API prints it empty",
            ["/snmp/community"]                       = "IPv6 any-address renders '::' where the API prints '::/0'",
            ["/system/ntp/client"]                     = "system-offset is a whole-millisecond `integer` on the wire where the API reports fractions — an information difference, not a decode gap (and it drifts constantly). freq-drift agrees.",
            ["/system/ntp/server"]                     = "enabled reads the wrong key (native says true where the API says false)",
        };

        /// <summary>
        /// Per shared, non-volatile field: the values the two transports gave for the same row, when they
        /// disagree. One entry per FIELD (not per row), naming a sample, so a wide table reports the field
        /// once rather than once per record.
        /// </summary>
        /// <remarks>
        /// Only rows both transports returned under the same <c>.id</c> are compared. A row native returned
        /// and the API did not is a ROW-count disagreement, already reported above; pairing by ordinal
        /// instead would line up unrelated records and invent differences.
        /// </remarks>
        private static List<string> CompareValues(Reading api, Reading native)
        {
            var diffs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in api.Rows)
            {
                if (!native.Rows.TryGetValue(kv.Key, out var nativeRow)) continue;
                foreach (var f in kv.Value)
                {
                    if (f.Key == ".id" || IsVolatile(f.Key)) continue;
                    if (!nativeRow.TryGetValue(f.Key, out string nativeValue)) continue;
                    if (string.Equals(f.Value ?? "", nativeValue ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(f.Key)) continue;
                    diffs.Add($"{f.Key}: api='{Trim(f.Value)}' native='{Trim(nativeValue)}'");
                }
            }
            return diffs;
        }

        private static string Trim(string v)
            => v == null ? "" : (v.Length > 40 ? v.Substring(0, 37) + "..." : v);

        [Ignore]
        [TestMethod]
        public void AuditPathMapAgainstApi()
        {
            string dumpDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["catalogDumpDir"] ?? @".\.tik4net"));
            Directory.CreateDirectory(dumpDir);
            string reportPath = Path.Combine(dumpDir, "winbox-native-path-audit.txt");

            var report = new List<string>();
            var staleGaps = new List<string>();
            int unmapped = 0, mismatched = 0, agreed = 0, apiRefused = 0, known = 0, valueMismatched = 0;

            using (var api = Open(TikConnectionType.Api))
            using (var native = Open(TikConnectionType.WinboxNative))
            {
                foreach (string path in EntityPaths())
                {
                    var a = Read(api, path);
                    var n = Read(native, path);

                    if (a.Error != null)
                    {
                        // The router itself refuses it (package not installed, path gone in this version) —
                        // nothing for the native map to be measured against.
                        apiRefused++;
                        report.Add($"ROUTER-N/A  {path}\tapi: {a.Error}");
                        continue;
                    }
                    if (n.Error != null)
                    {
                        if (n.Error.StartsWith("UNMAPPED") && NoWinboxWindow.TryGetValue(path, out string reason))
                        {
                            known++;
                            report.Add($"NO-WINDOW  {path}\t{reason}");
                            continue;
                        }
                        if (n.Error.StartsWith("UNMAPPED")) unmapped++; else mismatched++;
                        report.Add($"{(n.Error.StartsWith("UNMAPPED") ? "UNMAPPED   " : "NATIVE-ERR ")}{path}"
                                   + $"\tapi rows={a.RowCount}\t{n.Error}");
                        continue;
                    }

                    // Field names are the strongest cheap signal that both transports read the SAME window:
                    // a wrong handler answers with a different vocabulary long before the row count matches.
                    var onlyApi = a.FieldNames.Where(f => !n.FieldNames.Contains(f)).OrderBy(f => f).ToList();
                    var shared = a.FieldNames.Count(f => n.FieldNames.Contains(f));
                    bool rowsAgree = a.RowCount == n.RowCount;
                    bool fieldsAgree = a.FieldNames.Count == 0 || shared * 2 >= a.FieldNames.Count;

                    if (!rowsAgree || !fieldsAgree)
                    {
                        if (KnownFieldGaps.TryGetValue(path, out string why))
                        {
                            known++;
                            report.Add($"KNOWN-GAP  {path}\tapi rows={a.RowCount} native rows={n.RowCount}"
                                       + $"\tshared fields={shared}/{a.FieldNames.Count}\t{why}");
                        }
                        else
                        {
                            mismatched++;
                            var onlyNative = n.FieldNames.Where(f => !a.FieldNames.Contains(f)).OrderBy(f => f).ToList();
                            report.Add($"MISMATCH   {path}\tapi rows={a.RowCount} native rows={n.RowCount}"
                                       + $"\tshared fields={shared}/{a.FieldNames.Count}"
                                       + (onlyApi.Count > 0 ? "\tapi-only: " + string.Join(",", onlyApi.Take(12)) : "")
                                       + (onlyNative.Count > 0 ? "\tnative-only: " + string.Join(",", onlyNative.Take(12)) : ""));
                        }
                        continue;
                    }

                    // The right window, spelled right — which says nothing yet about what it SAYS. A field
                    // can carry a value RouterOS would never print and still count as shared above:
                    // /system/logging read `topics` as the raw handle list "[1]" where the API says "info",
                    // and this audit called the path OK for a release. So compare the values too, on rows
                    // paired by .id, over the fields both transports report.
                    var valueDiffs = CompareValues(a, n);
                    if (valueDiffs.Count == 0)
                    {
                        agreed++;
                        report.Add($"OK         {path}\trows={a.RowCount}\tshared fields={shared}/{a.FieldNames.Count}");
                        // A tally that only ever grows stops meaning anything (the lesson A12's enum list was
                        // built on). A path that now agrees must leave the table in the same change, or the
                        // remaining reasons stop describing what is actually still broken.
                        if (KnownValueGaps.ContainsKey(path)) staleGaps.Add(path);
                    }
                    else if (KnownValueGaps.TryGetValue(path, out string valueWhy))
                    {
                        known++;
                        report.Add($"KNOWN-GAP  {path}\trows={a.RowCount}\tvalues differ: "
                                   + string.Join("; ", valueDiffs.Take(6)) + $"\t{valueWhy}");
                    }
                    else
                    {
                        valueMismatched++;
                        report.Add($"VALUE-DIFF {path}\trows={a.RowCount}\t"
                                   + string.Join("; ", valueDiffs.Take(8))
                                   + (valueDiffs.Count > 8 ? $" (+{valueDiffs.Count - 8} more)" : ""));
                    }
                }
            }

            File.WriteAllLines(reportPath, report);
            foreach (string line in report) Console.WriteLine(line);
            Console.WriteLine();
            Console.WriteLine($"OK={agreed}  KNOWN-GAP={known}  MISMATCH={mismatched}  VALUE-DIFF={valueMismatched}"
                              + $"  UNMAPPED={unmapped}  ROUTER-N/A={apiRefused}");
            Console.WriteLine($"report: {reportPath}");

            Assert.AreEqual(0, unmapped, "paths the WinBox-native transport cannot address but the API can — see the report");
            Assert.AreEqual(0, mismatched, "paths where WinBox-native disagrees with the API — see the report");
            Assert.AreEqual(0, valueMismatched,
                "paths that read the right window but decode a value the API spells differently — see the report");
            Assert.AreEqual(0, staleGaps.Count,
                "these now agree with the API and must be removed from KnownValueGaps: "
                + string.Join(", ", staleGaps));
        }
    }
}
