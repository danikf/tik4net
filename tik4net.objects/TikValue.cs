using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace tik4net.Objects
{
    /// <summary>
    /// One list item as the router spells it — a value of <typeparamref name="T"/>, or the router's own word when
    /// <typeparamref name="T"/> cannot hold it.
    /// </summary>
    /// <typeparam name="T">The item type: <see cref="TikPortRange"/>, an enum, <c>string</c>, a
    /// <see cref="TikNegatableValue{T}"/>, … — the item type of the <see cref="TikValueList{T}"/> that holds it.</typeparam>
    /// <remarks>
    /// <para>
    /// A <see cref="TikValueList{T}"/> is a list of these: each item can be a word the item type does not know (a member
    /// a newer RouterOS added) without the rest of the list losing its type. A word keeps whatever the router printed,
    /// a member's <c>!</c> included (<c>!newflag</c>), and is written back as it came.
    /// </para>
    /// <para>
    /// A <see cref="TikValue{T}"/> takes no <c>!</c>. A member the router negates on its own (<c>tcp-flags=syn,!ack</c>) is
    /// a <see cref="TikNegatableValue{T}"/> item; a negated field is a <see cref="TikNegatableField{T}"/>. A field holds its
    /// value in a <see cref="TikField{T}"/>, not in a <see cref="TikValue{T}"/>: a field's own word is
    /// <c>TikField&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{DebuggerText,nq}")]
    public readonly struct TikValue<T> : IEquatable<TikValue<T>>, IComparable<TikValue<T>>, IComparable
    {
        private readonly T _value;
        private readonly string? _word;

        private TikValue(T value, string? word)
        {
            _value = value;
            _word = word;
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

        /// <summary>True, with the value, unless this is the router's word.</summary>
        public bool TryGetValue(out T value)
        {
            value = _word == null ? _value : default!;
            return _word == null;
        }

        /// <summary>
        /// The router's own word, sent as written — for an item <typeparamref name="T"/> has no member for (an enum's word
        /// on another RouterOS version). It reads back the same way. A field's own word is
        /// <see cref="TikField{T}.FromWire"/>.
        /// </summary>
        public static TikValue<T> FromWire(string word)
            => new TikValue<T>(default!, word ?? throw new ArgumentNullException(nameof(word)));

        /// <summary>A value — <c>rule.Port = 8080</c> converts through here.</summary>
        public static implicit operator TikValue<T>(T value) => new TikValue<T>(value, null);

        /// <summary>True when <paramref name="a"/> is a value equal to <paramref name="b"/>.</summary>
        public static bool operator ==(TikValue<T> a, T b)
            => a._word == null && EqualityComparer<T>.Default.Equals(a._value, b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is a value equal to <paramref name="a"/>.</summary>
        public static bool operator ==(T a, TikValue<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikValue<T> b) => !(b == a);

        /// <summary>Same value or word.</summary>
        public static bool operator ==(TikValue<T> a, TikValue<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, TikValue<T> b) => !a.Equals(b);

        /// <inheritdoc/>
        public bool Equals(TikValue<T> other)
            => string.Equals(_word, other._word, StringComparison.Ordinal)
               && (_word != null || EqualityComparer<T>.Default.Equals(_value, other._value));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikValue<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => _word == null ? (_value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value)) : _word.GetHashCode();

        /// <summary>Sort order: words (by word) before values (by value, <c>null</c> first).</summary>
        public int CompareTo(TikValue<T> other)
        {
            if ((_word == null) != (other._word == null))
                return _word == null ? 1 : -1;
            return _word != null ? string.CompareOrdinal(_word, other._word) : Comparer<T>.Default.Compare(_value, other._value);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikValue<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikValue<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        /// <summary>
        /// The value spelled the way the router prints it — an enum as its word, a bool as <c>true</c>/<c>false</c>, a
        /// number invariantly, a negated member with its <c>!</c>, the router's word as it came.
        /// </summary>
        public override string ToString() => _word ?? TikWireText.Format(_value);

        private string DebuggerText
            => _word != null ? "word \"" + _word + "\"" : _value == null ? "null" : TikWireText.Format(_value);
    }

    /// <summary>How a value is spelled on the wire, for display — shared by <see cref="TikValue{T}"/> and the field types.</summary>
    internal static class TikWireText
    {
        /// <summary>
        /// The whole-value <c>!</c> as the router spells it: on a list of <see cref="TikNegatableValue{T}"/> items, which
        /// carry their own <c>!</c>, a bare leading element (<c>tcp-flags=!,syn,!ack</c>) — <c>!syn</c> would read as one
        /// negated member; else a <c>!</c> in front of the value (<c>!10.0.0.0/8</c>, <c>dst-port=!22,8291</c>).
        /// </summary>
        internal static string Negation<T>(T value) => value is ITikValueList list && list.MembersNegatable ? "!," : "!";

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
