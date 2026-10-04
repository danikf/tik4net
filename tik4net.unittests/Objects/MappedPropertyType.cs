// MappedPropertyType.cs — the type a mapped property holds, for the convention tests: T for a TikField<T>
// (int?, string, TikDuration?, an enum?), the property type otherwise. Since 5.0 every built-in property is a
// TikField<T?>, and a test comparing PropertyType against typeof(bool) or typeof(string) would match nothing and
// pass having checked nothing.

using System;
using System.Reflection;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    internal static class MappedPropertyType
    {
        internal static Type Of(PropertyInfo property)
        {
            Type type = property.PropertyType;
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TikField<>)
                ? type.GetGenericArguments()[0]
                : type;
        }
    }
}
