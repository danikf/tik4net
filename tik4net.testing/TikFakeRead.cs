using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Testing
{
    /// <summary>
    /// A read's rows as <see cref="TikFakeConnection.WithEntities{TEntity}(Func{IEnumerable{TEntity}})"/> evaluates them:
    /// the plain filters and the field list, and a refusal for anything else that would change which rows come back.
    /// </summary>
    internal sealed class TikFakeRead
    {
        // Print arguments that change how a row is printed, not which rows: the fake prints every row the same way.
        private static readonly HashSet<string> HarmlessArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "detail", "stats", "without-paging", "terse", "brief", "show-ids", "show-sensitive", "as-value", "value-list", "oid",
        };

        private readonly List<Func<IReadOnlyDictionary<string, string>, bool>> _filters
            = new List<Func<IReadOnlyDictionary<string, string>, bool>>();
        private HashSet<string>? _fields;

        private TikFakeRead()
        {
        }

        /// <exception cref="InvalidOperationException">A row the fake cannot evaluate.</exception>
        internal static TikFakeRead Parse(IEnumerable<string> rows)
        {
            var read = new TikFakeRead();
            foreach (string row in rows.Skip(1))
            {
                if (row.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    continue;
                if (row.StartsWith("?", StringComparison.Ordinal))
                    read.AddFilter(row);
                else if (row.StartsWith("=" + TikSpecialProperties.Proplist + "=", StringComparison.Ordinal))
                    read._fields = new HashSet<string>(
                        row.Substring(TikSpecialProperties.Proplist.Length + 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.Ordinal);
                else if (!(row.StartsWith("=", StringComparison.Ordinal) && HarmlessArguments.Contains(NameOf(row.Substring(1)))))
                    throw Refused(row);
            }
            return read;
        }

        private void AddFilter(string row)
        {
            string body = row.Substring(1);
            if (body.Length == 0 || body[0] == '#' || body[0] == '>' || body[0] == '<')
                throw Refused(row);
            if (body[0] == '-')
            {
                // '?-name', sent as '?-name=' by a parameter with no value.
                string absent = NameOf(body.Substring(1));
                _filters.Add(words => !words.ContainsKey(absent));
                return;
            }
            int eq = body.IndexOf('=');
            if (eq < 0)
            {
                _filters.Add(words => words.ContainsKey(body));
                return;
            }
            string name = body.Substring(0, eq);
            string value = Canonical(body.Substring(eq + 1));
            _filters.Add(words => words.TryGetValue(name, out var v) && Canonical(v) == value);
        }

        // A boolean is the same value in either spelling: the mapper writes yes/no, the API prints true/false.
        private static string Canonical(string word)
            => TikBoolText.TryParse(word, out bool value) ? (value ? "true" : "false") : word;

        private static string NameOf(string nameAndValue)
        {
            int eq = nameAndValue.IndexOf('=');
            return eq < 0 ? nameAndValue : nameAndValue.Substring(0, eq);
        }

        private static InvalidOperationException Refused(string row)
            => new InvalidOperationException($"TikFakeConnection.WithEntities answers a plain read; '{row}' changes which "
                + "rows a router returns in a way the fake does not evaluate. Script this read with WithResponse.");

        internal bool Matches(IReadOnlyDictionary<string, string> words) => _filters.All(f => f(words));

        internal Dictionary<string, string> Project(Dictionary<string, string> words)
            => _fields == null ? words : words.Where(w => _fields.Contains(w.Key)).ToDictionary(w => w.Key, w => w.Value);
    }
}
