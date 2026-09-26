// EntityCanonicalFormProbe.cs — probe: does an entity loaded and saved unchanged send nothing, on every
// transport? Validation run V2 of the 5.0 entity value model (TikValue<T>).
//
// WHY THIS EXISTS. In the 5.0 model a property is "changed" when its canonical wire string —
// ConvertToString(ConvertFromString(raw)), which is what TikEntityPropertyAccessor.GetEntityValue returns
// for a loaded entity — differs from the one snapshotted at load. That is the only change detection, so
// three things have to hold for every writable mapped property the router prints:
//   (a) ROUND TRIP  — canonical -> parse -> canonical is the same string. Otherwise a clone, a merge, or
//                     anything that copies a value through its string form reports a change nobody made.
//   (b) REPEAT      — two loads of the same row on the same transport give the same canonical string.
//                     FullUpdate diffs exactly those two loads; a writable field that moves on its own is
//                     sent back on every save.
//   (c) CROSS       — the transport gives the API's canonical string. SaveListDifferences and merge compare
//                     rows that may come from different connections; a CLI 00:05:00 against an API 5m must
//                     be equal, or an unchanged value is written.
// And, end to end, the change tracker must report no change for a freshly loaded entity (what Save(OnlyChanges)
// consults before it builds a command).
//
// WHAT IT DOES NOT PROVE. Only rows the lab router holds are exercised: a menu with no rows says nothing.
// Rows are paired by .id (singletons by being the only row); a row that appears or goes between two loads
// is counted, not compared. Absence is only visible on nullable properties — a non-nullable one that the
// router did not print reads its declared default, which is the 4.x behaviour the model replaces, and shows
// up here as a CROSS difference when another transport did print it.
//
// Values are printed except for /queue/tree (real customer data on the lab router) and for secret-looking
// fields (secret, key, password): there only whether a value was present is.
//
// Environment:
//   TIK4NET_V2_TRANSPORTS  comma list of TikConnectionType names (default: every transport except Api)
//   TIK4NET_V2_OUT         report file (default: TestResults/v2/canonical-<router>.txt under the solution)
//   TIK4NET_V2_ROUTER      "lab" (default) or "romonTarget" — the second lab router from App.config;
//                          it has no MAC in App.config, so the MAC-layer transports are skipped there
//
// [Ignore] keeps it out of the matrix — run via --filter EntityCanonicalFormProbe with the attribute removed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("V2 canonical-form sweep — loads every settable entity several times per transport against a live router. Remove the attribute to run.")]
    [TestClass]
    public class EntityCanonicalFormProbe
    {
        /// <summary>Verb entities: a load of these runs a command (or streams forever) rather than reading a menu.</summary>
        private static readonly HashSet<string> VerbPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/ping", "/tool/romon/ping", "/tool/torch", "/tool/traceroute", "/interface/monitor-traffic",
        };

        private static readonly HashSet<string> PrivatePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/queue/tree",
        };

        private sealed class Loaded
        {
            public List<object> Rows = new List<object>();
            public string Error;
            public long Ms;
        }

        [TestMethod]
        public void SweepCanonicalFormsOfEveryEntity()
        {
            string routerKind = Environment.GetEnvironmentVariable("TIK4NET_V2_ROUTER") ?? "lab";
            bool romonTarget = string.Equals(routerKind, "romonTarget", StringComparison.OrdinalIgnoreCase);

            var transports = (Environment.GetEnvironmentVariable("TIK4NET_V2_TRANSPORTS") is string list && list.Length > 0
                    ? list.Split(',').Select(s => (TikConnectionType)Enum.Parse(typeof(TikConnectionType), s.Trim(), true))
                    : Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>().Where(t => t != TikConnectionType.Api))
                .Where(t => !(romonTarget && IsMacLayer(t)))
                .ToList();

            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => (Type: t, Attr: t.GetCustomAttribute<TikEntityAttribute>()))
                .Where(x => x.Attr != null && !x.Type.IsAbstract)
                // A verb entity declares LoadCommand = "": loading it runs the command (ping, discover, torch …),
                // and some of those stream until cancelled.
                .Where(x => x.Attr.LoadCommand != "")
                .Where(x => !VerbPaths.Contains(x.Attr.EntityPath) && !x.Attr.EntityPath.EndsWith("/monitor", StringComparison.Ordinal))
                .OrderBy(x => x.Attr.EntityPath, StringComparer.Ordinal)
                .ToList();

            string outPath = Environment.GetEnvironmentVariable("TIK4NET_V2_OUT")
                ?? Path.Combine(SolutionDir(), "TestResults", "v2", $"canonical-{routerKind}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            _progressPath = outPath + ".progress";
            File.WriteAllText(_progressPath, "");

            var report = new StringBuilder();
            var summary = new StringBuilder();
            report.AppendLine($"# V2 canonical-form sweep — router={routerKind} — {DateTime.Now:yyyy-MM-dd HH:mm}");

            // The API is the reference for (c), loaded once and reused for every transport.
            var apiRows = new Dictionary<Type, Loaded>();
            string version;
            using (var api = Open(TikConnectionType.Api, romonTarget))
            {
                version = api.LoadSingle<tik4net.Objects.System.SystemResource>().Version.Value ?? "?";
                foreach (var (type, _) in entities)
                    apiRows[type] = Load(api, type);
            }
            report.AppendLine($"# RouterOS {version}");

            // (a), (b) and the tracker on the API itself, then everything against it for each transport.
            foreach (var transport in new[] { TikConnectionType.Api }.Concat(transports))
            {
                var stopwatch = Stopwatch.StartNew();
                int compared = 0, roundTrip = 0, repeat = 0, cross = 0, tracker = 0, errors = 0, unpaired = 0, withRows = 0;
                var crossFields = new SortedDictionary<string, int>(StringComparer.Ordinal);
                report.AppendLine();
                report.AppendLine($"## {transport}");
                Progress("== " + transport);

                ITikConnection connection = null;
                try
                {
                    foreach (var (type, attr) in entities)
                    {
                        var metadata = Metadata(type);
                        var writable = metadata.Properties.Where(p => !p.IsReadOnly).ToList();
                        if (writable.Count == 0)
                            continue;
                        bool hideRows = PrivatePaths.Contains(metadata.EntityPath);

                        try { if (connection == null) connection = Open(transport, romonTarget); }
                        catch (Exception ex)
                        {
                            report.AppendLine($"OPEN-ERROR {ex.GetType().Name}: {ex.Message}");
                            errors++;
                            break;
                        }

                        var first = transport == TikConnectionType.Api ? apiRows[type] : Load(connection, type);
                        if (first.Error != null)
                        {
                            // A connection that failed mid-read is not trusted for the next entity.
                            if (apiRows[type].Error == null)
                            {
                                report.AppendLine($"{metadata.EntityPath}: LOAD-ERROR {first.Error}");
                                errors++;
                            }
                            connection.Dispose();
                            connection = null;
                            continue;
                        }
                        if (first.Rows.Count == 0)
                            continue;
                        withRows++;

                        // Tracker: freshly loaded rows must report no change.
                        foreach (var row in first.Rows)
                            if (connection.ChangeTracker().HasChanges(row, metadata))
                            {
                                tracker++;
                                report.AppendLine($"{metadata.EntityPath} {Id(metadata, row)}: TRACKER reports a change right after load");
                            }

                        // (a) round trip
                        foreach (var row in first.Rows)
                        {
                            var fresh = Activator.CreateInstance(type, nonPublic: true);
                            foreach (var p in writable)
                            {
                                string s1 = p.GetEntityValue(row);
                                string s2;
                                try
                                {
                                    p.SetEntityValue(fresh, s1);
                                    s2 = p.GetEntityValue(fresh);
                                }
                                catch (Exception ex) { s2 = "<" + ex.GetType().Name + ": " + ex.Message + ">"; }
                                compared++;
                                if (s1 != s2)
                                {
                                    roundTrip++;
                                    report.AppendLine($"{metadata.EntityPath} {Id(metadata, row)} {p.FieldName}: ROUNDTRIP {Show(s1, hideRows || IsSecret(p.FieldName))} -> {Show(s2, hideRows || IsSecret(p.FieldName))}");
                                }
                            }
                        }

                        // (b) repeat
                        var second = Load(connection, type);
                        if (second.Error != null)
                        {
                            report.AppendLine($"{metadata.EntityPath}: LOAD-ERROR (second load) {second.Error}");
                            errors++;
                            connection.Dispose();
                            connection = null;
                        }
                        else
                            foreach (var (a, b) in Pair(metadata, first.Rows, second.Rows, ref unpaired))
                                foreach (var p in writable)
                                {
                                    string va = p.GetEntityValue(a), vb = p.GetEntityValue(b);
                                    if (va != vb)
                                    {
                                        repeat++;
                                        report.AppendLine($"{metadata.EntityPath} {Id(metadata, a)} {p.FieldName}: REPEAT {Show(va, hideRows || IsSecret(p.FieldName))} then {Show(vb, hideRows || IsSecret(p.FieldName))}");
                                    }
                                }

                        // (c) cross — against the API
                        if (transport != TikConnectionType.Api && apiRows[type].Error == null)
                            foreach (var (a, r) in Pair(metadata, first.Rows, apiRows[type].Rows, ref unpaired))
                                foreach (var p in writable)
                                {
                                    string vt = p.GetEntityValue(a), va = p.GetEntityValue(r);
                                    if (vt != va)
                                    {
                                        cross++;
                                        string key = metadata.EntityPath + " " + p.FieldName;
                                        crossFields[key] = crossFields.TryGetValue(key, out int n) ? n + 1 : 1;
                                        report.AppendLine($"{metadata.EntityPath} {Id(metadata, a)} {p.FieldName}: CROSS {transport}={Show(vt, hideRows || IsSecret(p.FieldName))} api={Show(va, hideRows || IsSecret(p.FieldName))}");
                                    }
                                }
                    }
                }
                finally
                {
                    connection?.Dispose();
                }

                string line = $"{transport,-16} entities-with-rows={withRows} values={compared} ROUNDTRIP={roundTrip} REPEAT={repeat} "
                            + $"CROSS={cross} (fields={crossFields.Count}) TRACKER={tracker} errors={errors} unpaired={unpaired} "
                            + $"{stopwatch.Elapsed.TotalSeconds:0}s";
                summary.AppendLine(line);
                report.AppendLine("# " + line);
                foreach (var kv in crossFields)
                    report.AppendLine($"#   cross field {kv.Key} x{kv.Value}");
                Console.WriteLine(line);
            }

            report.AppendLine();
            report.AppendLine("# SUMMARY");
            report.Append(summary);

            File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Report: " + outPath);
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────────

        private static ITikConnection Open(TikConnectionType type, bool romonTarget)
        {
            if (!romonTarget)
                return TestBase.LabSetup(type).Create(type);
            return new TikConnectionSetup(
                    TikRouterAddress.FromHost(ConfigurationManager.AppSettings["romonTargetHost"]),
                    ConfigurationManager.AppSettings["romonTargetUser"],
                    ConfigurationManager.AppSettings["romonTargetPass"] ?? "")
                { AllowInvalidCertificate = true }
                .Create(type);
        }

        private static bool IsMacLayer(TikConnectionType t)
            => t == TikConnectionType.MacTelnet || t == TikConnectionType.WinboxCliMac || t == TikConnectionType.WinboxNativeMac;

        private static TikEntityMetadata Metadata(Type type)
            => (TikEntityMetadata)typeof(TikEntityMetadataCache).GetMethod(nameof(TikEntityMetadataCache.GetMetadata))
                .MakeGenericMethod(type).Invoke(null, null);

        private static string _progressPath;

        private static void Progress(string line)
        {
            if (_progressPath != null)
                File.AppendAllText(_progressPath, $"{DateTime.Now:HH:mm:ss} {line}" + Environment.NewLine);
        }

        private static Loaded Load(ITikConnection connection, Type type)
        {
            Progress("load " + type.Name);
            var result = new Loaded();
            var sw = Stopwatch.StartNew();
            try
            {
                var metadata = Metadata(type);
                if (metadata.IsSingleton)
                {
                    var single = typeof(TikConnectionExtensions).GetMethods()
                        .First(m => m.Name == nameof(TikConnectionExtensions.LoadSingle) && m.GetParameters().Length == 2)
                        .MakeGenericMethod(type)
                        .Invoke(null, new object[] { connection, Array.Empty<ITikCommandParameter>() });
                    result.Rows.Add(single);
                }
                else
                {
                    var all = (System.Collections.IEnumerable)typeof(TikConnectionExtensions).GetMethods()
                        .First(m => m.Name == nameof(TikConnectionExtensions.LoadAll) && m.GetParameters().Length == 1)
                        .MakeGenericMethod(type)
                        .Invoke(null, new object[] { connection });
                    foreach (var row in all)
                        result.Rows.Add(row);
                }
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                result.Error = inner.GetType().Name + ": " + FirstLine(inner.Message);
            }
            result.Ms = sw.ElapsedMilliseconds;
            Progress($"  {result.Rows.Count} rows {result.Ms} ms {result.Error}");
            return result;
        }

        private static IEnumerable<(object, object)> Pair(TikEntityMetadata metadata, List<object> left, List<object> right, ref int unpaired)
        {
            var pairs = new List<(object, object)>();
            if (metadata.IsSingleton)
            {
                if (left.Count == 1 && right.Count == 1)
                    pairs.Add((left[0], right[0]));
                return pairs;
            }
            if (!metadata.HasIdProperty)
                return pairs;

            var byId = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var r in right)
                if (Id(metadata, r) is string id && id.Length > 0)
                    byId[id] = r;
            foreach (var l in left)
            {
                if (Id(metadata, l) is string id && byId.TryGetValue(id, out var r))
                    pairs.Add((l, r));
                else
                    unpaired++;
            }
            return pairs;
        }

        private static string Id(TikEntityMetadata metadata, object row)
            => metadata.IsSingleton ? "(singleton)" : metadata.IdProperty?.GetEntityValue(row) ?? "(no id)";

        // Secrets are compared, never printed: the report shows only whether a value was there.
        private static bool IsSecret(string field)
            => field.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf("passphrase", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Show(string value, bool hide)
            => value == null ? "<null>" : hide ? "<hidden>" : "'" + (value.Length > 80 ? value.Substring(0, 80) + "…" : value) + "'";

        private static string FirstLine(string s)
        {
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl < 0 ? s : s.Substring(0, nl);
        }

        private static string SolutionDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tik4net.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
