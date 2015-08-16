using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public class TikEntityPropertyDescriptor
    {
        public string PropertyName { get; private set; }

        public Type PropertyType { get; private set; }

        public string FieldName { get; private set; }

        public bool IsReadOnly { get; private set; }

        public bool IsMandatory { get; private set; }

        public string DefaultValue { get; private set; }

        public PropertyInfo PropertyIfo { get; private set; }     
        
        public TikEntityPropertyDescriptor(PropertyInfo propertyInfo)
        {
            PropertyIfo = propertyInfo;

            //From property code
            PropertyName = propertyInfo.Name;
            PropertyType = propertyInfo.PropertyType;

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            IsReadOnly = (propertyInfo.SetMethod == null) || (propertyAttribute.IsReadOnly);
            IsMandatory = propertyAttribute.IsMandatory;
            DefaultValue = propertyAttribute.DefaultValue;
        }

        public override string ToString()
        {
            return PropertyName + "(" + FieldName + ")";
        }
    }
}
