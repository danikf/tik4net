using System;
using System.Linq;
using System.Reflection;
using tik4net.Objects;

namespace tik4net.Testing
{
    /// <summary>
    /// Fluent builder extensions for constructing fake entity instances in tests.
    /// <para>
    /// MikroTik entity properties that are populated by the router (marked
    /// <c>IsReadOnly = true</c>) have private setters and cannot be set via normal
    /// object initializers.  These extensions use reflection to set them directly,
    /// mirroring what the O/R mapper does when it deserializes a real API response.
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
        /// The typed value to assign. Must be assignment-compatible with the C# property type
        /// (e.g. pass <c>true</c> for <c>bool</c>, not the wire string <c>"yes"</c>).
        /// </param>
        /// <exception cref="ArgumentException">No property with the given field name exists on <typeparamref name="TEntity"/>.</exception>
        /// <exception cref="InvalidOperationException">Property has no setter at all (computed property).</exception>
        public static TEntity WithValue<TEntity>(this TEntity entity, string tikFieldName, object value)
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

            if (propInfo == null)
                throw new InvalidOperationException(
                    $"Property '{accessor.PropertyName}' not found on {typeof(TEntity).Name}.");

            // SetValue works even when the setter is private (same approach used internally
            // by TikEntityPropertyAccessor.SetEntityValue).
            propInfo.SetValue(entity, value);
            return entity;
        }
    }
}
