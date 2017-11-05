using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute to set mikrotik code for enum values used as field types on mikrotik objects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class RosEnumAttribute : Attribute {
        /// <summary>
        /// Mikrotik enum value.
        /// </summary>
        public string Value { get; private set; }
        
        public RosEnumAttribute(string value) {
            if (string.IsNullOrEmpty(value)) {
                throw new ArgumentException(nameof(value));
            }

            Value = value;
        }
    }
}
