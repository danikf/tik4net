#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T3 of the WinBox + MAC layer review: a golden dump of the shipped alias tables — every map of every
    /// <c>WinboxFieldResolver.ShippedFieldAliases</c> entry, the resolver's other static tables, and the
    /// <see cref="WinboxHandlerMap"/> path tables — sorted, so a rewrite of how they are written can be shown to leave
    /// the table the same.
    /// </summary>
    /// <remarks>
    /// C21 rewrites <c>FieldAliasSet</c> so a symmetric rename is written once, and C20 moves the tables into a partial
    /// file of their own; both claim the table does not change, and <c>Golden/winbox-alias-tables.txt</c> is what
    /// checks it. The dump is by reflection over the static fields, so it follows the tables wherever the partial
    /// files put them. Whether a string map compares case-insensitively is part of the pin (<c>ci</c>/<c>cs</c>).
    /// </remarks>
    [TestClass]
    public class WinboxAliasTableGoldenTests
    {
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        [TestMethod]
        public void ShippedAliasTables_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T3 - WinBox shipped alias tables (static tables of WinboxFieldResolver and WinboxHandlerMap)" };

            var aliases = (IDictionary)typeof(WinboxFieldResolver).GetField("ShippedFieldAliases", Statics)!.GetValue(null)!;
            lines.Add("ShippedFieldAliases count=" + aliases.Count + " " + ComparerOf(aliases));
            foreach (var path in aliases.Keys.Cast<string>().OrderBy(p => p, StringComparer.Ordinal))
            {
                object set = aliases[path]!;
                foreach (var f in set.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
                    lines.Add("ShippedFieldAliases[" + path + "]." + f.Name + " = " + Render(f.GetValue(set), 0));
            }

            foreach (var type in new[] { typeof(WinboxFieldResolver), typeof(WinboxHandlerMap) })
                foreach (var f in type.GetFields(Statics).Where(f => f.IsInitOnly && f.Name != "ShippedFieldAliases")
                                       .OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    object? value = f.GetValue(null);
                    if (value is IDictionary || value is IEnumerable && !(value is string))
                        lines.Add(type.Name + "." + f.Name + " = " + Render(value, 0));
                }

            GoldenFile.AssertMatches("winbox-alias-tables.txt", lines);
        }

        // ── a deterministic rendering of whatever the tables hold ──────────────

        private static string Render(object? value, int depth)
        {
            if (depth > 8) return "…";
            switch (value)
            {
                case null: return "null";
                case string s: return "'" + s + "'";
                case bool b: return b ? "true" : "false";
                case Enum e: return e.ToString();
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                case IDictionary d:
                {
                    var entries = new List<string>();
                    foreach (DictionaryEntry kv in d) entries.Add(Render(kv.Key, depth + 1) + "=" + Render(kv.Value, depth + 1));
                    entries.Sort(StringComparer.Ordinal);
                    return ComparerOf(d) + "{" + string.Join(", ", entries) + "}";
                }
                case Array a when a.Rank == 1 && a.GetType().GetElementType()!.IsPrimitive:
                    return "[" + string.Join(",", a.Cast<object>().Select(x => Render(x, depth + 1))) + "]";
                case IEnumerable seq when IsSet(seq):
                {
                    var items = seq.Cast<object?>().Select(x => Render(x, depth + 1)).ToList();
                    items.Sort(StringComparer.Ordinal);
                    return ComparerOf(seq) + "{" + string.Join(", ", items) + "}";
                }
                case IEnumerable seq:
                    return "[" + string.Join(", ", seq.Cast<object?>().Select(x => Render(x, depth + 1))) + "]";
            }

            Type t = value.GetType();
            if (t.IsGenericType && t.FullName!.StartsWith("System.Tuple", StringComparison.Ordinal))
                return "(" + string.Join(", ", t.GetProperties().Where(p => p.Name.StartsWith("Item", StringComparison.Ordinal))
                    .OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Render(p.GetValue(value), depth + 1))) + ")";

            // An object (WinboxJgField, WinboxJgElementPart): every property, in name order.
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p =>
                {
                    string v;
                    try { v = Render(p.GetValue(value), depth + 1); }
                    catch (TargetInvocationException ex) { v = "throws " + ex.InnerException?.GetType().Name; }
                    return p.Name + "=" + v;
                });
            return t.Name + "{" + string.Join(" ", props) + "}";
        }

        private static bool IsSet(IEnumerable seq)
            => seq.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));

        /// <summary><c>ci</c> or <c>cs</c> for a collection keyed by strings — whether "A" and "a" are one key.</summary>
        private static string ComparerOf(IEnumerable collection)
        {
            object? comparer = collection.GetType().GetProperty("Comparer")?.GetValue(collection);
            if (comparer is IEqualityComparer<string> sc) return sc.Equals("Ab", "aB") ? "ci" : "cs";
            return "";
        }
    }
}
