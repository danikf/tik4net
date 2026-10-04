using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace tik4net.Objects
{
    /// <summary>
    /// What an entity property holds for one field of a row: whether the router printed it, and if so whether its
    /// value could be read. See <see cref="TikField{T}"/>.
    /// </summary>
    /// <remarks>
    /// The three states are exclusive and stay so. A negated matcher (<c>!value</c>) is a flag on a present value
    /// (<see cref="TikField{T}.IsNegated"/>), not a fourth state, so a comparison with <c>==</c> against a state keeps its
    /// meaning.
    /// </remarks>
    public enum TikFieldState
    {
        /// <summary>
        /// The field was not printed, or not loaded — a field another RouterOS version does not have, one the row
        /// does not carry in its current state, or a property of an entity that was never loaded and never assigned.
        /// This is the <c>default</c> of <see cref="TikField{T}"/>.
        /// </summary>
        Absent = 0,

        /// <summary>
        /// The router printed the field and it was read, or the caller assigned a value. <see cref="TikField{T}.Value"/>
        /// holds it, and may be <c>null</c> (an assigned <c>null</c>, which an update sends as an unset).
        /// </summary>
        Present = 1,

        /// <summary>
        /// The router printed the field but the property's type cannot hold the value — a word the enum does not
        /// know, or a value that is not in the type's format. <see cref="TikField{T}.RawValue"/> keeps the router's
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
    /// nor keep a value its type cannot hold. <see cref="TikField{T}"/> carries both: a load leaves a field the row
    /// lacks <see cref="TikFieldState.Absent"/>, and a value that does not parse <see cref="TikFieldState.Unparsed"/>
    /// with the router's word in <see cref="RawValue"/> — so a read never fails on a value from another RouterOS
    /// version, and a save never invents one.
    /// </para>
    /// <para>
    /// A firewall-style matcher can be negated — <c>src-address=!10.0.0.0/8</c> matches everything outside that network.
    /// On a property that declares <see cref="TikPropertyAttribute.Negatable"/> the <c>!</c> is not part of the value:
    /// it reads as <see cref="IsNegated"/>, and <see cref="Not"/> writes one (<c>rule.SrcAddress =
    /// TikField&lt;string?&gt;.Not("10.0.0.0/8")</c>). A negated value never equals the plain one.
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
#if NET8_0_OR_GREATER
    [global::System.Text.Json.Serialization.JsonConverter(typeof(TikFieldJsonConverterFactory))]
#endif
    public readonly struct TikField<T> : IEquatable<TikField<T>>, IComparable<TikField<T>>, IComparable, ITikField
    {
        private readonly T _value;
        private readonly string? _rawValue;
        private readonly TikFieldState _state;
        private readonly bool _negated;

        private TikField(TikFieldState state, T value, string? rawValue, bool negated = false)
        {
            _state = state;
            _value = value;
            _rawValue = rawValue;
            _negated = negated;
        }

        /// <summary>Whether the field was printed, and whether its value could be read.</summary>
        public TikFieldState State => _state;

        /// <summary>
        /// The value: when <see cref="State"/> is <see cref="TikFieldState.Present"/> what was read or assigned (possibly
        /// <c>null</c>), when <see cref="TikFieldState.Absent"/> <c>null</c> — there is none.
        /// </summary>
        /// <exception cref="TikUnparsedValueException">
        /// The router printed a value <typeparamref name="T"/> cannot hold (<see cref="TikFieldState.Unparsed"/>). Returning
        /// <c>null</c> would invent a value; read <see cref="RawValue"/>, or use <see cref="TryGetValue"/> or
        /// <see cref="ValueOrDefault"/> where an unreadable value is expected.
        /// </exception>
        public T Value => _state == TikFieldState.Unparsed ? throw new TikUnparsedValueException(typeof(T), _rawValue) : _value;

        /// <summary>The router's word when <see cref="State"/> is <see cref="TikFieldState.Unparsed"/>; otherwise <c>null</c>.</summary>
        public string? RawValue => _state == TikFieldState.Unparsed ? _rawValue : null;

        /// <summary>
        /// On a present <c>[Flags]</c> value: the words the router printed that the enum has no member for (comma-separated),
        /// else <c>null</c>. They are not part of <see cref="Value"/>, take no part in <c>==</c> against a value, and are
        /// written back by a save. <see cref="With"/> and <see cref="Without"/> keep them; assigning a new value replaces the
        /// whole set, them included.
        /// </summary>
        public string? UnknownFlagWords => _state == TikFieldState.Present ? _rawValue : null;

        /// <summary>
        /// True when the value is negated — the router's <c>!</c> in front of a matcher (<c>src-address=!10.0.0.0/8</c>,
        /// <c>connection-state=!established,related</c>), which matches everything the value does not. Only a present,
        /// non-null value can be negated, and only a property that declares <see cref="TikPropertyAttribute.Negatable"/>
        /// reads or writes one.
        /// </summary>
        /// <remarks>
        /// The <c>!</c> negates the whole value, a list included. A field whose members are negated one by one
        /// (<c>tcp-flags=syn,!ack</c>) keeps its <c>!</c>s in the value itself.
        /// </remarks>
        public bool IsNegated => _state == TikFieldState.Present && _negated;

        /// <summary>
        /// A negated value — the router's <c>!value</c>: <c>rule.SrcAddress = TikField&lt;string?&gt;.Not("10.0.0.0/8")</c>.
        /// Writing it to a property that does not declare <see cref="TikPropertyAttribute.Negatable"/> throws.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>: there is nothing to negate.</exception>
        public static TikField<T> Not(T value)
            => new TikField<T>(TikFieldState.Present,
                value ?? throw new ArgumentNullException(nameof(value), "Only a value can be negated."), null, negated: true);

        /// <summary>This value with <see cref="IsNegated"/> cleared — the matcher it negates.</summary>
        public TikField<T> WithoutNegation() => new TikField<T>(_state, _value, _rawValue);

        internal TikField<T> AsNegated()
            => new TikField<T>(_state, _value, _rawValue, negated: _state == TikFieldState.Present && _value != null);

        /// <summary>
        /// This <c>[Flags]</c> value with <paramref name="flags"/> added, keeping the router's words the enum does not know
        /// (<see cref="UnknownFlagWords"/>) and a negation (<see cref="IsNegated"/>). An absent or unparsed value becomes
        /// <paramref name="flags"/>.
        /// </summary>
        public TikField<T> With(T flags) => Combine(flags, add: true);

        /// <summary>
        /// This <c>[Flags]</c> value with <paramref name="flags"/> removed, keeping the router's words the enum does not know.
        /// </summary>
        public TikField<T> Without(T flags) => Combine(flags, add: false);

        private TikField<T> Combine(T flags, bool add)
        {
            Type enumType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (!enumType.IsEnum || flags == null)
                throw new InvalidOperationException("With/Without apply to a [Flags] enum value and a non-null argument.");
            long current = _state == TikFieldState.Present && _value != null ? Convert.ToInt64(_value, CultureInfo.InvariantCulture) : 0;
            long change = Convert.ToInt64(flags, CultureInfo.InvariantCulture);
            long result = add ? current | change : current & ~change;
            return new TikField<T>(TikFieldState.Present, (T)Enum.ToObject(enumType, result),
                _state == TikFieldState.Present ? _rawValue : null, IsNegated);
        }

        internal static TikField<T> FromPresentWithUnknownFlags(T value, string? unknownWords, bool negated = false)
            => new TikField<T>(TikFieldState.Present, value, string.IsNullOrEmpty(unknownWords) ? null : unknownWords,
                negated && value != null);

        /// <summary>True when the router printed the field (or the caller assigned it) and the value was read.</summary>
        public bool IsPresent => _state == TikFieldState.Present;

        /// <summary>The field was not printed or not loaded (the <c>default</c>).</summary>
        public static TikField<T> Absent => default;

        /// <summary><see cref="Value"/> when present, <paramref name="fallback"/> when absent or unparsed.</summary>
        public T ValueOrDefault(T fallback) => _state == TikFieldState.Present ? _value : fallback;

        /// <summary>
        /// This value, or <paramref name="fallback"/> when this one is <see cref="TikFieldState.Absent"/> — the field-level
        /// "the source says nothing, keep what is there" of a merge:
        /// <c>.Field(e =&gt; e.Comment, (expected, current) =&gt; expected.IfAbsent(current))</c>.
        /// </summary>
        public TikField<T> IfAbsent(TikField<T> fallback) => _state == TikFieldState.Absent ? fallback : this;

        /// <summary>
        /// This value where the router prints the field on <paramref name="current"/>; <paramref name="current"/> itself
        /// (<see cref="TikFieldState.Absent"/>) where it does not — the field-level "this field does not apply to this row"
        /// of a merge: <c>.Field(e =&gt; e.Passthrough, (expected, current) =&gt; expected.IfPrintedIn(current))</c>.
        /// </summary>
        /// <remarks>
        /// RouterOS leaves a field out of a row it does not apply to — mangle <c>passthrough</c> on a <c>jump</c> rule, a
        /// field this version or this hardware lacks — and writing it changes nothing it prints. Without this rule an
        /// expected row that assigns such a field never equals the loaded one, and every merge run updates the row. A
        /// field that is merely unset is left out too, and is then never written by this rule: use it only for fields
        /// that do not apply, not for ones that are optional.
        /// </remarks>
        public TikField<T> IfPrintedIn(TikField<T> current) => current._state == TikFieldState.Absent ? current : this;

        /// <summary>True, with the value, when <see cref="State"/> is <see cref="TikFieldState.Present"/>.</summary>
        public bool TryGetValue(out T value)
        {
            value = _state == TikFieldState.Present ? _value : default!;
            return _state == TikFieldState.Present;
        }

        /// <summary>
        /// A value spelled in the router's own word, sent as written — for a word <typeparamref name="T"/> has no member for
        /// (an enum's word on another RouterOS version: <c>TikField&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>). Its
        /// <see cref="State"/> is <see cref="TikFieldState.Unparsed"/>, as it would read back.
        /// </summary>
        public static TikField<T> FromWire(string word)
            => new TikField<T>(TikFieldState.Unparsed, default!, word ?? throw new ArgumentNullException(nameof(word)));

        /// <summary>A value the caller assigns — <see cref="TikFieldState.Present"/>, including a <c>null</c>.</summary>
        public static implicit operator TikField<T>(T value) => new TikField<T>(TikFieldState.Present, value, null);

        internal static TikField<T> FromUnparsed(string raw) => new TikField<T>(TikFieldState.Unparsed, default!, raw);

        /// <summary>
        /// True when <paramref name="a"/> is present, not negated, and holds <paramref name="b"/>. Compared with <c>null</c> it
        /// means "has no value": true when absent or present with <c>null</c>, never when unparsed (the router printed
        /// something). A negated value (<c>!10.0.0.0/8</c>) matches the opposite of <paramref name="b"/>, so it is never equal.
        /// </summary>
        public static bool operator ==(TikField<T> a, T b)
            => b == null
                ? a._state == TikFieldState.Absent || (a._state == TikFieldState.Present && a._value == null)
                : a._state == TikFieldState.Present && !a._negated && EqualityComparer<T>.Default.Equals(a._value, b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikField<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is present and holds <paramref name="a"/>.</summary>
        public static bool operator ==(T a, TikField<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikField<T> b) => !(b == a);

        /// <summary>Same state, same value, same raw word, same negation.</summary>
        public static bool operator ==(TikField<T> a, TikField<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikField<T> a, TikField<T> b) => !a.Equals(b);

        // ── Ordering: nullable semantics against a value; Absent < Unparsed < Present for sorting ──

        /// <summary>
        /// True when both have a value and <paramref name="a"/>'s is smaller (like a lifted nullable comparison). A negated
        /// value is not ordered against a plain one: every comparison with it is false.
        /// </summary>
        public static bool operator <(TikField<T> a, T b) => Lifted(a, b, out int c) && c < 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is larger.</summary>
        public static bool operator >(TikField<T> a, T b) => Lifted(a, b, out int c) && c > 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not larger.</summary>
        public static bool operator <=(TikField<T> a, T b) => Lifted(a, b, out int c) && c <= 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not smaller.</summary>
        public static bool operator >=(TikField<T> a, T b) => Lifted(a, b, out int c) && c >= 0;

        private static bool Lifted(TikField<T> a, T b, out int comparison)
        {
            comparison = 0;
            if (a._state != TikFieldState.Present || a._negated || a._value == null || b == null)
                return false;
            comparison = Comparer<T>.Default.Compare(a._value, b);
            return true;
        }

        /// <summary>
        /// Sort order: Absent, then Unparsed (by word), then Present (by value, <c>null</c> first; a plain value before its
        /// negation).
        /// </summary>
        public int CompareTo(TikField<T> other)
        {
            if (_state != other._state)
                return Rank(_state).CompareTo(Rank(other._state));
            if (_state == TikFieldState.Unparsed)
                return string.CompareOrdinal(_rawValue, other._rawValue);
            if (_state != TikFieldState.Present)
                return 0;
            int byValue = Comparer<T>.Default.Compare(_value, other._value);
            return byValue != 0 ? byValue : _negated.CompareTo(other._negated);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikField<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikField<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        private static int Rank(TikFieldState state)
            => state == TikFieldState.Absent ? 0 : state == TikFieldState.Unparsed ? 1 : 2;

        /// <inheritdoc/>
        public bool Equals(TikField<T> other)
            => _state == other._state
               && _negated == other._negated
               && EqualityComparer<T>.Default.Equals(_value, other._value)
               && string.Equals(_rawValue, other._rawValue, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikField<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)_state + (_negated ? 16 : 0);
                hash = hash * 397 ^ (_value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value));
                return hash * 397 ^ (_rawValue == null ? 0 : _rawValue.GetHashCode());
            }
        }

        /// <summary>
        /// The value for display — grids, logs, string formatting — spelled the way the router prints it: an enum as its
        /// word (<c>auto</c>, not <c>Auto</c>), a bool as <c>true</c>/<c>false</c>, a number invariantly, a negated value
        /// with its <c>!</c>; the router's word when unparsed; an empty string when absent.
        /// </summary>
        public override string ToString()
            => _state == TikFieldState.Present ? (_negated ? "!" : "") + JoinWords(WireText(_value), _rawValue)
             : _state == TikFieldState.Unparsed ? _rawValue ?? string.Empty
             : string.Empty;

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "An enum TikField<T> holds a value only through the O/R mapper, which carries the same warning.")]
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

        private static string JoinWords(string known, string? unknown)
            => string.IsNullOrEmpty(unknown) ? known : known.Length == 0 ? unknown! : known + "," + unknown;

        private string DebuggerText
            => _state == TikFieldState.Absent ? "Absent"
             : _state == TikFieldState.Unparsed ? "Unparsed \"" + _rawValue + "\""
             : _value == null ? "null"
             : (_negated ? "!" : "") + (_rawValue != null ? WireText(_value) + " +" + _rawValue : WireText(_value));

        TikFieldState ITikField.State => _state;
        object? ITikField.BoxedValue => _value;
        string? ITikField.RawValue => RawValue;
        string? ITikField.UnknownFlagWords => UnknownFlagWords;
        bool ITikField.IsNegated => IsNegated;
    }

    /// <summary>
    /// Thrown by <see cref="TikField{T}.Value"/> when the router printed a value the property's type cannot hold — a word
    /// its enum does not know, or a value in another format (<see cref="TikFieldState.Unparsed"/>).
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
                "The router printed '{0}', which {1} cannot hold (TikFieldState.Unparsed). Read RawValue, or use TryGetValue / ValueOrDefault.",
                rawValue, Nullable.GetUnderlyingType(valueType)?.Name ?? valueType.Name))
        {
            RawValue = rawValue;
            ValueType = valueType;
        }
    }

    /// <summary>The mapper's non-generic view of a <see cref="TikField{T}"/>.</summary>
    /// <summary>
    /// <c>GetValueOrDefault()</c> for a <see cref="TikField{T}"/>, as <see cref="Nullable{T}.GetValueOrDefault()"/>: the value
    /// when present, <c>default</c> when absent or unparsed — <c>false</c>, <c>0</c> or an enum's zero member for a value
    /// type, <c>null</c> for a reference type. It never throws, so an unparsed value reads as "none"; where that must not
    /// happen, read <see cref="TikField{T}.Value"/>.
    /// </summary>
    public static class TikFieldExtensions
    {
        /// <summary>The value when present, <c>default(T)</c> otherwise (<c>bool d = addr.Disabled.GetValueOrDefault();</c>).</summary>
        public static T GetValueOrDefault<T>(this TikField<T?> value) where T : struct
            => value.State == TikFieldState.Present ? value.Value.GetValueOrDefault() : default;

        /// <summary>The value when present, <c>null</c> otherwise.</summary>
        public static T? GetValueOrDefault<T>(this TikField<T?> value) where T : class
            => value.State == TikFieldState.Present ? value.Value : null;
    }

    internal interface ITikField
    {
        TikFieldState State { get; }
        object? BoxedValue { get; }
        string? RawValue { get; }
        string? UnknownFlagWords { get; }
        bool IsNegated { get; }
    }
}
