using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace tik4net.Objects
{
    /// <summary>
    /// Converts one value between the router's spelling and a CLR type — the conversion a mapped property uses for its
    /// value, and a <see cref="TikValueList{T}"/> property for each item.
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    internal sealed class TikWireConverter
    {
        private readonly Type _type;
        private readonly bool _nullable;
        private readonly TikEnumMetadata? _enum;
        private readonly string _propertyName;
        private readonly string _fieldName;
        private ITikTypeConverter? _converter;

        /// <param name="type">The value type (an enum, int, string, TikPortRange, TikNumberRange, …), not its nullable form.</param>
        /// <param name="nullable">Whether a <c>null</c> string reads as <c>null</c> rather than failing to parse.</param>
        /// <param name="propertyName">For error messages.</param>
        /// <param name="fieldName">For error messages.</param>
        internal TikWireConverter(Type type, bool nullable, string propertyName, string fieldName)
        {
            _type = type;
            _nullable = nullable;
            _propertyName = propertyName;
            _fieldName = fieldName;
            if (type.GetTypeInfo().IsEnum)
                _enum = TikEnumMetadata.Get(type);
        }

        /// <summary>A presence flag: an empty value reads <c>true</c> (the router sends <c>fib=</c> when it is set).</summary>
        internal bool IsPresenceFlag { get; set; }

        /// <summary>
        /// The <see cref="ITikTypeConverter"/> handling the type, or null when none does.
        /// </summary>
        /// <remarks>
        /// Resolved on the first conversion rather than in the ctor, and only for a type no built-in
        /// claimed — so a converter registered after the entity was first used still takes effect instead of
        /// being silently ignored, and a built-in type never pays for the lookup. The assignment is
        /// idempotent, so the missing lock costs at most a repeated scan.
        /// </remarks>
        private ITikTypeConverter? ResolveConverter()
        {
            return _converter ?? (_converter = TikTypeConverters.Resolve(_type));
        }

        // unknownWord: the router's word(s) an enum property read as its TikEnumUnknown member for, else null.
        internal object? ConvertFromString(string? strValue, out string? unknownWord)
        {
            unknownWord = null;
            try
            {
                // A nullable property is the only one that can carry "the router did not report this field"
                // as itself. Everything else has to fall through and be parsed, including the empty string —
                // a valueless presence flag reads back as false, and that is existing behaviour, not this.
                if (_nullable && strValue == null)
                    return null;

                // Past this point strValue is null only for a non-nullable property, which is a caller
                // error the parse calls below already turn into a FormatException via the catch - the `!`
                // just types that pre-existing behaviour instead of changing it.
                string value = strValue!;

                //convert to property real type
                if (_type == typeof(string))
                    return value;
                else if (_type == typeof(TimeSpan))
                    return TikTimeHelper.FromTikTimeToTimeSpan(value);
                // A duration the router may also answer with a word (none / disabled / auto). Both of the
                // forms the router writes durations in parse to the same value here, which is the point:
                // the API says "10s" and the CLI says "00:00:10" for the same field.
                // Rates, like durations, arrive spelled differently per transport: 1000000 over the API,
                // 1M over the CLI. Both parse to the same value here.
                else if (_type == typeof(TikDataRate))
                    return value.Length == 0 ? (_nullable ? (object?)null : default(TikDataRate)) : TikDataRate.Parse(value);
                else if (_type == typeof(TikRatePair))
                    return value.Length == 0 ? (_nullable ? (object?)null : default(TikRatePair)) : TikRatePair.Parse(value);
                // Hex over the API, decimal over the CLI before 7.24 — both read to the same number.
                else if (_type == typeof(TikHexNumber))
                    return value.Length == 0 ? (_nullable ? (object?)null : default(TikHexNumber)) : TikHexNumber.Parse(value);
                else if (_type == typeof(TikPortRange))
                    return TikPortRange.Parse(value);
                else if (_type == typeof(TikNumberRange))
                    return TikNumberRange.Parse(value);
                else if (_type == typeof(TikDuration))
                {
                    // An empty value is the router saying the field carries nothing, which is not the same
                    // as a zero-length duration. A nullable property can say that; a non-nullable one has
                    // nowhere to put it and keeps the type's own default.
                    if (value.Length == 0)
                        return _nullable ? (object?)null : default(TikDuration);
                    return TikDuration.Parse(value);
                }
                // InvariantCulture on every numeric conversion, in both directions. The thread's culture
                // has no business here: the router's wire form is invariant, and the digits being the same
                // in every culture is not enough — a few (sv-SE, fi-FI) render minus as U+2212, which
                // RouterOS will not parse, and which will not parse the router's own U+002D back.
                else if (_type == typeof(int))
                    return int.Parse(value, CultureInfo.InvariantCulture);
                else if (_type == typeof(long))
                    return long.Parse(value, CultureInfo.InvariantCulture);
                else if (_type == typeof(byte))
                    return byte.Parse(value, CultureInfo.InvariantCulture);
                else if (_type == typeof(uint))
                    return uint.Parse(value, CultureInfo.InvariantCulture);
                else if (_type == typeof(ulong))
                    return ulong.Parse(value, CultureInfo.InvariantCulture);
                else if (_type == typeof(DateTime))
                    return TikDateTimeHelper.FromTikDateTime(value);
                else if (_type == typeof(MacAddress))
                    return new MacAddress(value);
                else if (_type == typeof(bool))
                {
                    // A presence flag is "set" by BEING THERE: the binary API and REST send `fib=` with an
                    // empty value and omit the word when it is clear, so the empty string is the router
                    // saying true. Absence is handled above (null for a nullable property) and is not
                    // turned into false here — that is the router reporting nothing, not reporting false.
                    if (IsPresenceFlag && value.Length == 0)
                        return true;
                    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
                }
                else if (_type.GetTypeInfo().IsEnum)
                {
                    // _enum is set in the constructor exactly when _type is an enum (see there).
                    if (_enum!.IsFlags && value.Contains(','))
                    {
                        long result = 0;
                        var unknownParts = new List<string>();
                        foreach (string raw in value.Split(','))
                        {
                            string part = raw.Trim();
                            if (_enum.TryParseNumeric(part, out long numeric))
                                result |= numeric;
                            else if (_enum.UnknownMember != null)
                                unknownParts.Add(part);   // kept, and the Unknown bit set below
                            else
                                result |= _enum.ParseNumeric(part);   // throws, naming the word
                        }
                        if (unknownParts.Count > 0)
                        {
                            result |= _enum.UnknownNumeric;
                            unknownWord = string.Join(",", unknownParts);
                        }
                        return Enum.ToObject(_type, result);
                    }
                    else
                    {
                        // A word the enum does not know reads as its TikEnumUnknown member when it has one — the
                        // word is kept (SetEntityValue) — instead of failing the read of the whole menu.
                        return _enum.ParseTolerant(value, out unknownWord);
                    }
                }
                else
                {
                    var converter = ResolveConverter();
                    if (converter != null)
                        return converter.ConvertFromString(value, _type);

                    throw new NotImplementedException(string.Format("Property type {0} not supported. Register an ITikTypeConverter for it via TikTypeConverters.Register.", _type));
                }
            }
            catch(NotImplementedException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new FormatException(string.Format("Value '{0}' for property '{1}({2})' is not in expected format '{3}'.", strValue, _propertyName, _fieldName, _type), ex);
            }
        }

        internal string? ConvertToString(object? propValue)
        {
            // Null reaches here only from a nullable property that was never assigned. It has no wire form —
            // the point is that nothing is sent — so it stays null all the way out to the caller.
            if (propValue == null)
                return null;

            if (propValue is string)
                return (string)propValue;

            //convert to string used in mikrotik
            if (_type == typeof(string))
                return propValue.ToString();
            else if (_type == typeof(TimeSpan))
                return TikTimeHelper.ToTikTime((int)((TimeSpan)propValue).TotalSeconds);
            else if (_type == typeof(TikPortRange))
                return ((TikPortRange)propValue).ToString();
            else if (_type == typeof(TikNumberRange))
                return ((TikNumberRange)propValue).ToString();
            else if (_type == typeof(TikDuration))
                return ((TikDuration)propValue).ToString();
            else if (_type == typeof(TikDataRate))
                return ((TikDataRate)propValue).ToString();
            else if (_type == typeof(TikRatePair))
                return ((TikRatePair)propValue).ToString();
            else if (_type == typeof(TikHexNumber))
                return ((TikHexNumber)propValue).ToString();
            else if (_type == typeof(int))
                return ((int)propValue).ToString(CultureInfo.InvariantCulture);
            else if (_type == typeof(long))
                return ((long)propValue).ToString(CultureInfo.InvariantCulture);
            // byte parses on the way in (above) and had no branch here, so writing one fell through to the
            // converter lookup and threw — and for a NON-nullable byte property it threw while the metadata
            // was still being built, because DefaultValue is formatted through this method.
            else if (_type == typeof(byte))
                return ((byte)propValue).ToString(CultureInfo.InvariantCulture);
            else if (_type == typeof(uint))
                return ((uint)propValue).ToString(CultureInfo.InvariantCulture);
            else if (_type == typeof(ulong))
                return ((ulong)propValue).ToString(CultureInfo.InvariantCulture);
            else if (_type == typeof(DateTime))
                return TikDateTimeHelper.ToTikValue((DateTime)propValue);
            else if (_type == typeof(MacAddress))
                return ((MacAddress)propValue).Address;
            // yes/no is accepted for every boolean argument, including fields the router itself prints as
            // true/false (disable-running-check=no measured on Api, Rest, Telnet; disabled=no on WinboxNative), so a
            // bool needs no per-property spelling. A field that must be READ as a word other than true/yes/false/no
            // is modelled as an enum with [TikEnum] instead.
            else if (_type == typeof(bool))
                return ((bool)propValue) ? "yes" : "no";
            else if (_type.GetTypeInfo().IsEnum)
            {
                // _enum is set in the constructor exactly when _type is an enum (see there).
                if (_enum!.IsFlags)
                    return _enum.FormatFlags(propValue);
                else
                    return _enum.Format(propValue);
            }
            else
            {
                var converter = ResolveConverter();
                if (converter != null)
                    return converter.ConvertToString(propValue, _type);

                throw new NotImplementedException(string.Format("Property type {0} not supported. Register an ITikTypeConverter for it via TikTypeConverters.Register.", _type));
            }
        }
    }
}
