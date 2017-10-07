using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace tik4net.Objects
{
#if V35 || V40
    /// <summary>
    /// Extension class to implement string fuctions from newer .NET - to support .NET 3.5 build.
    /// </summary>
    public static class PropertyInfoExtensions
    {
        /// <summary>
        /// <see cref="PropertyInfo.SetValue(object, object, object[])"/>.
        /// </summary>
        public static void SetValue(this PropertyInfo propertyInfo, object obj, object value)
        {
            propertyInfo.SetValue(obj, value, null);
        }

        /// <summary>
        /// See <see cref="PropertyInfo.GetValue(object, object[])"/>.
        /// </summary>
        public static object GetValue(this PropertyInfo propertyInfo, object obj)
        {
            return propertyInfo.GetValue(obj, null);
        }

        /// <summary>
        /// See <see cref="MemberInfo.GetCustomAttributes(bool)"/> - takes first of <typeparamref name="TAttribute"/> or null.
        /// </summary>
        public static TAttribute GetCustomAttribute<TAttribute>(this MemberInfo propertyInfo,  bool inherit)
            where TAttribute: Attribute
        {
            return propertyInfo.GetCustomAttributes(inherit).OfType<TAttribute>().FirstOrDefault();
        }
    }
#endif
}
