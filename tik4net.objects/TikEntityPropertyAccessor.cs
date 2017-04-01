using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Accessor to one tik entity property.
    /// </summary>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadata"/>
    public sealed class TikEntityPropertyAccessor
    {
        private readonly TikEntityMetadata _owner;
        private bool _isReadOnly;

        /// <summary>
        /// Name of the property in C# code of the entity.
        /// </summary>
        public string PropertyName { get; private set; }

        /// <summary>
        /// Type of the property in C# code of the entity.
        /// </summary>
        public Type PropertyType { get; private set; }

        /// <summary>
        /// Name of the field in mikrotik router.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.FieldName"/>
        public string FieldName { get; private set; }

        /// <summary>
        /// If property (and mikrotik field) is R/O.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsReadOnly"/>
        public bool IsReadOnly
        {
            get { return _isReadOnly || _owner.IsReadOnly; }
        }

        /// <summary>
        /// If property (and mikrotik field) are madatory during load - should be present in resultset.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsMandatory"/>
        public bool IsMandatory { get; private set; }

        /// <summary>
        /// Defaukt value of the property.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.DefaultValue"/>
        public string DefaultValue { get; private set; }

        /// <summary>
        /// If value should be unset during update (save modified entity) when property contains default value (set to default will be called when false).
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.UnsetOnDefault"/>
        public bool UnsetOnDefault { get; private set; }

        private PropertyInfo PropertyInfo { get; set; }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="owner">Metadata of the owning entity.</param>
        /// <param name="propertyInfo">PropertyInfo of the accessed  entity property.</param>
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
            _isReadOnly = (propertyInfo.GetSetMethod() == null) || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
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
            UnsetOnDefault = propertyAttribute.UnsetOnDefault;
        }

        /// <summary>
        /// Readable description of the accessor.
        /// </summary>
        /// <returns>Readable description of the accessor.</returns>
        public override string ToString()
        {
            return PropertyName + "(" + FieldName + ")";
        }

        private object ConvertFromString(string strValue)
        {
            try
            {
                //convert to property real type            
                if (PropertyType == typeof(string))
                    return strValue;
                else if (PropertyType == typeof(TimeSpan))
                    return TikTimeHelper.FromTikTimeToTimeSpan(strValue);
                else if (PropertyType == typeof(int))
                    return int.Parse(strValue);
                else if (PropertyType == typeof(long))
                    return long.Parse(strValue);
                else if (PropertyType == typeof(bool))
                    return string.Equals(strValue, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(strValue, "yes", StringComparison.OrdinalIgnoreCase);
                else if (PropertyType.IsEnum)
                    return Enum.GetNames(PropertyType)
                        .Where(en => string.Equals(PropertyType.GetMember(en)[0].GetCustomAttribute<TikEnumAttribute>(false).Value, strValue, StringComparison.OrdinalIgnoreCase))
                        .Select(en => Enum.Parse(PropertyType, en, true))
                        .Single(); //TODO safer implementation
                                   //else if (PropertyType == typeof(Ipv4Address))
                                   //    return new Ipv4Address(strValue);
                                   //else if (PropertyType == typeof(Ipv4AddressWithSubnet))
                                   //    return new Ipv4AddressWithSubnet(strValue);
                                   //else if (PropertyType == typeof(MacAddress))
                                   //    return new MacAddress(strValue);
                else
                    throw new NotImplementedException(string.Format("Property type {0} not supported.", PropertyType));
            }
            catch(NotImplementedException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new FormatException(string.Format("Value '{0}' for property '{1}({2})' is not in expected format '{3}'.", strValue, PropertyName, FieldName, PropertyType), ex);
            }
        }

        private string ConvertToString(object propValue)
        {
            if (propValue is string)
                return (string)propValue;

            //convert to string used in mikrotik            
            if (PropertyType == typeof(string))
                return propValue.ToString();
            else if (PropertyType == typeof(TimeSpan))
                return TikTimeHelper.ToTikTime((int)((TimeSpan)propValue).TotalSeconds);
            else if (PropertyType == typeof(int))
                return ((int)propValue).ToString();
            else if (PropertyType == typeof(long))
                return ((long)propValue).ToString();
            else if (PropertyType == typeof(bool))
                return ((bool)propValue) ? "yes" : "no"; //TODO add attribute definition for support true/false
            else if (PropertyType.IsEnum)
                return PropertyType.GetMember(propValue.ToString())[0].GetCustomAttribute<TikEnumAttribute>(false).Value; //TODO safer implementation
            //else if (PropertyType == typeof(Ipv4Address))
            //    return ((Ipv4Address)propValue).Address;
            //else if (PropertyType == typeof(Ipv4AddressWithSubnet))
            //    return ((Ipv4AddressWithSubnet)propValue).Address;
            //else if (PropertyType == typeof(MacAddress))
            //    return ((MacAddress)propValue).Address;
            else
                throw new NotImplementedException(string.Format("Property type {0} not supported.", PropertyType));
        }

        /// <summary>
        /// Returns if accessed property of given <paramref name="entity"/> contains null or <see cref="DefaultValue"/>.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns>True if accessed property od given entity contains default value.</returns>
        public bool HasDefaultValue(object entity)
        {
            string propValue = GetEntityValue(entity);

            return (propValue == null) || (Convert.ToString(propValue) == DefaultValue);
        }

        /// <summary>
        /// Sets the value of accesed property on given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to be modified.</param>
        /// <param name="propValue">New property value.</param>
        public void SetEntityValue(object entity, string propValue)
        {
            PropertyInfo.SetValue(entity, ConvertFromString(propValue)); //NOTE: works even if setter is private
        }

        /// <summary>
        /// Gets the value of accesed property from given <paramref name="entity"/>.
        /// </summary>
        /// <param name="entity">Entity to read peroperty value from.</param>
        /// <returns>Property value from giuven entity</returns>
        public string GetEntityValue(object entity)
        {
            object propValue = PropertyInfo.GetValue(entity);
            if (propValue == null)
                propValue = DefaultValue;   
            return ConvertToString(propValue);
        }
    }
}
