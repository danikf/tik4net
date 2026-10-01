// EntityUnparsedWordProbe.cs — probe: when a value does not parse into the property's type, is the word each
// transport keeps the word the API prints? Validation run V3 of the 5.0 entity value model (TikValue<T>).
//
// WHY THIS EXISTS. The model reads such a value as Unparsed(raw) and writes the raw word back unchanged, so the
// word has to be the router's own on every transport — WinBox native in particular renders words from the .jg
// catalog's maps, not from what the API prints. On the lab routers almost every word is one the shipped enums
// know, so the probe forces the case: each enum below knows ONE word and has an Unknown member, over fields whose
// rows use many words. Every other word is then an unknown one, and its kept word (GetUnknownWord) is compared
// against the API's for the same .id. Read-only.
//
// Environment: TIK4NET_V3_TRANSPORTS (comma list, default every non-API transport), TIK4NET_V2_ROUTER ("lab" or
// "romonTarget"), TIK4NET_V3_OUT (report path).
//
// [Ignore] keeps it out of the matrix — run via --filter EntityUnparsedWordProbe with the attribute removed.

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
    [Ignore("V3 unparsed-word sweep — reads a few menus through one-word enums on every transport. Remove the attribute to run.")]
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class EntityUnparsedWordProbe : LockedTestBase
    {
        public enum OneType { [TikEnum("ether")] Ether, [TikEnumUnknown] Unknown = -1 }
        public enum OneKind { [TikEnum("pfifo")] Pfifo, [TikEnumUnknown] Unknown = -1 }
        public enum OneTarget { [TikEnum("memory")] Memory, [TikEnumUnknown] Unknown = -1 }
        public enum OneAction { [TikEnum("accept")] Accept, [TikEnumUnknown] Unknown = -1 }
        public enum OneChain { [TikEnum("input")] Input, [TikEnumUnknown] Unknown = -1 }

        [TikEntity("/interface", IncludeDetails = true, SupportedOperations = TikEntityOperations.None)]
        public class V3Interface
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string Id { get; private set; }
            [TikProperty("type")] public OneType? Type { get; set; }
        }

        [TikEntity("/queue/type", SupportedOperations = TikEntityOperations.None)]
        public class V3QueueType
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string Id { get; private set; }
            [TikProperty("kind")] public OneKind? Kind { get; set; }
        }

        [TikEntity("/system/logging/action", SupportedOperations = TikEntityOperations.None)]
        public class V3LoggingAction
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string Id { get; private set; }
            [TikProperty("target")] public OneTarget? Target { get; set; }
        }

        [TikEntity("/ip/firewall/filter", IncludeDetails = true, SupportedOperations = TikEntityOperations.None)]
        public class V3Filter
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)] public string Id { get; private set; }
            [TikProperty("action")] public OneAction? Action { get; set; }
            [TikProperty("chain")] public OneChain? Chain { get; set; }
        }

        [TestMethod]
        public void SweepUnparsedWordsOnEveryTransport()
        {
            string routerKind = Environment.GetEnvironmentVariable("TIK4NET_V2_ROUTER") ?? "lab";
            bool romonTarget = string.Equals(routerKind, "romonTarget", StringComparison.OrdinalIgnoreCase);
            string list = Environment.GetEnvironmentVariable("TIK4NET_V3_TRANSPORTS");
            var transports = (!string.IsNullOrEmpty(list)
                    ? list.Split(',').Select(s => (TikConnectionType)Enum.Parse(typeof(TikConnectionType), s.Trim(), true))
                    : Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>().Where(t => t != TikConnectionType.Api))
                .Where(t => !(romonTarget && (IsMacLayer(t) || t == TikConnectionType.Rest || t == TikConnectionType.RestSsl)))
                .ToList();

            var report = new StringBuilder();
            report.AppendLine($"# V3 unparsed-word sweep — router={routerKind} — {DateTime.Now:yyyy-MM-dd HH:mm}");

            Dictionary<string, Dictionary<string, string>> reference;
            using (var api = Open(TikConnectionType.Api, romonTarget))
            {
                report.AppendLine("# RouterOS " + api.LoadSingle<tik4net.Objects.System.SystemResource>().Version);
                reference = Read(api);
                report.AppendLine("## Api (reference)");
                foreach (var field in reference.Keys.OrderBy(k => k))
                    report.AppendLine($"{field}: {reference[field].Count} rows, words: "
                                      + string.Join(",", reference[field].Values.Distinct().OrderBy(w => w)));
            }

            foreach (var transport in transports)
            {
                report.AppendLine();
                report.AppendLine("## " + transport);
                try
                {
                    Dictionary<string, Dictionary<string, string>> read;
                    using (var conn = Open(transport, romonTarget))
                        read = Read(conn);

                    foreach (var field in reference.Keys.OrderBy(k => k))
                    {
                        var mine = read.TryGetValue(field, out var m) ? m : new Dictionary<string, string>();
                        int same = 0, missing = 0;
                        var diffs = new List<string>();
                        foreach (var kv in reference[field])
                        {
                            if (!mine.TryGetValue(kv.Key, out string word)) { missing++; continue; }
                            if (word == kv.Value) same++;
                            else diffs.Add($"{kv.Key} api='{kv.Value}' {transport}='{word}'");
                        }
                        report.AppendLine($"{field}: same={same} different={diffs.Count} unpaired={missing}");
                        foreach (var d in diffs.Distinct().Take(20))
                            report.AppendLine("  " + d);
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine($"ERROR {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
            }

            string outPath = Environment.GetEnvironmentVariable("TIK4NET_V3_OUT")
                ?? Path.Combine(SolutionDir(), "TestResults", "v3", $"unparsed-{routerKind}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine(report.ToString());
        }

        // field -> (.id -> the word the entity holds: the known member's word, or the kept unknown word).
        private static Dictionary<string, Dictionary<string, string>> Read(ITikConnection c)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            void Add<T>(string field, IEnumerable<T> rows, Func<T, string> id, Func<T, object> value, string property)
            {
                var words = new Dictionary<string, string>();
                foreach (var row in rows)
                {
                    object v = value(row);
                    words[id(row)] = v == null ? "<null>"
                        : v.ToString() == "Unknown" ? "?" + (row.GetUnknownWord(property) ?? "<no word>")
                        : v.ToString().ToLowerInvariant();
                }
                result[field] = words;
            }
            Add("/interface type", c.LoadAll<V3Interface>(), r => r.Id, r => r.Type, nameof(V3Interface.Type));
            Add("/queue/type kind", c.LoadAll<V3QueueType>(), r => r.Id, r => r.Kind, nameof(V3QueueType.Kind));
            Add("/system/logging/action target", c.LoadAll<V3LoggingAction>(), r => r.Id, r => r.Target, nameof(V3LoggingAction.Target));
            var filter = c.LoadAll<V3Filter>().ToList();
            Add("/ip/firewall/filter action", filter, r => r.Id, r => r.Action, nameof(V3Filter.Action));
            Add("/ip/firewall/filter chain", filter, r => r.Id, r => r.Chain, nameof(V3Filter.Chain));
            return result;
        }

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
