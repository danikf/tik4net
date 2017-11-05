using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute that is used to decorate tik entity class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RosRecordAttribute : Attribute {
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
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; set; }

        /// <summary>
        /// If entity should be loaded with =detail= option.
        /// </summary>
        public bool IncludeDetails { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RosRecordAttribute"/> class. ReadOnly = false, IncludeDetails=false.
        /// </summary>
        /// <param name="path">The entity path in API notation (/ip/firewall/mangle).</param>
        public RosRecordAttribute(string path) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException(nameof(path));
            }

            Path = path;
        }
    }

}
