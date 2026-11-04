using System;
using System.Collections.Generic;
using System.Globalization;

namespace tik4net.Objects
{
    /// <summary>One type the mapper converts to and from the router's spelling.</summary>
    internal sealed class TikWireType
    {
        internal TikWireType(Func<string, object> parse, Func<object, string> format, bool isWireString, bool holdsEmptyWord)
        {
            Parse = parse;
            Format = format;
            IsWireString = isWireString;
            HoldsEmptyWord = holdsEmptyWord;
        }

        /// <summary>The word as a value; throws when the type cannot hold it.</summary>
        internal Func<string, object> Parse { get; }

        internal Func<object, string> Format { get; }

        /// <summary>
        /// Travels through JSON as the string the router prints (10s, 1M, 10M/5M, AA:BB:…, 1000-2000), which it parses
        /// back; written as a JSON object it would collide with the <c>{"$raw":…}</c> form.
        /// </summary>
        internal bool IsWireString { get; }

        /// <summary>The empty word is one of the type's values. On any other type it says nothing.</summary>
        internal bool HoldsEmptyWord { get; }
    }

    /// <summary>
    /// Every type the mapper converts natively: the one place that says how each is spelled, so
    /// <see cref="TikWireConverter"/> and the JSON converters cannot disagree about one or forget one. An enum, a
    /// <see cref="TikNegatableValue{T}"/> and a converter's type are built per type by <see cref="TikWireConverter"/>.
    /// </summary>
    internal static class TikWireTypes
    {
        private static readonly Dictionary<Type, TikWireType> Table = new Dictionary<Type, TikWireType>
        {
            [typeof(string)] = new TikWireType(word => word, value => (string)value, isWireString: false, holdsEmptyWord: true),
            // Durations and rates arrive spelled differently per transport (the API says 10s and 1000000, the CLI
            // 00:00:10 and 1M); both parse to the same value here.
            [typeof(TikDuration)] = WireString(TikDuration.Parse),
            [typeof(TikDataRate)] = WireString(TikDataRate.Parse),
            [typeof(TikRatePair)] = WireString(TikRatePair.Parse),
            // Hex over the API, decimal over the CLI before 7.24 — both read to the same number.
            [typeof(TikHexNumber)] = WireString(TikHexNumber.Parse),
            [typeof(TikPortRange)] = WireString(TikPortRange.Parse),
            [typeof(TikNumberRange)] = WireString(TikNumberRange.Parse),
            [typeof(MacAddress)] = WireString(MacAddress.Parse),
            // InvariantCulture on every numeric conversion, in both directions. The thread's culture has no business
            // here: the router's wire form is invariant, and a few cultures (sv-SE, fi-FI) render minus as U+2212, which
            // RouterOS will not parse, and which will not parse the router's own U+002D back.
            [typeof(int)] = Plain(word => int.Parse(word, CultureInfo.InvariantCulture), value => value.ToString(CultureInfo.InvariantCulture)),
            [typeof(long)] = Plain(word => long.Parse(word, CultureInfo.InvariantCulture), value => value.ToString(CultureInfo.InvariantCulture)),
            [typeof(byte)] = Plain(word => byte.Parse(word, CultureInfo.InvariantCulture), value => value.ToString(CultureInfo.InvariantCulture)),
            [typeof(uint)] = Plain(word => uint.Parse(word, CultureInfo.InvariantCulture), value => value.ToString(CultureInfo.InvariantCulture)),
            [typeof(ulong)] = Plain(word => ulong.Parse(word, CultureInfo.InvariantCulture), value => value.ToString(CultureInfo.InvariantCulture)),
            [typeof(DateTime)] = Plain(TikDateTimeHelper.FromTikDateTime, TikDateTimeHelper.ToTikValue),
            // yes/no is accepted for every boolean argument, including fields the router itself prints as true/false
            // (disable-running-check=no measured on Api, Rest, Telnet; disabled=no on WinboxNative). A field that must be
            // READ as a word other than these four is modelled as an enum with [TikEnum] instead.
            [typeof(bool)] = Plain(ParseBool, TikBoolText.ToCli),
        };

        internal static TikWireType? Find(Type type) => Table.TryGetValue(type, out TikWireType? found) ? found : null;

        private static TikWireType WireString<T>(Func<string, T> parse) where T : notnull
            => new TikWireType(word => parse(word), value => value.ToString()!, isWireString: true, holdsEmptyWord: false);

        private static TikWireType Plain<T>(Func<string, T> parse, Func<T, string> format) where T : notnull
            => new TikWireType(word => parse(word), value => format((T)value), isWireString: false, holdsEmptyWord: false);

        private static bool ParseBool(string word)
            => TikBoolText.TryParse(word, out bool value) ? value
               : throw new FormatException("'" + word + "' is not a bool: the router prints true, false, yes or no.");
    }
}
