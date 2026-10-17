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
    /// (<see cref="TikNegatableField{T}.IsNegated"/>), not a fourth state, so a comparison with <c>==</c> against a state
    /// keeps its meaning.
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
    /// What one mapped field of an entity holds — whether the router printed it at all (<see cref="State"/>), and if so
    /// the value, or the router's word when the type cannot hold it.
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
    /// A <see cref="TikField{T}"/> never takes a <c>!</c>: a leading <c>!</c> is part of its value (a comment or a name may
    /// start with one). A firewall-style matcher that RouterOS negates (<c>src-address=!10.0.0.0/8</c>) is a
    /// <see cref="TikNegatableField{T}"/> instead, so a negation assigned to a field that cannot hold one does not compile.
    /// </para>
    /// <para>
    /// Assign a value directly (<c>entity.Port = 8080</c>, <c>entity.Comment = null</c>), or the router's own word with
    /// <see cref="FromWire"/>; read it with <see cref="Value"/> or <see cref="ValueOrDefault"/>. There is deliberately no
    /// implicit conversion back to <typeparamref name="T"/>: it would make <c>==</c> ambiguous and silently turn an
    /// unparsed value into <c>null</c>. <c>entity.Port == 8080</c> compares a present value.
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
        private readonly TikFieldCore<T> _core;

        private TikField(TikValue<T> content)
        {
            _core = new TikFieldCore<T>(content);
        }

        internal TikFieldCore<T> Core => _core;

        /// <summary>Whether the field was printed, and whether its value could be read.</summary>
        public TikFieldState State => _core.State;

        /// <summary>
        /// The value: when <see cref="State"/> is <see cref="TikFieldState.Present"/> what was read or assigned (possibly
        /// <c>null</c>), when <see cref="TikFieldState.Absent"/> <c>null</c> — there is none.
        /// </summary>
        /// <exception cref="TikUnparsedValueException">
        /// The router printed a value <typeparamref name="T"/> cannot hold (<see cref="TikFieldState.Unparsed"/>). Returning
        /// <c>null</c> would invent a value; read <see cref="RawValue"/>, or use <see cref="TryGetValue"/> or
        /// <see cref="ValueOrDefault"/> where an unreadable value is expected.
        /// </exception>
        public T Value => _core.Value;

        /// <summary>The router's word when <see cref="State"/> is <see cref="TikFieldState.Unparsed"/>; otherwise <c>null</c>.</summary>
        public string? RawValue => _core.RawValue;

        internal static TikField<T> FromPresent(T value) => new TikField<T>(value);

        /// <summary>True when the router printed the field (or the caller assigned it) and the value was read.</summary>
        public bool IsPresent => State == TikFieldState.Present;

        /// <summary>The field was not printed or not loaded (the <c>default</c>).</summary>
        public static TikField<T> Absent => default;

        /// <summary><see cref="Value"/> when present, <paramref name="fallback"/> when absent or unparsed.</summary>
        public T ValueOrDefault(T fallback) => _core.ValueOrDefault(fallback);

        /// <summary>
        /// This value, or <paramref name="fallback"/> when this one is <see cref="TikFieldState.Absent"/> — the field-level
        /// "the source says nothing, keep what is there" of a merge:
        /// <c>.Field(e =&gt; e.Comment, (expected, current) =&gt; expected.IfAbsent(current))</c>.
        /// </summary>
        public TikField<T> IfAbsent(TikField<T> fallback) => _core.Printed ? this : fallback;

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
        public TikField<T> IfPrintedIn(TikField<T> current) => current._core.Printed ? this : current;

        /// <summary>True, with the value, when <see cref="State"/> is <see cref="TikFieldState.Present"/>.</summary>
        public bool TryGetValue(out T value) => _core.TryGetValue(out value);

        /// <summary>A value the caller assigns — <see cref="TikFieldState.Present"/>, including a <c>null</c>.</summary>
        public static implicit operator TikField<T>(T value) => new TikField<T>(value);

        /// <summary>
        /// The router's own word, sent as written — for a word <typeparamref name="T"/> has no member for (an enum's word
        /// on another RouterOS version: <c>proposal.PfsGroup = TikField&lt;PfsGroup?&gt;.FromWire("ec2n155")</c>). It reads
        /// as <see cref="TikFieldState.Unparsed"/>, the way a load of the same word does.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="word"/> is <c>null</c>.</exception>
        public static TikField<T> FromWire(string word) => new TikField<T>(TikValue<T>.FromWire(word));

        internal static TikField<T> FromUnparsed(string raw) => new TikField<T>(TikValue<T>.FromWire(raw));

        /// <summary>
        /// True when <paramref name="a"/> is present and holds <paramref name="b"/>. Compared with <c>null</c> it means "has
        /// no value": true when absent or present with <c>null</c>, never when unparsed (the router printed something).
        /// </summary>
        public static bool operator ==(TikField<T> a, T b) => a._core.EqualsValue(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikField<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is present and holds <paramref name="a"/>.</summary>
        public static bool operator ==(T a, TikField<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikField<T> b) => !(b == a);

        /// <summary>Same state, same value, same raw word.</summary>
        public static bool operator ==(TikField<T> a, TikField<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikField<T> a, TikField<T> b) => !a.Equals(b);

        // ── Ordering: nullable semantics against a value; Absent < Unparsed < Present for sorting ──

        /// <summary>True when both have a value and <paramref name="a"/>'s is smaller (like a lifted nullable comparison).</summary>
        public static bool operator <(TikField<T> a, T b) => a._core.Lifted(b, false, out int c) && c < 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is larger.</summary>
        public static bool operator >(TikField<T> a, T b) => a._core.Lifted(b, false, out int c) && c > 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not larger.</summary>
        public static bool operator <=(TikField<T> a, T b) => a._core.Lifted(b, false, out int c) && c <= 0;
        /// <summary>True when both have a value and <paramref name="a"/>'s is not smaller.</summary>
        public static bool operator >=(TikField<T> a, T b) => a._core.Lifted(b, false, out int c) && c >= 0;

        /// <summary>Sort order: Absent, then Unparsed (by word), then Present (by value, <c>null</c> first).</summary>
        public int CompareTo(TikField<T> other) => _core.CompareTo(other._core);

        int IComparable.CompareTo(object? obj)
            => obj is TikField<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikField<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        /// <inheritdoc/>
        public bool Equals(TikField<T> other) => _core.Equals(other._core);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikField<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _core.GetHashCode();

        /// <summary>
        /// The value for display — grids, logs, string formatting — spelled the way the router prints it: an enum as its
        /// word (<c>auto</c>, not <c>Auto</c>), a bool as <c>true</c>/<c>false</c>, a number invariantly; the router's word
        /// when unparsed; an empty string when absent.
        /// </summary>
        public override string ToString() => _core.ToString();

        private string DebuggerText => _core.DebuggerText;

        TikFieldState ITikField.State => State;
        object? ITikField.BoxedValue => _core.BoxedValue;
        string? ITikField.RawValue => RawValue;
        bool ITikField.IsNegated => false;
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

    /// <summary>
    /// <c>GetValueOrDefault()</c> for a <see cref="TikField{T}"/> or a <see cref="TikNegatableField{T}"/>, as <see cref="Nullable{T}.GetValueOrDefault()"/>: the value
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

        /// <summary>
        /// The value when present, <c>default(T)</c> otherwise. A negated value is present: this returns the value it
        /// negates, so check <see cref="TikNegatableField{T}.IsNegated"/> where that matters.
        /// </summary>
        public static T GetValueOrDefault<T>(this TikNegatableField<T?> value) where T : struct
            => value.State == TikFieldState.Present ? value.Value.GetValueOrDefault() : default;

        /// <summary>The value when present (negated or not), <c>null</c> otherwise.</summary>
        public static T? GetValueOrDefault<T>(this TikNegatableField<T?> value) where T : class
            => value.State == TikFieldState.Present ? value.Value : null;
    }

    /// <summary>The mapper's non-generic view of a <see cref="TikField{T}"/> or a <see cref="TikNegatableField{T}"/>.</summary>
    internal interface ITikField
    {
        TikFieldState State { get; }
        object? BoxedValue { get; }
        string? RawValue { get; }
        bool IsNegated { get; }
    }
}
