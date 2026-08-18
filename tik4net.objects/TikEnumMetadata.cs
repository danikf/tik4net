using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <c>tik4net.benchmarks</c>), and the mapper does it for every enum field of every row — the
    /// <c>[Flags]</c> path once per comma-separated part.
    /// </para>
    /// <para>
    /// The tables reproduce the old lookups exactly rather than tidying them up, including the parts that
    /// throw: an unknown wire value and an ambiguous one (two members declaring the same
    /// <see cref="TikEnumAttribute.Value"/>, which <c>Single()</c> used to reject) both still throw, and the
    /// caller still turns that into the same <see cref="FormatException"/>. The one deliberate difference is
    /// formatting a value that is not a defined member — a caller's <c>(SomeEnum)99</c> — which used to
    /// raise <see cref="ArgumentNullException"/> (a null <c>FieldInfo</c> handed to the
    /// <c>GetCustomAttribute</c> extension method) and now says which value and which type.
    /// </para>
    /// </remarks>
    internal sealed class TikEnumMetadata
    {
        private static readonly object _lockObj = new object();
        private static Dictionary<Type, TikEnumMetadata> _cache = new Dictionary<Type, TikEnumMetadata>();

        private readonly Type _enumType;
        private readonly Dictionary<string, object> _valueByWire;
        private readonly Dictionary<string, long> _numericByWire;
        private readonly HashSet<string> _ambiguousWire;
        private readonly Dictionary<long, string> _wireByNumeric;
        private readonly KeyValuePair<long, string>[] _flagMembers;
        // Set only when the enum actually has a zero member (with or without a [TikEnum] attribute);
        // FormatFlags's `?? ""` covers an enum that has none.
        private readonly string? _zeroMemberWire;

        /// <summary>True when the enum is decorated with <see cref="FlagsAttribute"/>.</summary>
        public bool IsFlags { get; private set; }

        /// <summary>
        /// Gets (or builds) the mapping for <paramref name="enumType"/>. Thread-safe, and built at most once
        /// per enum type per process.
        /// </summary>
        public static TikEnumMetadata Get(Type enumType)
        {
            TikEnumMetadata result;
            if (_cache.TryGetValue(enumType, out result))
                return result;

            lock (_lockObj)
            {
                if (!_cache.TryGetValue(enumType, out result))
                {
                    result = new TikEnumMetadata(enumType);
                    // Copy-on-write: readers never take the lock, so the dictionary they hold must not be
                    // mutated under them (the same shape TikEntityMetadataCache relies on, one level down).
                    var updated = new Dictionary<Type, TikEnumMetadata>(_cache);
                    updated[enumType] = result;
                    _cache = updated;
                }
            }
            return result;
        }

        private TikEnumMetadata(Type enumType)
        {
            _enumType = enumType;
            IsFlags = enumType.GetTypeInfo().GetCustomAttribute<FlagsAttribute>() != null;

            _valueByWire = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _numericByWire = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            _ambiguousWire = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _wireByNumeric = new Dictionary<long, string>();
            var flagMembers = new List<KeyValuePair<long, string>>();

            foreach (string name in Enum.GetNames(enumType))
            {
                string? wire = enumType.GetRuntimeField(name).GetCustomAttribute<TikEnumAttribute>(false)?.Value;
                object value = Enum.Parse(enumType, name, true);
                long numeric = Convert.ToInt64(value);

                if (wire != null)
                {
                    if (_valueByWire.ContainsKey(wire))
                        _ambiguousWire.Add(wire); //two members claim one wire value — Single() rejected this, so do we
                    else
                    {
                        _valueByWire.Add(wire, value);
                        _numericByWire.Add(wire, numeric);
                    }

                    if (!_wireByNumeric.ContainsKey(numeric))
                        _wireByNumeric.Add(numeric, wire);

                    if (numeric != 0)
                        flagMembers.Add(new KeyValuePair<long, string>(numeric, wire));
                    else
                        _zeroMemberWire = wire;
                }
                else if (numeric == 0)
                {
                    // A zero member with no attribute formatted as "" in the [Flags] path. Preserved.
                    _zeroMemberWire = "";
                }
            }

            // Declaration order, because that is the order Enum.GetNames returned and therefore the order
            // the joined [Flags] value has always been written in.
            _flagMembers = flagMembers.ToArray();
        }

        /// <summary>
        /// Resolves one wire value to its member. Throws when the value is unknown or ambiguous — the caller
        /// wraps that in the usual <see cref="FormatException"/> naming the property.
        /// </summary>
        public object Parse(string wireValue)
        {
            object result;
            if (wireValue != null && !_ambiguousWire.Contains(wireValue) && _valueByWire.TryGetValue(wireValue, out result))
                return result;

            throw new FormatException(string.Format("Unknown value '{0}' for enum type {1}.", wireValue, _enumType.Name));
        }

        /// <summary>Resolves one wire value to its numeric member value, for the <c>[Flags]</c> parse.</summary>
        public long ParseNumeric(string wireValue)
        {
            long result;
            if (wireValue != null && !_ambiguousWire.Contains(wireValue) && _numericByWire.TryGetValue(wireValue, out result))
                return result;

            throw new FormatException(string.Format("Unknown flags enum value '{0}' for type {1}.", wireValue, _enumType.Name));
        }

        /// <summary>Formats one member as its wire value.</summary>
        public string Format(object value)
        {
            string wire;
            if (_wireByNumeric.TryGetValue(Convert.ToInt64(value), out wire))
                return wire;

            throw new FormatException(string.Format("Value '{0}' is not a mapped member of enum type {1}.", value, _enumType.Name));
        }

        /// <summary>
        /// Formats a <c>[Flags]</c> combination as the router's comma-separated list, in declaration order.
        /// Zero formats as the zero member's wire value (or an empty string when there is none).
        /// </summary>
        public string FormatFlags(object value)
        {
            long numeric = Convert.ToInt64(value);
            if (numeric == 0)
                return _zeroMemberWire ?? "";

            return string.Join(",", _flagMembers
                .Where(m => (numeric & m.Key) == m.Key)
                .Select(m => m.Value));
        }
    }
}
