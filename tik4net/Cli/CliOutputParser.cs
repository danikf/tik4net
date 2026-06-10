using System;
using System.Collections.Generic;
using System.Text;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Parses RouterOS as-value output (produced via <c>:put [/path print as-value …]</c>) into a
    /// list of <see cref="TikRecordSentence"/> objects.
    ///
    /// <para>Format: <c>:put</c> emits ALL records on a single logical line, fields separated by
    /// <c>;</c>, with each record starting at <c>.id=</c>. Example:
    /// <code>.id=*2;name=ether1;type=ether;.id=*1;name=lo;type=loopback</code>
    /// Singleton entities (e.g. /system/resource) have no <c>.id</c> and form a single record.
    /// Newline-separated input (one record per line, as other transports may produce) is also
    /// accepted — newlines are treated as field separators and records are still split at <c>.id</c>.</para>
    ///
    /// <para>Limitations (v0.2):
    ///   - Quote-aware: values enclosed in "…" may contain embedded ';' without issue.
    ///   - Embedded ';' in unquoted list-type fields (route-count, wireless ranges, BGP stats) can
    ///     still cause incorrect splits — use :serialize delimiter="#" for those (TODO).
    ///   - A list entity that has no <c>.id</c> at all collapses into one record.</para>
    /// </summary>
    internal static class CliOutputParser
    {
        /// <summary>
        /// Parses <paramref name="output"/> into a list of re-sentences. Empty output → empty list.
        /// </summary>
        internal static IList<TikRecordSentence> ParseAsValue(string output)
        {
            var result = new List<TikRecordSentence>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            // Normalise line breaks into field separators (':put' output is one line, but be defensive).
            var flat = new StringBuilder(output.Length);
            foreach (char c in output)
                flat.Append(c == '\r' || c == '\n' ? ';' : c);

            // Parse the ordered key=value sequence, then group into records at '.id' boundaries.
            var pairs = ParseOrderedFields(flat.ToString());

            var records = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (var kv in pairs)
            {
                bool isId = string.Equals(kv.Key, TikSpecialProperties.Id, StringComparison.OrdinalIgnoreCase);
                if (current == null || (isId && current.Count > 0))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    records.Add(current);
                }
                current[kv.Key] = kv.Value;
            }

            foreach (var rec in records)
                if (rec.Count > 0)
                    result.Add(new TikRecordSentence(rec));

            return result;
        }

        /// <summary>
        /// Parses a single <c>key=value;key2=value2;…</c> line into a dictionary (last value wins).
        /// Retained for callers/tests that expect a flat dictionary.
        /// </summary>
        internal static Dictionary<string, string> ParseFields(string line)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ParseOrderedFields(line))
                fields[kv.Key] = kv.Value;
            return fields;
        }

        /// <summary>
        /// Parses a <c>key=value;…</c> string into an ordered list of pairs (preserving record order
        /// and duplicate <c>.id</c> keys so callers can split into records). Quote-aware: values in
        /// double-quotes may contain embedded ';'.
        /// </summary>
        internal static List<KeyValuePair<string, string>> ParseOrderedFields(string line)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(line))
                return pairs;

            int len = line.Length;
            int pos = 0;

            while (pos < len)
            {
                // Skip leading separators/whitespace between fields.
                while (pos < len && (line[pos] == ';' || line[pos] == ' ' || line[pos] == '\t'))
                    pos++;
                if (pos >= len)
                    break;

                int eq = line.IndexOf('=', pos);
                if (eq < 0)
                    break;

                string key = line.Substring(pos, eq - pos).Trim();
                pos = eq + 1;

                string value;
                if (pos < len && line[pos] == '"')
                {
                    // Quoted value: scan for closing '"' (RouterOS escapes embedded quotes with backslash).
                    int closeQuote = pos + 1;
                    while (closeQuote < len)
                    {
                        if (line[closeQuote] == '"' && line[closeQuote - 1] != '\\')
                            break;
                        closeQuote++;
                    }
                    if (closeQuote >= len)
                    {
                        value = line.Substring(pos + 1);
                        pos = len;
                    }
                    else
                    {
                        value = line.Substring(pos + 1, closeQuote - pos - 1).Replace("\\\"", "\"");
                        pos = closeQuote + 1;
                        if (pos < len && line[pos] == ';')
                            pos++;
                    }
                }
                else
                {
                    int semi = line.IndexOf(';', pos);
                    if (semi < 0)
                    {
                        value = line.Substring(pos);
                        pos = len;
                    }
                    else
                    {
                        value = line.Substring(pos, semi - pos);
                        pos = semi + 1;
                    }
                }

                if (!string.IsNullOrEmpty(key))
                    pairs.Add(new KeyValuePair<string, string>(key, value));
            }

            return pairs;
        }
    }
}
