// EntityDefaultValueProbe.cs — probe: which fields carrying a DefaultValue does the router leave out of a row?
// Audit for the 5.0 entity value model (TikValue<T>), before the mass conversion.
//
// WHY THIS EXISTS. In 4.x a field the router did not print read as the property's DefaultValue (or the type
// default). In 5.0 it reads Absent. Two kinds of caller code change meaning:
//   SOMETIMES — the router prints the field on some rows and not on others (mangle passthrough: on mark-* rules,
//               not on jump/return/accept). An expected row in a merge that assigns the default to a row of the
//               silent kind never equals the loaded one again, so every run updates it (migration B7 —
//               MikrotikManager's shaper would have rewritten every jump rule).
//   NEVER     — the router prints the field on no row it holds. Every 4.x read got the DefaultValue; 5.0 reads
//               Absent (migration B3). Either the field is conditional and the lab has no row of the printing
//               kind, or this RouterOS version does not have the field (renamed or removed).
// ALWAYS fields are safe: the default was never what a read returned.
//
// For a SOMETIMES field the probe names the fields whose values separate the printing rows from the silent ones
// (a perfect split only), which is usually the rule itself (action=jump → no passthrough).
//
// WHAT IT DOES NOT PROVE. Only rows the lab routers hold: a menu with no rows says nothing, and a SOMETIMES field
// can look ALWAYS when the lab holds rows of one kind only. It reads over the binary API, with the mapper's own
// load command (LoadCommand, detail, .proplist) sent raw so that the printed words are visible.
//
// Values are printed only for the discriminating fields, and never for /queue/tree (real customer data on the
// lab router) or secret-looking fields.
//
// Environment:
//   TIK4NET_DV_ROUTER  "lab" (default) or "romonTarget" — the second lab router from App.config (RouterOS 6)
//   TIK4NET_DV_OUT     report file (default: TestResults/v5/defaults-<router>.txt under the solution)
//
// [Ignore] keeps it out of the matrix — run via --filter EntityDefaultValueProbe with the attribute removed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("DefaultValue audit — reads every entity menu of a live router over the API. Remove the attribute to run.")]
    [TestClass]
    public class EntityDefaultValueProbe
    {
        private static readonly HashSet<string> VerbPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/ping", "/tool/romon/ping", "/tool/torch", "/tool/traceroute", "/interface/monitor-traffic",
        };

        private static readonly HashSet<string> PrivatePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/queue/tree",
        };

        [TestMethod]
        public void AuditDefaultValuesAgainstPrintedRows()
        {
            string routerKind = Environment.GetEnvironmentVariable("TIK4NET_DV_ROUTER") ?? "lab";
            bool romonTarget = string.Equals(routerKind, "romonTarget", StringComparison.OrdinalIgnoreCase);

            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<TikEntityAttribute>() })
                .Where(x => x.Attr != null && !x.Type.IsAbstract && x.Attr.LoadCommand != "")
                .Where(x => !VerbPaths.Contains(x.Attr.EntityPath) && !x.Attr.EntityPath.EndsWith("/monitor", StringComparison.Ordinal))
                .OrderBy(x => x.Attr.EntityPath, StringComparer.Ordinal)
                .ThenBy(x => x.Type.Name, StringComparer.Ordinal)
                .ToList();

            string outPath = Environment.GetEnvironmentVariable("TIK4NET_DV_OUT")
                ?? Path.Combine(SolutionDir(), "TestResults", "v5", $"defaults-{routerKind}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var always = new StringBuilder();
            var sometimes = new StringBuilder();
            var never = new StringBuilder();
            var noRows = new StringBuilder();
            var errors = new StringBuilder();
            int nAlways = 0, nSometimes = 0, nNever = 0, nNoRows = 0, nEntities = 0;
            string version;

            using (var connection = Open(romonTarget))
            {
                version = connection.LoadSingle<tik4net.Objects.System.SystemResource>().Version ?? "?";
                foreach (var entity in entities)
                {
                    var metadata = Metadata(entity.Type);
                    var withDefault = metadata.Properties.Where(p => p.DefaultValue != null).ToList();
                    if (withDefault.Count == 0)
                        continue;
                    nEntities++;
                    string label = $"{metadata.EntityPath} ({entity.Type.Name})";

                    List<Dictionary<string, string>> rows;
                    try { rows = LoadRaw(connection, metadata); }
                    catch (Exception ex)
                    {
                        errors.AppendLine($"{label}: {ex.GetType().Name}: {FirstLine(ex.Message)}");
                        continue;
                    }
                    if (rows.Count == 0)
                    {
                        nNoRows += withDefault.Count;
                        noRows.AppendLine($"{label}: {withDefault.Count} field(s)");
                        continue;
                    }

                    bool hide = PrivatePaths.Contains(metadata.EntityPath);
                    foreach (var p in withDefault)
                    {
                        var names = new[] { p.FieldName }.Concat(p.AlternateNames).ToArray();
                        var printing = rows.Where(r => names.Any(r.ContainsKey)).ToList();
                        var silent = rows.Where(r => !names.Any(r.ContainsKey)).ToList();
                        int sameAsDefault = printing.Count(r => names.Where(r.ContainsKey).Select(n => r[n]).First() == p.DefaultValue);
                        string line = $"{label} {p.FieldName}{(p.IsReadOnly ? " [ro]" : "")} default={Quote(p.DefaultValue)}: "
                            + $"printed {printing.Count}/{rows.Count}, ={Quote(p.DefaultValue)} on {sameAsDefault}";

                        if (silent.Count == 0)
                        {
                            nAlways++;
                            always.AppendLine(line);
                        }
                        else if (printing.Count == 0)
                        {
                            nNever++;
                            never.AppendLine(line);
                        }
                        else
                        {
                            nSometimes++;
                            sometimes.AppendLine(line);
                            foreach (string split in Discriminators(printing, silent, names, hide))
                                sometimes.AppendLine("    " + split);
                        }
                    }
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"# DefaultValue audit — router={routerKind}, RouterOS {version} — {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"# entities with a DefaultValue: {nEntities}; fields: ALWAYS {nAlways}, SOMETIMES {nSometimes}, NEVER {nNever}, NO-ROWS {nNoRows}");
            report.AppendLine();
            report.AppendLine("## SOMETIMES — printed on some rows only (B7: an expected row assigning the default never matches a silent row)");
            report.Append(sometimes);
            report.AppendLine();
            report.AppendLine("## NEVER — printed on no row (B3: 4.x read the default, 5.0 reads Absent)");
            report.Append(never);
            report.AppendLine();
            report.AppendLine("## NO-ROWS — menu empty on this router, not audited");
            report.Append(noRows);
            report.AppendLine();
            report.AppendLine("## ERRORS");
            report.Append(errors);
            report.AppendLine();
            report.AppendLine("## ALWAYS — printed on every row (safe)");
            report.Append(always);
            File.WriteAllText(outPath, report.ToString());
            Console.WriteLine($"ALWAYS {nAlways}, SOMETIMES {nSometimes}, NEVER {nNever}, NO-ROWS {nNoRows} → {outPath}");
        }

        // The fields whose values split the printing rows from the silent ones exactly: every value of the field seen on
        // a printing row is absent from the silent rows (a field missing from a row counts as the value "<none>").
        private static IEnumerable<string> Discriminators(List<Dictionary<string, string>> printing, List<Dictionary<string, string>> silent,
            string[] self, bool hide)
        {
            var fields = printing.Concat(silent).SelectMany(r => r.Keys).Distinct()
                .Where(f => f != TikSpecialProperties.Id && !self.Contains(f) && !IsSecret(f));
            foreach (string f in fields.OrderBy(f => f, StringComparer.Ordinal))
            {
                var inPrinting = new HashSet<string>(printing.Select(r => r.TryGetValue(f, out string v) ? v : "<none>"));
                var inSilent = new HashSet<string>(silent.Select(r => r.TryGetValue(f, out string v) ? v : "<none>"));
                if (inPrinting.Count + inSilent.Count > 12 || inPrinting.Overlaps(inSilent))
                    continue;
                yield return hide
                    ? $"split by {f}"
                    : $"split by {f}: printed when {string.Join("|", inPrinting.OrderBy(v => v))}; silent when {string.Join("|", inSilent.OrderBy(v => v))}";
            }
        }

        private static List<Dictionary<string, string>> LoadRaw(ITikConnection connection, TikEntityMetadata metadata)
        {
            // The mapper's own load over the API (TikConnectionExtensions.CreateLoadCommandWithFilter): the CLI-only
            // markers are dropped by the API transport anyway.
            var command = connection.CreateCommand(metadata.EntityPath + metadata.LoadCommand, metadata.LoadDefaultParameterFormat);
            if (metadata.IncludeDetails)
                command.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            if (metadata.IncludeProplist)
                command.AddParameter(TikSpecialProperties.Proplist, string.Join(",", metadata.Properties
                    .SelectMany(prop => new[] { prop.FieldName }.Concat(prop.AlternateNames)).ToArray()), TikCommandParameterFormat.NameValue);
            return command.ExecuteList().Select(s => s.Words.ToDictionary(w => w.Key, w => w.Value)).ToList();
        }

        private static ITikConnection Open(bool romonTarget)
        {
            if (!romonTarget)
                return TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);
            return new TikConnectionSetup(
                    TikRouterAddress.FromHost(ConfigurationManager.AppSettings["romonTargetHost"]),
                    ConfigurationManager.AppSettings["romonTargetUser"],
                    ConfigurationManager.AppSettings["romonTargetPass"] ?? "")
                .Create(TikConnectionType.Api);
        }

        private static TikEntityMetadata Metadata(Type type)
            => (TikEntityMetadata)typeof(TikEntityMetadataCache).GetMethod(nameof(TikEntityMetadataCache.GetMetadata))
                .MakeGenericMethod(type).Invoke(null, null);

        private static string Quote(string value) => "\"" + value + "\"";

        private static bool IsSecret(string field)
            => field.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0
            || field.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string FirstLine(string text)
        {
            int nl = text.IndexOfAny(new[] { '\r', '\n' });
            return nl < 0 ? text : text.Substring(0, nl);
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
