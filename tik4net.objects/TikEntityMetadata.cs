using System;
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
    public class TikEntityMetadata
    {
        private Dictionary<string, TikEntityPropertyAccessor> _properties; //<field_name_on_mikrotik, propertyAccessor>
        private Type _entityType;

        /// <summary>
        /// All properties of the entity which are decorated by <seealso cref="TikPropertyAttribute"/>
        /// </summary>
        public IEnumerable<TikEntityPropertyAccessor> Properties
        {
            get { return _properties.Values; }
        }

        /// <summary>
        /// entity path in API notation (e.q. /system/resource)
        /// </summary>
        /// <seealso cref="TikEntityAttribute.EntityPath"/>
        public string EntityPath { get; private set; }

        /// <summary>
        /// If the whole entity is R/O.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IsReadOnly"/>
        public bool IsReadOnly { get; private set; }

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
        /// If all <see cref="Properties"/> should be explicitly listed via .proplist option.
        /// </summary>
        /// <seealso cref="TikEntityAttribute.IncludeProplist"/>
        public bool IncludeProplist { get; private set; }

        /// <summary>
        /// If entity exists in single instance.
        /// </summary>
        public bool IsSingleton { get; private set; }

        /// <summary>
        /// The .id property of the entity or null (if no property is decorated by <see cref="TikPropertyAttribute.FieldName"/> = .id).
        /// </summary>
        public TikEntityPropertyAccessor IdProperty
        {
            get
            {
                if (HasIdProperty)
                    return GetPropertyDescriptor(TikSpecialProperties.Id);
                else
                    return null;
            }
        }

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
        public TikEntityMetadata(Type entityType)
        {
            TikEntityAttribute entityAttribute = (TikEntityAttribute)entityType.GetCustomAttributes(true).FirstOrDefault(a => a is TikEntityAttribute);
            if (entityAttribute == null)
                throw new ArgumentException("Entity class must be decorated by TikEntityAttribute attribute.");

            _entityType = entityType;

            EntityPath = entityAttribute.EntityPath;
            IsReadOnly = entityAttribute.IsReadOnly;
            IsOrdered = entityAttribute.IsOrdered;
            IncludeDetails = entityAttribute.IncludeDetails;
            IncludeProplist = entityAttribute.IncludeProplist;
            IsSingleton = entityAttribute.IsSingleton;

            //properties
            _properties = entityType.GetProperties()
                .Where(propInfo => propInfo.GetCustomAttribute<TikPropertyAttribute>(true) != null)
                .Select(propInfo => new TikEntityPropertyAccessor(this, propInfo))
                .ToDictionary(propDescriptor => propDescriptor.FieldName);                
        }

        private TikEntityPropertyAccessor GetPropertyDescriptor(string fieldName) 
        {
            TikEntityPropertyAccessor result;
            if (_properties.TryGetValue(fieldName, out result))
                return result;
            else
                throw new KeyNotFoundException(string.Format("Property for field '{0}' not found in '{1}' class.", fieldName, _entityType));
        }
    }
}
