using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Attribute to mark object property as readable/writable from/to mikrotik router.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class TikPropertyAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the property (on mikrotik).
        /// </summary>
        /// <value>The name of the property.</value>
        public string FieldName { get; private set; }

        /// <summary>
        /// Other names RouterOS prints the same field under — a field another RouterOS version renamed.
        /// </summary>
        /// <remarks>
        /// <para>RouterOS 7 prints <c>/ip/service</c>'s access list as <c>available-from</c>, RouterOS 6 as
        /// <c>address</c>; one property declared <c>[TikProperty("address", AlternateNames = new[] { "available-from" })]</c>
        /// reads it on both. A second property per name cannot do this: with a declared default, the name the
        /// router does not print reads as that default, and a write has to use the name this router accepts.</para>
        /// <para><b>Read:</b> the first of <see cref="FieldName"/>, then these in order, that the row carries; the
        /// default applies only when it carries none of them. <b>Write:</b> the name the entity was read under,
        /// so a loaded entity saves under the name its router uses; an entity that was never read (a create, a
        /// singleton saved without loading) sends <see cref="FieldName"/> — so make that the name both versions
        /// accept where there is one. A <c>.proplist</c> requests every name.</para>
        /// </remarks>
        public string[]? AlternateNames { get; set; }

        /// <summary>
        /// Whether a row must carry this field — for the <c>.id</c>, the row's key: a load of a row without one throws.
        /// </summary>
        /// <value><c>true</c> if mandatory; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// Not allowed on a <see cref="TikField{T}"/> property: a load must not fail because a row lacks a field (another
        /// RouterOS version, another row type) — the field reads <see cref="TikFieldState.Absent"/> instead — and what an
        /// add sends is what the caller assigned. The router refuses an add that lacks a field it requires.
        /// </remarks>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// If the property is R/O (should not be updated during save modified entity).
        /// </summary>
        /// <value>The edit mode of property.</value>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// The router's default for the field, written <b>the way the router spells it</b> — <c>"no"</c>/<c>"yes"</c> for a
        /// <see cref="bool"/>, the <c>[TikEnum]</c> value for an enum, never a C# literal like <c>"False"</c>.
        /// </summary>
        /// <remarks>
        /// Documentation only, for the reader and the entity catalog. It is never read into the property (a field the row
        /// lacks is <see cref="TikFieldState.Absent"/>) and never compared on a save (an add sends what was assigned).
        /// </remarks>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Marks a property whose value is free-form text and may therefore contain the CLI output
        /// format's own separators — <c>;</c>, <c>=</c> or newlines. A file body (<c>/file contents</c>)
        /// or a script source are the typical cases.
        /// <para>
        /// RouterOS's <c>as-value</c> output has no escaping, so such a value is indistinguishable from
        /// further fields and records and silently shreds the whole result set: measured on 7.23.2,
        /// <c>/file/print</c> returned 27 rows over the binary API and <b>1</b> over every CLI transport.
        /// Marking the property makes the O/R mapper read the entity through <c>:serialize to=json</c> on
        /// CLI transports, where the escaping is exact. Other transports are unaffected — they frame
        /// values themselves and never had the ambiguity.
        /// </para>
        /// <para>
        /// The JSON read needs RouterOS 7.13+; on older routers the CLI transports detect the refusal and
        /// fall back to <c>as-value</c>, i.e. to the shredding this flag exists to avoid — so on such a
        /// router, do not read these entities over a CLI transport.
        /// </para>
        /// <para>
        /// <b>It costs something, so mark only what needs it.</b> <c>:serialize to=json</c> fixes the
        /// framing and changes the VALUES: a duration comes back as a date counted from the Unix epoch
        /// (<c>ttl=1d</c> arrives as <c>1970-01-02 00:00:00</c>, <c>52w1d</c> as <c>1971-01-01 00:00:00</c>)
        /// and sub-second precision is truncated — <c>arp-interval=100ms</c> reads as <c>00:00:00</c>.
        /// <c>CliValueNormalizer</c> converts the dates back for the duration fields it knows about; the
        /// lost milliseconds cannot be recovered. This is why <c>comment</c> is not marked automatically
        /// even though a comment carrying a <c>;</c> does shred: it would put every entity on the lossy
        /// path to fix a rare case.
        /// </para>
        /// </summary>
        /// <seealso cref="tik4net.TikSpecialProperties.CliJson"/>
        public bool IsFreeText { get; set; }

        /// <summary>
        /// Marks a <see cref="bool"/> field the router stores as a valueless <b>presence flag</b>: the
        /// binary API reports it as <c>name=</c> (the word is present, the value empty) when the flag is
        /// set, and omits the word entirely when it is not.
        /// <para>
        /// Being printed is the value here, so an empty value means <b>true</b> — which is what the router
        /// said. A field the row lacks still reads <see cref="TikFieldState.Absent"/>: the router reported
        /// nothing, and the mapper does not invent a <c>false</c>. A <see cref="bool"/> printed empty and not
        /// marked reads <see cref="TikFieldState.Unparsed"/>, so the diagnostics show the missing marker;
        /// on any other type an empty value carries nothing and reads Absent.
        /// </para>
        /// <para>
        /// Only the binary API and REST use the valueless form; the CLI transports and
        /// <c>WinboxNative</c> report the same field as <c>true</c>, which parses the same way with or
        /// without this flag. Marking the property therefore makes every transport agree rather than
        /// changing one of them. Writes are unaffected — <c>=name=yes</c> is what the router accepts.
        /// </para>
        /// <para><c>/routing/table</c>'s <c>fib</c> is the field this exists for.</para>
        /// </summary>
        public bool IsPresenceFlag { get; set; }

        /// <summary>
        /// The field's label in WinBox (its <c>.jg</c> catalog name) — <c>"New Packet Mark"</c> for
        /// <c>new-packet-mark</c>. The WinBox-native transport resolves the field through this label in the router's
        /// own catalog before any name heuristic; a session field override still wins, and a label this RouterOS
        /// version's catalog does not have is ignored (the heuristic then decides). The mapper sends labels only to a
        /// connection declaring <see cref="TikConnectionCapability.FieldLabels"/>; other transports never see them.
        /// </summary>
        /// <remarks>
        /// A label that repeats within one window — two deck panes' <c>Stop on Full</c>, two tabs' <c>Port</c> — is
        /// written with its pane kind or tab: <c>"memory: Stop on Full"</c>. Labels are the stable identity across
        /// RouterOS versions (a field's key and wire type can change under the same label). The labels are
        /// generated from the catalog and checked against it by <c>EntityJgCatalogProbe</c>.
        /// </remarks>
        public string? WinboxLabel { get; set; }

        /// <summary>
        /// The oldest RouterOS release this field is known to be in, as the lab measured it, or <c>null</c> when it is in every
        /// release tik4net is tested on (6.49 and 7.x) or in every release its menu is in. A bound, not the release that introduced it: <c>"7"</c> is "in 7.x,
        /// not in 6.49"; <c>"7.22"</c> is "not in 7.21.5, in 7.24.5". For documentation and test gating; the mapper does
        /// not act on it.
        /// </summary>
        public string? MinRouterOs { get; set; }

        /// <summary>
        /// The newest RouterOS release this field is known to be in, or <c>null</c> when the current release still has it:
        /// <c>"6"</c> is "in 6.49, gone in 7"; <c>"7.21"</c> is "in 7.21.5, gone by 7.24.5". As <see cref="MinRouterOs"/>,
        /// a measured bound for documentation and test gating.
        /// </summary>
        public string? MaxRouterOs { get; set; }

        /// <summary>
        /// Marks a writable field whose value the <b>router changes by itself</b> between two reads —
        /// <c>/system/clock</c>'s <c>time</c> and <c>date</c>.
        /// <para>
        /// A <see cref="TikSaveMode.FullUpdate"/> save re-reads the entity and sends every field that differs from
        /// that read. Such a field always differs, so without this marker the save writes back the value it was
        /// loaded with — setting the clock back by however long the entity was held. With it, the field is sent
        /// only when the caller changed it since the load (or when the entity was never loaded, where the value it
        /// holds is all the intent there is). <see cref="TikSaveMode.OnlyChanges"/> already compares against the
        /// load and is unaffected.
        /// </para>
        /// </summary>
        public bool ChangesOnItsOwn { get; set; }

        /// <summary>
        /// Marks a field RouterOS treats as <b>sensitive</b> — a secret, a password, a pre-shared key.
        /// <para>
        /// RouterOS 7 leaves such values out of a terminal <c>print</c> unless it is given <c>show-sensitive</c>,
        /// while the binary API and REST return them. An entity with a sensitive property is therefore read with
        /// <c>show-sensitive</c> on the CLI transports (<c>Telnet</c>/<c>Ssh</c>/<c>MacTelnet</c>/<c>WinboxCli</c>/
        /// <c>WinboxCliMac</c>), which then read the same value as every other transport. RouterOS 6 has no such
        /// word and prints secrets without it; a menu that refuses the word is read without it. Other transports
        /// are unaffected. What the router returns still depends on the user's <c>sensitive</c> policy.
        /// </para>
        /// </summary>
        public bool IsSensitive { get; set; }

        /// <summary>
        /// What RouterOS's text <c>set</c> does with the members of this <see cref="TikNegatableValue{T}"/> list that the
        /// new value does not name; <see cref="TikListSetBehaviour.Exact"/> (the default) when it keeps none of them.
        /// </summary>
        /// <remarks>
        /// <see cref="TikListSetBehaviour.KeepsUnnamedHalf"/> (<c>tcp-flags</c>): an update that would leave a half on the
        /// router is refused before sending, except over <see cref="TikConnectionCapability.StructuredWrites"/>.
        /// <see cref="TikListSetBehaviour.KeepsUnnamedMembers"/> (a user group's <c>policy</c>): an update sends the members
        /// it leaves out as denied, and is refused on a row that was never loaded. Either needs list items of
        /// <see cref="TikNegatableValue{T}"/>; any other declaration is refused when the entity is first used. The enum's
        /// members document each behaviour in full.
        /// </remarks>
        public TikListSetBehaviour SetBehaviour { get; set; }

        /// <summary>
        /// Marks a list (a <see cref="TikValueList{T}"/>) that RouterOS keeps as a set: it prints the members sorted in an
        /// order of its own and drops a duplicate, whatever the order it was given (the radio chain lists, <c>2,0,1</c> reads
        /// <c>0,1,2</c>; a DHCP alert's <c>valid-server</c> MACs; <c>Docs/findings-value-lists.md</c> §3).
        /// <para>
        /// A list merge (<see cref="TikListMerge{TEntity}"/>) then compares the field by its distinct members, so the same
        /// members in another order, or with a duplicate, are no change. Without the flag a list compares in order, because
        /// that order is usually configuration: the first DNS server a DHCP client gets is its primary. A save still sends
        /// the members in the order the list holds them.
        /// </para>
        /// <para>
        /// A list of an enum's members needs no flag: RouterOS keeps such a list as a bitmask, and a merge already compares
        /// it by members. Declared on one anyway, the flag also makes a duplicate member no change. Declaring it on a field
        /// that is not a list is refused when the entity is first used.
        /// </para>
        /// </summary>
        public bool RouterSortsMembers { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikPropertyAttribute"/> class.
        /// </summary>
        /// <param name="fieldName">Name of the property (on mikrotik).</param>
        public TikPropertyAttribute(string fieldName)
        {
            Guard.ArgumentNotNullOrEmptyString(fieldName, "fieldName");
            FieldName = fieldName;
        }
    }
}

