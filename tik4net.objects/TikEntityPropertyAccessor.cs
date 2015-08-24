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
        private readonly TikEntityMetadata _owner;
        private bool _isReadOnly;

        public string PropertyName { get; private set; }

        public Type PropertyType { get; private set; }

        public string FieldName { get; private set; }

        public bool IsReadOnly
        {
            get { return _isReadOnly || _owner.IsReadOnly; }
        }
        public bool IsMandatory { get; private set; }

        public string DefaultValue { get; private set; }

        public bool UnsetWhenDefault { get; private set; }

        private PropertyInfo PropertyInfo { get; set; }

        public TikEntityPropertyAccessor(TikEntityMetadata owner, PropertyInfo propertyInfo)
        {
            _owner = owner;

            PropertyInfo = propertyInfo;

            //From property code
            PropertyName = propertyInfo.Name;
            PropertyType = propertyInfo.PropertyType;

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            _isReadOnly = (propertyInfo.SetMethod == null) || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
            IsMandatory = propertyAttribute.IsMandatory;
            if (propertyAttribute.DefaultValue != null)
                DefaultValue = propertyAttribute.DefaultValue;
            else
            {
                if (PropertyType.IsValueType)
                    DefaultValue = ConvertToString(Activator.CreateInstance(PropertyType)); //default value of value type. for example: (default)int
                else
                    DefaultValue = "";
            }
            UnsetWhenDefault = propertyAttribute.UnsetWhenDefault;
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

        public bool HasDefaultValue(object entity)
        {
            string propValue = GetEntityValue(entity);

            return (propValue == null) || (Convert.ToString(propValue) == DefaultValue);
        }

        public void SetEntityValue(object entity, string propValue)
        {
            PropertyInfo.SetValue(entity, ConvertFromString(propValue)); //NOTE: works even if setter is private
        }

        public string GetEntityValue(object entity)
        {
            object propValue = PropertyInfo.GetValue(entity);
            if (propValue == null)
                propValue = DefaultValue;   
            return ConvertToString(propValue);
        }

        //public string GetEntityValue(object entity)
        //{
        //    return GetEntityValue(entity, false);
        //}
    }
}
