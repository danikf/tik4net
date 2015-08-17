using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public sealed class TikEntityPropertyAccessor
    {
        public string PropertyName { get; private set; }

        public Type PropertyType { get; private set; }

        public string FieldName { get; private set; }

        public bool IsReadOnly { get; private set; }

        public bool IsMandatory { get; private set; }

        public string DefaultValue { get; private set; }

        private PropertyInfo PropertyInfo { get; set; }     
        
        public TikEntityPropertyAccessor(PropertyInfo propertyInfo)
        {
            PropertyInfo = propertyInfo;

            //From property code
            PropertyName = propertyInfo.Name;
            PropertyType = propertyInfo.PropertyType;

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            IsReadOnly = (propertyInfo.SetMethod == null) || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
            IsMandatory = propertyAttribute.IsMandatory;
            DefaultValue = propertyAttribute.DefaultValue;
        }

        public override string ToString()
        {
            return PropertyName + "(" + FieldName + ")";
        }

        private object ConvertFromString(string strValue)
        {
            //convert to property real type            
            if (PropertyType == typeof(string))
                return strValue;
            else if (PropertyType == typeof(int))
                return int.Parse(strValue);
            else if (PropertyType == typeof(long))
                return long.Parse(strValue);
            else if (PropertyType == typeof(bool))
                return bool.Parse(strValue);
            else
                throw new NotImplementedException(string.Format("Property type {0} not supported.", PropertyType));
        }

        private string ConvertToString(object propValue)
        {
            //convert to string used in mikrotik            
            if (PropertyType == typeof(string))
                return propValue.ToString();
            else if (PropertyType == typeof(int))
                return ((int)propValue).ToString();
            else if (PropertyType == typeof(long))
                return ((long)propValue).ToString();
            else if (PropertyType == typeof(bool))
                return ((bool)propValue) ? "true" : "false";
            else
                throw new NotImplementedException(string.Format("Property type {0} not supported.", PropertyType));
        }

        public void SetEntityValue(object entity, string propValue)
        {
            PropertyInfo.SetValue(entity, ConvertFromString(propValue)); //NOTE: works even if setter is private
        }

        public string GetEntityValue(object entity, bool presentDefaultAsNull)
        {
            object propValue = PropertyInfo.GetValue(entity);
            if (propValue == null)
                return null; //not set
            else if (presentDefaultAsNull && (Convert.ToString(propValue) == DefaultValue)) //has default value
                return null;
            else
                return ConvertToString(propValue);
        }
    }
}
