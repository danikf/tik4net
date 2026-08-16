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
        /// The type actually converted to and from the wire: <see cref="PropertyType"/>, or the type inside it
        /// when the property is a <see cref="Nullable{T}"/>.
        /// </summary>
        public Type ValueType { get; private set; }

        /// <summary>
        /// True when the property can hold <c>null</c> as a value distinct from any of its values — i.e. it is
        /// a <see cref="Nullable{T}"/>.
        /// </summary>
        /// <remarks>
        /// This is the difference that lets the mapper tell "the caller said nothing about this field" from
        /// "the caller asked for the value that happens to be the default". On a non-nullable property the two
        /// are the same state, so one of them is always misread — see <c>MapperValueSemanticsTests</c>.
        /// </remarks>
        public bool IsNullable { get; private set; }

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

        /// <summary>
        /// If the field holds free-form text that can contain the CLI as-value format's own separators,
        /// and the entity must therefore be read as JSON on CLI transports.
        /// </summary>
        /// <seealso cref="TikPropertyAttribute.IsFreeText"/>
        public bool IsFreeText { get; private set; }

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
            ValueType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
            IsNullable = ValueType != PropertyType;

            //From TikPropertyAttribute attribute
            var propertyAttribute = propertyInfo.GetCustomAttribute<TikPropertyAttribute>(true);
            if (propertyAttribute == null)
                throw new ArgumentException("Property must be decorated by TikPropertyAttribute.", "propertyInfo");
            FieldName = propertyAttribute.FieldName;
            _isReadOnly =
                (propertyInfo.SetMethod == null)
                || (!propertyInfo.CanWrite) || (propertyAttribute.IsReadOnly);
            IsMandatory = propertyAttribute.IsMandatory;
            if (propertyAttribute.DefaultValue != null)
                DefaultValue = propertyAttribute.DefaultValue;
            else if (IsNullable)
                // A nullable property that declares no default HAS no default: its unset state is null, and
                // null is already "do not send". Computing one from the underlying type (which would give a
                // bool? the value "no") would put back exactly the conflation nullability removes.
                DefaultValue = null;
            else if (PropertyType.GetTypeInfo().IsValueType)
                DefaultValue = ConvertToString(Activator.CreateInstance(PropertyType)); //default value of value type. for example: (default)int
            else
                DefaultValue = "";
            UnsetOnDefault = propertyAttribute.UnsetOnDefault;
            IsFreeText = propertyAttribute.IsFreeText;
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
                // A nullable property is the only one that can carry "the router did not report this field"
                // as itself. Everything else has to fall through and be parsed, including the empty string —
                // a valueless presence flag reads back as false, and that is existing behaviour, not this.
                if (IsNullable && strValue == null)
                    return null;

                //convert to property real type
                if (ValueType == typeof(string))
                    return strValue;
                else if (ValueType == typeof(TimeSpan))
                    return TikTimeHelper.FromTikTimeToTimeSpan(strValue);
                else if (ValueType == typeof(int))
                    return int.Parse(strValue);
                else if (ValueType == typeof(long))
                    return long.Parse(strValue);
                else if (ValueType == typeof(byte))
                    return byte.Parse(strValue);
                else if (ValueType == typeof(bool))
                    return string.Equals(strValue, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(strValue, "yes", StringComparison.OrdinalIgnoreCase);
                else if (ValueType.GetTypeInfo().IsEnum)
                {
                    if (ValueType.GetCustomAttribute<FlagsAttribute>() != null && strValue.Contains(','))
                    {
                        long result = 0;
                        foreach (string part in strValue.Split(','))
                        {
                            string trimmed = part.Trim();
                            string name = Enum.GetNames(ValueType).FirstOrDefault(en =>
                                string.Equals(ValueType.GetRuntimeField(en).GetCustomAttribute<TikEnumAttribute>(false)?.Value,
                                    trimmed, StringComparison.OrdinalIgnoreCase));
                            if (name == null)
                                throw new FormatException(string.Format("Unknown flags enum value '{0}' for type {1}.", trimmed, ValueType.Name));
                            result |= Convert.ToInt64(Enum.Parse(ValueType, name, true));
                        }
                        return Enum.ToObject(ValueType, result);
                    }
                    else
                    {
                        return Enum.GetNames(ValueType)
                            .Where(en => string.Equals(ValueType.GetRuntimeField(en).GetCustomAttribute<TikEnumAttribute>(false)?.Value,
                                strValue, StringComparison.OrdinalIgnoreCase))
                            .Select(en => Enum.Parse(ValueType, en, true))
                            .Single();
                    }
                }
                                   //else if (ValueType == typeof(Ipv4Address))
                                   //    return new Ipv4Address(strValue);
                                   //else if (ValueType == typeof(Ipv4AddressWithSubnet))
                                   //    return new Ipv4AddressWithSubnet(strValue);
                                   //else if (ValueType == typeof(MacAddress))
                                   //    return new MacAddress(strValue);
                else
                    throw new NotImplementedException(string.Format("Property type {0} not supported.", ValueType));
            }
            catch(NotImplementedException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new FormatException(string.Format("Value '{0}' for property '{1}({2})' is not in expected format '{3}'.", strValue, PropertyName, FieldName, ValueType), ex);
            }
        }

        private string ConvertToString(object propValue)
        {
            // Null reaches here only from a nullable property that was never assigned. It has no wire form —
            // the point is that nothing is sent — so it stays null all the way out to the caller.
            if (propValue == null)
                return null;

            if (propValue is string)
                return (string)propValue;

            //convert to string used in mikrotik            
            if (ValueType == typeof(string))
                return propValue.ToString();
            else if (ValueType == typeof(TimeSpan))
                return TikTimeHelper.ToTikTime((int)((TimeSpan)propValue).TotalSeconds);
            else if (ValueType == typeof(int))
                return ((int)propValue).ToString();
            else if (ValueType == typeof(long))
                return ((long)propValue).ToString();
            else if (ValueType == typeof(bool))
                return ((bool)propValue) ? "yes" : "no"; //TODO add attribute definition for support true/false
            else if (ValueType.GetTypeInfo().IsEnum)
            {
                if (ValueType.GetCustomAttribute<FlagsAttribute>() != null)
                {
                    long intValue = Convert.ToInt64(propValue);
                    if (intValue == 0)
                    {
                        string zeroName = Enum.GetNames(ValueType)
                            .FirstOrDefault(en => Convert.ToInt64(Enum.Parse(ValueType, en)) == 0);
                        return zeroName != null
                            ? ValueType.GetRuntimeField(zeroName).GetCustomAttribute<TikEnumAttribute>(false)?.Value ?? ""
                            : "";
                    }
                    return string.Join(",", Enum.GetNames(ValueType)
                        .Where(en => {
                            long v = Convert.ToInt64(Enum.Parse(ValueType, en));
                            return v != 0 && (intValue & v) == v;
                        })
                        .Select(en => ValueType.GetRuntimeField(en).GetCustomAttribute<TikEnumAttribute>(false)?.Value)
                        .Where(v => v != null));
                }
                else
                {
                    return ValueType.GetRuntimeField(propValue.ToString()).GetCustomAttribute<TikEnumAttribute>(false).Value;
                }
            }
            //else if (ValueType == typeof(Ipv4Address))
            //    return ((Ipv4Address)propValue).Address;
            //else if (ValueType == typeof(Ipv4AddressWithSubnet))
            //    return ((Ipv4AddressWithSubnet)propValue).Address;
            //else if (ValueType == typeof(MacAddress))
            //    return ((MacAddress)propValue).Address;
            else
                throw new NotImplementedException(string.Format("Property type {0} not supported.", ValueType));
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

            // A null NULLABLE property means "nothing was said about this field", and that has to survive to
            // the caller as null so the save path can leave the field out. A null reference property keeps the
            // old substitution: a string has no unset state to distinguish, and turning its null into null
            // here would change what every existing entity sends.
            if (propValue == null && !IsNullable)
                propValue = DefaultValue;

            return ConvertToString(propValue);
        }
    }
}
