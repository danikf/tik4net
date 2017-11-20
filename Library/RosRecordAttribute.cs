using System;

namespace InvertedTomato.TikLink {
    /// <summary>
    /// Attribute that is used to decorate RouterOS record.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RosRecordAttribute : Attribute {
        /// <summary>
        /// Entity path in API notation.
        /// </summary>
        /// <example>
        /// /ip/firewall/mangle
        /// </example>
        public string Path { get; private set; }

        public RosRecordAttribute(string path) {
            if (null == path) {
                throw new ArgumentNullException(nameof(path));
            }

            Path = path;
        }
    }
}
