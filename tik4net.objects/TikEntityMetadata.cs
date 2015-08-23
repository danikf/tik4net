using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace tik4net.Objects
{
    public class TikEntityMetadata
    {
        private Dictionary<string, TikEntityPropertyAccessor> _properties;

        public IEnumerable<TikEntityPropertyAccessor> Properties
        {
            get { return _properties.Values; }
        }        

        public string EntityPath { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool IncludeDetails { get; private set; }

        public TikEntityPropertyAccessor IdProperty
        {
            get { return GetPopertyDescriptor(".id"); }
        }

        public TikEntityMetadata(Type entityType)
        {
            TikEntityAttribute entityAttribute = (TikEntityAttribute)entityType.GetCustomAttributes(true).FirstOrDefault(a => a is TikEntityAttribute);
            if (entityAttribute == null)
                throw new ArgumentException("Entity class must be decorated by TikEntityAttribute attribute.");

            EntityPath = entityAttribute.EntityPath;
            IsReadOnly = entityAttribute.IsReadOnly;
            IncludeDetails = entityAttribute.IncludeDetails;

            //properties
            _properties = entityType.GetProperties()
                .Where(propInfo => propInfo.GetCustomAttribute<TikPropertyAttribute>(true) != null)
                .Select(propInfo => new TikEntityPropertyAccessor(this, propInfo))
                .ToDictionary(propDescriptor => propDescriptor.FieldName);                
        }

        public TikEntityPropertyAccessor GetPopertyDescriptor(string propertyName)
        {
            return _properties[propertyName];
        }
    }
}
