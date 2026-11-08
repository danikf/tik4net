// FirewallMatcherConsistencyTests.cs — the firewall tables declare their shared matchers once each, so this holds
// the copies the same.
//
// FirewallFilter, FirewallRaw, FirewallMangle and FirewallNat each declare the same matchers (src-address, connection-state,
// tcp-flags, limit, …), and RouterOS documents them once for every table. Three copies drift: a matcher added to
// one table and not the others, or the same matcher Negatable in one and plain in another. Both have happened.
// A base class would remove the copies at the price of a public type change; this test removes the drift instead.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.unittests.Objects
{
    [TestClass]
    public class FirewallMatcherConsistencyTests
    {
        private static readonly Type[] Tables =
            { typeof(FirewallFilter), typeof(FirewallRaw), typeof(FirewallMangle), typeof(FirewallNat) };

        /// <summary>
        /// What the router does NOT offer on each table, from its own completion of <c>/ip/firewall/&lt;table&gt; add </c>
        /// on 7.25.1 (the <c>mikrotik_cli_complete</c> tool). Raw runs before connection tracking, so it has no
        /// <c>connection-*</c> matchers; mangle's <c>new-*</c>, <c>passthrough</c>, <c>route-dst</c> and <c>sniff-*</c>
        /// belong to its own actions, <c>reject-with</c> to filter's, and <c>to-addresses</c>/<c>to-ports</c> (with
        /// <c>randomise-ports</c>, <c>same-not-by-dst</c> and the three <c>socks*</c> fields) to NAT's,
        /// which has no <c>connection-state</c>, <c>connection-nat-state</c>, <c>p2p</c>, <c>tcp-flags</c> or <c>tls-host</c>
        /// either. Any
        /// other field one table declares the others must declare too.
        /// </summary>
        private static readonly string[] MangleActionFields =
        {
            "new-connection-mark", "new-dscp", "new-mss", "new-packet-mark", "new-priority", "new-routing-mark", "new-ttl",
            "passthrough", "route-dst", "sniff-id", "sniff-target", "sniff-target-port",
        };

        private static readonly string[] NatActionFields =
        {
            "randomise-ports", "same-not-by-dst", "socks5-port", "socks5-server", "socksify-service", "to-addresses", "to-ports",
        };

        private static readonly Dictionary<Type, string[]> NotOffered = new Dictionary<Type, string[]>
        {
            [typeof(FirewallFilter)] = MangleActionFields.Concat(NatActionFields).ToArray(),
            [typeof(FirewallRaw)] = MangleActionFields.Concat(NatActionFields).Concat(new[]
            {
                "connection-bytes", "connection-limit", "connection-mark", "connection-nat-state", "connection-rate",
                "connection-state", "connection-type", "layer7-protocol", "p2p", "realm", "reject-with", "routing-mark",
            }).ToArray(),
            [typeof(FirewallMangle)] = NatActionFields.Concat(new[] { "reject-with" }).ToArray(),
            [typeof(FirewallNat)] = MangleActionFields.Concat(new[]
            {
                "connection-nat-state", "connection-state", "p2p", "reject-with", "tcp-flags", "tls-host",
            }).ToArray(),
        };

        /// <summary>Fields each table types on its own: its action vocabulary.</summary>
        private static readonly HashSet<string> OwnTypes = new HashSet<string>(StringComparer.Ordinal) { "action" };

        private static Dictionary<string, PropertyInfo> Fields(Type table)
            => table.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Property: p, Attribute: p.GetCustomAttribute<TikPropertyAttribute>()))
                .Where(x => x.Attribute != null)
                .ToDictionary(x => x.Attribute!.FieldName, x => x.Property, StringComparer.Ordinal);

        [TestMethod]
        public void EveryTableDeclaresEveryFieldTheRouterOffersOnIt()
        {
            var byTable = Tables.ToDictionary(t => t, Fields);
            var declaredSomewhere = new SortedSet<string>(byTable.Values.SelectMany(f => f.Keys), StringComparer.Ordinal);
            var problems = new List<string>();
            foreach (var table in Tables)
            {
                var notOffered = new HashSet<string>(NotOffered[table], StringComparer.Ordinal);
                foreach (string field in declaredSomewhere)
                {
                    if (byTable[table].ContainsKey(field) || notOffered.Contains(field)) continue;
                    problems.Add($"{table.Name} lacks {field}, which "
                        + string.Join(" and ", Tables.Where(t => byTable[t].ContainsKey(field)).Select(t => t.Name))
                        + " declare(s) — declare it, or list it in NotOffered if the router does not offer it there");
                }
                foreach (string listed in notOffered.Where(byTable[table].ContainsKey))
                    problems.Add($"{table.Name} declares {listed}, which NotOffered says the router does not offer there");
            }
            Assert.AreEqual(0, problems.Count, "\n" + string.Join("\n", problems));
        }

        [TestMethod]
        public void ASharedMatcherIsDeclaredTheSameInEveryTable()
        {
            var byTable = Tables.ToDictionary(t => t, Fields);
            var problems = new List<string>();
            foreach (string field in byTable.Values.SelectMany(f => f.Keys).Distinct().OrderBy(f => f, StringComparer.Ordinal))
            {
                var declared = Tables.Where(t => byTable[t].ContainsKey(field)).Select(t => (Table: t, Property: byTable[t][field])).ToList();
                if (declared.Count < 2) continue;
                var first = declared[0];
                foreach (var other in declared.Skip(1))
                {
                    string where = $"{field}: {first.Table.Name} and {other.Table.Name}";
                    if (other.Property.Name != first.Property.Name)
                        problems.Add($"{where} name it {first.Property.Name} / {other.Property.Name}");
                    if (!OwnTypes.Contains(field) && other.Property.PropertyType != first.Property.PropertyType)
                        problems.Add($"{where} type it {TypeName(first.Property.PropertyType)} / {TypeName(other.Property.PropertyType)}");
                    foreach (var (name, a, b) in AttributeDifferences(first.Property, other.Property))
                        problems.Add($"{where} differ in [TikProperty] {name}: {a} / {b}");
                }
            }
            Assert.AreEqual(0, problems.Count, "\n" + string.Join("\n", problems));
        }

        [TestMethod]
        public void ASharedMatcherIsDocumentedTheSameInEveryTable()
        {
            var docs = XmlDocs();
            var byTable = Tables.ToDictionary(t => t, Fields);
            var problems = new List<string>();
            foreach (string field in byTable.Values.SelectMany(f => f.Keys).Distinct().OrderBy(f => f, StringComparer.Ordinal))
            {
                if (OwnTypes.Contains(field)) continue;
                var declared = Tables.Where(t => byTable[t].ContainsKey(field))
                    .Select(t => (Table: t, Summary: Summary(docs, byTable[t][field]))).ToList();
                if (declared.Count < 2) continue;
                foreach (var other in declared.Skip(1))
                    if (other.Summary != declared[0].Summary)
                        problems.Add($"{field}: {declared[0].Table.Name} \"{declared[0].Summary}\"\n"
                            + $"    {new string(' ', field.Length)}{other.Table.Name} \"{other.Summary}\"");
            }
            Assert.AreEqual(0, problems.Count, "\n" + string.Join("\n", problems));
        }

        private static IEnumerable<(string Name, object? A, object? B)> AttributeDifferences(PropertyInfo a, PropertyInfo b)
        {
            var aa = a.GetCustomAttribute<TikPropertyAttribute>()!;
            var ba = b.GetCustomAttribute<TikPropertyAttribute>()!;
            foreach (var p in typeof(TikPropertyAttribute).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.Name == nameof(Attribute.TypeId)) continue;
                object? av = p.GetValue(aa), bv = p.GetValue(ba);
                if (av is string[] al && bv is string[] bl ? !al.SequenceEqual(bl) : !Equals(av, bv))
                    yield return (p.Name, Show(av), Show(bv));
            }
        }

        private static object Show(object? v) => v is string[] l ? "[" + string.Join(",", l) + "]" : v ?? "null";

        private static string TypeName(Type t)
            => t.IsGenericType
                ? t.Name.Substring(0, t.Name.IndexOf('`')) + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">"
                : t.Name;

        private static Dictionary<string, string> XmlDocs()
        {
            string path = Path.ChangeExtension(typeof(FirewallFilter).Assembly.Location, ".xml");
            Assert.IsTrue(File.Exists(path), "the tik4net.objects XML documentation is not beside the assembly: " + path);
            return XDocument.Load(path).Descendants("member")
                .Where(m => m.Attribute("name") != null)
                .ToDictionary(m => m.Attribute("name")!.Value,
                    m => string.Join(" ", (m.Element("summary")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        }

        private static string Summary(Dictionary<string, string> docs, PropertyInfo p)
            => docs.TryGetValue("P:" + p.DeclaringType!.FullName!.Replace('+', '.') + "." + p.Name, out var s) ? s : "";
    }
}
