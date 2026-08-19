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
        /// Gets a value indicating whether this property is mandatory - should be present in loading resultset.
        /// </summary>
        /// <value><c>true</c> if mandatory; otherwise, <c>false</c>.</value>
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
        /// </remarks>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// If unset command should be called when saving modified object and marked property contains <see cref="DefaultValue"/> or null (set to default value will be used when false).
        /// </summary>
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

