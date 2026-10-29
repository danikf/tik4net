#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T4 of the WinBox + MAC layer review: which shipped aliases are redundant on the stored catalogs — an
    /// informational report, never a gate.
    /// </summary>
    /// <remarks>
    /// For every shipped path alias and every rename in <c>ShippedFieldAliases</c>, asks each catalog whether the API
    /// name already resolves verbatim to what the alias resolves to. An alias that does on every catalog is a candidate
    /// for removal — a candidate only: removing one also takes a full native leg on the version it was written for.
    /// Synthetic, fallback, paired, derived and numflag entries are not renames and are not assessed.
    /// <para>Runs only with <c>TIK4NET_CATALOG_REPORT</c> set to a catalog cache directory (the one a native connection
    /// fills, <c>CatalogCachePath</c>: <c>lists/&lt;router&gt;.list</c> plus <c>plugins/</c>), so CI never sees it.
    /// The report goes to the test output.</para>
    /// </remarks>
    [TestClass]
    public class WinboxAliasRedundancyReport
    {
        public TestContext TestContext { get; set; } = null!;

        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

        [TestMethod]
        public void ReportRedundantShippedAliases()
        {
            string? cache = Environment.GetEnvironmentVariable("TIK4NET_CATALOG_REPORT");
            if (string.IsNullOrEmpty(cache) || !Directory.Exists(Path.Combine(cache, "lists")))
                Assert.Inconclusive("Set TIK4NET_CATALOG_REPORT to a catalog cache directory to run this report.");

            var catalogs = new List<(string name, WinboxJgCatalog catalog)>();
            foreach (string list in Directory.GetFiles(Path.Combine(cache!, "lists"), "*.list").OrderBy(f => f, StringComparer.Ordinal))
            {
                var plugins = WinboxJgCatalog.ParseRememberedList(File.ReadAllText(list));
                var catalog = WinboxJgCatalog.Assemble(plugins,
                    p => { string f = Path.Combine(cache!, "plugins", p.Unique); return File.Exists(f) ? File.ReadAllText(f) : null; },
                    _ => null, (_, __) => { }, _ => { }, out bool complete);
                string roteros = plugins.FirstOrDefault(p => p.Name == "roteros.jg").Unique ?? "?";
                catalogs.Add((Path.GetFileNameWithoutExtension(list) + " (" + roteros + (complete ? "" : ", INCOMPLETE") + ")", catalog));
            }

            var verdicts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void Note(string item, string verdict)
            {
                if (!verdicts.TryGetValue(item, out var v)) verdicts[item] = v = new List<string>();
                v.Add(verdict);
            }

            var shipped = (IDictionary)typeof(WinboxHandlerMap).GetField("ShippedAlias", Statics)!.GetValue(null)!;
            var older = (IDictionary)typeof(WinboxHandlerMap).GetField("OlderCatalogAlias", Statics)!.GetValue(null)!;
            var fieldAliases = (IDictionary)typeof(WinboxFieldResolver).GetField("ShippedFieldAliases", Statics)!.GetValue(null)!;

            foreach (var (name, catalog) in catalogs)
            {
                var derived = catalog.GetDerivedPaths();
                var map = new WinboxHandlerMap();
                map.SetDerivedPaths(derived);

                foreach (DictionaryEntry e in shipped)
                    Note("path " + e.Key + " -> " + e.Value, PathVerdict(derived, (string)e.Key, new[] { (string)e.Value! }));
                foreach (DictionaryEntry e in older)
                    Note("older-path " + e.Key + " -> " + string.Join("|", (string[])e.Value!), PathVerdict(derived, (string)e.Key, (string[])e.Value!));

                foreach (DictionaryEntry e in fieldAliases)
                {
                    string path = (string)e.Key;
                    string? windowKey = map.ResolveDerivedKey(path);
                    WinboxHandler? handler = map.Resolve(path);
                    var window = catalog.GetWindowFields(windowKey);
                    var handlerFields = handler != null ? catalog.GetHandlerFields(handler.Value) : null;
                    WinboxJgField? Find(string n)
                    {
                        string k = WinboxFieldResolver.NormalizeLabel(n);
                        if (window != null && window.TryGetValue(k, out var f)) return f;
                        if (handlerFields != null && handlerFields.TryGetValue(k, out var g)) return g;
                        return null;
                    }

                    object set = e.Value!;
                    var apiToJg = (IEnumerable<KeyValuePair<string, string>>)set.GetType().GetField("ApiToJg")!.GetValue(set)!;
                    var jgToApi = (IEnumerable<KeyValuePair<string, string>>)set.GetType().GetField("JgToApi")!.GetValue(set)!;
                    var keyToApi = (IEnumerable<KeyValuePair<int, string>>)set.GetType().GetField("KeyToApi")!.GetValue(set)!;
                    foreach (var kv in apiToJg)
                        Note("field " + path + " api->jg " + kv.Key + " -> " + kv.Value, FieldVerdict(handler, Find(kv.Value), Find(kv.Key)));
                    foreach (var kv in jgToApi)
                        Note("field " + path + " jg->api " + kv.Key + " -> " + kv.Value, FieldVerdict(handler, Find(kv.Key), Find(kv.Value)));
                    foreach (var kv in keyToApi)
                    {
                        var f = Find(kv.Value);
                        Note("field " + path + " key->api 0x" + kv.Key.ToString("X") + " -> " + kv.Value,
                            handler == null ? "no-handler" : f == null ? "needed" : f.Key == kv.Key ? "redundant" : "api-name-is-another-key");
                    }
                }
            }

            var report = new List<string> { "catalogs: " + string.Join("; ", catalogs.Select(c => c.name)) };
            var groups = verdicts.GroupBy(kv => kv.Value.All(v => v == "redundant") ? "REDUNDANT on every catalog"
                : kv.Value.Any(v => v == "redundant") ? "redundant on some catalogs" : "needed (or inert) everywhere");
            foreach (var g in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                report.Add("== " + g.Key + ": " + g.Count());
                if (g.Key != "needed (or inert) everywhere")
                    report.AddRange(g.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => "  " + kv.Key + "  [" + string.Join(", ", kv.Value) + "]"));
            }
            var tally = verdicts.SelectMany(kv => kv.Value).GroupBy(v => v).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + "=" + g.Count());
            report.Add("verdict tally (item x catalog): " + string.Join(" ", tally));

            foreach (string line in report) TestContext.WriteLine(line);
            string? output = Environment.GetEnvironmentVariable("TIK4NET_CATALOG_REPORT_OUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllLines(output, report);
        }

        private static string PathVerdict(IReadOnlyDictionary<string, WinboxHandler> derived, string apiPath, string[] targets)
        {
            WinboxHandler? viaAlias = targets.Select(t => derived.TryGetValue(t, out var h) ? h : (WinboxHandler?)null)
                .FirstOrDefault(h => h != null);
            bool verbatim = derived.TryGetValue(apiPath, out WinboxHandler direct);
            if (viaAlias == null) return verbatim ? "target-absent, verbatim-resolves" : "inert (target absent)";
            if (!verbatim) return "needed";
            return direct == viaAlias ? "redundant" : "needed (verbatim path is another window)";
        }

        private static string FieldVerdict(WinboxHandler? handler, WinboxJgField? viaLabel, WinboxJgField? verbatim)
        {
            if (handler == null) return "no-handler";
            if (viaLabel == null) return verbatim == null ? "inert (neither name in catalog)" : "label-absent, api-name-present";
            if (verbatim == null) return "needed";
            return verbatim.Key == viaLabel.Key ? "redundant" : "api-name-is-another-field";
        }
    }
}
