using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute that is used to decorate tik entity class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class TikRecordAttribute : Attribute {
        /// <summary>
        /// Gets the entity path in API notation (/ip/firewall/mangle).
        /// </summary>
        /// <value>The entity path.</value>
        public string Path { get; private set; }

        /// <summary>
        /// If the whole entity is R/O.
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// If entity list is ordered (move operation does make sense)
        /// </summary>
        public bool IsOrdered { get; set; }

        /// <summary>
        /// If entity should be loaded with =detail= option.
        /// </summary>
        public bool IncludeDetails { get; set; }

        /// <summary>
        /// If entity fields should be listed explicitly via .proplist option.
        /// </summary>
        public bool IncludeProplist { get; set; }

        /// <summary>
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikRecordAttribute"/> class.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        /// <param name="isReadOnly">If the whole entity is R/O.</param>
        /// <param name="isOrdered">If entity list is ordered (move operation does make sense).</param>
        /// <param name="includeDetails">If entity should be loaded with =detail= option.</param>
        /// <param name="isSingleton">If entity exists in single instance</param>
        public TikRecordAttribute(string entityPath, bool isReadOnly, bool isOrdered, bool includeDetails, bool isSingleton) {
            if (string.IsNullOrEmpty(entityPath)) {
                throw new ArgumentException(nameof(entityPath));
            }

            Path = entityPath;
            IsReadOnly = isReadOnly;
            IsOrdered = isOrdered;
            IncludeDetails = includeDetails;
            IsSingleton = isSingleton;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikRecordAttribute"/> class. ReadOnly = false, IncludeDetails=false.
        /// </summary>
        /// <param name="entityPath">The entity path in API notation (/ip/firewall/mangle).</param>
        public TikRecordAttribute(string entityPath) {
            if (string.IsNullOrEmpty(entityPath)) {
                throw new ArgumentException(nameof(entityPath));
            }

            Path = entityPath;
            IsReadOnly = false;
            IsOrdered = false;
            IncludeDetails = false;
            IncludeProplist = false;
            IsSingleton = false;
        }
    }

}
