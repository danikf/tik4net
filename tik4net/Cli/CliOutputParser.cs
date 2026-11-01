using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <para>Limitations:
    ///   - A <c>;</c> inside a value splits it: as-value has no escape. On RouterOS 7 the reads use DSV
    ///     (<see cref="CliDsvParser"/>) for that reason.
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

            // Parse the ordered key=value sequence, then group into records at '.id' boundaries — or, for rows
            // that carry no .id (/console inspect: name=add;node-type=cmd;type=self;name=blackhole;…), where a
            // key comes round again: a row never holds one field twice, and without this every row merged into
            // one, the last value of each field winning.
            var pairs = ParseOrderedFields(flat.ToString());

            var records = new List<Dictionary<string, string>>();
            Dictionary<string, string>? current = null;
            foreach (var kv in pairs)
            {
                bool isId = string.Equals(kv.Key, TikSpecialProperties.Id, StringComparison.OrdinalIgnoreCase);
                if (current == null || (isId && current.Count > 0) || current.ContainsKey(kv.Key))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    records.Add(current);
                }
                // The one place every as-value field passes through, which is where the router's internal
                // spelling is turned back into the API's (see CliValueNormalizer) — and where RouterOS 6's
                // 'comment=' for a row with none is left out, as the API leaves it out (TikEmptyComment).
                if (TikEmptyComment.IsNoComment(kv.Key, kv.Value))
                    continue;
                current[kv.Key] = CliValueNormalizer.Normalize(kv.Key, kv.Value);
            }

            foreach (var rec in records)
                if (rec.Count > 0)
                    result.Add(new TikRecordSentence(rec));

            return result;
        }

        /// <summary>
        /// True when <paramref name="rows"/> are as-value records rather than the router's complaint read as one: the
        /// text in front of a <c>=</c> in a complaint (<c>failure: already have such address=1.2.3.4</c>) is no field name.
        /// </summary>
        internal static bool AreRecords(IList<TikRecordSentence> rows)
            => rows.Count > 0 && rows.All(row => row.Words.Keys.All(IsFieldName));

        private static bool IsFieldName(string key)
        {
            foreach (char c in key)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                    return false;
            return key.Length > 0;
        }

        /// <summary>
        /// Parses a <c>key=value;…</c> string into an ordered list of pairs (preserving record order
        /// and duplicate <c>.id</c> keys so callers can split into records). A <c>"</c> is an ordinary character:
        /// as-value has no quoting, and a comment reading <c>"a;b</c> is written with no closing quote and no escape
        /// (7.25, 7.21.5, 6.49.21).
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
                {
                    // No more 'key=' tokens. Any remaining ';'-separated tokens carry no '=' and are
                    // therefore multi-value continuations of the last field (see the keyRaw handling
                    // below) — append them so a trailing list field is not lost.
                    AppendContinuations(pairs, line.Substring(pos));
                    break;
                }

                // The substring up to the '=' is normally just the field name. But RouterOS renders a
                // multi-value (list) field in as-value output with ';' BETWEEN elements — the same
                // character it uses between fields — e.g. 'key-usage=key-cert-sign;crl-sign;name=…'.
                // The element 'crl-sign' has no '=', so it lands inside this substring as
                // 'crl-sign;name'. Split on the LAST ';': everything before it is list-element
                // continuation of the PREVIOUS field's value; the part after it is the real key.
                string keyRaw = line.Substring(pos, eq - pos);
                int lastSemi = keyRaw.LastIndexOf(';');
                string key;
                if (lastSemi >= 0)
                {
                    AppendContinuations(pairs, keyRaw.Substring(0, lastSemi));
                    key = keyRaw.Substring(lastSemi + 1).Trim();
                }
                else
                {
                    key = keyRaw.Trim();
                }
                pos = eq + 1;

                string value;
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

                if (!string.IsNullOrEmpty(key))
                    pairs.Add(new KeyValuePair<string, string>(key, value));
            }

            return pairs;
        }

        /// <summary>
        /// Appends the ';'-separated tokens in <paramref name="continuations"/> to the value of the last
        /// parsed pair, joined with ',' (the API multi-value separator). Used to reassemble a list field
        /// whose elements RouterOS separated with ';' in as-value output. No-op when there is no previous
        /// field or nothing to append.
        /// </summary>
        private static void AppendContinuations(List<KeyValuePair<string, string>> pairs, string continuations)
        {
            if (pairs.Count == 0 || string.IsNullOrEmpty(continuations))
                return;

            var prev = pairs[pairs.Count - 1];
            var sb = new StringBuilder(prev.Value);
            foreach (var part in continuations.Split(';'))
            {
                string element = part.Trim();
                if (element.Length == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(element);
            }

            if (sb.Length != prev.Value.Length)
                pairs[pairs.Count - 1] = new KeyValuePair<string, string>(prev.Key, sb.ToString());
        }
    }
}
