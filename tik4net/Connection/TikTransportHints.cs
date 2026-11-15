using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Winbox;

namespace tik4net.Connection
{
    /// <summary>
    /// What tells one of tik4net's own transports how to read or write an entity: facts the O/R mapper takes from the
    /// entity's attributes, which never reach a router as words. They travel beside the command's parameters, from the
    /// mapper (<see cref="ITikTransportHintsCommand"/>) through <see cref="TikCommandDescriptor.Hints"/> to the transport,
    /// so a command's parameter list holds only words for the router, and a transport written outside tik4net, which
    /// cannot see this type, is never handed one.
    /// </summary>
    /// <remarks>
    /// Immutable, and built once per entity type (<c>TikEntityMetadata.ReadHints</c> / <c>LabelHints</c>). A transport
    /// that derives a descriptor from another one keeps the hints by building it with
    /// <see cref="TikCommandDescriptor.With"/>; a dropped hint fails silently (counters, secrets or flags read absent).
    /// </remarks>
    internal sealed class TikTransportHints
    {
        private static readonly IReadOnlyList<string> NoFields = Array.Empty<string>();

        /// <summary>No hints: a raw command, a hand-built one, a descriptor with no parent.</summary>
        internal static readonly TikTransportHints None =
            new TikTransportHints(false, false, NoFields, NoFields, false, (IReadOnlyDictionary<string, string>?)null);

        /// <summary>Creates the hints of one entity type.</summary>
        /// <param name="mergeStats">See <see cref="MergeStats"/>.</param>
        /// <param name="serializeRead">See <see cref="SerializeRead"/>.</param>
        /// <param name="freeTextFields">See <see cref="FreeTextFields"/>.</param>
        /// <param name="flagFields">See <see cref="FlagFields"/>.</param>
        /// <param name="showSensitive">See <see cref="ShowSensitive"/>.</param>
        /// <param name="winboxLabels">The entity's <c>api-name → WinBox label</c> pairs as the attributes write them
        /// (<c>"disk: Stop on Full"</c> included); normalised here to the names the <c>.jg</c> catalog files its fields
        /// under. <c>null</c> or empty for none.</param>
        internal TikTransportHints(bool mergeStats, bool serializeRead, IReadOnlyList<string> freeTextFields,
            IReadOnlyList<string> flagFields, bool showSensitive, IEnumerable<KeyValuePair<string, string>>? winboxLabels)
            : this(mergeStats, serializeRead, freeTextFields, flagFields, showSensitive,
                   WinboxFieldResolver.NormalizeLabelHints(winboxLabels))
        {
        }

        private TikTransportHints(bool mergeStats, bool serializeRead, IReadOnlyList<string> freeTextFields,
            IReadOnlyList<string> flagFields, bool showSensitive, IReadOnlyDictionary<string, string>? normalizedLabels)
        {
            MergeStats = mergeStats;
            SerializeRead = serializeRead;
            FreeTextFields = freeTextFields ?? NoFields;
            FlagFields = flagFields ?? NoFields;
            ShowSensitive = showSensitive;
            WinboxLabels = normalizedLabels;
            WinboxLabelsKey = normalizedLabels == null
                ? null
                : string.Join("|", normalizedLabels.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>
        /// The CLI transports read the entity twice — <c>print detail</c> and <c>print stats</c> — and merge the rows by
        /// <c>.id</c>, because a terminal print leaves the live counters out (<c>TikEntityAttribute.IncludeCliStats</c>).
        /// </summary>
        internal bool MergeStats { get; }

        /// <summary>
        /// The CLI transports read through <c>:serialize to=json</c>, since a field holds free text that the unescaped
        /// <c>as-value</c> form cannot represent (<c>TikPropertyAttribute.IsFreeText</c>). Requires RouterOS 7.13; an
        /// older router is read with <c>as-value</c> and the <see cref="FreeTextFields"/> are re-read row by row.
        /// </summary>
        internal bool SerializeRead { get; }

        /// <summary>
        /// The free-text fields the CLI transports re-read one by one with <c>get</c> on a router without
        /// <c>:serialize</c> (before 7.13). May be empty while <see cref="SerializeRead"/> is set: a read that wants the
        /// escaped form and has no field to re-read.
        /// </summary>
        internal IReadOnlyList<string> FreeTextFields { get; }

        /// <summary>
        /// The flag fields (<c>disabled</c> and the read-only bools) the CLI transports ask for by name on RouterOS before
        /// 7.20, whose <c>print as-value</c> leaves every flag out. Only names the menu knows are asked for.
        /// </summary>
        internal IReadOnlyList<string> FlagFields { get; }

        /// <summary>
        /// The CLI transports add <c>show-sensitive</c> to the print, since RouterOS 7 leaves secrets out of a terminal
        /// print unless asked (<c>TikPropertyAttribute.IsSensitive</c>). A menu that refuses the word (RouterOS 6) is
        /// read without it.
        /// </summary>
        internal bool ShowSensitive { get; }

        /// <summary>
        /// The entity's WinBox labels as <c>api-name → catalog name</c>, which WinBox native resolves a field through
        /// before its name heuristic, on reads and writes alike (<c>TikPropertyAttribute.WinboxLabel</c>). <c>null</c>
        /// when the entity labels no field.
        /// </summary>
        internal IReadOnlyDictionary<string, string>? WinboxLabels { get; }

        /// <summary>
        /// The labels as one string — the pairs sorted by API name, <c>name=label</c> joined by <c>|</c> — that a menu
        /// schema described under them is cached by, so two entities with the same labels share it. <c>null</c> without
        /// labels.
        /// </summary>
        internal string? WinboxLabelsKey { get; }

        /// <summary>These hints with <paramref name="fields"/> as the <see cref="FlagFields"/>.</summary>
        internal TikTransportHints WithFlagFields(IReadOnlyList<string> fields)
            => new TikTransportHints(MergeStats, SerializeRead, FreeTextFields, fields, ShowSensitive, WinboxLabels);

        /// <summary>These hints with <see cref="ShowSensitive"/> off: the read of a menu that refused the word.</summary>
        internal TikTransportHints WithoutShowSensitive()
            => ShowSensitive
                ? new TikTransportHints(MergeStats, SerializeRead, FreeTextFields, FlagFields, false, WinboxLabels)
                : this;

        /// <summary>These hints with <see cref="SerializeRead"/> on and no field to re-read.</summary>
        internal TikTransportHints WithSerializeRead()
            => new TikTransportHints(MergeStats, true, FreeTextFields, FlagFields, ShowSensitive, WinboxLabels);
    }

    /// <summary>
    /// A command that can carry <see cref="TikTransportHints"/>: tik4net's own command types for the transports that read
    /// them, and the test double. The O/R mapper sets them; the binary API's command does not implement it, since the API
    /// needs none.
    /// </summary>
    internal interface ITikTransportHintsCommand
    {
        /// <summary>The hints the command's runs hand to the transport; <see cref="TikTransportHints.None"/> by default.</summary>
        TikTransportHints Hints { get; set; }
    }
}
