using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace tik4net.Objects
{
    /// <summary>
    /// Receives a <see cref="TikEntityReadReport"/> for every entity read that found something a caller may want to know:
    /// a value the property's type could not hold, a mapped field no row carried, or a field no property maps. Attach one
    /// with <see cref="TikEntityDiagnosticsExtensions.SetEntityDiagnostics(ITikConnection, ITikEntityDiagnostics)"/>.
    /// </summary>
    /// <remarks>
    /// A load never fails on a value from another RouterOS version; this is how drift shows up without every caller
    /// checking every entity (<see cref="TikValueStrictnessExtensions.EnsureStrict{TEntity}(TEntity, TikStrictness, string[])"/>
    /// is the per-entity check). It is called on the thread that ran the load, after the rows are built and before the
    /// load returns them. An exception it throws is swallowed: a diagnostics sink never changes what a load returns.
    /// </remarks>
    public interface ITikEntityDiagnostics
    {
        /// <summary>One read with at least one finding.</summary>
        void OnEntityRead(TikEntityReadReport report);
    }

    /// <summary>A field whose value the property's type could not hold (<see cref="TikValueState.Unparsed"/>).</summary>
    public sealed class TikUnparsedField
    {
        internal TikUnparsedField(string fieldName, string propertyName, string? rawValue, int rows)
        {
            FieldName = fieldName;
            PropertyName = propertyName;
            RawValue = rawValue;
            Rows = rows;
        }

        /// <summary>The RouterOS field.</summary>
        public string FieldName { get; }

        /// <summary>The C# property.</summary>
        public string PropertyName { get; }

        /// <summary>The router's word, from the first row that had it.</summary>
        public string? RawValue { get; }

        /// <summary>How many rows of the read carried a value that could not be read.</summary>
        public int Rows { get; }

        /// <inheritdoc/>
        public override string ToString() => FieldName + " '" + RawValue + "'" + (Rows > 1 ? " (" + Rows + " rows)" : "");
    }

    /// <summary>What one entity read found. See <see cref="ITikEntityDiagnostics"/>.</summary>
    public sealed class TikEntityReadReport
    {
        internal TikEntityReadReport(Type entityType, string commandText, int rowCount, bool isComplete,
            IReadOnlyList<TikUnparsedField> unparsedFields, IReadOnlyList<string> fieldsAbsentFromEveryRow,
            IReadOnlyList<string> unmappedFields)
        {
            EntityType = entityType;
            CommandText = commandText;
            RowCount = rowCount;
            IsComplete = isComplete;
            UnparsedFields = unparsedFields;
            FieldsAbsentFromEveryRow = fieldsAbsentFromEveryRow;
            UnmappedFields = unmappedFields;
        }

        /// <summary>The entity the rows were read into.</summary>
        public Type EntityType { get; }

        /// <summary>The command that was read.</summary>
        public string CommandText { get; }

        /// <summary>How many rows the read returned; <c>1</c> for a row delivered by a callback.</summary>
        public int RowCount { get; }

        /// <summary>
        /// Whether the report covers a whole read. <c>false</c> for a row delivered by a callback or a listen, where there
        /// is no end of the read to judge "every row" against, so <see cref="FieldsAbsentFromEveryRow"/> is empty.
        /// </summary>
        public bool IsComplete { get; }

        /// <summary>Fields whose value the property's type could not hold, in declaration order.</summary>
        public IReadOnlyList<TikUnparsedField> UnparsedFields { get; }

        /// <summary>
        /// Mapped fields (RouterOS names) that no row of a complete, non-empty read carried — a field this RouterOS version
        /// does not have, one it renamed, or one it prints only for some rows and none of these. A field the read did not
        /// ask for (a caller's <c>.proplist</c>) is not listed.
        /// </summary>
        public IReadOnlyList<string> FieldsAbsentFromEveryRow { get; }

        /// <summary>
        /// Fields the router sent that no property maps, under any of its names. A renamed field shows up here under its
        /// new name. Empty on WinBox native, which reports its windows' own fields besides the API's.
        /// </summary>
        public IReadOnlyList<string> UnmappedFields { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var parts = new List<string>();
            if (UnparsedFields.Count > 0)
                parts.Add("unparsed: " + string.Join(", ", UnparsedFields));
            if (FieldsAbsentFromEveryRow.Count > 0)
                parts.Add("absent from every row: " + string.Join(", ", FieldsAbsentFromEveryRow));
            if (UnmappedFields.Count > 0)
                parts.Add("unmapped: " + string.Join(", ", UnmappedFields));
            return EntityType.Name + " (" + CommandText + ", " + RowCount + " rows): " + string.Join("; ", parts);
        }
    }

    /// <summary>Attaches an <see cref="ITikEntityDiagnostics"/> to a connection.</summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikEntityDiagnosticsExtensions
    {
        private sealed class Holder
        {
            internal ITikEntityDiagnostics? Sink;
        }

        private sealed class ActionSink : ITikEntityDiagnostics
        {
            private readonly Action<TikEntityReadReport> _action;
            internal ActionSink(Action<TikEntityReadReport> action) => _action = action;
            public void OnEntityRead(TikEntityReadReport report) => _action(report);
        }

        // Entity-layer state kept beside the connection, as TikChangeTracker keeps its snapshots: released with it.
        private static readonly ConditionalWeakTable<ITikConnection, Holder> _perConnection
            = new ConditionalWeakTable<ITikConnection, Holder>();

        /// <summary>
        /// Sends a report of every entity read on <paramref name="connection"/> that found something to
        /// <paramref name="diagnostics"/>. One sink per connection: a second call replaces the first, and
        /// <see cref="ClearEntityDiagnostics"/> removes it.
        /// </summary>
        public static void SetEntityDiagnostics(this ITikConnection connection, ITikEntityDiagnostics diagnostics)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNull(diagnostics, nameof(diagnostics));
            _perConnection.GetOrCreateValue(connection).Sink = diagnostics;
        }

        /// <summary>Stops reporting the reads of <paramref name="connection"/>.</summary>
        public static void ClearEntityDiagnostics(this ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            if (_perConnection.TryGetValue(connection, out var holder))
                holder.Sink = null;
        }

        /// <inheritdoc cref="SetEntityDiagnostics(ITikConnection, ITikEntityDiagnostics)"/>
        public static void SetEntityDiagnostics(this ITikConnection connection, Action<TikEntityReadReport> onEntityRead)
        {
            Guard.ArgumentNotNull(onEntityRead, nameof(onEntityRead));
            connection.SetEntityDiagnostics(new ActionSink(onEntityRead));
        }

        /// <summary>The sink attached to <paramref name="connection"/>, or <c>null</c>.</summary>
        public static ITikEntityDiagnostics? GetEntityDiagnostics(this ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            return _perConnection.TryGetValue(connection, out var holder) ? holder.Sink : null;
        }

        /// <summary>
        /// Reports what the read of <paramref name="sentences"/> into <paramref name="entities"/> found, when a sink is
        /// attached and there is something to report. <paramref name="isComplete"/> is false for a callback row.
        /// </summary>
        internal static void Report<TEntity>(ITikCommand command, IReadOnlyList<ITikReSentence> sentences,
            IReadOnlyList<TEntity> entities, bool isComplete)
        {
            var connection = command.Connection;
            if (connection == null)
                return;
            var sink = connection.GetEntityDiagnostics();
            if (sink == null)
                return;

            TikEntityReadReport? report;
            try
            {
                report = Build(command, connection, sentences, entities, isComplete);
            }
            catch
            {
                return;   // a report that cannot be built is no reason to fail a load that succeeded
            }
            if (report == null)
                return;
            try
            {
                sink.OnEntityRead(report);
            }
            catch
            {
                // See ITikEntityDiagnostics: a sink never changes what a load returns.
            }
        }

        private static TikEntityReadReport? Build<TEntity>(ITikCommand command, ITikConnection connection,
            IReadOnlyList<ITikReSentence> sentences, IReadOnlyList<TEntity> entities, bool isComplete)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            var unparsed = new List<TikUnparsedField>();
            foreach (var property in metadata.Properties.Where(p => p.IsWrapped))
            {
                int rows = 0;
                string? first = null;
                foreach (var entity in entities)
                {
                    var value = (ITikValue)property.GetWrapped(entity!);
                    if (value.State != TikValueState.Unparsed)
                        continue;
                    if (rows == 0)
                        first = value.RawValue;
                    rows++;
                }
                if (rows > 0)
                    unparsed.Add(new TikUnparsedField(property.FieldName, property.PropertyName, first, rows));
            }

            var absent = new List<string>();
            if (isComplete && sentences.Count > 0)
            {
                var asked = RequestedFields(command);
                foreach (var property in metadata.Properties)
                {
                    if (property.FieldName == TikSpecialProperties.Id)
                        continue;
                    if (asked != null && !asked.Contains(property.FieldName)
                        && !property.AlternateNames.Any(asked.Contains))
                        continue;
                    if (sentences.All(s => property.NameInSentence(s) == null))
                        absent.Add(property.FieldName);
                }
            }

            var unmapped = new List<string>();
            if (!connection.Supports(TikConnectionCapability.FieldLabels))
            {
                var mapped = new HashSet<string>(metadata.Properties
                    .SelectMany(p => new[] { p.FieldName }.Concat(p.AlternateNames)), StringComparer.Ordinal);
                foreach (var name in sentences.SelectMany(s => s.Words.Keys).Distinct(StringComparer.Ordinal))
                {
                    // '.tag', '.dead', '.section', '.nextid': protocol words, not fields of the menu.
                    if (name.StartsWith(".", StringComparison.Ordinal) && name != TikSpecialProperties.Id)
                        continue;
                    if (!mapped.Contains(name))
                        unmapped.Add(name);
                }
                unmapped.Sort(StringComparer.Ordinal);
            }

            if (unparsed.Count == 0 && absent.Count == 0 && unmapped.Count == 0)
                return null;
            return new TikEntityReadReport(typeof(TEntity), command.CommandText, sentences.Count, isComplete,
                unparsed, absent, unmapped);
        }

        // The fields a caller's .proplist asked for, or null when the read asked for every field.
        private static HashSet<string>? RequestedFields(ITikCommand command)
        {
            var proplist = command.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);
            if (proplist == null || string.IsNullOrEmpty(proplist.Value))
                return null;
            return new HashSet<string>(proplist.Value!.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0),
                StringComparer.Ordinal);
        }
    }
}
