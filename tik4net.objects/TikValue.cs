using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace tik4net.Objects
{
    /// <summary>
    /// What an entity property holds for one field of a row: whether the router printed it, and if so whether its
    /// value could be read. See <see cref="TikValue{T}"/>.
    /// </summary>
    /// <remarks>
    /// The three states are exclusive and stay so. A negated matcher (<c>!value</c>) will be a separate flag on the value,
    /// not a fourth state, so a comparison with <c>==</c> against a state keeps its meaning.
    /// </remarks>
    public enum TikValueState
    {
        /// <summary>
        /// The field was not printed, or not loaded — a field another RouterOS version does not have, one the row
        /// does not carry in its current state, or a property of an entity that was never loaded and never assigned.
        /// This is the <c>default</c> of <see cref="TikValue{T}"/>.
        /// </summary>
        Absent = 0,

        /// <summary>
        /// The router printed the field and it was read, or the caller assigned a value. <see cref="TikValue{T}.Value"/>
        /// holds it, and may be <c>null</c> (an assigned <c>null</c>, which an update sends as an unset).
        /// </summary>
        Present = 1,

        /// <summary>
        /// The router printed the field but the property's type cannot hold the value — a word the enum does not
        /// know, or a value that is not in the type's format. <see cref="TikValue{T}.RawValue"/> keeps the router's
        /// word, and a save that does not change the property writes it back unchanged.
        /// </summary>
        Unparsed = 2,
    }

    /// <summary>
    /// The value of one mapped field of an entity — the value itself, and whether the router printed it at all
    /// (<see cref="State"/>).
    /// </summary>
    /// <typeparam name="T">
    /// The property's value type, always in its nullable form — <c>int?</c>, <c>bool?</c>, <c>string?</c>, an enum
    /// <c>?</c> — so that <c>entity.Port = null</c> compiles and means "unset this field".
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// A plain property cannot tell "the router did not print this field" from "the router printed its default",
    /// nor keep a value its type cannot hold. <see cref="TikValue{T}"/> carries both: a load leaves a field the row
    /// lacks <see cref="TikValueState.Absent"/>, and a value that does not parse <see cref="TikValueState.Unparsed"/>
    /// with the router's word in <see cref="RawValue"/> — so a read never fails on a value from another RouterOS
    /// version, and a save never invents one.
    /// </para>
    /// <para>
    /// Assign a value directly (<c>entity.Port = 8080</c>, <c>entity.Comment = null</c>); read it with
    /// <see cref="Value"/> or <see cref="ValueOrDefault"/>. There is deliberately no implicit conversion back to
    /// <typeparamref name="T"/>: it would make <c>==</c> ambiguous and silently turn an unparsed value into
    /// <c>null</c>. <c>entity.Port == 8080</c> compares a present value.
    /// </para>
    /// <para>
    /// Changes are detected against the value loaded, not by tracking assignment — so copying a property from one
    /// entity to another (<c>target.Port = source.Port</c>) carries its state with it, and a save still sees the
    /// change.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{DebuggerText,nq}")]
    public readonly struct TikValue<T> : IEquatable<TikValue<T>>, IComparable<TikValue<T>>, IComparable, ITikValue
    {
        private readonly T _value;
        private readonly string? _rawValue;
        private readonly TikValueState _state;

        private TikValue(TikValueState state, T value, string? rawValue)
        {
            _state = state;
            _value = value;
            _rawValue = rawValue;
        }

        /// <summary>Whether the field was printed, and whether its value could be read.</summary>
        public TikValueState State => _state;

        /// <summary>
        /// The value: when <see cref="State"/> is <see cref="TikValueState.Present"/> what was read or assigned (possibly
        /// <c>null</c>), when <see cref="TikValueState.Absent"/> <c>null</c> — there is none.
        /// </summary>
        /// <exception cref="TikUnparsedValueException">
        /// The router printed a value <typeparamref name="T"/> cannot hold (<see cref="TikValueState.Unparsed"/>). Returning
        /// <c>null</c> would invent a value; read <see cref="RawValue"/>, or use <see cref="TryGetValue"/> or
        /// <see cref="ValueOrDefault"/> where an unreadable value is expected.
        /// </exception>
        public T Value => _state == TikValueState.Unparsed ? throw new TikUnparsedValueException(typeof(T), _rawValue) : _value;

        /// <summary>The router's word when <see cref="State"/> is <see cref="TikValueState.Unparsed"/>; otherwise <c>null</c>.</summary>
        public string? RawValue => _rawValue;

        /// <summary>True when the router printed the field (or the caller assigned it) and the value was read.</summary>
        public bool IsPresent => _state == TikValueState.Present;

        /// <summary>The field was not printed or not loaded (the <c>default</c>).</summary>
        public static TikValue<T> Absent => default;

        /// <summary><see cref="Value"/> when present, <paramref name="fallback"/> when absent or unparsed.</summary>
        public T ValueOrDefault(T fallback) => _state == TikValueState.Present ? _value : fallback;

        /// <summary>True, with the value, when <see cref="State"/> is <see cref="TikValueState.Present"/>.</summary>
        public bool TryGetValue(out T value)
        {
            value = _state == TikValueState.Present ? _value : default!;
            return _state == TikValueState.Present;
        }

        /// <summary>
        /// A value spelled in the router's own word, sent as written — for a word <typeparamref name="T"/> has no member for
        /// (an enum's word on another RouterOS version: <c>TikValue&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>). Its
        /// <see cref="State"/> is <see cref="TikValueState.Unparsed"/>, as it would read back.
        /// </summary>
        public static TikValue<T> FromWire(string word)
            => new TikValue<T>(TikValueState.Unparsed, default!, word ?? throw new ArgumentNullException(nameof(word)));

        /// <summary>A value the caller assigns — <see cref="TikValueState.Present"/>, including a <c>null</c>.</summary>
        public static implicit operator TikValue<T>(T value) => new TikValue<T>(TikValueState.Present, value, null);

        internal static TikValue<T> FromUnparsed(string raw) => new TikValue<T>(TikValueState.Unparsed, default!, raw);

        /// <summary>
        /// True when <paramref name="a"/> is present and holds <paramref name="b"/>. Compared with <c>null</c> it means "has no
        /// value": true when absent or present with <c>null</c>, never when unparsed (the router printed something).
        /// </summary>
        public static bool operator ==(TikValue<T> a, T b)
            => b == null
                ? a._state == TikValueState.Absent || (a._state == TikValueState.Present && a._value == null)
                : a._state == TikValueState.Present && EqualityComparer<T>.Default.Equals(a._value, b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is present and holds <paramref name="a"/>.</summary>
        public static bool operator ==(T a, TikValue<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikValue<T> b) => !(b == a);

        /// <summary>Same state, same value, same raw word.</summary>
        public static bool operator ==(TikValue<T> a, TikValue<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValue<T> a, TikValue<T> b) => !a.Equals(b);

        // ── Ordering: nullable semantics against a value; Absent < Unparsed < Present for sorting ──

        /// <summary>True when both have a value and <paramref name="a"/>'s is smaller (like a lifted nullable comparison).</summary>
        public static bool operator <(TikValue<T> a, T b) => Lifted(a, b, out int c) && c < 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is larger.</summary>
        public static bool operator >(TikValue<T> a, T b) => Lifted(a, b, out int c) && c > 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not larger.</summary>
        public static bool operator <=(TikValue<T> a, T b) => Lifted(a, b, out int c) && c <= 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not smaller.</summary>
        public static bool operator >=(TikValue<T> a, T b) => Lifted(a, b, out int c) && c >= 0;

        private static bool Lifted(TikValue<T> a, T b, out int comparison)
        {
            comparison = 0;
            if (a._state != TikValueState.Present || a._value == null || b == null)
                return false;
            comparison = Comparer<T>.Default.Compare(a._value, b);
            return true;
        }

        /// <summary>Sort order: Absent, then Unparsed (by word), then Present (by value, <c>null</c> first).</summary>
        public int CompareTo(TikValue<T> other)
        {
            if (_state != other._state)
                return Rank(_state).CompareTo(Rank(other._state));
            if (_state == TikValueState.Unparsed)
                return string.CompareOrdinal(_rawValue, other._rawValue);
            return _state == TikValueState.Present ? Comparer<T>.Default.Compare(_value, other._value) : 0;
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikValue<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikValue<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        private static int Rank(TikValueState state)
            => state == TikValueState.Absent ? 0 : state == TikValueState.Unparsed ? 1 : 2;

        /// <inheritdoc/>
        public bool Equals(TikValue<T> other)
            => _state == other._state
               && EqualityComparer<T>.Default.Equals(_value, other._value)
               && string.Equals(_rawValue, other._rawValue, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikValue<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)_state;
                hash = hash * 397 ^ (_value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value));
                return hash * 397 ^ (_rawValue == null ? 0 : _rawValue.GetHashCode());
            }
        }

        /// <summary>
        /// The value for display — grids, logs, string formatting — spelled the way the router prints it: an enum as its
        /// word (<c>auto</c>, not <c>Auto</c>), a bool as <c>true</c>/<c>false</c>, a number invariantly; the router's word
        /// when unparsed; an empty string when absent.
        /// </summary>
        public override string ToString()
            => _state == TikValueState.Present ? WireText(_value)
             : _state == TikValueState.Unparsed ? _rawValue ?? string.Empty
             : string.Empty;

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "An enum TikValue<T> holds a value only through the O/R mapper, which carries the same warning.")]
        private static string WireText(T value)
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

        private string DebuggerText
            => _state == TikValueState.Absent ? "Absent"
             : _state == TikValueState.Unparsed ? "Unparsed \"" + _rawValue + "\""
             : _value == null ? "null" : WireText(_value);

        TikValueState ITikValue.State => _state;
        object? ITikValue.BoxedValue => _value;
        string? ITikValue.RawValue => _rawValue;
    }

    /// <summary>
    /// Thrown by <see cref="TikValue{T}.Value"/> when the router printed a value the property's type cannot hold — a word
    /// its enum does not know, or a value in another format (<see cref="TikValueState.Unparsed"/>).
    /// </summary>
    public class TikUnparsedValueException : InvalidOperationException
    {
        /// <summary>The router's word.</summary>
        public string? RawValue { get; }

        /// <summary>The type that could not hold it.</summary>
        public Type ValueType { get; }

        /// <summary>.ctor</summary>
        public TikUnparsedValueException(Type valueType, string? rawValue)
            : base(string.Format(CultureInfo.InvariantCulture,
                "The router printed '{0}', which {1} cannot hold (TikValueState.Unparsed). Read RawValue, or use TryGetValue / ValueOrDefault.",
                rawValue, Nullable.GetUnderlyingType(valueType)?.Name ?? valueType.Name))
        {
            RawValue = rawValue;
            ValueType = valueType;
        }
    }

    /// <summary>The mapper's non-generic view of a <see cref="TikValue{T}"/>.</summary>
    internal interface ITikValue
    {
        TikValueState State { get; }
        object? BoxedValue { get; }
        string? RawValue { get; }
    }
}
