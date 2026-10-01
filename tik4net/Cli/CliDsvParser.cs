using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Parses the per-row DSV blocks of <see cref="CliCommandBuilder.DsvRows"/>: for every row a line of its field
    /// names and a line of its values, both separated by the connection's
    /// <see cref="ITikCliFieldSeparatorConnection.CliFieldSeparator"/>.
    /// </summary>
    /// <remarks>
    /// <para>The values are spelled exactly as as-value spells them (<c>1d00:00:00</c>, not the JSON read's
    /// <c>1970-01-02</c>), so they go through the same <see cref="CliValueNormalizer"/>. What changes is only the
    /// framing: a value keeps its <c>;</c>.</para>
    /// <para>A LIST field is still written with <c>;</c> between its elements (<c>policy=ftp;reboot;read</c>),
    /// and DSV carries no type, so a <c>;</c> in a value is turned into the API's <c>,</c> — as the as-value read
    /// does — except in <c>comment</c>, which is text on every menu and never a list. That is the field this read
    /// exists for; free-text fields marked <c>IsFreeText</c> take the JSON read.</para>
    /// <para>A value holding a line break spans lines — RouterOS writes it unquoted (a script's <c>source</c>,
    /// measured on 7.24) — so a row's values are gathered until they count as many separators as its header, and
    /// then up to the next row's field names, for a break in the last field. A value holding the separator itself
    /// makes the count disagree, and the read is refused rather than shifted into the wrong columns.</para>
    /// </remarks>
    internal static class CliDsvParser
    {
        internal static IList<TikRecordSentence> Parse(string output, string separator)
        {
            var result = new List<TikRecordSentence>();
            if (string.IsNullOrEmpty(output))
                return result;

            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string header = lines[i++].TrimEnd('\r');
                if (header.Trim().Length == 0)
                    continue;
                string[] names = Split(header, separator);

                if (i >= lines.Length)
                    throw Malformed("a field-name line with no values after it", header);
                string values = lines[i++].TrimEnd('\r');
                while (Count(values, separator) < names.Length - 1 && i < lines.Length)
                    values += "\n" + lines[i++].TrimEnd('\r');
                // A line break in the LAST field leaves the count complete on the first line, so the rest of the
                // value is every line up to the next row's field names. A row of one field has no separator to
                // tell its names from its text by, and keeps one line. Blank lines at the end of the gathered text are
                // dropped: the reader cannot tell a value's trailing line break from a blank line between rows, and
                // the last row of an answer loses them anyway (the answer is trimmed before the count line).
                if (names.Length > 1)
                {
                    string tail = string.Empty;
                    while (i < lines.Length && !IsFieldNameLine(lines[i].TrimEnd('\r'), separator))
                        tail += "\n" + lines[i++].TrimEnd('\r');
                    values += tail.TrimEnd('\n');
                }
                string[] cells = Split(values, separator);
                if (cells.Length != names.Length)
                    throw Malformed(names.Length + " field names and " + cells.Length + " values — a value holds the "
                        + "separator '" + separator + "'; choose another CliFieldSeparator", values);

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < names.Length; k++)
                {
                    string name = names[k];
                    string value = cells[k];
                    if (TikEmptyComment.IsNoComment(name, value))
                        continue;
                    if (!string.Equals(name, TikEmptyComment.Field, StringComparison.OrdinalIgnoreCase) && value.IndexOf(';') >= 0)
                        value = string.Join(",", value.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0));
                    fields[name] = CliValueNormalizer.Normalize(name, value);
                }
                result.Add(new TikRecordSentence(fields));
            }
            return result;
        }

        // A row's header: at least two field names, each spelled the way RouterOS names a field.
        private static bool IsFieldNameLine(string line, string separator)
        {
            if (line.IndexOf(separator, StringComparison.Ordinal) < 0)
                return false;
            foreach (string name in Split(line, separator))
            {
                if (name.Length == 0)
                    return false;
                foreach (char c in name)
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_'))
                        return false;
            }
            return true;
        }

        private static string[] Split(string line, string separator)
            => line.Split(new[] { separator }, StringSplitOptions.None);

        private static int Count(string text, string separator)
        {
            int n = 0;
            for (int at = text.IndexOf(separator, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(separator, at + separator.Length, StringComparison.Ordinal))
                n++;
            return n;
        }

        private static TikSentenceException Malformed(string what, string line)
            => new TikSentenceException("The CLI DSV read could not be parsed: " + what + ". Line: '"
                + (line.Length > 200 ? line.Substring(0, 200) + "…" : line) + "'");
    }
}
