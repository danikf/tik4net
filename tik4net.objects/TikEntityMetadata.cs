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
        private List<TikEntityPropertyDescriptor> _properties;

        public IReadOnlyList<TikEntityPropertyDescriptor> Properties
        {
            get { return _properties.AsReadOnly(); }
        }        

        public string EntityPath { get; private set; }
        public bool IsReadOnly { get; private set; }
        public bool IncludeDetails { get; private set; }

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
                .Select(propInfo => new TikEntityPropertyDescriptor(propInfo))
                .ToList();
        }
    }
}
