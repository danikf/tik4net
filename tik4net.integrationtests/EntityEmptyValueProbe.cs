// EntityEmptyValueProbe.cs — probe: what does each transport deliver for a field that is EMPTY? Validation run V4
// of the 5.0 entity value model (TikValue<T>).
//
// WHY THIS EXISTS. The model reads a field the row lacks as Absent and a field printed empty as Present(""), and
// on a value type an empty value is either a presence flag (Present(true)) or Unparsed(""). That only means the
// same thing on every transport if every transport delivers the same shape. V2 found that it does not for
// comment (WinBox native and the RouterOS 6 CLI print '' where the API omits the field); this measures it on
// rows whose state is known, instead of on whatever the lab router happens to hold:
//   * a field never set                         (comment on a new row)
//   * a field set, then cleared to ""            (comment="x", then comment="")
//   * a list field left empty                    (/interface/list include, exclude)
//   * a presence flag, set and not set           (/routing/table fib — RouterOS 7 only)
//
// Read at the level the mapper reads — a portable ITikCommand print with 'detail', one row per sentence — so
// "<null>" = the transport did not deliver the word and "''" = it delivered it empty. (The integration project is
// not nullable-annotated, so a probe entity could not tell the two apart.) Rows are created over the API with
// commands, named t4n-v4-*, and removed in finally.
//
// Environment: TIK4NET_V4_TRANSPORTS (comma list, default every transport), TIK4NET_V2_ROUTER ("lab" or
// "romonTarget", shared with the V2 probe), TIK4NET_V4_OUT (report path).
//
// [Ignore] keeps it out of the matrix — run via --filter EntityEmptyValueProbe with the attribute removed.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using tik4net;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    [Ignore("V4 empty-value sweep — creates t4n-v4-* rows on a live router and reads them on every transport. Remove the attribute to run.")]
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class EntityEmptyValueProbe : LockedTestBase
    {
        [TestMethod]
        public void SweepEmptyValuesOnEveryTransport()
        {
            string routerKind = Environment.GetEnvironmentVariable("TIK4NET_V2_ROUTER") ?? "lab";
            bool romonTarget = string.Equals(routerKind, "romonTarget", StringComparison.OrdinalIgnoreCase);
            string list = Environment.GetEnvironmentVariable("TIK4NET_V4_TRANSPORTS");
            var transports = (!string.IsNullOrEmpty(list)
                    ? list.Split(',').Select(s => (TikConnectionType)Enum.Parse(typeof(TikConnectionType), s.Trim(), true))
                    : Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>())
                .Where(t => !(romonTarget && (IsMacLayer(t) || t == TikConnectionType.Rest || t == TikConnectionType.RestSsl)))
                .ToList();

            var report = new StringBuilder();
            report.AppendLine($"# V4 empty-value sweep — router={routerKind} — {DateTime.Now:yyyy-MM-dd HH:mm}");
            string tag = Guid.NewGuid().ToString("N").Substring(0, 6);

            using (var api = Open(TikConnectionType.Api, romonTarget))
            {
                report.AppendLine("# RouterOS " + api.LoadSingle<tik4net.Objects.System.SystemResource>().Version);

                string cleared = null, never = null, fib = null, nofib = null;
                bool hasRoutingTable = !romonTarget;
                try
                {
                    cleared = api.CreateCommandAndParameters("/interface/list/add", "name", $"t4n-v4-cleared-{tag}", "comment", "x").ExecuteScalar();
                    never = api.CreateCommandAndParameters("/interface/list/add", "name", $"t4n-v4-never-{tag}").ExecuteScalar();
                    // Clear the comment to "" with a set, as a caller would.
                    api.CreateCommandAndParameters("/interface/list/set", ".id", cleared, "comment", "").ExecuteNonQuery();

                    if (hasRoutingTable)
                    {
                        fib = api.CreateCommandAndParameters("/routing/table/add", "name", $"t4n-v4-fib-{tag}", "fib", "").ExecuteScalar();
                        nofib = api.CreateCommandAndParameters("/routing/table/add", "name", $"t4n-v4-nofib-{tag}").ExecuteScalar();
                    }

                    foreach (var transport in transports)
                    {
                        report.AppendLine();
                        report.AppendLine("## " + transport);
                        try
                        {
                            using (var conn = transport == TikConnectionType.Api ? null : Open(transport, romonTarget))
                            {
                                var c = conn ?? api;
                                Report(report, c, "/interface/list", tag, "comment", "include", "exclude");
                                if (hasRoutingTable)
                                    Report(report, c, "/routing/table", tag, "fib", "comment");
                            }
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine($"ERROR {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                        }
                    }
                }
                finally
                {
                    foreach (var (path, id) in new[] { ("/interface/list", cleared), ("/interface/list", never),
                                                         ("/routing/table", fib), ("/routing/table", nofib) })
                    {
                        if (id == null) continue;
                        try { api.CreateCommandAndParameters(path + "/remove", ".id", id).ExecuteNonQuery(); }
                        catch (Exception ex) { report.AppendLine("CLEANUP " + ex.Message); }
                    }
                }
            }

            string outPath = Environment.GetEnvironmentVariable("TIK4NET_V4_OUT")
                ?? Path.Combine(SolutionDir(), "TestResults", "v4", $"empty-{routerKind}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine(report.ToString());
        }

        private static void Report(StringBuilder report, ITikConnection c, string path, string tag, params string[] fields)
        {
            var cmd = c.CreateCommand(path + "/print");
            cmd.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            foreach (var row in cmd.ExecuteList()
                         .Where(r => (r.GetResponseFieldOrDefault("name", "") ?? "").EndsWith(tag))
                         .OrderBy(r => r.GetResponseFieldOrDefault("name", "")))
            {
                string name = row.GetResponseFieldOrDefault("name", "").Replace("-" + tag, "");
                report.AppendLine(name + ": " + string.Join(" ", fields.Select(f => f + "=" + Show(row.GetResponseFieldOrDefault(f, null)))));
            }
        }

        private static string Show(string value) => value == null ? "<null>" : "'" + value + "'";

        private static ITikConnection Open(TikConnectionType type, bool romonTarget)
        {
            if (!romonTarget)
                return TestBase.LabSetup(type).Create(type);
            return new TikConnectionSetup(
                    TikRouterAddress.FromHost(LabConfig.Get("romonTargetHost")),
                    LabConfig.Get("romonTargetUser"),
                    LabConfig.Get("romonTargetPass") ?? "")
                { AllowInvalidCertificate = true }
                .Create(type);
        }

        private static bool IsMacLayer(TikConnectionType t)
            => t == TikConnectionType.MacTelnet || t == TikConnectionType.WinboxCliMac || t == TikConnectionType.WinboxNativeMac;

        private static string SolutionDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "tik4net.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
