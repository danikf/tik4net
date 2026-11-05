using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
        private readonly string _propertyName;
        private readonly string _fieldName;
        private readonly bool _isPresenceFlag;
        private readonly bool _holdsEmptyWord;
        // Set for a type this class knows how to spell; a converter's type is left to ResolveConverter.
        private readonly Func<string, object?>? _parse;
        private readonly Func<object, string?>? _format;
        private ITikTypeConverter? _converter;

        /// <param name="type">The value type (an enum, int, string, TikPortRange, TikNumberRange, …), not its nullable form.</param>
        /// <param name="propertyName">For error messages.</param>
        /// <param name="fieldName">For error messages.</param>
        /// <param name="isPresenceFlag">A presence flag: an empty value reads <c>true</c> (the router sends <c>fib=</c> when it is set).</param>
        internal TikWireConverter(Type type, string propertyName, string fieldName, bool isPresenceFlag = false)
        {
            _type = type;
            _propertyName = propertyName;
            _fieldName = fieldName;
            _isPresenceFlag = isPresenceFlag;

            TikWireType? known = TikWireTypes.Find(type);
            if (known != null)
            {
                _parse = known.Parse;
                _format = known.Format;
                _holdsEmptyWord = known.HoldsEmptyWord;
            }
            else if (type.GetTypeInfo().IsEnum)
            {
                var metadata = TikEnumMetadata.Get(type);
                _parse = metadata.Parse;
                _format = metadata.Format;
            }
            else if (TikNegatableValue.IsNegatableValue(type))
            {
                _holdsEmptyWord = true;     // the member decides
                var (parse, format) = NegatableCodec(type, propertyName, fieldName);
                _parse = parse;
                _format = format;
            }
        }

        // A member the router negates on its own: a leading '!' is its negation, the rest the member.
        private static (Func<string, object?>, Func<object, string?>) NegatableCodec(Type type, string propertyName, string fieldName)
        {
            Type memberType = type.GetTypeInfo().GenericTypeArguments[0];
            var member = new TikWireConverter(memberType, propertyName, fieldName);
            var create = (Func<object?, bool, object>)typeof(TikWireConverter).GetTypeInfo()
                .GetDeclaredMethod(nameof(MakeNegatable))!.MakeGenericMethod(memberType).Invoke(null, null)!;
            return (word =>
            {
                bool negated = TikNegation.TrySplit(word, out string rest);
                object? value = member.ConvertFromString(rest);
                return value == null ? null : create(value, negated);
            },
            value =>
            {
                var negatable = (ITikNegatableValue)value;
                return (negatable.IsNegated ? TikNegation.Bang : "") + member.ConvertToString(negatable.BoxedValue);
            });
        }

        private static Func<object?, bool, object> MakeNegatable<TMember>()
            => (value, negated) => TikNegatableValue<TMember>.Create((TMember)value!, negated);

        /// <summary>
        /// The <see cref="ITikTypeConverter"/> handling the type, or null when none does.
        /// </summary>
        /// <remarks>
        /// Resolved on the first conversion rather than in the ctor, and only for a type no built-in
        /// claimed — so a converter registered after the entity was first used still takes effect instead of
        /// being silently ignored, and a built-in type never pays for the lookup. The assignment is
        /// idempotent, so the missing lock costs at most a repeated scan.
        /// </remarks>
        private ITikTypeConverter ResolveConverter()
        {
            return _converter ?? (_converter = TikTypeConverters.Resolve(_type))
                ?? throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "Property type {0} not supported. Register an ITikTypeConverter for it via TikTypeConverters.Register.", _type));
        }

        /// <summary>
        /// The value <paramref name="word"/> spells, or <c>null</c> when the word carries none: an empty word on a type
        /// that has no empty value, which says nothing about the field. A presence flag is the exception that proves it:
        /// being printed is its value, so empty is <c>true</c>, and a <see cref="bool"/> not declared one refuses it.
        /// </summary>
        /// <exception cref="FormatException">The type cannot hold <paramref name="word"/>.</exception>
        internal object? ConvertFromString(string word)
        {
            if (word.Length == 0 && !_holdsEmptyWord)
            {
                if (_type != typeof(bool))
                    return null;
                if (_isPresenceFlag)
                    return true;
            }

            try
            {
                return _parse != null ? _parse(word) : ResolveConverter().ConvertFromString(word, _type);
            }
            catch (NotImplementedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Value '{0}' for property '{1}({2})' is not in expected format '{3}'.", word, _propertyName, _fieldName, _type), ex);
            }
        }

        internal string? ConvertToString(object? value)
        {
            // Null is an assigned null. It has no wire form — the point is that nothing is sent — so it stays null
            // all the way out to the caller.
            if (value == null)
                return null;
            return _format != null ? _format(value) : ResolveConverter().ConvertToString(value, _type);
        }
    }
}
