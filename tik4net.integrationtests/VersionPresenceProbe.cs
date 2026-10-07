// VersionPresenceProbe.cs — probe: which mapped menus and fields does each lab router have?
//
// For every [TikEntity] menu and every mapped [TikProperty], asks each lab router's own grammar (DescribeMenu over the
// WinBox CLI: /console/inspect on RouterOS 7, Tab completion on 6) whether the menu exists and whether it names the
// field (readable, or an add/set argument; FieldName or any AlternateNames). Prints only what differs between the
// routers: the evidence for the MinRouterOs metadata, which says the oldest release a menu or field is in.
//
// What it cannot tell apart: a menu missing because of the version and one missing because a package is not installed
// (wifi, container, iot) — read the lines against each router's /system/package. A field that is a valueless flag or
// only printed (never readable by 'get') can look absent; the line says which lists were asked.
//
// [Ignore] keeps it out of the matrix — comment it out and run via --filter VersionPresenceProbe. The grammar is cached
// on disk per build, so a second run is fast.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("Version presence sweep — describes every mapped menu on every lab router. Remove the attribute to run.")]
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class VersionPresenceProbe : LockedTestBase
    {
        private static readonly string[] Profiles = { "", "chr3", "chr2" };

        private sealed class Presence
        {
            public string Version;
            public readonly Dictionary<string, HashSet<string>> Menus = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        [TestMethod]
        public void SweepEveryMappedMenuAndFieldOnEveryLabRouter()
        {
            var entities = typeof(TikEntityAttribute).Assembly.GetTypes()
                .Select(t => (Type: t, Entity: t.GetCustomAttribute<TikEntityAttribute>()))
                .Where(x => x.Entity != null && x.Entity.EntityPath.StartsWith("/", StringComparison.Ordinal))
                .OrderBy(x => x.Entity.EntityPath, StringComparer.Ordinal)
                .ToList();

            var routers = new List<Presence>();
            foreach (string profile in Profiles)
            {
                var presence = new Presence();
                var setup = new TikConnectionSetup(TikRouterAddress.Parse(LabConfig.GetFor(profile, "host")),
                    LabConfig.GetFor(profile, "user"), LabConfig.GetFor(profile, "pass") ?? "");
                using (var connection = setup.Create(TikConnectionType.WinboxCli))
                {
                    presence.Version = connection.CreateCommand("/system/resource/print").ExecuteScalar("version");
                    foreach (string path in entities.Select(e => e.Entity.EntityPath).Distinct(StringComparer.Ordinal))
                    {
                        try
                        {
                            var schema = connection.DescribeMenu(path);
                            var fields = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var list in new[] { schema.ReadableFields, schema.AddArguments, schema.SetArguments })
                                if (list != null)
                                    fields.UnionWith(list);
                            presence.Menus[path] = fields;
                        }
                        catch (TikNoSuchCommandException) { /* not on this router */ }
                    }
                }
                routers.Add(presence);
                Console.WriteLine($"{(profile.Length == 0 ? "default" : profile)}: RouterOS {presence.Version}, {presence.Menus.Count} menus");
            }

            var report = new StringBuilder();
            string Mark(bool present) => present ? "yes" : " - ";
            report.AppendLine("menu / field | " + string.Join(" | ", routers.Select(r => r.Version)));
            foreach (var (type, entity) in entities)
            {
                string path = entity.EntityPath;
                var menuOn = routers.Select(r => r.Menus.ContainsKey(path)).ToArray();
                if (menuOn.Distinct().Count() > 1)
                    report.AppendLine($"MENU {path} ({type.Name}) | " + string.Join(" | ", menuOn.Select(Mark)));
                if (!menuOn.Any(m => m))
                {
                    report.AppendLine($"NOWHERE {path} ({type.Name})");
                    continue;
                }

                foreach (var property in type.GetProperties())
                {
                    var attr = property.GetCustomAttribute<TikPropertyAttribute>();
                    if (attr == null || attr.FieldName.StartsWith(".", StringComparison.Ordinal))
                        continue;
                    var names = new[] { attr.FieldName }.Concat(attr.AlternateNames ?? new string[0]).ToArray();
                    var fieldOn = routers.Select(r => r.Menus.TryGetValue(path, out var f) ? (bool?)names.Any(f.Contains) : null).ToArray();
                    var known = fieldOn.Where(f => f.HasValue).Select(f => f.Value).Distinct().ToList();
                    if (known.Count > 1 || (known.Count == 1 && !known[0]))
                        report.AppendLine($"FIELD {path} {attr.FieldName} ({type.Name}.{property.Name}) | "
                            + string.Join(" | ", fieldOn.Select(f => f == null ? "no menu" : Mark(f.Value))));
                }
            }

            string file = Path.Combine(Path.GetTempPath(), "tik4net-version-presence.txt");
            File.WriteAllText(file, report.ToString());
            Console.WriteLine(report.ToString());
            Console.WriteLine("written to " + file);
        }

        /// <summary>
        /// The grammar cannot prove a field absent — a field the router prints but does not name to <c>get</c> looks
        /// absent. This reads the rows over the API where a bound says the field is NOT, and reports every field the
        /// rows print anyway: each is a wrong bound. A menu with no rows proves nothing and is listed as such.
        /// </summary>
        [TestMethod]
        public void TheRowsAgreeWithEveryBound()
        {
            var checks = new List<(string Profile, string Path, string Field, string Bound)>();
            foreach (var type in typeof(TikEntityAttribute).Assembly.GetTypes())
            {
                var entity = type.GetCustomAttribute<TikEntityAttribute>();
                if (entity == null)
                    continue;
                foreach (var property in type.GetProperties())
                {
                    var attr = property.GetCustomAttribute<TikPropertyAttribute>();
                    if (attr == null)
                        continue;
                    string bound = attr.MinRouterOs != null ? "Min " + attr.MinRouterOs : attr.MaxRouterOs != null ? "Max " + attr.MaxRouterOs : null;
                    // Where the bound says the field is missing: before a Min, after a Max.
                    string profile = attr.MinRouterOs == "7" ? "chr2" : attr.MinRouterOs == "7.22" ? "chr3"
                        : attr.MaxRouterOs != null ? "" : null;
                    if (profile != null)
                        checks.Add((profile, entity.EntityPath, attr.FieldName, bound));
                }
            }

            var report = new StringBuilder();
            foreach (var router in checks.GroupBy(c => c.Profile))
            {
                var setup = new TikConnectionSetup(TikRouterAddress.Parse(LabConfig.GetFor(router.Key, "host")),
                    LabConfig.GetFor(router.Key, "user"), LabConfig.GetFor(router.Key, "pass") ?? "");
                using (var connection = setup.Create(TikConnectionType.Api))
                {
                    foreach (var menu in router.GroupBy(c => c.Path))
                    {
                        List<ITikReSentence> rows;
                        try
                        {
                            rows = connection.CreateCommand(menu.Key + "/print").ExecuteList().ToList();
                            if (rows.Count == 0 || !menu.Key.StartsWith("/system", StringComparison.Ordinal))
                                rows.AddRange(TryDetail(connection, menu.Key));
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine($"{router.Key} {menu.Key}: no read ({ex.GetType().Name})");
                            continue;
                        }
                        if (rows.Count == 0)
                        {
                            report.AppendLine($"{router.Key} {menu.Key}: no rows - {menu.Count()} bound(s) unproven");
                            continue;
                        }
                        var printed = new HashSet<string>(rows.SelectMany(r => r.Words.Keys), StringComparer.Ordinal);
                        foreach (var check in menu.Where(c => printed.Contains(c.Field)))
                            report.AppendLine($"WRONG {router.Key} {check.Path} {check.Field} ({check.Bound}) - printed by {rows.Count} row(s)");
                    }
                }
            }
            string file = Path.Combine(Path.GetTempPath(), "tik4net-version-rows.txt");
            File.WriteAllText(file, report.ToString());
            Console.WriteLine(report.ToString());
        }

        private static IEnumerable<ITikReSentence> TryDetail(ITikConnection connection, string path)
        {
            try { return connection.CreateCommandAndParameters(path + "/print", "detail", "").ExecuteList().ToList(); }
            catch (TikCommandException) { return Enumerable.Empty<ITikReSentence>(); }
        }
    }
}
