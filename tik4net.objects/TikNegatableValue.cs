using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace tik4net.Objects
{
    /// <summary>
    /// A list member RouterOS negates on its own — <c>tcp-flags=syn,!ack</c>, <c>hotspot=!from-client,http</c>, logging
    /// <c>topics=info,!debug</c>: a value of <typeparamref name="T"/> and whether it carries a <c>!</c>.
    /// </summary>
    /// <typeparam name="T">The member's type: an enum, <c>string</c>, … — never a nullable form.</typeparam>
    /// <remarks>
    /// <para>
    /// A field whose members the router negates one by one is a list of these:
    /// <c>TikField&lt;TikValueList&lt;TikNegatableValue&lt;FirewallHotspotMatch&gt;&gt;?&gt;</c>. A plain value converts
    /// implicitly, so a list mixes both: <c>new TikValueList&lt;TikNegatableValue&lt;FirewallTcpFlag&gt;&gt;(FirewallTcpFlag.Syn,
    /// TikNegatableValue&lt;FirewallTcpFlag&gt;.Not(FirewallTcpFlag.Ack))</c>. A <c>string</c> member also takes the router's own
    /// spelling — such a list has no literal leading <c>!</c> to send — so <c>"!debug"</c> is <c>Not("debug")</c>:
    /// <c>new TikValueList&lt;TikNegatableValue&lt;string&gt;&gt;("info", "!debug")</c>.
    /// </para>
    /// <para>
    /// The item type is what lets a member carry a <c>!</c>: a <see cref="TikValueList{T}"/> of any other type takes none, so
    /// a negated member on a field the router does not negate member by member does not compile. A <c>!</c> on the whole
    /// list is the field's, not a member's: <see cref="TikNegatableField{T}"/>.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct TikNegatableValue<T> : IEquatable<TikNegatableValue<T>>, IComparable<TikNegatableValue<T>>, IComparable, ITikNegatableValue
    {
        private readonly T _value;
        private readonly bool _negated;

        private TikNegatableValue(T value, bool negated)
        {
            _value = value;
            _negated = negated;
        }

        /// <summary>The member itself, without its <c>!</c> (<see cref="IsNegated"/> says whether it has one).</summary>
        public T Value => _value;

        /// <summary>True when the member is negated — the router's <c>!</c> in front of it, which matches what it does not.</summary>
        public bool IsNegated => _negated;

        /// <summary>
        /// A negated member — the router's <c>!member</c>: <c>TikNegatableValue&lt;FirewallTcpFlag&gt;.Not(FirewallTcpFlag.Ack)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>: there is nothing to negate.</exception>
        public static TikNegatableValue<T> Not(T value)
            => new TikNegatableValue<T>(value ?? throw new ArgumentNullException(nameof(value), "Only a value can be negated."), negated: true);

        /// <summary>This member with <see cref="IsNegated"/> cleared.</summary>
        public TikNegatableValue<T> WithoutNegation() => new TikNegatableValue<T>(_value, negated: false);

        internal static TikNegatableValue<T> Create(T value, bool negated) => new TikNegatableValue<T>(value, negated);

        /// <summary>
        /// A plain member. For a <c>string</c> member a leading <c>!</c> is the negation, as it is on the router:
        /// <c>"!debug"</c> is <c>Not("debug")</c>. A bare <c>"!"</c> stays text.
        /// </summary>
        public static implicit operator TikNegatableValue<T>(T value)
            => value is string text && text.Length > 1 && text[0] == '!'
                ? new TikNegatableValue<T>((T)(object)text.Substring(1), negated: true)
                : new TikNegatableValue<T>(value, negated: false);

        /// <summary>True when <paramref name="a"/> is the member <paramref name="b"/> spells — <c>"!debug"</c> is the negation.</summary>
        public static bool operator ==(TikNegatableValue<T> a, T b) => a.Equals((TikNegatableValue<T>)b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikNegatableValue<T> a, T b) => !(a == b);

        /// <summary>True when <paramref name="b"/> is the member <paramref name="a"/> spells.</summary>
        public static bool operator ==(T a, TikNegatableValue<T> b) => b == a;

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(T a, TikNegatableValue<T> b) => !(b == a);

        /// <summary>Same member, same negation.</summary>
        public static bool operator ==(TikNegatableValue<T> a, TikNegatableValue<T> b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikNegatableValue<T> a, TikNegatableValue<T> b) => !a.Equals(b);

        /// <inheritdoc/>
        public bool Equals(TikNegatableValue<T> other)
            => _negated == other._negated && EqualityComparer<T>.Default.Equals(_value, other._value);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikNegatableValue<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => (_value == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value)) * 2 + (_negated ? 1 : 0);

        /// <summary>Sort order: by member (<c>null</c> first); a plain member before its negation.</summary>
        public int CompareTo(TikNegatableValue<T> other)
        {
            int byValue = Comparer<T>.Default.Compare(_value, other._value);
            return byValue != 0 ? byValue : _negated.CompareTo(other._negated);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikNegatableValue<T> other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a " + nameof(TikNegatableValue<T>) + "<" + typeof(T).Name + ">.", nameof(obj));

        /// <summary>The member spelled the way the router prints it, with its <c>!</c>: <c>!ack</c>, <c>debug</c>.</summary>
        public override string ToString() => (_negated ? "!" : "") + TikWireText.Format(_value);

        bool ITikNegatableValue.IsNegated => _negated;
        object? ITikNegatableValue.BoxedValue => _value;
    }

    /// <summary>The mapper's non-generic view of a <see cref="TikNegatableValue{T}"/>.</summary>
    internal interface ITikNegatableValue
    {
        bool IsNegated { get; }
        object? BoxedValue { get; }
    }

    internal static class TikNegatableValue
    {
        /// <summary>Whether <paramref name="type"/> is a <see cref="TikNegatableValue{T}"/>.</summary>
        internal static bool IsNegatableValue(Type type)
            => type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == typeof(TikNegatableValue<>);
    }
}
