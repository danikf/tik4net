#nullable enable

using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.Benchmarks
{
    /// <summary>The same <see cref="FirewallFilter"/> row <see cref="MapperBenchmarks"/> loads, as field → wire value.</summary>
    internal static class FirewallRows
    {
        internal static Dictionary<string, string> Prototype()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<FirewallFilter>();
            var prototype = new FirewallFilter
            {
                Action = FirewallFilter.ActionType.Drop,
                Chain = "forward",
                Comment = "benchmark rule",
                ConnectionState = new TikValueList<FirewallConnectionState>(FirewallConnectionState.Established, FirewallConnectionState.Related),
                ConnectionLimit = "32,32",
                DstAddress = "10.0.0.0/8",
                DstPort = new TikValueList<TikPortRange>(443),
                InInterface = "ether1",
                Protocol = "tcp",
                SrcAddress = "192.168.88.0/24",
            };
            var words = metadata.Properties
                .Select(p => new { p.FieldName, Value = p.GetEntityValue(prototype) })
                .Where(p => p.Value != null)
                .ToDictionary(p => p.FieldName, p => p.Value!);
            // What a real print also carries and the prototype leaves null: counters and flags.
            words["bytes"] = "123456789";
            words["packets"] = "98765";
            words["dynamic"] = "false";
            words["invalid"] = "false";
            words["disabled"] = "false";
            return words;
        }

        internal static List<Dictionary<string, string>> Rows(int count)
        {
            var proto = Prototype();
            var rows = new List<Dictionary<string, string>>(count);
            for (int i = 0; i < count; i++)
            {
                var row = new Dictionary<string, string>(proto) { [".id"] = "*" + i.ToString("X") };
                rows.Add(row);
            }
            return rows;
        }
    }
}
