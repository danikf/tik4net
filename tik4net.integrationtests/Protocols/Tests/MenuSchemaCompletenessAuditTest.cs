using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Whether <see cref="TikMenuSchema.ReadableFields"/> — the names the router's <c>get value-name=</c> takes — covers
    /// every field the binary API prints, for every menu an entity maps. A filter on a field the menu does not have is
    /// refused on that list, so a field the API prints and the list lacks would make a valid filter throw.
    /// </summary>
    /// <remarks>
    /// Read-only: it prints each menu over the API and describes it over the same connection (RouterOS 7) or over Telnet
    /// (RouterOS 6, which has no <c>/console/inspect</c>). A menu with no rows prints no field names and is counted as
    /// unmeasured, not as agreeing. Run it with <c>TIK4NET_SCHEMA_AUDIT=1</c> against each lab router
    /// (<c>-Router chr2</c>, <c>-Router chr3</c>); the report goes to the catalog dump directory.
    /// </remarks>
    [TestClass]
    public class MenuSchemaCompletenessAuditTest
    {
        // Menus whose print needs inputs, or runs an action, or is too large to be worth it.
        private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/log", "/ping", "/tool/ping", "/tool/torch", "/tool/profile", "/tool/traceroute",
            "/tool/bandwidth-test", "/tool/flood-ping", "/tool/ip-scan", "/tool/wol",
            "/interface/monitor-traffic", "/interface/ethernet/monitor", "/interface/pppoe-client/monitor",
            "/system/reboot", "/system/shutdown", "/system/reset-configuration", "/system/script/run",
            "/file", "/queue/tree", "/ip/firewall/connection",
        };

        private static IEnumerable<string> EntityPaths()
        {
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Type t in typeof(TikEntityAttribute).Assembly.GetTypes())
            {
                var ea = t.GetCustomAttribute<TikEntityAttribute>();
                if (ea == null || string.IsNullOrEmpty(ea.EntityPath) || ea.LoadCommand != "/print") continue;   // a print with inputs (ping, torch) needs them
                string p = ea.EntityPath.StartsWith("/") ? ea.EntityPath : "/" + ea.EntityPath;
                if (!Skip.Contains(p)) paths.Add(p);
            }
            return paths;
        }

        [TestMethod]
        public void DumpOneMenu()
        {
            string path = Environment.GetEnvironmentVariable("TIK4NET_SCHEMA_DUMP");
            if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set TIK4NET_SCHEMA_DUMP=/ip/route.");
            using (var telnet = TestBase.LabSetup(TikConnectionType.Telnet).Create(TikConnectionType.Telnet))
            {
                var schema = telnet.DescribeMenu(path);
                Console.WriteLine("SOURCE " + schema.Source);
                Console.WriteLine("COMMANDS " + string.Join(",", schema.Commands));
                Console.WriteLine("READABLE " + (schema.ReadableFields == null ? "null" : string.Join(",", schema.ReadableFields)));
                Console.WriteLine("SET " + (schema.SetArguments == null ? "null" : string.Join(",", schema.SetArguments)));
            }
        }

        [TestMethod]
        public void ReadableFieldsCoverEveryFieldTheApiPrints()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_SCHEMA_AUDIT") != "1")
                Assert.Inconclusive("Set TIK4NET_SCHEMA_AUDIT=1.");

            string dumpDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                LabConfig.Get("catalogDumpDir") ?? @".\.tik4net"));
            Directory.CreateDirectory(dumpDir);

            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            {
                string version = api.CreateCommand("/system/resource/print").ExecuteSingleRow().GetResponseField("version");
                bool v6 = version.StartsWith("6.");
                using (var describer = v6 ? TestBase.LabSetup(TikConnectionType.Telnet).Create(TikConnectionType.Telnet) : null)
                {
                    var describe = describer ?? api;
                    var report = new List<string>();
                    int agreed = 0, missing = 0, noRows = 0, refused = 0, noGet = 0;

                    foreach (string path in EntityPaths())
                    {
                        List<ITikReSentence> rows;
                        try { rows = api.CreateCommand(path + "/print").ExecuteList().ToList(); }
                        catch (Exception ex) { refused++; report.Add("N/A        " + path + "\t" + ex.GetType().Name); continue; }

                        var printed = new SortedSet<string>(rows.SelectMany(r => r.Words.Keys)
                            .Where(k => !k.StartsWith(".", StringComparison.Ordinal)), StringComparer.Ordinal);
                        if (printed.Count == 0) { noRows++; report.Add("NO-ROWS    " + path); continue; }

                        TikMenuSchema schema;
                        try { schema = describe.DescribeMenu(path); }
                        catch (Exception ex) { refused++; report.Add("NO-SCHEMA  " + path + "\t" + ex.GetType().Name + ": " + ex.Message); continue; }

                        if (schema.ReadableFields == null) { noGet++; report.Add("NO-GET     " + path); continue; }
                        var listed = new HashSet<string>(schema.ReadableFields, StringComparer.Ordinal);
                        // The API prints a second spelling of some flags ('dynamic2' beside 'dynamic') that it cannot
                        // filter on ('?dynamic2=false' matches nothing, 7.24.4): not a field the list should carry.
                        var notListed = printed.Where(f => !listed.Contains(f)
                            && !(f.EndsWith("2", StringComparison.Ordinal) && listed.Contains(f.Substring(0, f.Length - 1)))).ToList();
                        if (notListed.Count == 0) { agreed++; report.Add("OK         " + path); }
                        else { missing++; report.Add("NOT-LISTED " + path + "\t" + string.Join(", ", notListed)); }
                    }

                    string header = $"RouterOS {version}, described over {(v6 ? "Telnet (Tab completion)" : "the API (/console/inspect)")}: "
                                    + $"OK={agreed} NOT-LISTED={missing} NO-ROWS={noRows} NO-GET={noGet} N/A={refused}";
                    string file = Path.Combine(dumpDir, "schema-audit-" + version.Split(' ')[0] + ".txt");
                    File.WriteAllLines(file, new[] { header }.Concat(report));
                    Console.WriteLine(header + Environment.NewLine + "report: " + file);
                    Assert.AreEqual(0, missing, header + Environment.NewLine
                        + string.Join(Environment.NewLine, report.Where(l => l.StartsWith("NOT-LISTED"))));
                }
            }
        }
    }
}
