using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace tik4net.Objects
{
    /// <summary>
    /// Metadata of one mikrotik entity (scaned via reflection of entity class and its attributes).
    /// Entity class must be decorated by <seealso cref="TikEntityAttribute"/> and every managed property
    /// should be decoraded by <seealso cref="TikPropertyAttribute"/>.
    /// </summary>
    /// <seealso cref="TikEntityAttribute"/>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadataCache"/>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public class TikEntityMetadata
    {
        private Dictionary<string, TikEntityPropertyAccessor> _properties; //<field_name_on_mikrotik, propertyAccessor>

        /// <summary>
        /// All properties of the entity which are decorated by <seealso cref="TikPropertyAttribute"/>
        /// </summary>
        public IEnumerable<TikEntityPropertyAccessor> Properties
        {
            get { return _properties.Values; }
        }

        /// <summary>
        /// entity path in API notation (e.q. /system/resource), always absolute.
        /// </summary>
        /// <remarks>
        /// The leading slash is added when the entity declares the path without one — 42 of the shipped
        /// entities do. Every transport already tolerates it (each normalizes on its way to the wire), but
        /// anything that COMPARES the path has to remember to, and one that forgets gets a silent miss
        /// rather than an error. Normalizing here means the mapper hands out one spelling.
        /// </remarks>
        /// <seealso cref="TikEntityAttribute.EntityPath"/>
        public string EntityPath { get; private set; }

        /// <summary>
        /// Suffix added to the entity path when loading, e.g. <c>/print</c>.
        /// </summary>
        public string LoadCommand { get; private set; }

        /// <summary>
        /// Parameter format (when parameter itself is set to <see cref="TikCommandParameterFormat.Default"/>) during  load operation.
        /// </summary>
        public TikCommandParameterFormat LoadDefaultParameterFormat { get; private set; }

        /// <summary>
        /// The write verbs the RouterOS menu offers, as the entity declares them.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.SupportedOperations"/>
        public TikEntityOperations SupportedOperations { get; private set; }

        /// <summary>
        /// True when the menu offers every verb in <paramref name="operations"/>.
        /// </summary>
        /// <param name="operations">One verb, or several combined — all of them must be supported.</param>
        /// <remarks>
        /// Same "all of the requested flags" reading as
        /// <see cref="TikConnectionCapabilityExtensions.Supports(ITikConnection, TikConnectionCapability)"/>,
        /// so a combined value asks one question rather than several.
        /// </remarks>
        public bool Supports(TikEntityOperations operations)
            => (SupportedOperations & operations) == operations;

        /// <summary>
        /// True when no mapped property may be written, because the menu offers neither <c>add</c> nor
        /// <c>set</c>. This — not "the entity supports nothing" — is what makes a field read-only by
        /// inheritance: <c>/ppp/active</c> answers true here and still supports
        /// <see cref="TikEntityOperations.Remove"/>.
        /// </summary>
        public bool AreFieldsReadOnly
            => !Supports(TikEntityOperations.Add) && !Supports(TikEntityOperations.Set);

        /// <summary>
        /// If the whole entity is R/O.
        /// </summary>
        /// <remarks>
        /// Kept for the same reason as <see cref="TikEntityAttribute.IsReadOnly"/>: a caller still on the
        /// bool gets a message naming the replacement rather than an unresolved-name error. Ask
        /// <see cref="AreFieldsReadOnly"/> for "may a field be written", and
        /// <see cref="Supports"/> for one verb.
        /// </remarks>
        [Obsolete("A single bool cannot express a menu that allows remove but not add/set. Use "
                + "AreFieldsReadOnly to ask whether fields can be written, or Supports(TikEntityOperations.X) "
                + "to ask about one verb - see https://github.com/danikf/tik4net/issues/84.", error: true)]
        public bool IsReadOnly => SupportedOperations == TikEntityOperations.None;

        /// <summary>
        /// If entity list is ordered (move operation does make sense).
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IsOrdered"/>
        public bool IsOrdered { get; private set; }

        /// <summary>
        /// If =detail= option should be used during entity load.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IncludeDetails"/>
        public bool IncludeDetails { get; private set; }

        /// <summary>
        /// If the entity has live counter fields that require a separate <c>print stats</c> query
        /// on CLI transports.  API/REST already get counters via <c>detail</c>.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IncludeCliStats"/>
        public bool IncludeCliStats { get; private set; }

        /// <summary>
        /// If all <see cref="Properties"/> should be explicitly listed via .proplist option.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IncludeProplist"/>
        public bool IncludeProplist { get; private set; }

        /// <summary>
        /// If any of <see cref="Properties"/> holds free-form text and the entity must therefore be read
        /// as JSON on CLI transports, whose as-value format cannot escape it.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsFreeText"/>
        public bool HasFreeTextProperties { get; private set; }

        /// <summary>
        /// True when at least one property is a secret (<see cref="TikPropertyAttribute.IsSensitive"/>), so the entity
        /// is read with <c>show-sensitive</c> on CLI transports, which otherwise leave secrets out.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsSensitive"/>
        public bool HasSensitiveProperties { get; private set; }

        /// <summary>
        /// The entity's WinBox labels as the <see cref="TikSpecialProperties.WinboxLabels"/> marker value
        /// (<c>api-name=Label|…</c>, every name a property is read under), or <c>null</c> when no property declares one.
        /// </summary>
        internal string? WinboxLabelsMarker { get; private set; }

        /// <summary>
        /// The field names of the entity's flags — <c>disabled</c> and every read-only <c>bool</c> property
        /// (<c>dynamic</c>, <c>running</c>, <c>invalid</c>, …). RouterOS before 7.20 leaves them out of the CLI's
        /// <c>print as-value</c>; the list goes to the CLI transports as <see cref="TikSpecialProperties.CliFlags"/>
        /// so they can ask for them by name.
        /// </summary>
        /// <remarks>
        /// A writable <c>bool</c> other than <c>disabled</c> is an ordinary field that every version prints
        /// (<c>log=false</c> on a firewall rule, 7.17). A presence flag is left out: it reads back as an empty
        /// value, never <c>true</c>/<c>false</c>.
        /// </remarks>
        internal IEnumerable<string> CliFlagFields { get; private set; }

        /// <summary>
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; private set; }

        /// <summary>
        /// The .id property of the entity or null (if no property is decorated by <see cref="TikPropertyAttribute.FieldName"/> = .id).
        /// </summary>
        public TikEntityPropertyAccessor? IdProperty
            => _properties.TryGetValue(TikSpecialProperties.Id, out var id) ? id : null;

        /// <summary>The property of the <c>dynamic</c> field, or <c>null</c> when the entity has none.</summary>
        internal TikEntityPropertyAccessor? DynamicProperty { get; private set; }

        /// <summary>
        /// Determines if entity has property for .id field (property which is decorated by <see cref="TikPropertyAttribute.FieldName"/> = .id)
        /// </summary>
        public bool HasIdProperty
        {
            get { return _properties.ContainsKey(TikSpecialProperties.Id); }
        }

        /// <summary>
        /// .ctor. Performs reflection scan ot given entity type and its properties.
        /// </summary>
        /// <param name="entityType">Type of the entity.</param>
        /// <remarks>Slow operation.</remarks>
        internal TikEntityMetadata(Type entityType)
        {
            TikEntityAttribute? entityAttribute = (TikEntityAttribute?)entityType.GetTypeInfo().GetCustomAttributes(true).FirstOrDefault(a => a is TikEntityAttribute);
            if (entityAttribute == null)
                throw new ArgumentException("Entity class must be decorated by TikEntityAttribute attribute.");

            EntityPath = string.IsNullOrEmpty(entityAttribute.EntityPath) || entityAttribute.EntityPath.StartsWith("/", StringComparison.Ordinal)
                ? entityAttribute.EntityPath
                : "/" + entityAttribute.EntityPath;
            LoadCommand = entityAttribute.LoadCommand;
            LoadDefaultParameterFormat = entityAttribute.LoadDefaultParameterFormat;
            SupportedOperations = entityAttribute.SupportedOperations;
            IsOrdered = entityAttribute.IsOrdered;
            IncludeDetails = entityAttribute.IncludeDetails;
            IncludeCliStats = entityAttribute.IncludeCliStats;
            IncludeProplist = entityAttribute.IncludeProplist;
            IsSingleton = entityAttribute.IsSingleton;

            //properties
            _properties = entityType.GetTypeInfo().GetProperties()
                .Where(propInfo => propInfo.GetCustomAttribute<TikPropertyAttribute>(true) != null)
                .Select(propInfo => new TikEntityPropertyAccessor(this, propInfo))
                .ToDictionary(propDescriptor => propDescriptor.FieldName);

            DynamicProperty = _properties.TryGetValue("dynamic", out var dynamicProperty) ? dynamicProperty : null;
            HasFreeTextProperties = Properties.Any(p => p.IsFreeText);
            HasSensitiveProperties = Properties.Any(p => p.IsSensitive);
            CliFlagFields = Properties
                .Where(p => p.ValueType == typeof(bool) && !p.IsPresenceFlag && (p.IsReadOnly || p.FieldName == "disabled"))
                .Select(p => p.FieldName)
                .ToList();
            var labels = Properties.Where(p => p.WinboxLabel != null)
                .SelectMany(p => new[] { p.FieldName }.Concat(p.AlternateNames).Select(name => name + "=" + p.WinboxLabel))
                .ToList();
            WinboxLabelsMarker = labels.Count > 0 ? string.Join("|", labels) : null;
        }
    }
}
