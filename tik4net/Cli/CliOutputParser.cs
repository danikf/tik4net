using System;
using System.Collections.Generic;

namespace tik4net.Cli
{
    /// <summary>
    /// Parses RouterOS <c>print as-value</c> output (one record per line, fields separated by <c>;</c>)
    /// into a list of <see cref="CliReSentence"/> objects.
    ///
    /// Limitations (v0.2):
    ///   - Quote-aware parsing: values enclosed in "…" may contain embedded ';' without issue.
    ///   - Embedded ';' in unquoted list-type fields (e.g. route-count, wireless ranges, BGP stats)
    ///     will still cause incorrect splits. Use the :serialize delimiter="#" workaround for those
    ///     entities (TODO for a future version).
    /// </summary>
    internal static class CliOutputParser
    {
        /// <summary>
        /// Parses <paramref name="output"/> (output of <c>/path print as-value</c>) into a list of
        /// re-sentences. Empty output returns an empty list.
        /// </summary>
        internal static IList<CliReSentence> ParseAsValue(string output)
        {
            var result = new List<CliReSentence>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            // Split on newlines — each non-empty line is one record
            var lines = output.Split('\n');
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                // Skip blank lines and lines that look like RouterOS prompts or echoes
                if (string.IsNullOrEmpty(line))
                    continue;
                // Skip lines that don't contain '=' (headers, blank separator lines, etc.)
                if (line.IndexOf('=') < 0)
                    continue;

                var fields = ParseFields(line);
                if (fields.Count > 0)
                    result.Add(new CliReSentence(fields));
            }

            return result;
        }

        /// <summary>
        /// Parses a single <c>key=value;key2=value2;…</c> line into a dictionary.
        /// Handles values enclosed in double-quotes (which may contain embedded ';').
        /// </summary>
        internal static Dictionary<string, string> ParseFields(string line)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int len = line.Length;
            int pos = 0;

            while (pos < len)
            {
                // Find '=' for this field
                int eq = line.IndexOf('=', pos);
                if (eq < 0)
                    break;

                string key = line.Substring(pos, eq - pos).Trim();
                pos = eq + 1;

                // Parse value — may be quoted
                string value;
                if (pos < len && line[pos] == '"')
                {
                    // Quoted value: scan for closing '"' (no escape handling — RouterOS doesn't use \")
                    int closeQuote = line.IndexOf('"', pos + 1);
                    if (closeQuote < 0)
                    {
                        // Unterminated quote — take rest of line
                        value = line.Substring(pos + 1);
                        pos = len;
                    }
                    else
                    {
                        value = line.Substring(pos + 1, closeQuote - pos - 1);
                        pos = closeQuote + 1;
                        // Consume trailing ';' if present
                        if (pos < len && line[pos] == ';')
                            pos++;
                    }
                }
                else
                {
                    // Unquoted: scan to next ';'
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
                    fields[key] = value;
            }

            return fields;
        }
    }
}
