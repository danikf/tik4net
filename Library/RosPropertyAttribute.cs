using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute to mark object property as readable/writable from/to mikrotik router.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class RosPropertyAttribute : Attribute {
        /// <summary>
        /// Property name, according to RouterOS
        /// </summary>
        public string RosName { get; private set; }

        /// <summary>
        /// If the property is read-only - this will cause the property to ignore during PUT operations
        /// </summary>
        public bool IsReadOnly { get; set; }



        /// <summary>
        /// Gets a value indicating whether this property is mandatory - should be present in loading resultset.
        /// </summary>
        [Obsolete("TODO: Is this needed? Adds any value?")]
        public bool IsRequired { get; set; }

        /// <summary>
        /// Property default value (if is different from type default).
        /// </summary>
        [Obsolete("TODO: Is this needed? Adds any value?")]
        public string DefaultValue { get; set; } // TODO: This needed? Adds any value?

        /// <summary>
        /// If unset command should be called when saving modified object and marked property contains <see cref="DefaultValue"/> or null (set to default value will be used when false).
        /// </summary>
        [Obsolete("TODO: Is this needed? Adds any value?")]
        public bool UnsetOnDefault { get; set; } // TODO: This needed? Adds any value?


        /// <summary>
        /// Initializes a new instance of the <see cref="RosPropertyAttribute"/> class.
        /// </summary>
        /// <param name="name">Name of the property (on mikrotik).</param>
        public RosPropertyAttribute(string name) {
            if (null == name) {
                throw new ArgumentNullException(nameof(name));
            }

            RosName = name;
        }
    }
}

