using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using tik4net.Connection;

namespace tik4net.Objects
{
    /// <summary>
    /// <see cref="TikConnectionSetup.ValidateWrites"/>: an entity write checked against the router's own argument list
    /// before it is sent, a renamed field written under the name the router takes.
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    internal static class TikWriteValidation
    {
        // Arguments RouterOS takes without listing them for every menu.
        private static readonly HashSet<string> AlwaysTaken = new HashSet<string>(StringComparer.Ordinal)
        {
            "numbers", "place-before", "copy-from",
        };

        /// <summary>
        /// Checks <paramref name="commands"/> (an add, a set, its unsets) when the connection asks for it, and renames a
        /// field the router takes under another of its names. Throws before anything is sent.
        /// </summary>
        internal static void Validate(ITikConnection connection, TikEntityMetadata metadata, IEnumerable<ITikCommand?> commands)
        {
            if (!(connection is ITikMenuSchemaConnection described) || !described.ValidateWrites)
                return;

            TikMenuSchema schema;
            try
            {
                schema = TikMenuSchemaExtensions.DescribeMenu(connection, metadata.EntityPath, metadata.WinboxLabelsMarker);
            }
            catch (TikNoSuchCommandException)
            {
                return;   // the router cannot describe the menu (RouterOS 6 over the API): sent as before
            }
            // WinBox native refuses a field it has no key for by itself, and its names are tik4net's own.
            if (schema.Source == TikMenuSchemaSource.WinboxCatalog)
                return;

            foreach (var command in commands)
                if (command != null)
                    Check(command, metadata, schema);
        }

        /// <summary>The check itself, for one command against one schema (router-free, for the tests).</summary>
        internal static void Check(ITikCommand command, TikEntityMetadata metadata, TikMenuSchema schema)
        {
            string verb = TikPath.Verb(command.CommandText);
            var taken = verb == "add" ? schema.AddArguments : schema.SetArguments;   // an unset names a set argument
            if (taken == null)
                return;   // the menu has no such verb: the router says so itself
            var names = new HashSet<string>(taken, StringComparer.Ordinal);

            var unknown = new List<string>();
            foreach (var parameter in command.Parameters)
            {
                bool isUnsetName = verb == "unset" && parameter.Name == TikSpecialProperties.UnsetValueName;
                string field = isUnsetName ? parameter.Value ?? string.Empty : parameter.Name;
                if (!isUnsetName && field.StartsWith(".", StringComparison.Ordinal))
                    continue;   // .id and the library's own markers
                if (names.Contains(field) || AlwaysTaken.Contains(field))
                    continue;

                var property = metadata.Properties.FirstOrDefault(p => p.FieldName == field || p.AlternateNames.Contains(field));
                string? accepted = property == null ? null
                    : new[] { property.FieldName }.Concat(property.AlternateNames).FirstOrDefault(names.Contains);
                if (accepted == null)
                    unknown.Add(field);
                else if (isUnsetName)
                    parameter.Value = accepted;
                else
                    parameter.Name = accepted;
            }

            if (unknown.Count > 0)
                throw new TikUnknownArgumentException(command, unknown);
        }
    }
}
