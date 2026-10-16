using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace tik4net.Objects
{
    /// <summary>
    /// A field RouterOS negates with a leading <c>!</c> — a firewall-style matcher: <c>src-address=!10.0.0.0/8</c> matches
    /// everything outside that network, <c>connection-state=!established,related</c> every other state. Everything a
    /// <see cref="TikField{T}"/> holds, plus <see cref="IsNegated"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The property's value type, always in its nullable form — <c>string?</c>, <c>int?</c>, an enum <c>?</c>, or a
    /// <c>TikValueList&lt;T&gt;?</c> when the whole list is negated (<c>dst-port=!22,8291</c>).
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// The <c>!</c> is not part of the value: a load reads it as <see cref="IsNegated"/> and parses the rest as
    /// <typeparamref name="T"/>, and a save writes it back. Every transport spells it the same way — the API, REST and the
    /// CLI print the <c>!</c>, and WinBox native carries it as the field's <c>not</c> flag. A negated value never equals
    /// the plain one: it is the opposite matcher.
    /// </para>
    /// <para>
    /// Write a negation with <see cref="Not"/> (<c>rule.Port = TikNegatableField&lt;int?&gt;.Not(22)</c>), or, for a
    /// <c>string?</c> field, in the router's own spelling — the router has no way to send a literal leading <c>!</c> to
    /// a negatable field, so a string that starts with one is the negation:
    /// <c>rule.SrcAddress = "!10.0.0.0/8"</c> is <c>Not("10.0.0.0/8")</c>, and compares equal to it.
    /// </para>
    /// <para>
    /// Only the properties RouterOS negates have this type, so a negation assigned anywhere else does not compile. A list
    /// whose <b>members</b> the router negates one by one (<c>tcp-flags=syn,!ack</c>) keeps each member's <c>!</c> on the
    /// item, <see cref="TikValue{T}.IsNegated"/> (<see cref="TikPropertyAttribute.NegatableMembers"/>).
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{DebuggerText,nq}")]
#if NET8_0_OR_GREATER
    [global::System.Text.Json.Serialization.JsonConverter(typeof(TikFieldJsonConverterFactory))]
#endif
    public readonly struct TikNegatableField<T> : IEquatable<TikNegatableField<T>>, IComparable<TikNegatableField<T>>, IComparable, ITikField
    {
        private readonly TikFieldCore<T> _core;

        private TikNegatableField(TikValue<T> content)
        {
            _core = new TikFieldCore<T>(content);
        }

        internal TikNegatableField(TikFieldCore<T> core)
        {
            _core = core;
        }

        internal TikFieldCore<T> Core => _core;

        /// <summary>Whether the field was printed, and whether its value could be read.</summary>
        public TikFieldState State => _core.State;

        /// <summary>
        /// The value — the matcher itself, without the <c>!</c> (<see cref="IsNegated"/> says whether it is negated): when
        /// <see cref="State"/> is <see cref="TikFieldState.Present"/> what was read or assigned (possibly <c>null</c>), when
        /// <see cref="TikFieldState.Absent"/> <c>null</c>.
        /// </summary>
        /// <exception cref="TikUnparsedValueException">
        /// The router printed a value <typeparamref name="T"/> cannot hold (<see cref="TikFieldState.Unparsed"/>). Read
        /// <see cref="RawValue"/>, or use <see cref="TryGetValue"/> or <see cref="ValueOrDefault"/>.
        /// </exception>
        public T Value => _core.Value;

        /// <summary>The router's word when <see cref="State"/> is <see cref="TikFieldState.Unparsed"/>; otherwise <c>null</c>.</summary>
        public string? RawValue => _core.RawValue;

        /// <summary>
        /// True when the value is negated — the router's <c>!</c> in front of the matcher, which matches everything the
        /// value does not. Only a present, non-null value can be negated.
        /// </summary>
        /// <remarks>
        /// The <c>!</c> negates the whole value, a list included. On a list whose members the router negates one by one
        /// (<c>tcp-flags=syn,!ack</c>) each member's <c>!</c> is that item's <see cref="TikValue{T}.IsNegated"/>.
        /// </remarks>
        public bool IsNegated => _core.IsNegated;

        /// <summary>This value with <see cref="IsNegated"/> cleared — the matcher it negates.</summary>
        public TikNegatableField<T> WithoutNegation() => _core.Printed ? new TikNegatableField<T>(_core.Content.WithoutNegation()) : this;

        /// <summary>
        /// A negated value — the router's <c>!value</c>: <c>rule.Port = TikNegatableField&lt;int?&gt;.Not(22)</c>,
        /// <c>rule.DstPort = TikNegatableField&lt;TikValueList&lt;TikPortRange&gt;?&gt;.Not(new TikValueList&lt;TikPortRange&gt;(22, 8291))</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>: there is nothing to negate.</exception>
        public static TikNegatableField<T> Not(T value)
            => new TikNegatableField<T>(((TikValue<T>)(value ?? throw new ArgumentNullException(nameof(value), "Only a value can be negated."))).AsNegated());

        /// <summary>
        /// The value a text spells, the way the router writes it: <c>TikNegatableField&lt;int?&gt;.Parse("!22")</c> is
        /// <c>Not(22)</c>, <c>Parse("22")</c> the plain value. A leading <c>!</c> is the negation; the rest is parsed as
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// Not for a list: whether <c>!a,b</c> negates the list or only its first member depends on the field. Write a list
        /// as <c>Not(TikValueList&lt;T&gt;.Parse("a,b"))</c>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException">
        /// The text (after the <c>!</c>) is not a value of <typeparamref name="T"/> — a word an enum has no member for goes
        /// in as <see cref="FromWire"/> — or it is a bare <c>!</c>, which negates nothing.
        /// </exception>
        /// <exception cref="NotSupportedException"><typeparamref name="T"/> is a <see cref="TikValueList{T}"/>.</exception>
        [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
        [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
        public static TikNegatableField<T> Parse(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            Type valueType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(TikValueList<>))
                throw new NotSupportedException("Whether '!a,b' negates the list or its first member depends on the field: write "
                    + "TikNegatableField<TikValueList<T>?>.Not(TikValueList<T>.Parse(\"a,b\")) for the whole list.");
            bool negated = text.Length > 0 && text[0] == '!';
            string word = negated ? text.Substring(1) : text;
            if (negated && word.Length == 0)
                throw new FormatException("\"!\" negates nothing.");
            object? value;
            string? unknownWord;
            try
            {
                value = new TikWireConverter(valueType, valueType != typeof(T), "TikNegatableField<" + valueType.Name + ">", "Parse")
                    .ConvertFromString(word, out unknownWord);
            }
            catch (FormatException e)
            {
                throw new FormatException("\"" + word + "\" is not a " + valueType.Name + ".", e);
            }
            if (unknownWord != null)
                throw new FormatException("\"" + word + "\" is not a " + valueType.Name + "; a word from another RouterOS version goes in as "
                    + "TikNegatableField<" + valueType.Name + "?>.FromWire(\"" + text + "\").");
            return FromPresent((T)value!, negated);
        }

        internal static TikNegatableField<T> FromPresent(T value, bool negated)
            => new TikNegatableField<T>(((TikValue<T>)value).AsNegated(negated && value != null));

        /// <summary>True when the router printed the field (or the caller assigned it) and the value was read.</summary>
        public bool IsPresent => State == TikFieldState.Present;

        /// <summary>The field was not printed or not loaded (the <c>default</c>).</summary>
        public static TikNegatableField<T> Absent => default;

        /// <summary>
        /// <see cref="Value"/> when present — negated or not, so check <see cref="IsNegated"/> where that matters —,
        /// <paramref name="fallback"/> when absent or unparsed.
        /// </summary>
        public T ValueOrDefault(T fallback) => _core.ValueOrDefault(fallback);

        /// <summary>
        /// This value, or <paramref name="fallback"/> when this one is <see cref="TikFieldState.Absent"/> — the field-level
        /// "the source says nothing, keep what is there" of a merge (<see cref="TikField{T}.IfAbsent"/>).
        /// </summary>
        public TikNegatableField<T> IfAbsent(TikNegatableField<T> fallback) => _core.Printed ? this : fallback;

        /// <summary>
        /// This value where the router prints the field on <paramref name="current"/>; <paramref name="current"/> itself
        /// where it does not — the field-level "this field does not apply to this row" of a merge
        /// (<see cref="TikField{T}.IfPrintedIn"/>).
        /// </summary>
        public TikNegatableField<T> IfPrintedIn(TikNegatableField<T> current) => current._core.Printed ? this : current;

        /// <summary>
        /// True, with the value, when <see cref="State"/> is <see cref="TikFieldState.Present"/> — negated or not, so check
        /// <see cref="IsNegated"/> where that matters.
        /// </summary>
        public bool TryGetValue(out T value) => _core.TryGetValue(out value);

        /// <summary>
        /// A value the caller assigns — <see cref="TikFieldState.Present"/>, including a <c>null</c>. For a <c>string?</c>
        /// field a leading <c>!</c> is the negation, as it is on the router: <c>rule.SrcAddress = "!10.0.0.0/8"</c> is
        /// <c>Not("10.0.0.0/8")</c>.
        /// </summary>
        public static implicit operator TikNegatableField<T>(T value) => new TikNegatableField<T>(Spelled(value));

        /// <summary>
        /// The router's own word, sent as written — for a word <typeparamref name="T"/> has no member for. It reads as
        /// <see cref="TikFieldState.Unparsed"/>, the way a load of the same word does (<see cref="TikField{T}.FromWire"/>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="word"/> is <c>null</c>.</exception>
        public static TikNegatableField<T> FromWire(string word) => new TikNegatableField<T>(TikValue<T>.FromWire(word));

        internal static TikNegatableField<T> FromUnparsed(string raw) => new TikNegatableField<T>(TikValue<T>.FromWire(raw));

        // A string's leading '!' is the router's negation: a negatable field has no literal '!' to send (the mapper reads a
        // loaded one the same way). A bare "!" stays text, as a load leaves it.
        private static TikValue<T> Spelled(T value)
            => value is string text && text.Length > 1 && text[0] == '!'
                ? ((TikValue<T>)(T)(object)text.Substring(1)).AsNegated()
                : value;

        /// <summary>
        /// True when <paramref name="a"/> is present and holds the matcher <paramref name="b"/> spells — for a
        /// <c>string?</c> field <c>"!10.0.0.0/8"</c> is the negation, so it equals <c>Not("10.0.0.0/8")</c> and not the plain
        /// value. Compared with <c>null</c> it means "has no value": true when absent or present with <c>null</c>, never
        /// when unparsed.
        /// </summary>
        public static bool operator ==(TikNegatableField<T> a, T b) => a._core.EqualsValue(Spelled(b));

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikNegatableField<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is present and holds the matcher <paramref name="a"/> spells.</summary>
        public static bool operator ==(T a, TikNegatableField<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikNegatableField<T> b) => !(b == a);

        /// <summary>Same state, same value, same raw word, same negation.</summary>
        public static bool operator ==(TikNegatableField<T> a, TikNegatableField<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikNegatableField<T> a, TikNegatableField<T> b) => !a.Equals(b);

        /// <summary>
        /// True when both have a plain value and <paramref name="a"/>'s is smaller (like a lifted nullable comparison). A
        /// negated value is not ordered against a plain one: every comparison with it is false.
        /// </summary>
        public static bool operator <(TikNegatableField<T> a, T b) => a._core.Lifted(Spelled(b), out int c) && c < 0;
        /// <summary>True when both have a plain value and <paramref name="a"/>'s is larger.</summary>
        public static bool operator >(TikNegatableField<T> a, T b) => a._core.Lifted(Spelled(b), out int c) && c > 0;
        /// <summary>True when both have a plain value and <paramref name="a"/>'s is not larger.</summary>
        public static bool operator <=(TikNegatableField<T> a, T b) => a._core.Lifted(Spelled(b), out int c) && c <= 0;
        /// <summary>True when both have a plain value and <paramref name="a"/>'s is not smaller.</summary>
        public static bool operator >=(TikNegatableField<T> a, T b) => a._core.Lifted(Spelled(b), out int c) && c >= 0;

        /// <summary>
        /// Sort order: Absent, then Unparsed (by word), then Present (by value, <c>null</c> first; a plain value before its
        /// negation).
        /// </summary>
        public int CompareTo(TikNegatableField<T> other) => _core.CompareTo(other._core);

        int IComparable.CompareTo(object? obj)
            => obj is TikNegatableField<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikNegatableField<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        /// <inheritdoc/>
        public bool Equals(TikNegatableField<T> other) => _core.Equals(other._core);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikNegatableField<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _core.GetHashCode();

        /// <summary>
        /// The value for display, spelled the way the router prints it (<see cref="TikField{T}.ToString"/>) — a negated
        /// value with its <c>!</c>.
        /// </summary>
        public override string ToString() => _core.ToString();

        private string DebuggerText => _core.DebuggerText;

        TikFieldState ITikField.State => State;
        object? ITikField.BoxedValue => _core.BoxedValue;
        string? ITikField.RawValue => RawValue;
        bool ITikField.IsNegated => IsNegated;
    }
}
