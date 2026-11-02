using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using tik4net.Objects;

namespace tik4net.Testing
{
    /// <summary>
    /// Fluent builder extensions for constructing fake entity instances in tests.
    /// <para>
    /// Entity properties the router populates (<c>.id</c>, <c>dynamic</c>, <c>actual-interface</c>, …) have private
    /// setters and cannot be set in an object initializer. These extensions set them by reflection, as the O/R mapper
    /// does when it reads a real answer.
    /// </para>
    /// <example>
    /// <code>
    /// var entry = new FirewallAddressList
    ///     {
    ///         List    = "BLACKLIST",
    ///         Address = "10.0.0.1",
    ///     }
    ///     .WithId("*1")
    ///     .WithValue("dynamic", false);
    /// </code>
    /// </example>
    /// </summary>
    public static class TikFakeEntityExtensions
    {
        /// <summary>
        /// Sets the <c>.id</c> property on the entity and returns the entity (fluent).
        /// Equivalent to <c>.WithValue(".id", id)</c>.
        /// </summary>
        /// <typeparam name="TEntity">Entity type decorated with <see cref="TikEntityAttribute"/>.</typeparam>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="id">Value to assign (e.g. <c>"*1"</c>).</param>
        public static TEntity WithId<TEntity>(this TEntity entity, string id)
            where TEntity : new()
            => entity.WithValue(TikSpecialProperties.Id, id);

        /// <summary>
        /// Sets any property — including read-only ones with private setters — identified
        /// by its MikroTik field name (the value passed to <see cref="TikPropertyAttribute"/>).
        /// Returns the entity for fluent chaining.
        /// </summary>
        /// <typeparam name="TEntity">Entity type decorated with <see cref="TikEntityAttribute"/>.</typeparam>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="tikFieldName">
        /// The MikroTik field name as declared in <see cref="TikPropertyAttribute.FieldName"/>,
        /// e.g. <c>".id"</c>, <c>"dynamic"</c>, <c>"actual-interface"</c>.
        /// </param>
        /// <param name="value">
        /// The typed value to assign: the plain value the field holds (pass <c>true</c> for a <c>TikField&lt;bool?&gt;</c>,
        /// not the wire string <c>"yes"</c>; a number is converted to the field's number type), or the field itself
        /// (a <c>TikField&lt;T&gt;</c>). <c>null</c> leaves the field absent, as a router that does not print it.
        /// </param>
        /// <exception cref="ArgumentException">No property with the given field name exists on <typeparamref name="TEntity"/>,
        /// or <paramref name="value"/> is not of a kind the field holds.</exception>
        /// <exception cref="InvalidOperationException">Property has no setter at all (computed property).</exception>
        public static TEntity WithValue<TEntity>(this TEntity entity, string tikFieldName, object? value)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var accessor = metadata.Properties.FirstOrDefault(p => p.FieldName == tikFieldName);
            if (accessor == null)
                throw new ArgumentException(
                    $"No TikProperty with field name '{tikFieldName}' found on {typeof(TEntity).Name}. " +
                    $"Available fields: {string.Join(", ", metadata.Properties.Select(p => p.FieldName))}",
                    nameof(tikFieldName));

            var propInfo = typeof(TEntity).GetProperty(
                accessor.PropertyName,
                BindingFlags.Public | BindingFlags.Instance);

            if (propInfo == null || propInfo.SetMethod == null)
                throw new InvalidOperationException(
                    $"Property '{accessor.PropertyName}' on {typeof(TEntity).Name} has no setter.");

            // SetValue works even when the setter is private, as the O/R mapper's own reads set them.
            propInfo.SetValue(entity, ToPropertyType(propInfo.PropertyType, value, tikFieldName));
            return entity;
        }

        // The value as the property's own type: as it is when it already is one; null as the type's default (an absent
        // field, or a null reference); otherwise through the type's implicit conversion from the value it wraps
        // (TikField<T>'s from T), converting a number to that T first. Reflection applies none of these by itself.
        private static object? ToPropertyType(Type propertyType, object? value, string tikFieldName)
        {
            if (value == null)
                return propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null;
            if (propertyType.IsInstanceOfType(value))
                return value;

            foreach (var conversion in propertyType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "op_Implicit" && m.ReturnType == propertyType))
            {
                var from = conversion.GetParameters()[0].ParameterType;
                var wrapped = Nullable.GetUnderlyingType(from) ?? from;
                if (wrapped.IsInstanceOfType(value))
                    return conversion.Invoke(null, new[] { value });
                if (value is IConvertible && IsNumber(value.GetType()) && IsNumber(wrapped))
                {
                    try
                    {
                        return conversion.Invoke(null, new[] { Convert.ChangeType(value, wrapped, CultureInfo.InvariantCulture) });
                    }
                    catch (OverflowException ex)
                    {
                        throw new ArgumentException($"{value} does not fit the field '{tikFieldName}' ({wrapped.Name}).", nameof(value), ex);
                    }
                }
            }
            throw new ArgumentException(
                $"The field '{tikFieldName}' is a {Describe(propertyType)}; a {value.GetType().Name} cannot be assigned to it.",
                nameof(value));
        }

        private static bool IsNumber(Type type)
            => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
               || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
               || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

        private static string Describe(Type type)
            => type.IsGenericType
                ? type.Name.Substring(0, type.Name.IndexOf('`')) + "<" + string.Join(", ", type.GetGenericArguments().Select(Describe)) + ">"
                : type.Name;
    }
}
