using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Parses RouterOS's <b>interactive</b> (non-<c>as-value</c>) command table one line at a time, so a
    /// long-running command can be turned into records while it is still running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the counterpart of <see cref="CliOutputParser.ParseAsValue"/> for commands that must NOT be
    /// wrapped in <c>:put [… as-value]</c>. Measured on 7.23.2: the wrapped form emits
    /// <b>nothing at all</b> until the command finishes, because <c>:put</c> is handed an already-complete
    /// array — a <c>count=20</c> ping produced one 322-byte burst at +4019 ms and not a byte before it.
    /// The bare interactive form of the same command streams a row per second. So streaming is not a
    /// property of how we read, it is a property of which command shape we send.
    /// </para>
    /// <para>
    /// <b>Why the columns are read by character offset and not by splitting on whitespace.</b> A column
    /// can be empty, and RouterOS still prints the ones after it in their own places. A timed-out ping row
    /// carries only SEQ, HOST and STATUS:
    /// <code>
    ///   SEQ HOST                                     SIZE TTL TIME       STATUS
    ///     0 192.168.4.99                                                 timeout
    ///     0 127.0.0.1                                  56  64 107us
    /// </code>
    /// Whitespace-splitting the timeout row yields three tokens and maps <c>timeout</c> onto <c>size</c>.
    /// </para>
    /// <para>
    /// <b>The span rule.</b> Header tokens measured live sit at SEQ[3..5], HOST[7..10], SIZE[48..51],
    /// TTL[53..55], TIME[57..60], STATUS[68..73]; the values under them at 0[4], 127.0.0.1[6..14],
    /// 56[49..50], 64[53..54], 107us[56..60], timeout[67..73]. Note that a value may start ONE character
    /// left of its own header token (<c>107us</c> at 56 under <c>TIME</c> at 57) — RouterOS reserves a
    /// single pad character in front of each column — while a left-aligned value may run far past its
    /// header (<c>192.168.4.99</c> under <c>HOST</c>). Hence a column spans from one character before its
    /// header token up to one character before the next column's, which is the only rule that places every
    /// measured value in the right column in both row shapes above.
    /// </para>
    /// <para>
    /// Field names are the header tokens lower-cased, which is exactly what the binary API calls them
    /// (<c>seq</c>, <c>host</c>, <c>size</c>, <c>ttl</c>, <c>time</c>, <c>status</c>) — so this path is no
    /// less faithful than the as-value one. It is in fact closer: the API reports <c>time=60us</c>,
    /// matching this table, whereas <c>as-value</c> renders the same field as <c>00:00:00.000060</c>.
    /// </para>
    /// <para>
    /// The trailing aggregate line (<c>sent=2 received=2 packet-loss=0% min-rtt=…</c>) is recognised and
    /// dropped rather than emitted as a record: it is a summary of the table, not a row of it, and it
    /// arrives after the last data row, so its values cannot be attached to the rows the way the binary API
    /// attaches them. That matches the as-value path, which never carried those fields either.
    /// </para>
    /// </remarks>
    internal sealed class CliTableParser
    {
        // A header token and the character span its column occupies in the data rows below it.
        private struct Column
        {
            internal string? Name; // null for a column that occupies space but names no field (the '#' ordinal)
            internal int Start;   // inclusive
            internal int End;     // exclusive; int.MaxValue for the last column
        }

        private List<Column>? _columns;

        /// <summary>True once a header row has been seen and the column layout is known.</summary>
        internal bool HasHeader => _columns != null;

        /// <summary>
        /// Consumes one line of terminal output and returns the record it holds, or <c>null</c> when the
        /// line is not a data row (echo, header, blank, aggregate summary, prompt).
        /// </summary>
        internal TikRecordSentence? Feed(string? line)
        {
            if (line == null)
                return null;

            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
                return null;

            // A header can appear more than once: traceroute repaints the whole table every round, so a
            // second header means "new frame", not "bad input". Re-deriving the layout also means a table
            // whose column widths change between frames stays parseable.
            if (IsHeaderLine(trimmed))
            {
                _columns = BuildColumns(trimmed);
                return null;
            }

            if (_columns == null)
                return null;              // still before the header: command echo, banner, stray prompt

            if (RouterOsCliLogin.IsShellPrompt(trimmed))
                return null;

            // The aggregate/summary line is 'name=value name=value …'; a data row never contains '=' in
            // its first token. Also covers traceroute's 'Columns: …' declaration, which is not a row.
            string firstToken = trimmed.TrimStart().Split(' ')[0];
            if (firstToken.IndexOf('=') >= 0 || firstToken.EndsWith(":", StringComparison.Ordinal))
                return null;

            var words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in _columns)
            {
                if (col.Name == null)     // the '#' ordinal — spans the line, names no field
                    continue;
                if (col.Start >= line.Length)
                    continue;
                int end = col.End == int.MaxValue ? line.Length : Math.Min(col.End, line.Length);
                if (end <= col.Start)
                    continue;

                string value = line.Substring(col.Start, end - col.Start).Trim();
                if (value.Length > 0)
                    words[col.Name] = value;
            }

            // Every column empty → not a row at all (a separator or a stray repaint fragment).
            return words.Count == 0 ? null : new TikRecordSentence(words);
        }

        // RouterOS prints its column headers in upper case. A header line therefore has at least two
        // tokens, contains an upper-case letter and contains no lower-case one — which rejects the command
        // echo ('/ping address=…'), a data row carrying any text value ('107us', 'timeout') and the
        // aggregate line. A hypothetical all-numeric data row would pass that test, but cannot be mistaken
        // for the header in practice: the header is printed before any row, so it is always seen first.
        private static bool IsHeaderLine(string line)
        {
            var tokens = Tokens(line);
            if (tokens.Count < 2)
                return false;

            bool anyUpper = false;
            foreach (Match t in tokens)
            {
                foreach (char ch in t.Value)
                {
                    if (char.IsLower(ch)) return false;
                    if (char.IsUpper(ch)) anyUpper = true;
                }
            }
            return anyUpper;
        }

        private static List<Column> BuildColumns(string header)
        {
            var tokens = Tokens(header);
            var columns = new List<Column>(tokens.Count);

            for (int i = 0; i < tokens.Count; i++)
            {
                // One character of left pad belongs to the column, except for the first, which owns
                // everything from the start of the line (its value may be wider than its own header).
                int start = i == 0 ? 0 : tokens[i].Index - 1;
                int end = i == tokens.Count - 1 ? int.MaxValue : Math.Max(tokens[i + 1].Index - 1, start);
                string name = tokens[i].Value.ToLowerInvariant();
                // '#' is the terminal's row ordinal (traceroute prints one; ping does not), not a field —
                // the binary API returns '.id' in its place and no field named '#'. Kept as a column so the
                // spans of the real ones stay right, but never emitted as a word.
                columns.Add(new Column
                {
                    Name = name == "#" ? null : name,
                    Start = start,
                    End = end,
                });
            }
            return columns;
        }

        private static List<Match> Tokens(string line)
            => Regex.Matches(line, @"\S+").Cast<Match>().ToList();
    }
}
