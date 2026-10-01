using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Connection
{
    /// <summary>
    /// A read's filter checked against the fields the router says the menu has, on every transport: a filter on a field
    /// the menu does not have throws <see cref="TikUnknownFieldException"/> instead of being sent.
    /// </summary>
    /// <remarks>
    /// Without it every transport answers such a filter wrongly — the API and REST with no rows, which looks like a real
    /// answer, and the CLI with EVERY row: RouterOS evaluates <c>where routing-mark=main</c> on 7.x (the field is
    /// <c>routing-table</c> there) as true for each one (Docs/findings-cli.md §2). The list is the router's
    /// <c>get value-name=</c> (<see cref="TikMenuSchema.ReadableFields"/>); it names exactly the fields the API can
    /// filter on — it leaves out the <c>dynamic2</c>-style second spellings the API prints but cannot query
    /// (<c>?dynamic2=false</c> matches nothing on 7.24.4).
    /// <para>Skipped — the read is sent as before — where the router cannot say: RouterOS 6 over the API (no
    /// <c>/console/inspect</c>), a menu without <c>get</c>, a path WinBox native has no window for.</para>
    /// </remarks>
    internal static class TikFilterValidation
    {
        /// <param name="connection">The connection the read goes over.</param>
        /// <param name="command">The command, for the exception; <c>null</c> to create one from <paramref name="commandText"/>.</param>
        /// <param name="commandText">The read's command text (<c>/ip/route/print</c>).</param>
        /// <param name="filterNames">The field names of the read's filters, as sent.</param>
        /// <param name="winboxLabels">The entity's WinBox labels, when the read carries them (WinBox native names by them).</param>
        internal static void Check(ITikConnection connection, ITikCommand? command, string commandText,
            IEnumerable<string> filterNames, string? winboxLabels)
        {
            if (!string.Equals(TikPath.Verb(commandText), "print", StringComparison.Ordinal))
                return;
            var names = filterNames.Select(FieldOf).Where(n => n != null).Select(n => n!).Distinct(StringComparer.Ordinal).ToList();
            if (names.Count == 0)
                return;
            if (!(connection is ITikMenuSchemaConnection described) || !connection.Supports(TikConnectionCapability.MenuSchema))
                return;

            TikMenuSchema schema;
            try
            {
                schema = described.DescribeMenu(TikMenuSchemaPath.Normalize(TikPath.Parent(commandText)), winboxLabels);
            }
            catch (TikNoSuchCommandException)
            {
                return;   // the router cannot describe it (or has no such menu, which the read itself will say)
            }
            var unknown = Unknown(schema, names);
            // A cached list can be stale for this router: only the router's own answer refuses.
            if (unknown != null && unknown.Count > 0 && schema.Relearn != null)
                unknown = Unknown(schema.Relearn(), names);
            if (unknown != null && unknown.Count > 0)
                throw new TikUnknownFieldException(command ?? connection.CreateCommand(commandText), unknown, TikUnknownFieldUse.Filter);
        }

        // The names the menu's readable fields do not hold; null when the router cannot say (no 'get').
        private static List<string>? Unknown(TikMenuSchema schema, List<string> names)
        {
            var readable = schema.ReadableFields;
            if (readable == null)
                return null;
            var set = new HashSet<string>(readable, StringComparer.Ordinal);
            return names.Where(n => !set.Contains(n)).ToList();
        }

        // The field a filter word names: '.id' and the '#|' stack operators are not fields; an operator prefix
        // ('-name', '<count', '>count') is not part of the name.
        private static string? FieldOf(string name)
        {
            string field = name.TrimStart('?');
            if (field.Length == 0 || field.StartsWith(".", StringComparison.Ordinal) || field.StartsWith("#", StringComparison.Ordinal))
                return null;
            field = field.TrimStart('-', '<', '>', '=');
            return field.Length == 0 ? null : field;
        }
    }
}
