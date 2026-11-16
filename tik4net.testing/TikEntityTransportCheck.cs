using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using tik4net.Connection;
using tik4net.Objects;
using tik4net.Helpers;

namespace tik4net.Testing
{
    /// <summary>
    /// Reads one entity over several live connections and reports where a transport reads it differently from a
    /// reference — a field absent there, a value that differs, a value the property's type cannot hold — with the entity
    /// option that usually fixes it. Read-only, so safe to point at a production router.
    /// </summary>
    /// <remarks>
    /// <para>The options with a transport consequence (<c>IncludeCliStats</c>, <c>IsSensitive</c>, <c>IsFreeText</c>,
    /// <c>IsReadOnly</c> on a status flag, <c>WinboxLabel</c>, <c>IsPresenceFlag</c>) fail silently when they are left
    /// out: an entity written and tried over the binary API reads over a CLI transport without its counters or its
    /// secrets, and nothing throws. This is the check that finds them.</para>
    /// <para>Unlike the rest of this package it needs live connections. Each connection is read once with
    /// <c>LoadAll</c> (a singleton with <c>LoadSingle</c>) under an entity-diagnostics sink of its own, which replaces the
    /// connection's sink for the read and is put back afterwards. Rows are paired by <c>.id</c>; a singleton is its one
    /// row; an entity without <c>.id</c> gets the presence findings only. Presence is compared for every field, values only
    /// for writable fields that do not change on their own, and as typed values (the binary API's <c>10s</c> and the CLI's
    /// <c>00:00:10</c> are equal). A secret's value is never printed.</para>
    /// <para>A suggestion follows from the finding's kind and the transport family, never from the field's name.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var report = TikEntityTransportCheck.Compare&lt;MyQueue&gt;(api, ssh, winboxNative);
    /// Console.WriteLine(report);
    /// report.ThrowIfAnyFinding();
    /// </code>
    /// </example>
    public static class TikEntityTransportCheck
    {
        /// <summary>
        /// Reads <typeparamref name="TEntity"/> over <paramref name="reference"/> and over each of <paramref name="others"/>,
        /// and reports how each of them differs from the reference.
        /// </summary>
        /// <param name="reference">The connection the others are compared with; any transport (a CLI-first author can make
        /// it SSH and compare the binary API against it).</param>
        /// <param name="others">The connections to check. Each is opened by the caller and left open.</param>
        /// <exception cref="ArgumentNullException">A connection is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">No connection to compare with.</exception>
        /// <exception cref="Exception">Whatever the reference's read throws: without it there is nothing to compare with.
        /// A failed read on one of <paramref name="others"/> is a finding, not an exception.</exception>
        public static TikEntityTransportReport Compare<TEntity>(ITikConnection reference, params ITikConnection[] others)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(reference, nameof(reference));
            Guard.ArgumentNotNull(others, nameof(others));
            if (others.Length == 0)
                throw new ArgumentException("Name at least one connection to compare with the reference.", nameof(others));
            foreach (var other in others)
                Guard.ArgumentNotNull(other, nameof(others));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var referenceRead = Read<TEntity>(reference, metadata);
            if (referenceRead.Error != null)
                ExceptionDispatchInfo.Capture(referenceRead.Error).Throw();

            var findings = new List<TikEntityTransportFinding>();
            foreach (var other in others)
            {
                var read = Read<TEntity>(other, metadata);
                if (read.Error != null)
                {
                    findings.Add(new TikEntityTransportFinding(read.Name, read.Type, null, null,
                        TikEntityTransportFindingKind.ReadFailed, null, null, read.Error.Message,
                        read.Error is TikPathNotMappedException
                            ? "the transport has no way to reach this menu (TikPathNotMappedException)"
                            : "the transport could not read the entity: see the message") { Exception = read.Error });
                    continue;
                }
                Compare(metadata, referenceRead, read, findings);
            }

            return new TikEntityTransportReport(typeof(TEntity), metadata.EntityPath, referenceRead.Name,
                referenceRead.Rows.Count, others.Select(Name).ToList(), findings);
        }

        // ── One read ──────────────────────────────────────────────────────────

        private sealed class EntityRead
        {
            internal string Name = string.Empty;
            internal TikConnectionType? Type;
            internal IList<object> Rows = new List<object>();
            internal TikEntityReadReport? Report;
            internal Exception? Error;
        }

        private static EntityRead Read<TEntity>(ITikConnection connection, TikEntityMetadata metadata)
            where TEntity : new()
        {
            var read = new EntityRead { Name = Name(connection), Type = TypeOf(connection) };
            var previous = connection.GetEntityDiagnostics();
            connection.SetEntityDiagnostics(report =>
            {
                if (report.EntityType == typeof(TEntity) && report.IsComplete)
                    read.Report = report;
            });
            try
            {
                read.Rows = metadata.IsSingleton
                    ? new List<object> { connection.LoadSingle<TEntity>()! }
                    : connection.LoadAll<TEntity>().Cast<object>().ToList();
            }
            catch (Exception ex)
            {
                read.Error = ex;
            }
            finally
            {
                if (previous != null)
                    connection.SetEntityDiagnostics(previous);
                else
                    connection.ClearEntityDiagnostics();
            }
            return read;
        }

        private static TikConnectionType? TypeOf(ITikConnection connection)
            => (connection as TikConnectionBase)?.ConnectionType;

        private static string Name(ITikConnection connection)
            => TypeOf(connection) is TikConnectionType type ? TikTransportInfo.Of(type).DisplayName : connection.GetType().Name;

        // ── The comparison ────────────────────────────────────────────────────

        private static void Compare(TikEntityMetadata metadata, EntityRead reference, EntityRead other,
            List<TikEntityTransportFinding> findings)
        {
            var referenceAbsent = AbsentSet(reference);
            var otherAbsent = AbsentSet(other);
            var referenceUnparsed = UnparsedByField(reference);
            var otherUnparsed = UnparsedByField(other);
            bool bothHaveRows = reference.Rows.Count > 0 && other.Rows.Count > 0;

            foreach (var property in metadata.Properties)
            {
                string field = property.FieldName;
                if (field == TikSpecialProperties.Id)
                    continue;

                if (bothHaveRows && !referenceAbsent.Contains(field) && otherAbsent.Contains(field))
                {
                    findings.Add(new TikEntityTransportFinding(other.Name, other.Type, field, property.PropertyName,
                        TikEntityTransportFindingKind.AbsentFromEveryRow, null, null, null,
                        AbsentSuggestion(metadata, property, other)));
                }
                else if (bothHaveRows && referenceAbsent.Contains(field) && !otherAbsent.Contains(field)
                         && !(property.IsPresenceFlag && FamilyOf(reference.Type) == Family.Api))
                {
                    // (A presence flag set on no row is printed by no row of the binary API or REST: expected.)
                    findings.Add(new TikEntityTransportFinding(reference.Name, reference.Type, field, property.PropertyName,
                        TikEntityTransportFindingKind.AbsentOnTheReferenceOnly, null, null, null,
                        "read by " + other.Name + " but by no row of the reference: "
                            + AbsentSuggestion(metadata, property, reference)));
                }

                if (otherUnparsed.TryGetValue(field, out var unparsed) && !referenceUnparsed.ContainsKey(field))
                    findings.Add(new TikEntityTransportFinding(other.Name, other.Type, field, property.PropertyName,
                        TikEntityTransportFindingKind.Unparsed, null, null, Mask(property, unparsed.RawValue),
                        UnparsedSuggestion(property, unparsed.RawValue)));
                else if (referenceUnparsed.TryGetValue(field, out var refUnparsed) && !otherUnparsed.ContainsKey(field))
                    findings.Add(new TikEntityTransportFinding(reference.Name, reference.Type, field, property.PropertyName,
                        TikEntityTransportFindingKind.UnparsedOnTheReferenceOnly, null, null, Mask(property, refUnparsed.RawValue),
                        "read by " + other.Name + " but not by the reference: " + UnparsedSuggestion(property, refUnparsed.RawValue)));
            }

            // A field unparsed on either side is reported as that; its word is no value to compare.
            var unparsedEither = new HashSet<string>(referenceUnparsed.Keys.Concat(otherUnparsed.Keys), StringComparer.Ordinal);
            CompareValues(metadata, reference, other, unparsedEither, findings);
        }

        private static void CompareValues(TikEntityMetadata metadata, EntityRead reference, EntityRead other,
            HashSet<string> skip, List<TikEntityTransportFinding> findings)
        {
            var pairs = Pair(metadata, reference.Rows, other.Rows);
            if (pairs.Count == 0)
                return;

            foreach (var property in metadata.Properties)
            {
                if (property.FieldName == TikSpecialProperties.Id || property.IsReadOnly || property.ChangesOnItsOwn
                    || skip.Contains(property.FieldName))
                    continue;
                foreach (var (id, mine, theirs) in pairs)
                {
                    string? expected = property.GetEntityValue(mine);
                    string? actual = property.GetEntityValue(theirs);
                    if (expected == null || actual == null || string.Equals(expected, actual, StringComparison.Ordinal))
                        continue;
                    findings.Add(new TikEntityTransportFinding(other.Name, other.Type, property.FieldName,
                        property.PropertyName, TikEntityTransportFindingKind.ValueDiffers, id,
                        Mask(property, expected), Mask(property, actual),
                        ValueSuggestion(property, other)));
                    break;   // one row per field is enough to name it
                }
            }
        }

        // Rows paired by .id; a singleton's one row with the other's. An entity without .id pairs nothing.
        private static List<(string? Id, object Reference, object Other)> Pair(TikEntityMetadata metadata,
            IList<object> reference, IList<object> other)
        {
            var pairs = new List<(string?, object, object)>();
            if (metadata.IsSingleton)
            {
                if (reference.Count == 1 && other.Count == 1)
                    pairs.Add((null, reference[0], other[0]));
                return pairs;
            }
            var idProperty = metadata.IdProperty;
            if (idProperty == null)
                return pairs;
            var byId = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in other)
            {
                string? id = idProperty.GetEntityValue(row);
                if (id != null)
                    byId[id] = row;
            }
            foreach (var row in reference)
            {
                string? id = idProperty.GetEntityValue(row);
                if (id != null && byId.TryGetValue(id, out var match))
                    pairs.Add((id, row, match));
            }
            return pairs;
        }

        private static HashSet<string> AbsentSet(EntityRead read)
            => new HashSet<string>(read.Report?.FieldsAbsentFromEveryRow ?? (IEnumerable<string>)Array.Empty<string>(),
                StringComparer.Ordinal);

        private static Dictionary<string, TikUnparsedField> UnparsedByField(EntityRead read)
        {
            var result = new Dictionary<string, TikUnparsedField>(StringComparer.Ordinal);
            foreach (var u in read.Report?.UnparsedFields ?? (IEnumerable<TikUnparsedField>)Array.Empty<TikUnparsedField>())
                result[u.FieldName] = u;
            return result;
        }

        private static string? Mask(TikEntityPropertyAccessor property, string? value)
            => value == null ? null : property.IsSensitive ? "***" : value;

        // ── Suggestions: from the finding's kind and the transport family ─────

        private enum Family { Api, Cli, WinboxNative, Other }

        private static Family FamilyOf(TikConnectionType? type)
        {
            switch (type)
            {
                case TikConnectionType.Api:
                case TikConnectionType.ApiSsl:
                case TikConnectionType.Rest:
                case TikConnectionType.RestSsl:
                    return Family.Api;
                case TikConnectionType.Telnet:
                case TikConnectionType.Ssh:
                case TikConnectionType.MacTelnet:
                case TikConnectionType.WinboxCli:
                case TikConnectionType.WinboxCliMac:
                    return Family.Cli;
                case TikConnectionType.WinboxNative:
                case TikConnectionType.WinboxNativeMac:
                    return Family.WinboxNative;
                default:
                    return Family.Other;
            }
        }

        private static string AbsentSuggestion(TikEntityMetadata metadata, TikEntityPropertyAccessor property, EntityRead read)
        {
            switch (FamilyOf(read.Type))
            {
                case Family.Cli:
                    if (property.ValueType == typeof(bool))
                        return property.IsReadOnly || property.FieldName == "disabled"
                            ? "a flag already read by name on the CLI transports: a transport gap to report"
                            : "a status flag the router sets: IsReadOnly = true on the property (flags are read by name before RouterOS 7.20)";
                    var options = new List<string>();
                    if (!metadata.IncludeCliStats)
                        options.Add("IncludeCliStats = true on [TikEntity] (a counter)");
                    if (!property.IsSensitive)
                        options.Add("IsSensitive = true on the property (a secret)");
                    return options.Count > 0
                        ? "only on the CLI transports: " + string.Join(", or ", options)
                        : "IncludeCliStats and IsSensitive are both declared: a transport gap to report";
                case Family.WinboxNative:
                    return property.WinboxLabel == null
                        ? "only on WinBox native: WinboxLabel = \"<its WinBox label>\" on the property"
                        : "only on WinBox native, and the property's WinboxLabel \"" + property.WinboxLabel
                            + "\" is not in this RouterOS version's catalog, or names another field";
                default:
                    var unmapped = read.Report?.UnmappedFields;
                    return "this transport reports no such field: a version range (MinRouterOs / MaxRouterOs) or a rename "
                        + "(AlternateNames)"
                        + (unmapped != null && unmapped.Count > 0 ? "; it reports, unmapped: " + string.Join(", ", unmapped) : string.Empty);
            }
        }

        private static string UnparsedSuggestion(TikEntityPropertyAccessor property, string? rawValue)
        {
            if (property.ValueType == typeof(bool) && string.IsNullOrEmpty(rawValue) && !property.IsPresenceFlag)
                return "an empty word on a bool: IsPresenceFlag = true on the property (the binary API and REST print a set flag as a bare name)";
            return "the property's type cannot hold this transport's spelling: a transport gap to report, or a wider type";
        }

        private static string ValueSuggestion(TikEntityPropertyAccessor property, EntityRead read)
            => FamilyOf(read.Type) == Family.Cli && !property.IsFreeText
                ? "differs only on the CLI: IsFreeText = true on the property (a value with ';', '=', quotes or line breaks)"
                : "the transport reads another value: a transport gap to report";
    }

    /// <summary>What <see cref="TikEntityTransportCheck"/> found for one entity.</summary>
    public sealed class TikEntityTransportReport
    {
        internal TikEntityTransportReport(Type entityType, string entityPath, string reference, int referenceRowCount,
            IReadOnlyList<string> compared, IReadOnlyList<TikEntityTransportFinding> findings)
        {
            EntityType = entityType;
            EntityPath = entityPath;
            Reference = reference;
            ReferenceRowCount = referenceRowCount;
            Compared = compared;
            Findings = findings;
        }

        /// <summary>The entity checked.</summary>
        public Type EntityType { get; }

        /// <summary>Its menu.</summary>
        public string EntityPath { get; }

        /// <summary>The reference connection's transport name.</summary>
        public string Reference { get; }

        /// <summary>How many rows the reference read. With none, only a failed read can be found.</summary>
        public int ReferenceRowCount { get; }

        /// <summary>The transport names of the connections compared with the reference.</summary>
        public IReadOnlyList<string> Compared { get; }

        /// <summary>What differs, in the order the connections were named.</summary>
        public IReadOnlyList<TikEntityTransportFinding> Findings { get; }

        /// <summary>Whether anything differs.</summary>
        public bool HasFindings => Findings.Count > 0;

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> listing the findings, when there is one — the type
        /// <see cref="TikFakeConnection.AssertWasSent(string)"/> throws, so it works under any test framework.
        /// </summary>
        public void ThrowIfAnyFinding()
        {
            if (HasFindings)
                throw new InvalidOperationException(ToString());
        }

        /// <summary>One line per finding under a header naming the entity and the reference.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(EntityType.Name).Append(' ').Append(EntityPath).Append(" — reference ").Append(Reference)
                .Append(", ").Append(ReferenceRowCount.ToString(CultureInfo.InvariantCulture))
                .Append(ReferenceRowCount == 1 ? " row" : " rows");
            if (!HasFindings)
                sb.Append("; ").Append(string.Join(", ", Compared)).Append(": no difference");
            foreach (var finding in Findings)
                sb.AppendLine().Append("  ").Append(finding);
            return sb.ToString();
        }
    }

    /// <summary>What kind of difference a <see cref="TikEntityTransportFinding"/> is.</summary>
    public enum TikEntityTransportFindingKind
    {
        /// <summary>The reference read the field and this transport read it on no row.</summary>
        AbsentFromEveryRow,

        /// <summary>This transport read the field and the reference read it on no row.</summary>
        AbsentOnTheReferenceOnly,

        /// <summary>This transport printed a value the property's type cannot hold; the reference's could.</summary>
        Unparsed,

        /// <summary>The reference printed a value the property's type cannot hold; the other transport's could.</summary>
        UnparsedOnTheReferenceOnly,

        /// <summary>A writable field reads another value on the same row.</summary>
        ValueDiffers,

        /// <summary>The transport could not read the entity at all.</summary>
        ReadFailed,
    }

    /// <summary>One difference between a transport's read of an entity and the reference's.</summary>
    public sealed class TikEntityTransportFinding
    {
        internal TikEntityTransportFinding(string transport, TikConnectionType? connectionType, string? fieldName,
            string? propertyName, TikEntityTransportFindingKind kind, string? rowId, string? referenceValue, string? value,
            string suggestion)
        {
            Transport = transport;
            ConnectionType = connectionType;
            FieldName = fieldName;
            PropertyName = propertyName;
            Kind = kind;
            RowId = rowId;
            ReferenceValue = referenceValue;
            Value = value;
            Suggestion = suggestion;
        }

        /// <summary>The transport the finding is about — the reference itself for the <c>…OnTheReferenceOnly</c> kinds.</summary>
        public string Transport { get; }

        /// <summary>Its connection type; <c>null</c> for a transport written outside tik4net, or a test double.</summary>
        public TikConnectionType? ConnectionType { get; }

        /// <summary>The RouterOS field; <c>null</c> for <see cref="TikEntityTransportFindingKind.ReadFailed"/>.</summary>
        public string? FieldName { get; }

        /// <summary>The entity property that maps it.</summary>
        public string? PropertyName { get; }

        /// <summary>What differs.</summary>
        public TikEntityTransportFindingKind Kind { get; }

        /// <summary>The row (<c>.id</c>) a <see cref="TikEntityTransportFindingKind.ValueDiffers"/> was seen on.</summary>
        public string? RowId { get; }

        /// <summary>The reference's value (<see cref="TikEntityTransportFindingKind.ValueDiffers"/>); <c>***</c> for a secret.</summary>
        public string? ReferenceValue { get; }

        /// <summary>This transport's value, the unparsed word, or the read's error message; <c>***</c> for a secret.</summary>
        public string? Value { get; }

        /// <summary>The entity option that usually fixes it, or why none does.</summary>
        public string Suggestion { get; }

        /// <summary>What the read threw, for <see cref="TikEntityTransportFindingKind.ReadFailed"/>; otherwise <c>null</c>.
        /// A <see cref="TikPathNotMappedException"/> is WinBox native saying the menu has no window to reach.</summary>
        public Exception? Exception { get; internal set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            string what;
            switch (Kind)
            {
                case TikEntityTransportFindingKind.AbsentFromEveryRow: what = "absent from every row"; break;
                case TikEntityTransportFindingKind.AbsentOnTheReferenceOnly: what = "absent from every row of the reference"; break;
                case TikEntityTransportFindingKind.Unparsed: what = "unparsed '" + Shown(Value) + "'"; break;
                case TikEntityTransportFindingKind.UnparsedOnTheReferenceOnly: what = "unparsed on the reference '" + Shown(Value) + "'"; break;
                case TikEntityTransportFindingKind.ValueDiffers:
                    what = "\"" + Shown(ReferenceValue) + "\" vs \"" + Shown(Value) + "\" on " + (RowId ?? "the row"); break;
                default: what = "read failed: " + Value; break;
            }
            return Transport.PadRight(14) + " " + (FieldName ?? "(entity)").PadRight(16) + " " + what + "  -> " + Suggestion;
        }

        private const int ShownLength = 60;

        // A value as one readable line: control characters escaped (a file body, a binary file), and cut to its first
        // characters with its length. The properties keep the whole value.
        private static string Shown(string? value)
        {
            if (value == null)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                if (sb.Length >= ShownLength)
                    break;
                if (c == (char)10) sb.Append(@"\n");
                else if (c == (char)13) sb.Append(@"\r");
                else if (c == (char)9) sb.Append(@"\t");
                else if (char.IsControl(c)) sb.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Length >= ShownLength && value.Length > ShownLength
                ? sb.ToString() + "... (" + value.Length.ToString(CultureInfo.InvariantCulture) + " chars)"
                : sb.ToString();
        }
    }
}
