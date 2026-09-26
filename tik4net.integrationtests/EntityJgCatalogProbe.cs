// EntityJgCatalogProbe.cs — probe: what the router's own WinBox catalog (.jg) says about every entity property.
// Audit for the 5.0 entity value model, companion of EntityDefaultValueProbe.
//
// WHY THIS EXISTS. The lab routers hold rows of few kinds, so a sweep of printed rows cannot say which fields apply
// to which kind of row, nor whether a DefaultValue attribute is right. WinBox (and WebFig) must know both to draw a
// window: a field sits in a `deck` pane shown only for some values of a selector (mangle passthrough: the
// mark-* actions), and a new row's box is filled from the field's `def`. The catalog is per RouterOS version, so a
// field the version lacks is not in it at all.
//
// Per property it reports:
//   MISSING      — no field of that API name in this router's catalog: this version lacks it, or no WinBox window
//                  shows it (compare the two routers' reports to tell which).
//   KIND         — the field applies only to rows whose <selector> is one of the listed values; the router leaves
//                  it out of other rows (IfPrintedIn territory in a merge).
//   DEFAULT      — the catalog's def rendered as the API prints it, against the attribute's DefaultValue, both run
//                  through the entity's own converter (so no/false spellings compare equal): MISMATCH, UNSET (the
//                  catalog's def is the "not set" marker), or match (counted only).
//
// It reads the catalog only — no rows. Resolution goes through the WinBox native transport's own resolver, so a
// field the transport cannot map shows as MISSING too; the path-map audit covers that separately.
//
// Environment:
//   TIK4NET_JG_ROUTER  "lab" (default) or "romonTarget" — the second lab router from App.config (RouterOS 6)
//   TIK4NET_JG_OUT     report file (default: TestResults/v5/jg-<router>.txt under the solution)
//
// [Ignore] keeps it out of the matrix — run via --filter EntityJgCatalogProbe with the attribute removed.

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
using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.integrationtests
{
    [Ignore("Catalog audit — reads the WinBox .jg catalog of a live router. Remove the attribute to run.")]
    [TestClass]
    public class EntityJgCatalogProbe
    {
        private static readonly HashSet<string> VerbPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/ping", "/tool/romon/ping", "/tool/torch", "/tool/traceroute", "/interface/monitor-traffic",
        };

        [TestMethod]
        public void AuditEntitiesAgainstTheWinboxCatalog()
        {
            string routerKind = Environment.GetEnvironmentVariable("TIK4NET_JG_ROUTER") ?? "lab";
            bool romonTarget = string.Equals(routerKind, "romonTarget", StringComparison.OrdinalIgnoreCase);

            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<TikEntityAttribute>() })
                .Where(x => x.Attr != null && !x.Type.IsAbstract && x.Attr.LoadCommand != "")
                .Where(x => !VerbPaths.Contains(x.Attr.EntityPath) && !x.Attr.EntityPath.EndsWith("/monitor", StringComparison.Ordinal))
                .OrderBy(x => x.Attr.EntityPath, StringComparer.Ordinal)
                .ThenBy(x => x.Type.Name, StringComparer.Ordinal)
                .ToList();

            string outPath = Environment.GetEnvironmentVariable("TIK4NET_JG_OUT")
                ?? Path.Combine(SolutionDir(), "TestResults", "v5", $"jg-{routerKind}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var missing = new StringBuilder();
            var kind = new StringBuilder();
            var mismatch = new StringBuilder();
            var unset = new StringBuilder();
            var noHandler = new StringBuilder();
            int nFields = 0, nMissing = 0, nKind = 0, nMatch = 0, nMismatch = 0, nUnset = 0, nNoDef = 0, nNoAttr = 0;
            string version;

            using (var connection = (WinboxNativeConnection)Open(romonTarget))
            {
                version = connection.LoadSingle<tik4net.Objects.System.SystemResource>().Version ?? "?";
                foreach (var entity in entities)
                {
                    var metadata = Metadata(entity.Type);
                    string label = $"{metadata.EntityPath} ({entity.Type.Name})";
                    var described = connection.DescribeFields(metadata.EntityPath);
                    if (described == null)
                    {
                        noHandler.AppendLine(label);
                        continue;
                    }
                    var byName = new Dictionary<string, (string ApiName, WinboxJgField Field, Func<object, string> Render)>(StringComparer.Ordinal);
                    foreach (var d in described)
                        if (!byName.ContainsKey(d.ApiName))
                            byName[d.ApiName] = d;
                    var byKey = described.GroupBy(d => d.Field.Key).ToDictionary(g => g.Key, g => g.First());

                    foreach (var p in metadata.Properties.Where(p => p.FieldName != TikSpecialProperties.Id))
                    {
                        nFields++;
                        var names = new[] { p.FieldName }.Concat(p.AlternateNames).ToArray();
                        string hit = names.FirstOrDefault(byName.ContainsKey);
                        string ro = p.IsReadOnly ? " [ro]" : "";
                        if (hit == null)
                        {
                            nMissing++;
                            missing.AppendLine($"{metadata.EntityPath} {p.FieldName}{ro}");
                            continue;
                        }
                        var (_, field, render) = byName[hit];

                        if (field.PaneSelectorKey != 0 && field.PaneValues != null && field.PaneValues.Length > 0)
                        {
                            nKind++;
                            string selectorName = byKey.TryGetValue(field.PaneSelectorKey, out var sel) ? sel.ApiName : "?";
                            var map = sel.Field?.EnumMap;
                            string values = string.Join("|", field.PaneValues.Select(v =>
                                map != null && map.TryGetValue(v, out string l) ? WinboxFieldResolver.NormalizeLabel(l) : v.ToString()));
                            kind.AppendLine($"{label} {p.FieldName}{ro}: only when {selectorName} = {values}");
                        }

                        string declared = entity.Type.GetProperty(p.PropertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetCustomAttribute<TikPropertyAttribute>()?.DefaultValue;
                        if (!field.Def.HasValue) { nNoDef++; continue; }
                        if (declared == null) { nNoAttr++; continue; }
                        object raw = field.WireType == "bool" ? (object)(field.Def.Value != 0) : field.Def.Value;
                        string catalogDefault;
                        try { catalogDefault = render(raw); }
                        catch (Exception ex) { catalogDefault = "<" + ex.GetType().Name + ">"; }
                        if (catalogDefault == null)
                        {
                            nUnset++;
                            unset.AppendLine($"{label} {p.FieldName}{ro}: attribute {Quote(declared)}, catalog def {field.Def} = not set");
                            continue;
                        }
                        if (Canonical(entity.Type, p, declared) == Canonical(entity.Type, p, catalogDefault))
                            nMatch++;
                        else
                        {
                            nMismatch++;
                            mismatch.AppendLine($"{label} {p.FieldName}{ro}: attribute {Quote(declared)}, catalog {Quote(catalogDefault)}");
                        }
                    }
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"# WinBox catalog audit — router={routerKind}, RouterOS {version} — {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"# properties {nFields}: missing {nMissing}; kind-dependent {nKind}; default match {nMatch}, "
                + $"MISMATCH {nMismatch}, catalog-unset {nUnset}, no catalog def {nNoDef}, no attribute {nNoAttr}");
            report.AppendLine();
            report.AppendLine("## DEFAULT MISMATCH — the attribute's DefaultValue is not the catalog's def");
            report.Append(mismatch);
            report.AppendLine();
            report.AppendLine("## DEFAULT UNSET — the catalog's def is the not-set marker; the attribute names a value");
            report.Append(unset);
            report.AppendLine();
            report.AppendLine("## KIND — applies only to some rows (the router leaves it out of the others)");
            report.Append(kind);
            report.AppendLine();
            report.AppendLine("## NO HANDLER — the path is in no WinBox window of this version");
            report.Append(noHandler);
            report.AppendLine();
            report.AppendLine("## MISSING — no field of this name in this version's catalog (path field)");
            report.Append(missing);
            File.WriteAllText(outPath, report.ToString());
            Console.WriteLine(report.ToString().Split('\n')[1] + " → " + outPath);
        }

        // The form the entity writes a value in: through its own converter, so "no" and "false" agree.
        private static string Canonical(Type entityType, TikEntityPropertyAccessor p, string value)
        {
            try
            {
                var fresh = Activator.CreateInstance(entityType, nonPublic: true);
                p.SetEntityValue(fresh, value);
                return p.GetEntityValue(fresh) ?? "";
            }
            catch (Exception) { return value; }
        }

        private static ITikConnection Open(bool romonTarget)
        {
            if (!romonTarget)
                return TestBase.LabSetup(TikConnectionType.WinboxNative).Create(TikConnectionType.WinboxNative);
            return new TikConnectionSetup(
                    TikRouterAddress.FromHost(ConfigurationManager.AppSettings["romonTargetHost"]),
                    ConfigurationManager.AppSettings["romonTargetUser"],
                    ConfigurationManager.AppSettings["romonTargetPass"] ?? "")
                .Create(TikConnectionType.WinboxNative);
        }

        private static TikEntityMetadata Metadata(Type type)
            => (TikEntityMetadata)typeof(TikEntityMetadataCache).GetMethod(nameof(TikEntityMetadataCache.GetMetadata))
                .MakeGenericMethod(type).Invoke(null, null);

        private static string Quote(string value) => "\"" + value + "\"";

        private static string SolutionDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tik4net.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
