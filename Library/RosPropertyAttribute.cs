using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute to mark object property as readable/writable from/to mikrotik router.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class RosPropertyAttribute : Attribute {
        /// <summary>
        /// Gets the name of the property (on mikrotik).
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// RouterOS data type.
        /// </summary>
        public RosDataType RosData { get; set; }

        /// <summary>
        /// Gets a value indicating whether this property is mandatory - should be present in loading resultset.
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// If the property is R/O (should not be updated during save modified entity).
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Property default value (if is different from type default).
        /// </summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// If unset command should be called when saving modified object and marked property contains <see cref="DefaultValue"/> or null (set to default value will be used when false).
        /// </summary>
        public bool UnsetOnDefault { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RosPropertyAttribute"/> class.
        /// </summary>
        /// <param name="name">Name of the property (on mikrotik).</param>
        public RosPropertyAttribute(string name, RosDataType dataType = RosDataType.String, bool isReadOnly=false) {
            if (null == name) {
                throw new ArgumentNullException(nameof(name));
            }

            // TODO: make dataType required

            Name = name;
            RosData = dataType;
            IsReadOnly = isReadOnly;
        }
    }
}

