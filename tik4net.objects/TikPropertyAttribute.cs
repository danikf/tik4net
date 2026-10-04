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
        /// Gets a value indicating whether this property is mandatory - should be present in loading resultset.
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
        /// The property's default, written <b>the way the router spells it</b> — <c>"no"</c>/<c>"yes"</c> for a
        /// <see cref="bool"/>, the <c>[TikEnum]</c> value for an enum, never a C# literal like <c>"False"</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The mapper serializes the property and compares the result against this string to decide whether
        /// the field is worth sending on <c>/add</c>: equal means "leave it out and let the router choose".
        /// A value the serializer can never produce — the C# spelling of a bool, say — therefore matches
        /// nothing, and the field is sent on every create and every set.
        /// </para>
        /// <para>
        /// <b>Whose default this is depends on whether the property can be null.</b> A nullable property
        /// distinguishes "untouched" from a value, so the <i>router's</i> default belongs here. A
        /// non-nullable one cannot: an untouched <c>bool</c> is <c>false</c>, and declaring the router's
        /// <c>"yes"</c> makes the comparison fail forever, so the field is sent on every create and the row
        /// gets the opposite of what the router would have chosen. 56 properties are in that position today
        /// (see <c>EntityDefaultValueConventionTests</c>); the fix is <c>bool?</c>, not a different string,
        /// because declaring <c>"no"</c> instead would silently drop an <i>explicitly assigned</i>
        /// <c>false</c>, which is worse: a two-state type cannot carry the protocol's three states.
        /// </para>
        /// <para>
        /// <b>It is also what a field the row does not carry reads as</b> — a field this RouterOS version does not
        /// have, or one a CLI print leaves out. With no default declared, a nullable property (<c>string?</c>,
        /// <c>int?</c>, an enum <c>?</c>) reads <c>null</c> and a non-nullable one its type's default. A
        /// property that must be able to say "the router did not print this" is therefore nullable and declares
        /// none. (A reference type counts as nullable when it is annotated so; one compiled without nullable
        /// annotations reads <c>""</c>.)
        /// </para>
        /// <para>
        /// <b>On a <see cref="TikField{T}"/> property it is documentation only</b> — the router's default, for the
        /// reader and the entity catalog. It is never read into the property (a field the row lacks is
        /// <see cref="TikFieldState.Absent"/>) and never compared on a save (an add sends what was assigned).
        /// </para>
        /// </remarks>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// If unset command should be called when saving modified object and marked property contains <see cref="DefaultValue"/> or null (set to default value will be used when false).
        /// </summary>
        /// <remarks>
        /// Not allowed on a <see cref="TikField{T}"/> property: assigning <c>null</c> to a loaded value unsets it, and an
        /// Absent value — which "equals the default" to this rule — must never be unset on the strength of silence.
        /// </remarks>
        public bool UnsetOnDefault { get; set; }

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
        /// Without this marker the empty value parses as <c>false</c>, so such a field reads back as
        /// <c>false</c> whatever its true state is. With it, an empty value means <b>true</b> — which is
        /// what the router said. Absence still leaves a nullable property <c>null</c>: the router reported
        /// nothing, and the mapper does not invent a <c>false</c>.
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
        /// Marks a matcher RouterOS can <b>negate</b> with a leading <c>!</c> — <c>src-address=!10.0.0.0/8</c>,
        /// <c>in-interface=!ether1</c>, <c>connection-state=!established,related</c>.
        /// <para>
        /// On a <see cref="TikField{T}"/> property the <c>!</c> is then not part of the value: a load reads it as
        /// <see cref="TikField{T}.IsNegated"/> and parses the rest as <c>T</c>, and a negated value
        /// (<see cref="TikValue{T}.Not"/>) is written with it. Every transport spells it the same way — the API, REST
        /// and the CLI print the <c>!</c>, and WinBox native carries it as the field's <c>not</c> flag. Without the
        /// marker a leading <c>!</c> is part of the value: a comment or a name may start with one.
        /// </para>
        /// <para>
        /// The <c>!</c> negates the whole value, a list included (<c>dst-port=!22,8291</c>). A list whose members the
        /// router negates one by one declares <see cref="NegatableMembers"/> as well or instead.
        /// </para>
        /// </summary>
        public bool Negatable { get; set; }

        /// <summary>
        /// Marks a <see cref="TikValueList{T}"/> field whose <b>members</b> RouterOS negates one by one —
        /// <c>hotspot=!from-client,http</c>, logging <c>topics=info,!debug</c>, <c>tcp-flags=syn,!ack</c>.
        /// <para>
        /// A load reads each member's <c>!</c> as that item's <see cref="TikValue{T}.IsNegated"/>, and a save writes it.
        /// Without the marker a negated item is refused before anything is sent — the router either refuses it
        /// (<c>connection-state=established,!related</c>: <c>invalid value for argument state</c>) or reads the <c>!</c>
        /// as part of a word.
        /// </para>
        /// <para>
        /// A field that also takes a <c>!</c> on the whole list (<c>tcp-flags</c>) declares <see cref="Negatable"/> too;
        /// the router spells that one as a bare leading element: <c>tcp-flags=!,syn,!ack</c>.
        /// </para>
        /// </summary>
        public bool NegatableMembers { get; set; }

        /// <summary>
        /// Marks a <see cref="NegatableMembers"/> list on which RouterOS's text <c>set</c> replaces only the half it names:
        /// the plain members when the value names a plain one, the negated members when it names a negated one
        /// (<c>tcp-flags</c>, measured on 6.49.13 and 7.24.5 over the API, REST and the CLI:
        /// <c>!,syn,!ack</c> + <c>set rst</c> → <c>rst,!ack</c>).
        /// <para>
        /// An update of a loaded row that would leave such a half on the router — the loaded value has members of a kind
        /// the new one has none of — is refused before anything is sent, unless the transport writes structured data
        /// (<see cref="TikConnectionCapability.StructuredWrites"/>). A value naming both kinds, and any add, is exact.
        /// </para>
        /// <para>
        /// Not caught: <c>unset</c> hides the field but keeps both halves on the router, so after an unset the next
        /// one-kind <c>set</c> brings the old half back, and the row loads as if the field had none.
        /// </para>
        /// </summary>
        public bool SetKeepsUnnamedHalf { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikPropertyAttribute"/> class.
        /// </summary>
        /// <param name="fieldName">Name of the property (on mikrotik).</param>
        /// <param name="isMandatory">If this property is mandatory - should be present in loading resultset</param>
        /// <param name="isReadOnly">If the property is R/O (should not be updated during save modified entity).</param>
        /// <param name="defaultValue">Property default value (if is different from type default).</param>
        /// <param name="unsetOnDefault">If unset command should be called when saving modified object and marked property contains <see cref="DefaultValue"/> or null (set to default value will be used when false).</param>
        public TikPropertyAttribute(string fieldName, bool isMandatory, bool isReadOnly, string defaultValue, bool unsetOnDefault)
        {
            Guard.ArgumentNotNullOrEmptyString(fieldName, "fieldName");

            FieldName = fieldName;
            IsMandatory = isMandatory;
            IsReadOnly = isReadOnly;
            DefaultValue = defaultValue;
            UnsetOnDefault = unsetOnDefault;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikPropertyAttribute"/> class.
        /// </summary>
        /// <param name="fieldName">Name of the property (on mikrotik).</param>
        public TikPropertyAttribute(string fieldName)
        {
            Guard.ArgumentNotNullOrEmptyString(fieldName, "fieldName");
            FieldName = fieldName;
            IsMandatory = false;
            IsReadOnly = false;
            DefaultValue = null;
            UnsetOnDefault = false;
        }
    }
}

