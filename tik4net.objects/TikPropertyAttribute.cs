using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    /// <summary>
    /// Attribute to mark object property as auto-readable from mikrotik router.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TikPropertyAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the property (on mikrotik).
        /// </summary>
        /// <value>The name of the property.</value>
        public string FieldName { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this property is mandatory.
        /// </summary>
        /// <value><c>true</c> if mandatory; otherwise, <c>false</c>.</value>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// Gets the edit mode of property.
        /// </summary>
        /// <value>The edit mode of property.</value>
        public bool IsReadOnly { get; set; }

        public string DefaultValue { get; set; }

        public bool UnsetWhenDefault { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikPropertyAttribute"/> class.
        /// </summary>
        /// <param name="fieldName">Name of the property (on mikrotik).</param>
        /// <param name="propertyType">Data type of the property.</param>
        /// <param name="mandatory">if set to <c>true</c> [mandatory].</param>
        /// <param name="editMode">The property edit mode.</param>
        public TikPropertyAttribute(string fieldName, bool isMandatory, bool isReadOnly, string defaultValue, bool unsetWhenDefault)
        {
            Guard.ArgumentNotNullOrEmptyString(fieldName, "fieldName");

            FieldName = fieldName;
            IsMandatory = isMandatory;
            IsReadOnly = isReadOnly;
            DefaultValue = defaultValue;
            UnsetWhenDefault = unsetWhenDefault;
        }

        public TikPropertyAttribute(string fieldName)
        {
            Guard.ArgumentNotNullOrEmptyString(fieldName, "fieldName");
            FieldName = fieldName;
            IsMandatory = false;
            IsReadOnly = false;
            DefaultValue = null;
            UnsetWhenDefault = false;
        }
    }
}

