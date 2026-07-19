using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>The footer RouterOS prints before/after every torch frame (not a data row).</summary>
        private const string TorchFooter = "-- [Q quit|D dump|C-z pause]";

        /// <summary>
        /// Parses the output of a <see cref="CliCommandBuilder.BuildTorchSnapshot"/> command (torch driven
        /// with <c>freeze-frame-interval</c> + an explicit <c>proplist</c>) into a list of re-sentences.
        /// The field ORDER is read from each frame's own <c>Columns:</c> declaration rather than assumed to
        /// match the requested <c>proplist</c> order — confirmed live that RouterOS reorders the columns to
        /// its own canonical order (<c>ip-protocol</c> first) regardless of the order requested. Because of
        /// that reordering, a data row can no longer be recognised by "starts with an address" (an earlier
        /// version of this parser did — it silently produced zero rows whenever a non-address field, e.g.
        /// <c>ip-protocol</c>, sorted first). Instead: RouterOS always declares exactly as many <c>Columns:</c>
        /// fields as requested (see <see cref="CliCommandBuilder.TorchFields"/>'s length), and repeats that
        /// same field list, unabbreviated, as a plain space-separated header row before the data — so the
        /// header/data boundary is found by locating that repeat, not by guessing at row content.
        /// <para>
        /// A run may flush zero, one or several frames (each terminated by <see cref="TorchFooter"/>) before
        /// the command self-terminates at <c>duration</c>; only the LAST complete frame — the freshest reading
        /// — is parsed. A resolved port value can embed a space (<c>"23 (telnet)"</c>) — the extra
        /// parenthesised token is consumed and discarded so it does not shift the remaining fields out of
        /// alignment.
        /// </para>
        /// </summary>
        internal static IList<TikRecordSentence> ParseTorchFrame(string output)
        {
            var result = new List<TikRecordSentence>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            string normalized = output.Replace("\r", "");
            var segments = normalized.Split(new[] { TorchFooter }, StringSplitOptions.None);

            string frameSegment = null;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (segments[i].IndexOf("Columns:", StringComparison.Ordinal) >= 0)
                {
                    frameSegment = segments[i];
                    break;
                }
            }
            if (frameSegment == null)
                return result; // no frame flushed within the command's duration

            int columnsIdx = frameSegment.IndexOf("Columns:", StringComparison.Ordinal);
            string afterColumns = frameSegment.Substring(columnsIdx + "Columns:".Length);
            var lines = afterColumns.Split('\n');

            // The Columns: declaration always names exactly this many fields (RouterOS returns the same set
            // we requested, just reordered) — read that many comma-separated tokens regardless of how many
            // physical lines they wrap across. The LAST token's comma-split chunk also swallows the plain
            // header row and all data that follows it (torch's plain-text values never contain a comma), so
            // only its first whitespace token — the real field name — is kept.
            int expectedFieldCount = CliCommandBuilder.TorchFields.Length;
            var fieldOrder = new List<string>();
            foreach (var rawPart in afterColumns.Split(','))
            {
                if (fieldOrder.Count >= expectedFieldCount)
                    break;
                string collapsed = Regex.Replace(rawPart, @"\s+", " ").Trim();
                if (collapsed.Length == 0)
                    continue;
                string name = collapsed.Split(' ')[0].ToLowerInvariant();
                if (name.Length > 0)
                    fieldOrder.Add(name);
            }
            if (fieldOrder.Count == 0)
                return result;

            // Locate the plain header row: the first line whose own first whitespace-token equals
            // fieldOrder[0] WITHOUT a trailing comma (which rules out matching the wrapped Columns:
            // declaration line itself, since there the same token is followed by ',').
            int dataStart = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0)
                    continue;
                string firstToken = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (string.Equals(firstToken, fieldOrder[0], StringComparison.OrdinalIgnoreCase))
                {
                    dataStart = i + 1;
                    break;
                }
            }
            if (dataStart < 0)
                return result; // header row not found — nothing reliable to parse

            for (int i = dataStart; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                    continue;

                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int idx = 0;
                bool complete = true;
                foreach (var fieldName in fieldOrder)
                {
                    if (idx >= tokens.Length) { complete = false; break; }
                    string value = tokens[idx];
                    if (string.Equals(fieldName, "tx", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fieldName, "rx", StringComparison.OrdinalIgnoreCase))
                        value = ParseBitrate(value);
                    fields[fieldName] = value;
                    idx++;
                    bool isPortField = string.Equals(fieldName, "src-port", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fieldName, "dst-port", StringComparison.OrdinalIgnoreCase);
                    if (isPortField && idx < tokens.Length && tokens[idx].StartsWith("(", StringComparison.Ordinal))
                        idx++; // discard the "(service-name)" annotation token
                }
                if (complete)
                    result.Add(new TikRecordSentence(fields));
            }

            return result;
        }

        private static readonly Regex BitrateToken = new Regex(
            @"^(?<num>\d+(\.\d+)?)(?<unit>bps|kbps|Mbps|Gbps)$", RegexOptions.Compiled);

        /// <summary>
        /// Converts a torch <c>tx</c>/<c>rx</c> display value (e.g. <c>"599.9Mbps"</c>, <c>"3.7kbps"</c>,
        /// <c>"0bps"</c>) into a plain integer bits-per-second string, matching the raw numeric form the
        /// binary API returns for the same field (<c>ToolTorch.Tx</c>/<c>Rx</c> are typed <c>long</c>).
        /// RouterOS's display value is itself already rounded to one decimal place, so the round-trip through
        /// this conversion is an approximation of the true counter — there is no lossless alternative available
        /// from the plain-text torch display. Returns the input unchanged if it doesn't match the expected
        /// <c>&lt;number&gt;&lt;unit&gt;</c> shape (letting the O/R mapper's own error surface any real mismatch).
        /// </summary>
        internal static string ParseBitrate(string token)
        {
            var m = BitrateToken.Match(token);
            if (!m.Success)
                return token;

            double num = double.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
            double multiplier;
            switch (m.Groups["unit"].Value)
            {
                case "kbps": multiplier = 1_000d; break;
                case "Mbps": multiplier = 1_000_000d; break;
                case "Gbps": multiplier = 1_000_000_000d; break;
                default: multiplier = 1d; break; // bps
            }
            return ((long)Math.Round(num * multiplier)).ToString(CultureInfo.InvariantCulture);
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
