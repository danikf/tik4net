using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace tik4net.Objects
{
    /// <summary>
    /// One value as the router spells it — a value of <typeparamref name="T"/>, or the router's own word when
    /// <typeparamref name="T"/> cannot hold it — with its own <c>!</c> (<see cref="IsNegated"/>).
    /// </summary>
    /// <typeparam name="T">The value's type: <c>string?</c>, <c>int?</c>, an enum, … — the same <c>T</c> as the
    /// <see cref="TikField{T}"/> that holds it.</typeparam>
    /// <remarks>
    /// <para>
    /// A <see cref="TikField{T}"/> is what an entity property holds: whether the router printed the field, and if so a
    /// <see cref="TikValue{T}"/>. The value is what can be negated and what can be a word the type does not know, so
    /// both live here — on a single value, and on each item of a list.
    /// </para>
    /// <para>
    /// A negated matcher is written <c>rule.SrcAddress = TikValue&lt;string?&gt;.Not("10.0.0.0/8")</c> (the router's
    /// <c>!10.0.0.0/8</c>); a word from another RouterOS version that an enum has no member for is written
    /// <c>TikValue&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>. Both convert implicitly to the property's
    /// <see cref="TikField{T}"/>.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{DebuggerText,nq}")]
    public readonly struct TikValue<T> : IEquatable<TikValue<T>>, IComparable<TikValue<T>>, IComparable
    {
        private readonly T _value;
        private readonly string? _word;
        private readonly bool _negated;

        private TikValue(T value, string? word, bool negated)
        {
            _value = value;
            _word = word;
            _negated = negated;
        }

        /// <summary>
        /// True when this is the router's word rather than a value of <typeparamref name="T"/> — a word the enum has no
        /// member for, or text that is not in the type's format. <see cref="RawValue"/> holds it.
        /// </summary>
        public bool IsWord => _word != null;

        /// <summary>The value.</summary>
        /// <exception cref="TikUnparsedValueException">
        /// This is the router's word (<see cref="IsWord"/>), which <typeparamref name="T"/> cannot hold. Read
        /// <see cref="RawValue"/>, or use <see cref="TryGetValue"/>.
        /// </exception>
        public T Value => _word != null ? throw new TikUnparsedValueException(typeof(T), _word) : _value;

        /// <summary>The router's word when <see cref="IsWord"/>; otherwise <c>null</c>.</summary>
        public string? RawValue => _word;

        /// <summary>
        /// True when the value is negated — the router's <c>!</c> in front of it, which matches everything the value does
        /// not. Only a field that takes a <c>!</c> (<see cref="TikPropertyAttribute.Negatable"/>) reads or writes one.
        /// </summary>
        public bool IsNegated => _negated;

        /// <summary>True, with the value, unless this is the router's word.</summary>
        public bool TryGetValue(out T value)
        {
            value = _word == null ? _value : default!;
            return _word == null;
        }

        /// <summary>
        /// A negated value — the router's <c>!value</c>: <c>rule.SrcAddress = TikValue&lt;string?&gt;.Not("10.0.0.0/8")</c>.
        /// Writing it to a property that does not declare <see cref="TikPropertyAttribute.Negatable"/> throws.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>: there is nothing to negate.</exception>
        public static TikValue<T> Not(T value)
            => new TikValue<T>(value ?? throw new ArgumentNullException(nameof(value), "Only a value can be negated."), null, negated: true);

        /// <summary>
        /// The router's own word, sent as written — for a word <typeparamref name="T"/> has no member for (an enum's word
        /// on another RouterOS version: <c>TikValue&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>). It reads back the same way.
        /// </summary>
        public static TikValue<T> FromWire(string word)
            => new TikValue<T>(default!, word ?? throw new ArgumentNullException(nameof(word)), negated: false);

        /// <summary>This value with <see cref="IsNegated"/> cleared — the matcher it negates.</summary>
        public TikValue<T> WithoutNegation() => new TikValue<T>(_value, _word, negated: false);

        internal TikValue<T> AsNegated(bool negated = true) => new TikValue<T>(_value, _word, negated);

        /// <summary>A value — <c>rule.Port = 8080</c> converts through here.</summary>
        public static implicit operator TikValue<T>(T value) => new TikValue<T>(value, null, negated: false);

        /// <summary>True when <paramref name="a"/> is a value, not negated, and equal to <paramref name="b"/>.</summary>
        public static bool operator ==(TikValue<T> a, T b)
            => a._word == null && !a._negated && EqualityComparer<T>.Default.Equals(a._value, b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is a value, not negated, and equal to <paramref name="a"/>.</summary>
        public static bool operator ==(T a, TikValue<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikValue<T> b) => !(b == a);

        /// <summary>Same value or word, same negation.</summary>
        public static bool operator ==(TikValue<T> a, TikValue<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, TikValue<T> b) => !a.Equals(b);

        /// <inheritdoc/>
        public bool Equals(TikValue<T> other)
            => _negated == other._negated
               && string.Equals(_word, other._word, StringComparison.Ordinal)
               && (_word != null || EqualityComparer<T>.Default.Equals(_value, other._value));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikValue<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _negated ? 16 : 0;
                hash = hash * 397 ^ (_word == null ? (_value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value)) : _word.GetHashCode());
                return hash;
            }
        }

        /// <summary>Sort order: words (by word) before values (by value, <c>null</c> first); a plain value before its negation.</summary>
        public int CompareTo(TikValue<T> other)
        {
            if ((_word == null) != (other._word == null))
                return _word == null ? 1 : -1;
            int byValue = _word != null ? string.CompareOrdinal(_word, other._word) : Comparer<T>.Default.Compare(_value, other._value);
            return byValue != 0 ? byValue : _negated.CompareTo(other._negated);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikValue<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikValue<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        /// <summary>
        /// The value spelled the way the router prints it — an enum as its word, a bool as <c>true</c>/<c>false</c>, a
        /// number invariantly, the router's word as it came, a negated value with its <c>!</c>.
        /// </summary>
        public override string ToString() => (_negated ? "!" : "") + (_word ?? TikWireText.Format(_value));

        private string DebuggerText
            => (_negated ? "!" : "") + (_word != null ? "word \"" + _word + "\"" : _value == null ? "null" : TikWireText.Format(_value));
    }

    /// <summary>How a value is spelled on the wire, for display — shared by <see cref="TikValue{T}"/> and <see cref="TikField{T}"/>.</summary>
    internal static class TikWireText
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "An enum TikValue<T> holds a value only through the O/R mapper, which carries the same warning.")]
        internal static string Format<T>(T value)
        {
            if (value == null)
                return string.Empty;
            object boxed = value;
            Type type = boxed.GetType();
            if (type.IsEnum)
            {
                var metadata = TikEnumMetadata.Get(type);
                return metadata.IsFlags ? metadata.FormatFlags(boxed) : metadata.Format(boxed);
            }
            if (boxed is bool b)
                return b ? "true" : "false";
            if (boxed is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            return boxed.ToString() ?? string.Empty;
        }
    }
}
