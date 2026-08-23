using System;
using System.Collections.Generic;

namespace tik4net.Connection
{
    /// <summary>
    /// Parses the API's own sentence-row format — <c>=name=value</c>, <c>?name=value</c>, <c>?name</c> —
    /// into typed command parameters, for the transports that cannot simply put the words on the wire.
    /// </summary>
    /// <remarks>
    /// <para>The binary API sends a row verbatim and lets the ROUTER judge it, so a malformed one comes
    /// back as a trap. Every other transport has to understand the row before it can build CLI text, a REST
    /// body or an M2 field — and understanding it is where a row that makes no sense used to be dropped in
    /// silence: <c>CallCommandSync("/ip/address/add", "address=10.0.0.1/24")</c>, one leading <c>=</c>
    /// short, sent an add with no fields at all and reported success. One typo, loud on one transport and
    /// invisible on the other ten.</para>
    /// <para>So a row this cannot parse is an <see cref="ArgumentException"/> naming the row and the forms
    /// that exist. The one thing still passed over in silence is an API sentence MARKER — a row whose name
    /// begins with <c>.</c>, such as <c>.tag</c> or <c>.proplist</c> — because those are words of the API's
    /// own protocol that the other transports genuinely cannot express, and skipping them is the documented
    /// behaviour rather than a failure to understand them.</para>
    /// </remarks>
    internal static class TikCommandRow
    {
        /// <summary>
        /// Parses <paramref name="rows"/> from <paramref name="startIndex"/> onwards.
        /// </summary>
        /// <param name="rows">The sentence, one word per row.</param>
        /// <param name="startIndex">First row to read — <c>1</c> when row 0 is the command text.</param>
        /// <exception cref="ArgumentException">A row is neither a parameter nor an API sentence marker.</exception>
        internal static List<ITikCommandParameter> ParseParameters(IList<string> rows, int startIndex)
        {
            var parameters = new List<ITikCommandParameter>();
            if (rows == null) return parameters;

            for (int i = startIndex; i < rows.Count; i++)
            {
                var parameter = ParseRow(rows[i]);
                if (parameter != null) parameters.Add(parameter);
            }
            return parameters;
        }

        /// <summary>
        /// One row as a parameter, or <c>null</c> for an API sentence marker this transport ignores.
        /// </summary>
        /// <exception cref="ArgumentException">The row is neither.</exception>
        internal static ITikCommandParameter? ParseRow(string row)
        {
            if (string.IsNullOrEmpty(row))
                throw Malformed(row, "an empty row is not a word");

            // Filter: '?name=value', '?=name=value' (the redundant form the API also accepts), or the bare
            // '?name', which is the API's "this property is set" and has no value at all — the CLI layer
            // spells it as the bare field name in a where-clause. Dropping it, as this used to, turned a
            // real filter into no filter and answered with the whole table.
            if (row[0] == '?')
            {
                string kv = row.Substring(1);
                if (kv.StartsWith("=", StringComparison.Ordinal)) kv = kv.Substring(1);
                if (kv.Length == 0)
                    throw Malformed(row, "a filter needs a property name");
                int eq = kv.IndexOf('=');
                if (eq == 0)
                    throw Malformed(row, "a filter needs a property name");
                return eq < 0
                    ? new TikCommandParameter(kv, null!, TikCommandParameterFormat.Filter)
                    : new TikCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1),
                                              TikCommandParameterFormat.Filter);
            }

            if (row[0] == '=')
            {
                string kv = row.Substring(1);
                int eq = kv.IndexOf('=');
                if (eq <= 0)
                    throw Malformed(row, eq < 0
                        ? "a name-value needs a value, as '=name=value' (an empty one is '=name=')"
                        : "a name-value needs a property name");
                return new TikCommandParameter(kv.Substring(0, eq), kv.Substring(eq + 1),
                                               TikCommandParameterFormat.NameValue);
            }

            // An API sentence marker — '.tag', '.proplist', the CLI-layer signals. Words of the API's own
            // protocol, which these transports cannot express and are documented as ignoring.
            if (row[0] == '.')
                return null;

            throw Malformed(row, "a parameter starts with '=' (name-value) or '?' (filter)");
        }

        private static ArgumentException Malformed(string row, string why)
            => new ArgumentException(
                $"'{row ?? "(null)"}' is not a valid command row: {why}. "
                + "Rows are the API's sentence words: '=name=value', '?name=value' or '?name'.",
                nameof(row));
    }
}
