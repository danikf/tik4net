using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Attribute that is used to decorate tik entity class.
    /// </summary>
    /// <seealso cref="TikConnectionExtensions"/>
    /// <seealso cref="TikEntityObjectsExtensions"/>
    /// <seealso cref="TikEntityAttribute"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class TikEntityAttribute : Attribute
    {
        /// <summary>
        /// Gets the entity path in API notation (/ip/firewall/mangle).
        /// </summary>
        /// <value>The entity path.</value>
        public string EntityPath { get; private set; }

        /// <summary>
        /// Sufix added to entity path when loading. eq. /print
        /// Default: /print
        /// </summary>
        public string LoadCommand { get; set; }

        /// <summary>
        /// Parameter format (when parameter itself is set to <see cref="TikCommandParameterFormat.Default"/>) during  load operation.
        /// Default: <see cref="TikCommandParameterFormat.Filter"/>.
        /// </summary>
        public TikCommandParameterFormat LoadDefaultParameterFormat { get; set; }

        /// <summary>
        /// The write verbs the RouterOS menu offers. Default: <see cref="TikEntityOperations.All"/> —
        /// seeded by every constructor, so an entity states what it <i>lacks</i> by narrowing this.
        /// </summary>
        /// <remarks>
        /// The mapper checks this before it builds a command, and throws
        /// <see cref="InvalidOperationException"/> naming the verb and the path when the menu does not offer
        /// it. A menu whose rows can be dropped but whose fields cannot be written — <c>/ppp/active</c>,
        /// <c>/ip/hotspot/active</c>, the registration tables — declares
        /// <c>SupportedOperations = TikEntityOperations.Remove</c>; every mapped property is then read-only
        /// (see <see cref="TikEntityMetadata.AreFieldsReadOnly"/>) while
        /// <see cref="TikConnectionExtensions.Delete">Delete</see> works.
        /// </remarks>
        /// <seealso cref="TikEntityOperations"/>
        public TikEntityOperations SupportedOperations { get; set; }

        /// <summary>
        /// If the whole entity is R/O.
        /// </summary>
        /// <remarks>
        /// Replaced by <see cref="SupportedOperations"/> in 4.0. The two share one storage — reading this
        /// answers <c>SupportedOperations == None</c>, writing it sets <c>None</c> or <c>All</c> — so a
        /// class that sets both keeps whichever named argument the compiler applies last.
        /// <para>
        /// <b>It stays in the frozen 4.0 surface deliberately.</b> Removing it would give a caller coming
        /// from 3.x <c>CS0117</c> — a name that does not exist on the attribute — where keeping it hands
        /// them the replacement and the mapping between the two. The cost is one property on an attribute
        /// nobody can compile against; that is worth less than the migration message it carries.
        /// </para>
        /// </remarks>
        [Obsolete("Use SupportedOperations: IsReadOnly = true becomes SupportedOperations = "
                + "TikEntityOperations.None, and false becomes TikEntityOperations.All. The bool was "
                + "replaced because it cannot express a menu that allows remove but not add/set - "
                + "/ppp/active and the registration tables are SupportedOperations = "
                + "TikEntityOperations.Remove - see https://github.com/danikf/tik4net/issues/84.",
                error: true)]
        public bool IsReadOnly
        {
            get { return SupportedOperations == TikEntityOperations.None; }
            set { SupportedOperations = value ? TikEntityOperations.None : TikEntityOperations.All; }
        }

        /// <summary>
        /// If entity list is ordered (move operation does make sense)
        /// </summary>
        /// <remarks>
        /// Whether the menu <i>has</i> an order. Whether reordering is permitted is
        /// <see cref="TikEntityOperations.Move"/> in <see cref="SupportedOperations"/>; both are checked,
        /// this one first.
        /// </remarks>
        public bool IsOrdered { get; set; }

        /// <summary>
        /// If entity should be loaded with =detail= option.
        /// </summary>
        public bool IncludeDetails { get; set; }

        /// <summary>
        /// If the entity has live counter fields (bytes/packets/rx-byte/tx-byte…) that are only
        /// returned by CLI's <c>print stats as-value</c> mode.  When true, CLI transports perform
        /// two print queries (detail + stats) and merge the results by <c>.id</c>.
        /// API and REST transports already receive counters via <c>detail</c> — they ignore this flag.
        /// Default: false.
        /// </summary>
        public bool IncludeCliStats { get; set; }

        /// <summary>
        /// If entity fields should be listed explicitly via .proplist option.
        /// </summary>
        public bool IncludeProplist { get; set; }

        /// <summary>
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikEntityAttribute"/> class.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        /// <param name="loadCommand">Sufix added to entity path when loading. eq. /print</param>
        /// <param name="loadDefaultParameterFormat">Parameter format (when parameter itself is set to <see cref="TikCommandParameterFormat.Default"/>) during  load operation.</param>
        /// <param name="isReadOnly">If the whole entity is R/O. Coarse: maps to <see cref="TikEntityOperations.None"/> or <see cref="TikEntityOperations.All"/> — set <see cref="SupportedOperations"/> instead to state a menu's verbs exactly.</param>
        /// <param name="isOrdered">If entity list is ordered (move operation does make sense).</param>
        /// <param name="includeDetails">If entity should be loaded with =detail= option.</param>
        /// <param name="isSingleton">If entity exists in single instance</param>
        public TikEntityAttribute(string entityPath, string loadCommand, TikCommandParameterFormat loadDefaultParameterFormat, bool isReadOnly, bool isOrdered, bool includeDetails, bool isSingleton)
        {
            Guard.ArgumentNotNullOrEmptyString(entityPath, "entityPath");
            EntityPath = entityPath;
            LoadCommand = loadCommand;
            LoadDefaultParameterFormat = loadDefaultParameterFormat;
            // Seeded like the other ctor and then narrowed, rather than assigning the obsolete IsReadOnly:
            // one place decides what the bool means, and it is the property's own get/set pair.
            SupportedOperations = isReadOnly ? TikEntityOperations.None : TikEntityOperations.All;
            IsOrdered = isOrdered;
            IncludeDetails = includeDetails;
            IsSingleton = isSingleton;            
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikEntityAttribute"/> class.
        /// SupportedOperations = <see cref="TikEntityOperations.All"/>, IncludeDetails = false.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        public TikEntityAttribute(string entityPath)
        {
            Guard.ArgumentNotNullOrEmptyString(entityPath, "entityPath");
            EntityPath = entityPath;
            LoadCommand = "/print";
            LoadDefaultParameterFormat = TikCommandParameterFormat.Filter;

            SupportedOperations = TikEntityOperations.All;
            IsOrdered = false;
            IncludeDetails = false;
            IncludeProplist = false;
            IsSingleton = false;
        }
    }

}
