using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Reflection;

namespace tik4net.Objects
{
    /// <summary>
    /// The wire-value ↔ member mapping of one enum type, resolved once and reused for every conversion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built because the conversion, not the property access, is the mapper's cost. Resolving a value by
    /// walking <see cref="Enum.GetNames(Type)"/> and asking each member for its
    /// <see cref="TikEnumAttribute"/> costs ~425× a string assignment per value converted (measured, see
    /// <c>tik4net.benchmarks</c>), and the mapper does it for every enum field of every row.
    /// </para>
    /// <para>
    /// An unknown wire value and an ambiguous one (two members declaring the same
    /// <see cref="TikEnumAttribute.Value"/>) both throw, and the caller turns that into the field's unparsed state.
    /// Formatting a value that is not a defined member — a caller's <c>(SomeEnum)99</c> — names the offending value and
    /// its type.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    internal sealed class TikEnumMetadata
    {
        private static readonly ConcurrentDictionary<Type, Lazy<TikEnumMetadata>> _cache
            = new ConcurrentDictionary<Type, Lazy<TikEnumMetadata>>();

        private readonly Type _enumType;
        private readonly Dictionary<string, object> _memberByWire;
        private readonly Dictionary<long, string> _wireByNumeric;

        /// <summary>
        /// Gets (or builds) the mapping for <paramref name="enumType"/>. Thread-safe, and built at most once
        /// per enum type per process.
        /// </summary>
        public static TikEnumMetadata Get(Type enumType)
            => _cache.GetOrAdd(enumType, type => new Lazy<TikEnumMetadata>(() => new TikEnumMetadata(type))).Value;

        private TikEnumMetadata(Type enumType)
        {
            _enumType = enumType;
            _memberByWire = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _wireByNumeric = new Dictionary<long, string>();
            var ambiguous = new List<string>();

            foreach (string name in Enum.GetNames(enumType))
            {
                string? wire = enumType.GetRuntimeField(name)!.GetCustomAttribute<TikEnumAttribute>(false)?.Value; // name comes from Enum.GetNames(enumType), so the field always exists
                if (wire == null)
                    continue;

                object value = Enum.Parse(enumType, name, true);
                if (_memberByWire.ContainsKey(wire))
                    ambiguous.Add(wire);    // two members claim one wire value: neither is what it reads as
                else
                    _memberByWire.Add(wire, value);

                long numeric = Convert.ToInt64(value);
                if (!_wireByNumeric.ContainsKey(numeric))
                    _wireByNumeric.Add(numeric, wire);
            }

            foreach (string wire in ambiguous)
                _memberByWire.Remove(wire);
        }

        /// <summary>The member <paramref name="wireValue"/> names; <c>false</c> when it names none, or two.</summary>
        public bool TryFind(string wireValue, out object? member)
            => _memberByWire.TryGetValue(wireValue, out member);

        /// <summary>
        /// Resolves one wire value to its member. Throws when the value is unknown or ambiguous — the caller
        /// wraps that in the usual <see cref="FormatException"/> naming the property.
        /// </summary>
        public object Parse(string wireValue)
        {
            if (TryFind(wireValue, out object? result))
                return result!;

            throw new FormatException(string.Format("Unknown value '{0}' for enum type {1}.", wireValue, _enumType.Name));
        }

        /// <summary>Formats one member as its wire value.</summary>
        public string Format(object value)
        {
            if (_wireByNumeric.TryGetValue(Convert.ToInt64(value), out var wire))
                return wire;

            throw new FormatException(string.Format("Value '{0}' is not a mapped member of enum type {1}.", value, _enumType.Name));
        }
    }
}
