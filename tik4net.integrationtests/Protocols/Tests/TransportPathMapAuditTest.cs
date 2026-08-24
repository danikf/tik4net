// TransportPathMapAuditTest.cs — diagnostic: does the transport under test reach everything the binary
// API reaches, and does it come back with the SAME table?
//
// Every transport promises the same contract over a different wire, and each has its own way of getting a
// field wrong: WinBox-native resolves a path to an M2 handler and can answer with somebody else's records;
// the CLI family parses TEXT, where a column can be truncated, a value re-rendered or a field simply not
// printed; REST renders values its own way. None of that shows up in a normal suite run — a test that
// cannot reach a path skips, and a test that reads plausible rows passes.
//
// So this compares, per API path, what the binary API returns against what the transport under test
// returns: row count, the set of field names, and — on rows paired by .id — the VALUES of the fields both
// report. The values matter as much as the names: /system/logging read `topics` as the raw handle list
// "[1]" where the API says "info", and an audit that only counted field names called the path OK for a
// release.
//
// The transport is TIK4NET_AUDIT_TRANSPORT (default WinboxNative); the report is named after it, so runs
// against different transports do not overwrite each other. It writes next to the other catalog dumps
// (App.config catalogDumpDir). Run it after touching the alias tables, a codec or a CLI parser, after the
// .jg harvest, or on a new RouterOS version.
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
    public class TransportPathMapAuditTest
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

        // Paths that reach the RIGHT window but whose FIELD vocabulary still differs from the API's — a
        // decode-layer gap (label <-> api-name), not a path-map one. EMPTY, and meant to stay that way: an
        // entry here excuses a whole path's field set, so anything added must be a difference the router
        // itself imposes. The last occupant was /system/health, whose reason said state/state-after-reboot
        // were "API-only fields with no WinBox equivalent" — the router sends both at [24,14] 0x8/0x9 and
        // no .jg window names those keys, which is a gap in us, not in WinBox (G10). A path that agrees
        // must leave this table in the same change; the stale check below enforces it.
        private static readonly Dictionary<string, string> KnownFieldGaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
        };

        // Paths WinBox genuinely does not expose as a readable window. The list is NOT kept here: it lives
        // in the library (WinboxHandlerMap.NoWinboxWindow), because the transport needs it too — a path with
        // no window raises an error saying so, instead of the "add a PathAlias" advice that fits a genuine
        // mapping gap. One table, so the runtime error and this test cannot drift apart.
        private static readonly IReadOnlyDictionary<string, string> NoWinboxWindow =
            tik4net.Winbox.WinboxHandlerMap.NoWinboxWindow;

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

        // Through TestBase's lab policy rather than a second copy of it: that is what knows a MAC transport
        // is addressed by routerMac and no host, and that the CHR's certificate has to be accepted for the
        // TLS ones. Both were reasons this audit could only ever have run against a plain-TCP transport.
        private static ITikConnection Open(TikConnectionType type) => TestBase.LabSetup(type).Create(type);

        /// <summary>
        /// The transport being held against the binary API, from <c>TIK4NET_AUDIT_TRANSPORT</c>.
        /// </summary>
        /// <remarks>
        /// An environment variable rather than the runsettings, because this class does not derive from
        /// <see cref="TestBase"/> — it owns both connections, and one of them is always the API baseline.
        /// The default keeps the transport this audit was written for.
        /// </remarks>
        private static TikConnectionType TransportUnderTest
        {
            get
            {
                string name = Environment.GetEnvironmentVariable("TIK4NET_AUDIT_TRANSPORT");
                if (string.IsNullOrEmpty(name)) return TikConnectionType.WinboxNative;
                foreach (TikConnectionType t in Enum.GetValues(typeof(TikConnectionType)))
                    if (string.Equals(t.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return t;
                throw new InvalidOperationException("TIK4NET_AUDIT_TRANSPORT names no transport: " + name);
            }
        }

        /// <summary>A bare <c>HH:MM:SS</c>, the one duration spelling the API never uses.</summary>
        private static bool IsClockShaped(string v)
        {
            if (v == null || v.Length != 8 || v[2] != ':' || v[5] != ':') return false;
            for (int i = 0; i < 8; i++)
                if (i != 2 && i != 5 && (v[i] < '0' || v[i] > '9')) return false;
            return true;
        }

        private static bool IsNative(TikConnectionType t)
            => t == TikConnectionType.WinboxNative || t == TikConnectionType.WinboxNativeMac;

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

        /// <summary>
        /// Reads a path, asking for <c>detail</c> and falling back to a plain print when the router will
        /// not take the word.
        /// </summary>
        /// <remarks>
        /// A CLI read without <c>detail</c> returns the SUMMARY columns only — `/interface` comes back with
        /// no default-name, no mtu, no byte counters — so a plain print made the CLI look like it was
        /// missing a fifth of the API's vocabulary when it had simply been asked a narrower question. But
        /// <c>detail</c> is not universally accepted either: sent to the binary API it is a word the router
        /// sees, and 31 of the audited menus refuse it outright, which turned those paths into
        /// ROUTER-N/A and measured nothing at all.
        /// <para>So it is attempted and retried without, per path per transport, rather than decided from a
        /// list of which transports or which menus take it. The retry costs one refused call on the paths
        /// that do not, and it cannot silently under-ask: the fallback happens only after the router has
        /// said no.</para>
        /// </remarks>
        private static List<ITikReSentence> PrintRows(ITikConnection conn, string path)
        {
            try
            {
                return conn.CreateCommand(path + "/print",
                               conn.CreateParameter("detail", "", TikCommandParameterFormat.NameValue))
                           .ExecuteList().ToList();
            }
            catch (Exception)
            {
                return conn.CreateCommand(path + "/print").ExecuteList().ToList();
            }
        }

        private static Reading Read(ITikConnection conn, string path)
        {
            var r = new Reading();
            try
            {
                var rows = PrintRows(conn, path);
                r.RowCount = rows.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var w in rows[i].Words)
                        fields[w.Key] = w.Value;
                    // A name the API transport INVENTED rather than one the router used. RouterOS sends the
                    // same word twice on some rows (/certificate 'trusted', /ip/arp 'published',
                    // /interface/list 'dynamic', two ipsec fields), and ApiSentence keeps the second under
                    // base+2 so it is not lost. Counting that as a router field makes every other transport
                    // look one name short of the API - the audit comparing itself, exactly as it did with
                    // '.tag'. The rule that recognises them lives with the code that creates them.
                    foreach (var name in fields.Keys)
                        if (!tik4net.Api.ApiSentence.IsDuplicateWorkaroundName(name, fields.Keys))
                            r.FieldNames.Add(name);
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
        /// The individual FIELDS whose values the two transports are not required to agree on, with the
        /// reason for each. Everything else is compared.
        /// </summary>
        /// <remarks>
        /// <para>Per field, not per path, so a path carrying one excused field still has every OTHER field
        /// of every row compared — a path-level pardon excused a whole table for one known difference, and
        /// a new disagreement anywhere on that path went unreported. An entry whose field turns out to
        /// AGREE fails the run and must be deleted (see the stale check), so this list cannot quietly
        /// outlive what it describes.</para>
        /// <para>This is no longer a list of missing decoders. Every decode class the first value-comparing
        /// run turned up — interval/duration, raw MAC, zero-spelled-as-a-word sentinels, the empty list, set
        /// order, date/epoch, the <c>age</c> uptime clock, the wire-type key collision, the union/tuple
        /// element, the <c>multibits</c> bitmask, an <c>enm</c>'s <c>postfix</c>, the address:port pair —
        /// has been closed, and so have the four G3 items that were recorded here as differences with a
        /// reason and turned out to be client defects (G3.1 <c>fib</c>, G3.3 <c>auto-negotiation</c>,
        /// G3.4 <c>/ip/route</c>, G3.5 the wireless sniffer).</para>
        /// <para>What remains are differences neither side can remove.</para>
        /// </remarks>
        private static readonly Dictionary<string, Dictionary<string, string>> ValueComparisonExceptions =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/routing/table"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // The same fact spelled two ways, and neither spelling is wrong. RouterOS stores `fib` as a
                // valueless PRESENCE flag: the binary API and REST answer `fib=` (word present, value empty)
                // for a table that has it and omit the word for one that does not, while the CLI transports
                // and native answer `fib=true`. This audit compares RAW WORDS, so '' and 'true' differ here
                // for good. The O/R mapper does not: RoutingTable.Fib is declared IsPresenceFlag and reads
                // the empty value as true, which is what closed G3.1 — before that it read false on api and
                // rest whatever the router said.
                ["fib"] = "api/rest spell a set presence flag as an empty value, native and the CLI as 'true'",
            },
            ["/system/ntp/client"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Precision the wire does not carry. The offset rides as a plain signed integer of
                // milliseconds (0x67, measured on 7.24: u32 4294967280 = -16) where the API reports
                // thousandths, so native can only ever be the API's value with the fraction cut off —
                // and cut off, not rounded: a live pair read seconds apart was api 59.819 / native 59.
                //
                // Truncating before comparing would therefore be exact... until an NTP poll lands between
                // the two reads. The value does not drift continuously, it STEPS per poll, by tens of
                // milliseconds (one session: -22.362, -19.434, -16.395, +59.819, each held constant across
                // repeated reads in between). No tolerance survives that, so the field is excused rather
                // than compared cleverly.
                ["system-offset"] = "integer milliseconds on the wire vs thousandths over the API, and it steps per NTP poll",
            },
            // The DNS cache is not a configuration table: its rows appear and expire while the audit runs,
            // and `ttl` is a live countdown, so the two transports read it a moment apart and differ by the
            // moment. Excusing the field, not the path — every other name on it is still compared.
            ["/ip/dns/cache"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ttl"] = "a live countdown; the two transports read it at different instants",
            },
            ["/ip/dns/cache/all"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ttl"] = "a live countdown; the two transports read it at different instants",
            },
        };

        /// <summary>
        /// Paths whose ROW COUNT moves on its own, so the two transports can read different numbers of rows
        /// without either being wrong. The names are still compared, and so are the values of every row the
        /// two share.
        /// </summary>
        /// <remarks>
        /// Only the DNS cache so far, and it earned it: a run reported one row against two, and the same
        /// pair read by hand a minute later agreed on three — entries expire and arrive between the two
        /// reads. Excusing the COUNT is not excusing the path; a wrong window would still show up as a
        /// different field vocabulary, and a wrong value on a shared row still fails.
        /// </remarks>
        private static readonly Dictionary<string, string> VolatileRowCounts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/ip/dns/cache"]     = "cache entries expire and arrive between the two reads",
                ["/ip/dns/cache/all"] = "cache entries expire and arrive between the two reads",
                ["/ip/firewall/connection"] = "connections come and go between the two reads",
            };

        /// <summary>The fields excused on <paramref name="path"/>, or null when none are.</summary>
        private static Dictionary<string, string> ExceptionsFor(string path)
            => ValueComparisonExceptions.TryGetValue(path, out var e) ? e : null;

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
        private static List<string> CompareValues(Reading api, Reading probe, string path,
            out List<string> excusedButAgreeing, out int pairedRows)
        {
            pairedRows = 0;
            var diffs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excused = ExceptionsFor(path);
            // An excused field is watched, not ignored: a field that AGREES on every row it was compared on
            // no longer needs its exception, and the run fails until the entry is deleted. Recorded here
            // rather than inferred later, because only this loop knows the field was actually present on
            // both sides — a field nobody read agrees vacuously.
            var agreeing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disagreeing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in api.Rows)
            {
                if (!probe.Rows.TryGetValue(kv.Key, out var probeRow)) continue;
                pairedRows++;
                foreach (var f in kv.Value)
                {
                    if (f.Key == ".id" || IsVolatile(f.Key)) continue;
                    if (!probeRow.TryGetValue(f.Key, out string probeValue)) continue;
                    bool agrees = string.Equals(f.Value ?? "", probeValue ?? "", StringComparison.OrdinalIgnoreCase);
                    if (excused != null && excused.ContainsKey(f.Key))
                    {
                        (agrees ? agreeing : disagreeing).Add(f.Key);
                        continue;
                    }
                    if (agrees) continue;
                    if (!seen.Add(f.Key)) continue;
                    diffs.Add($"{f.Key}: api='{Trim(f.Value)}' probe='{Trim(probeValue)}'");
                }
            }

            excusedButAgreeing = agreeing.Where(f => !disagreeing.Contains(f)).OrderBy(f => f).ToList();
            return diffs;
        }

        private static string Trim(string v)
            => v == null ? "" : (v.Length > 40 ? v.Substring(0, 37) + "..." : v);

        /// <summary>
        /// For each field only the API reports, the field(s) only native reports that carry the SAME value
        /// on every row both transports returned - a proposed api-name/winbox-name pairing, found by moving
        /// the value rather than by matching the words.
        /// </summary>
        /// <remarks>
        /// <para>Naming what is missing is only half an answer: the fix for a missing name is an alias, and
        /// an alias may only exist once the pairing has been ESTABLISHED. This does that mechanically over
        /// whatever the two transports already read - the same evidence a hand-run probe produces, on every
        /// path at once.</para>
        /// <para>It cannot pair a field that is EMPTY on every row: two blanks agree vacuously, and so would
        /// every other blank field on the row. Those still need a value put into them by hand
        /// (<c>/user</c>'s <c>address</c> is one). And a proposal is a lead, never a licence - two fields can
        /// agree on the rows this router happens to have and still mean different things.</para>
        /// </remarks>
        private static Dictionary<string, List<string>> ProposePairings(
            Reading api, Reading probe, List<string> onlyApi, List<string> onlyProbe)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (onlyApi.Count == 0 || onlyProbe.Count == 0) return result;

            var paired = api.Rows.Where(kv => probe.Rows.ContainsKey(kv.Key))
                                 .Select(kv => new { Api = kv.Value, Probe = probe.Rows[kv.Key] })
                                 .ToList();
            if (paired.Count == 0) return result;

            foreach (string af in onlyApi)
            {
                var candidates = new List<string>();
                foreach (string nf in onlyProbe)
                {
                    int compared = 0;
                    bool allAgree = true;
                    foreach (var row in paired)
                    {
                        if (!row.Api.TryGetValue(af, out string av) || string.IsNullOrEmpty(av)) continue;
                        if (!row.Probe.TryGetValue(nf, out string nv)) { allAgree = false; break; }
                        if (!string.Equals(av, nv, StringComparison.OrdinalIgnoreCase)) { allAgree = false; break; }
                        compared++;
                    }
                    if (allAgree && compared > 0) candidates.Add(nf);
                }
                if (candidates.Count > 0) result[af] = candidates;
            }
            return result;
        }

        /// <summary>Renders <see cref="ProposePairings"/> as <c>api-name?=winbox-name</c>, an ambiguous
        /// proposal showing every candidate.</summary>
        private static string FormatPairings(Dictionary<string, List<string>> pairings)
            => pairings.Count == 0 ? ""
             : "\tvalue matches: " + string.Join(", ",
                   pairings.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                           .Select(kv => kv.Key + "?=" + string.Join("|", kv.Value)));

        [Ignore("The full transport-vs-API audit: minutes long, seeds and removes 62 rows on the router. "
            + "Comment the attribute out to run it — --filter alone will not, see the file header.")]
        [TestMethod]
        public void AuditPathMapAgainstApi()
        {
            string dumpDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["catalogDumpDir"] ?? @".\.tik4net"));
            Directory.CreateDirectory(dumpDir);
            var probeType = TransportUnderTest;
            string probeName = probeType.ToString().ToLowerInvariant();
            string reportPath = Path.Combine(dumpDir, probeName + "-path-audit.txt");

            var report = new List<string>();
            var staleGaps = new List<string>();
            int unmapped = 0, mismatched = 0, agreed = 0, apiRefused = 0, known = 0, valueMismatched = 0, uncompared = 0;
            // The field-NAME shortfall across the paths that pass: the API's vocabulary against ours. Counted
            // separately because the pass/fail check is a half-threshold and cannot see it (see the OK line).
            int apiFieldSlots = 0, notReported = 0;
            // Fields whose API value is a bare HH:MM:SS. The API spells a DURATION in units ("15s", "1w"),
            // so a value in this shape is a clock TIME — and that is the one thing a CLI reader cannot tell
            // apart by looking, because as-value spells durations as HH:MM:SS too. Anything converting the
            // CLI's spelling back has to leave these alone, so the list is reported rather than assumed
            // short: if it ever grows, the conversion rule has to grow with it.
            var clockShaped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            var fixtures = default(TransportAuditFixtures);
            var write = default(TransportWriteAudit);
            using (var api = Open(TikConnectionType.Api))
            {
            // Half the audited paths are empty on a stock router and are compared 0 rows against 0 rows —
            // an OK line that measured nothing. Put one row in each table that can be written without
            // hardware first, and take them away in the finally: residue on the router is a defect the
            // NEXT run inherits. The native connection is opened AFTER seeding, so its .jg catalog and
            // reference tables see the rows.
            fixtures = new TransportAuditFixtures(api);
            try
            {
            fixtures.SeedAll();
            using (var probe = Open(probeType))
            {
                foreach (string path in EntityPaths())
                {
                    var a = Read(api, path);
                    foreach (var row in a.Rows.Values)
                        foreach (var f in row)
                            if (IsClockShaped(f.Value)) clockShaped.Add(path + " " + f.Key);
                    var n = Read(probe, path);

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
                        if (IsNative(probeType) && n.Error.StartsWith("UNMAPPED")
                            && NoWinboxWindow.TryGetValue(path, out string reason))
                        {
                            known++;
                            report.Add($"NO-WINDOW  {path}\t{reason}");
                            continue;
                        }
                        if (n.Error.StartsWith("UNMAPPED")) unmapped++; else mismatched++;
                        report.Add($"{(n.Error.StartsWith("UNMAPPED") ? "UNMAPPED   " : "PROBE-ERR  ")}{path}"
                                   + $"\tapi rows={a.RowCount}\t{n.Error}");
                        continue;
                    }

                    // Field names are the strongest cheap signal that both transports read the SAME window:
                    // a wrong handler answers with a different vocabulary long before the row count matches.
                    // '.tag' is not a field the ROUTER has - it is the API sentence's own tag word, which
                    // tik4net puts there and only the API transport carries. Counting it made every one of
                    // the 61 field-bearing paths look one name short of the API.
                    a.FieldNames.Remove(TikSpecialProperties.Tag);
                    n.FieldNames.Remove(TikSpecialProperties.Tag);
                    var onlyApi = a.FieldNames.Where(f => !n.FieldNames.Contains(f)).OrderBy(f => f).ToList();
                    var shared = a.FieldNames.Count(f => n.FieldNames.Contains(f));
                    apiFieldSlots += a.FieldNames.Count;
                    notReported += onlyApi.Count;
                    bool rowsAgree = a.RowCount == n.RowCount || VolatileRowCounts.ContainsKey(path);
                    bool fieldsAgree = a.FieldNames.Count == 0 || shared * 2 >= a.FieldNames.Count;

                    if (!rowsAgree || !fieldsAgree)
                    {
                        if (KnownFieldGaps.TryGetValue(path, out string why))
                        {
                            known++;
                            report.Add($"KNOWN-GAP  {path}\tapi rows={a.RowCount} {probeName} rows={n.RowCount}"
                                       + $"\tshared fields={shared}/{a.FieldNames.Count}\t{why}");
                        }
                        else
                        {
                            mismatched++;
                            var onlyProbe = n.FieldNames.Where(f => !a.FieldNames.Contains(f)).OrderBy(f => f).ToList();
                            report.Add($"MISMATCH   {path}\tapi rows={a.RowCount} {probeName} rows={n.RowCount}"
                                       + $"\tshared fields={shared}/{a.FieldNames.Count}"
                                       + (onlyApi.Count > 0 ? "\tapi-only: " + string.Join(",", onlyApi.Take(12)) : "")
                                       + (onlyProbe.Count > 0 ? $"\t{probeName}-only: " + string.Join(",", onlyProbe.Take(12)) : "")
                                       + FormatPairings(ProposePairings(a, n, onlyApi, onlyProbe)));
                        }
                        continue;
                    }

                    // The right window, spelled right — which says nothing yet about what it SAYS. A field
                    // can carry a value RouterOS would never print and still count as shared above:
                    // /system/logging read `topics` as the raw handle list "[1]" where the API says "info",
                    // and this audit called the path OK for a release. So compare the values too, on rows
                    // paired by .id, over the fields both transports report.
                    var valueDiffs = CompareValues(a, n, path, out var excusedButAgreeing, out int pairedRows);

                    // A tally that only ever grows stops meaning anything (the lesson A12's enum list was
                    // built on). An excused field that now AGREES must leave the exception list in the same
                    // change, or the remaining reasons stop describing what is actually still broken.
                    // An excuse that is no longer needed must go — that is what this check is for. But a
                    // field excused for VOLATILITY is never "fixed": the DNS cache's ttl is a live
                    // countdown and two reads land in the same second often enough to look settled. Making
                    // the run fail on that would teach the next person to delete a true excuse.
                    foreach (string f in excusedButAgreeing)
                        if (!VolatileRowCounts.ContainsKey(path)) staleGaps.Add(path + " " + f);

                    if (valueDiffs.Count == 0)
                    {
                        agreed++;
                        // Same rule one level up: a path listed as a FIELD gap that now agrees on rows,
                        // names and values has no gap left to excuse.
                        if (KnownFieldGaps.ContainsKey(path)) staleGaps.Add(path + " (KnownFieldGaps)");
                        var excused = ExceptionsFor(path);
                        // A path can be OK and still report FEWER names than the API: the name check passes
                        // at half, so everything between half and all of the API's vocabulary is invisible
                        // in the OK/MISMATCH tally. Name them here - a field native does not report is a
                        // field a caller cannot read, whether or not it trips the threshold.
                        // Rows are paired by .id, so a path where the two transports spell .id
                        // differently pairs NOTHING and every value on it goes uncompared — an OK
                        // line that measured only names. /file is one: native reports the router's
                        // numeric handle where the API reports its opaque '**...' id. Say so rather
                        // than let it read as agreement.
                        if (pairedRows == 0 && a.RowCount > 0) uncompared++;
                        report.Add($"OK         {path}	rows={a.RowCount}"
                                   + (VolatileRowCounts.TryGetValue(path, out string vwhy)
                                       ? $" ({probeName} {n.RowCount}; row count not compared: {vwhy})" : "")
                                   + (pairedRows == 0 && a.RowCount > 0
                                       ? "	VALUES UNCOMPARED (no row paired by .id)" : "")
                                   + $"	shared fields={shared}/{a.FieldNames.Count}"
                                   + (onlyApi.Count > 0 ? "\tapi-only: " + string.Join(",", onlyApi) : "")
                                   + FormatPairings(ProposePairings(a, n, onlyApi,
                                       n.FieldNames.Where(f => !a.FieldNames.Contains(f))
                                        .OrderBy(f => f).ToList()))
                                   + (excused != null ? "\tnot compared: " + string.Join(", ", excused.Keys) : ""));
                    }
                    else
                    {
                        valueMismatched++;
                        report.Add($"VALUE-DIFF {path}\trows={a.RowCount}\t"
                                   + string.Join("; ", valueDiffs.Take(8))
                                   + (valueDiffs.Count > 8 ? $" (+{valueDiffs.Count - 8} more)" : ""));
                    }
                }

                // …and the other direction, on the rows this run created and will delete. Everything above
                // is a READ, so a mapping that names the wrong key is measured only where it misleads and
                // never where it does damage.
                write = new TransportWriteAudit(api, probe, probeName);
                write.Run(fixtures);
                write.RunAddsAndRemoves(fixtures);

                // The three verbs the CRUD probes above do not reach. Each has ONE implementation on the
                // native side and many correct answers, because each is translated into a field write whose
                // key and whose empty form are per window: enable/disable into ‹disabled›, unset into the
                // field default. VerbMatrixTest proves them per transport on one path; these ask the other
                // sixty tables the same question.
                write.RunToggles(fixtures);
                write.RunUnsets(fixtures);
                write.RunMoves(fixtures);
            }
            }
            finally
            {
                fixtures.Dispose();
            }
            }

            report.Add("");
            if (write != null)
            {
                foreach (var w in write.Results)
                    report.Add($"WRITE-{w.Outcome.ToString().ToUpperInvariant(),-12} {w.Path}	{w.Detail}");
                report.Add($"WRITES ok={write.Count(TransportWriteAudit.Outcome.Ok)}"
                           + $"  different={write.Count(TransportWriteAudit.Outcome.Different)}"
                           + $"  refused={write.Count(TransportWriteAudit.Outcome.Refused)}"
                           + $"  not-probeable={write.Count(TransportWriteAudit.Outcome.NotProbeable)}");
                report.Add("");
            }
            report.Add($"SEEDED {fixtures.SeededPaths.Count} paths that are empty on a stock router");
            foreach (string s2 in fixtures.Skipped) report.Add($"  seed-skipped {s2}");
            foreach (string s2 in fixtures.Leaked) report.Add($"  SEED NOT REMOVED {s2}");

            // The tallies go in the FILE as well as the console: the report is what gets read later (and
            // diffed against the last run), and a list of lines with no totals under it invites counting
            // them by hand.
            report.Add("");
            report.Add($"OK={agreed}  KNOWN-GAP={known}  MISMATCH={mismatched}  VALUE-DIFF={valueMismatched}  VALUES-UNCOMPARED={uncompared}"
                       + $"  UNMAPPED={unmapped}  ROUTER-N/A={apiRefused}");
            report.Add($"FIELD-NAMES not reported by {probeName}: {notReported}/{apiFieldSlots}"
                       + (apiFieldSlots > 0 ? $" ({notReported * 100 / apiFieldSlots}%)" : ""));
            report.Add($"CLOCK-SHAPED api values ({clockShaped.Count}): " + string.Join(", ", clockShaped));
            File.WriteAllLines(reportPath, report);
            foreach (string line in report) Console.WriteLine(line);
            Console.WriteLine();
            Console.WriteLine($"OK={agreed}  KNOWN-GAP={known}  MISMATCH={mismatched}  VALUE-DIFF={valueMismatched}  VALUES-UNCOMPARED={uncompared}"
                              + $"  UNMAPPED={unmapped}  ROUTER-N/A={apiRefused}");
            // Not an assertion - a number to watch. The pass/fail checks above cannot see it, so without
            // this line the report reads green while a fifth of the API's field names go unreported.
            Console.WriteLine($"FIELD-NAMES not reported by {probeName}: {notReported}/{apiFieldSlots}"
                              + (apiFieldSlots > 0 ? $" ({notReported * 100 / apiFieldSlots}%)" : ""));
            Console.WriteLine($"report: {reportPath}");

            Assert.AreEqual(0, unmapped, "paths the transport under test cannot address but the API can — see the report");
            Assert.AreEqual(0, mismatched, "paths where the transport under test disagrees with the API — see the report");
            Assert.AreEqual(0, valueMismatched,
                "paths that read the right window but decode a value the API spells differently — see the report");
            if (write != null)
            {
                Assert.AreEqual(0, write.Count(TransportWriteAudit.Outcome.Different),
                    "fields the transport under test writes differently from the API — see the report");
                Assert.AreEqual(0, write.Count(TransportWriteAudit.Outcome.Refused),
                    "fields the transport under test refused to write that the API wrote — see the report");
            }
            Assert.AreEqual(0, fixtures.Leaked.Count,
                "fixture rows this run could not remove — the router is holding residue: "
                + string.Join("; ", fixtures.Leaked));
            Assert.AreEqual(0, staleGaps.Count,
                "these now agree with the API and must be removed from the table naming them: "
                + string.Join(", ", staleGaps));
        }
    }
}
