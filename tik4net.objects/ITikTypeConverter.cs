using System;

namespace tik4net.Objects
{
    /// <summary>
    /// Converts an entity property type the mapper does not know natively to and from its wire value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this to use a property type of your own on a custom entity — a value object, a type from
    /// another library, an <c>IPAddress</c> — and register it with
    /// <see cref="TikTypeConverters.Register(ITikTypeConverter)"/>.
    /// </para>
    /// <para>
    /// <b>A converter cannot override a built-in type.</b> The mapper's own handling of <c>string</c>,
    /// <c>int</c>, <c>long</c>, <c>byte</c>, <c>uint</c>, <c>ulong</c>, <c>bool</c>, <c>TimeSpan</c>,
    /// <c>DateTime</c>, <c>MacAddress</c> and enums is tried first, and a converter is consulted only for a
    /// type none of them claims. This is deliberate: a converter that answered
    /// <see cref="CanConvert"/> for <c>string</c> or <c>bool</c> would silently re-route every entity in the
    /// process, including entities it knows nothing about, and the failure would surface as wrong data on
    /// the router rather than as an error. If you need different handling for a built-in type, wrap it in a
    /// type of your own.
    /// </para>
    /// <para>
    /// A converter is asked about the <b>underlying</b> type of a nullable property, and never sees
    /// <c>null</c>: an absent field and an unassigned nullable are decided by the mapper before conversion
    /// (see <see cref="TikEntityPropertyAccessor.IsNullable"/>).
    /// </para>
    /// <para>Implementations must be thread-safe and free of per-call state — one instance serves the whole process.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class IPAddressConverter : ITikTypeConverter
    /// {
    ///     public bool CanConvert(Type type) =&gt; type == typeof(IPAddress);
    ///     public object ConvertFromString(string value, Type targetType) =&gt; IPAddress.Parse(value);
    ///     public string ConvertToString(object value, Type sourceType) =&gt; ((IPAddress)value).ToString();
    /// }
    ///
    /// TikTypeConverters.Register(new IPAddressConverter());
    /// </code>
    /// </example>
    /// <seealso cref="TikTypeConverters"/>
    public interface ITikTypeConverter
    {
        /// <summary>
        /// Returns whether this converter handles <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The property type, already unwrapped from <see cref="Nullable{T}"/>.</param>
        /// <returns>True when <see cref="ConvertFromString"/> and <see cref="ConvertToString"/> can handle it.</returns>
        bool CanConvert(Type type);

        /// <summary>
        /// Converts a value received from the router to <paramref name="targetType"/>.
        /// </summary>
        /// <param name="value">The value as printed by the router. Never null.</param>
        /// <param name="targetType">The property type, already unwrapped from <see cref="Nullable{T}"/>.</param>
        /// <returns>The converted value.</returns>
        /// <remarks>
        /// An exception thrown here is wrapped by the mapper in a <see cref="FormatException"/> naming the
        /// property and the field, so there is no need to name them again.
        /// </remarks>
        object ConvertFromString(string value, Type targetType);

        /// <summary>
        /// Converts a property value to the string sent to the router.
        /// </summary>
        /// <param name="value">The property value. Never null.</param>
        /// <param name="sourceType">The property type, already unwrapped from <see cref="Nullable{T}"/>.</param>
        /// <returns>The value in the router's own format.</returns>
        string ConvertToString(object value, Type sourceType);
    }
}
