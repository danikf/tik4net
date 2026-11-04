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
    /// Reads what <c>/tool torch</c> prints: the freeze-frame form (<see cref="ParseTorchFrame"/>), and the plain table of a
    /// router whose torch has no <c>proplist=</c> (<see cref="ParseTorchTable"/>).
    /// </summary>
    internal static class CliTorchParser
    {
        /// <summary>The footer RouterOS prints before/after every torch frame (not a data row).</summary>
        private const string TorchFooter = "-- [Q quit|D dump|C-z pause]";

        /// <summary>
        /// Parses the output of a <see cref="CliCommandBuilder.BuildTorchSnapshot"/> command (torch driven
        /// with <c>freeze-frame-interval</c> + an explicit <c>proplist</c>) into a list of re-sentences.
        /// The field ORDER is read from each frame's own <c>Columns:</c> declaration rather than assumed to
        /// match the requested <c>proplist</c> order — confirmed live that RouterOS reorders the columns to
        /// its own canonical order (<c>ip-protocol</c> first) regardless of the order requested. Because of
        /// that reordering, a data row cannot be recognised by "starts with an address": a non-address field
        /// such as <c>ip-protocol</c> sorting first would silently produce zero rows. Instead: RouterOS
        /// always declares exactly as many <c>Columns:</c>
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

            string? frameSegment = null;
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

        /// <summary>
        /// Parses torch run WITHOUT <c>proplist</c> — RouterOS 6, whose torch refuses it — from its plain table: frames
        /// between <see cref="TorchFooter"/> lines, each a header of upper-case column names, one row per flow and a
        /// last row with only the totals. Only the last frame with a header is read, as in <see cref="ParseTorchFrame"/>.
        /// The columns are the ones the caller's own arguments asked for (<c>interface</c> alone prints the totals only),
        /// which is what the binary API returns for the same arguments, totals row included (6.49.13).
        /// </summary>
        /// <remarks>
        /// Not <see cref="CliTableParser"/>: its span rule assumes left-aligned values, and torch right-aligns the
        /// numbers — <c>1664bps</c> starts well left of its <c>TX</c> header and was split across two columns. Here a
        /// value is the column whose header it overlaps: a left-aligned value starts under its header, a right-aligned
        /// one ends under it, and either way they share characters. A cell can be empty (the totals row has only the
        /// last four), which a split on whitespace alone would shift. A port keeps its service name,
        /// <c>8291 (winbox)</c>, as the binary API reports it.
        /// </remarks>
        internal static IList<TikRecordSentence> ParseTorchTable(string output)
        {
            var result = new List<TikRecordSentence>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            var segments = output.Replace("\r", "").Split(new[] { TorchFooter }, StringSplitOptions.None);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                string[] lines = segments[i].Split('\n');
                int headerIndex = Array.FindIndex(lines, IsTorchHeader);
                if (headerIndex < 0)
                    continue;

                var columns = Tokens(lines[headerIndex], attachAnnotation: false);
                for (int l = headerIndex + 1; l < lines.Length; l++)
                {
                    var values = Tokens(lines[l], attachAnnotation: true);
                    if (values.Count == 0 || RouterOsCliLogin.IsShellPrompt(lines[l].Trim()))
                        continue;

                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var v in values)
                    {
                        int best = -1, bestScore = int.MinValue;
                        for (int c = 0; c < columns.Count; c++)
                        {
                            int overlap = Math.Min(v.End, columns[c].End) - Math.Max(v.Start, columns[c].Start);
                            if (overlap > bestScore) { bestScore = overlap; best = c; }
                        }
                        string name = columns[best].Text.ToLowerInvariant();
                        string value = v.Text;
                        if (name == "tx" || name == "rx")
                            value = ParseBitrate(value);
                        fields[name] = value;
                    }
                    result.Add(new TikRecordSentence(fields));
                }
                break;
            }
            return result;
        }

        private static bool IsTorchHeader(string line)
        {
            string t = line.Trim();
            return t.Length > 0 && t.IndexOf(' ') > 0 && t.Any(char.IsUpper) && !t.Any(char.IsLower);
        }

        private struct TableToken { internal int Start; internal int End; internal string Text; }

        /// <summary>
        /// The non-blank runs of a table line with their character positions; with <paramref name="attachAnnotation"/>
        /// a parenthesised run (a port's service name) is joined to the run before it.
        /// </summary>
        private static List<TableToken> Tokens(string line, bool attachAnnotation)
        {
            var tokens = new List<TableToken>();
            foreach (Match m in Regex.Matches(line, @"\S+"))
            {
                if (attachAnnotation && tokens.Count > 0 && m.Value.StartsWith("(", StringComparison.Ordinal))
                {
                    var last = tokens[tokens.Count - 1];
                    tokens[tokens.Count - 1] = new TableToken
                    {
                        Start = last.Start, End = m.Index + m.Length, Text = line.Substring(last.Start, m.Index + m.Length - last.Start),
                    };
                    continue;
                }
                tokens.Add(new TableToken { Start = m.Index, End = m.Index + m.Length, Text = m.Value });
            }
            return tokens;
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
        /// <c>&lt;number&gt;&lt;unit&gt;</c> shape, or is a fraction of a bit, which no rate is
        /// (<see cref="TikDataRate.TryParseNumber"/>), letting the O/R mapper's own error surface any real mismatch.
        /// </summary>
        private static string ParseBitrate(string token)
            => BitrateToken.IsMatch(token) && TikDataRate.TryParseNumber(token, out long bitsPerSecond)
                ? bitsPerSecond.ToString(CultureInfo.InvariantCulture)
                : token;
    }
}
