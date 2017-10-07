using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Attribute to set mikrotik code for enum values used as field types on mikrotik objects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class TikEnumAttribute:Attribute
    {
        /// <summary>
        /// Mikrotik enum value.
        /// </summary>
        public string Value { get; private set; }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="value">Mikrotik enum value (name).</param>
        public TikEnumAttribute(string value)
        {
            Value = value;
        }
    }
}
