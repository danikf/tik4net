using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>
    /// An immutable list of <see cref="TikValue{T}"/> — a field that holds several values, such as
    /// <c>dst-port=22,8291,1000-2000</c>, <c>connection-state=established,related</c> or <c>hotspot=!from-client,http</c>.
    /// </summary>
    /// <typeparam name="T">The item type: <see cref="TikPortRange"/>, an enum, <c>string</c>, … — never a nullable form;
    /// the property is <c>TikField&lt;TikValueList&lt;T&gt;?&gt;</c>, whose <c>null</c> is "unset the field".</typeparam>
    /// <remarks>
    /// <para>
    /// Each item is a <see cref="TikValue{T}"/>, so an item can be a word <typeparamref name="T"/> cannot hold (a member
    /// a newer RouterOS added) without the rest of the list losing its type, and on a field whose members the router
    /// negates one by one (<c>tcp-flags</c>, <c>hotspot</c>, logging <c>topics</c>) an item carries its own <c>!</c>.
    /// A <c>!</c> on the whole list (<c>dst-port=!22,8291</c>) is the negation of the <see cref="TikValue{T}"/> that holds
    /// the list: <c>TikValue&lt;TikValueList&lt;TikPortRange&gt;&gt;.Not(list)</c>.
    /// </para>
    /// <para>
    /// The list is immutable: <see cref="With(TikValue{T}[])"/> and <see cref="Without(TikValue{T}[])"/> return a new one,
    /// so assign the result back (<c>rule.DstPort = rule.DstPort.Value!.With(443)</c>). That is what keeps a copied
    /// property and the change tracker safe: a list changed in place would change the loaded snapshot too, and a save
    /// would send nothing.
    /// </para>
    /// <para>
    /// Two lists are equal when they hold the same items the same number of times, in any order. The router prints some
    /// lists in an order of its own (<c>ack,!syn,fin</c> reads back <c>fin,ack,!syn</c>), so an order-sensitive comparison
    /// would see a change on every reload. A save sends the items in the order the list holds them.
    /// </para>
    /// </remarks>
#if NET8_0_OR_GREATER
    [global::System.Text.Json.Serialization.JsonConverter(typeof(TikValueListJsonConverterFactory))]
#endif
    public sealed class TikValueList<T> : IReadOnlyList<TikValue<T>>, IEquatable<TikValueList<T>>, ITikValueList
    {
        private readonly TikValue<T>[] _items;

        /// <summary>A list of plain values: <c>new TikValueList&lt;TikPortRange&gt;(22, 8291)</c>.</summary>
        /// <exception cref="ArgumentNullException">An item is <c>null</c>.</exception>
        public TikValueList(params T[] items)
            : this(Checked(items).Select(i => (TikValue<T>)i))
        {
        }

        /// <summary>
        /// A list whose items may be negated or the router's words:
        /// <c>new TikValueList&lt;HotspotMatch&gt;(TikValue&lt;HotspotMatch&gt;.Not(HotspotMatch.FromClient), HotspotMatch.Http)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">An item holds a <c>null</c> value.</exception>
        public TikValueList(params TikValue<T>[] items)
            : this((IEnumerable<TikValue<T>>)(items ?? throw new ArgumentNullException(nameof(items))))
        {
        }

        /// <summary>A list of the given items, in their order.</summary>
        /// <exception cref="ArgumentNullException">An item holds a <c>null</c> value.</exception>
        public TikValueList(IEnumerable<TikValue<T>> items)
        {
            _items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
            foreach (var item in _items)
                if (!item.IsWord && item.Value == null)
                    throw new ArgumentNullException(nameof(items), "A list item holds a value; null is not one.");
        }

        private static T[] Checked(T[] items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            return items;
        }

        /// <summary>The empty list.</summary>
        public static TikValueList<T> Empty { get; } = new TikValueList<T>(Enumerable.Empty<TikValue<T>>());

        /// <inheritdoc/>
        public int Count => _items.Length;

        /// <inheritdoc/>
        public TikValue<T> this[int index] => _items[index];

        /// <summary>True when some item is negated — a field whose members the router negates one by one.</summary>
        public bool HasNegatedItems => _items.Any(i => i.IsNegated);

        /// <summary>This list with <paramref name="items"/> appended.</summary>
        public TikValueList<T> With(params TikValue<T>[] items)
            => new TikValueList<T>(_items.Concat(items ?? throw new ArgumentNullException(nameof(items))));

        /// <summary>This list with <paramref name="items"/> appended: <c>ports.With(443)</c>.</summary>
        public TikValueList<T> With(params T[] items)
            => With(Checked(items).Select(i => (TikValue<T>)i).ToArray());

        /// <summary>
        /// This list without the items equal to any of <paramref name="items"/> — a negated item matches only a negated
        /// one (<c>flags.Without(TikValue&lt;TcpFlag&gt;.Not(TcpFlag.Ack))</c>).
        /// </summary>
        public TikValueList<T> Without(params TikValue<T>[] items)
        {
            var remove = items ?? throw new ArgumentNullException(nameof(items));
            return new TikValueList<T>(_items.Where(i => !remove.Contains(i)));
        }

        /// <summary>This list without the plain items equal to any of <paramref name="items"/>: <c>ports.Without(22)</c>.</summary>
        public TikValueList<T> Without(params T[] items)
            => Without(Checked(items).Select(i => (TikValue<T>)i).ToArray());

        bool ITikValueList.HasNegatedItems => HasNegatedItems;

        string? ITikValueList.UnknownItems
        {
            get
            {
                var words = _items.Where(i => i.IsWord).Select(i => i.ToString()).ToList();
                return words.Count == 0 ? null : string.Join(",", words);
            }
        }

        /// <inheritdoc/>
        public IEnumerator<TikValue<T>> GetEnumerator() => ((IEnumerable<TikValue<T>>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>The same items the same number of times, in any order.</summary>
        public bool Equals(TikValueList<T>? other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || other._items.Length != _items.Length)
                return false;
            var counts = new Dictionary<TikValue<T>, int>();
            foreach (var item in _items)
                counts[item] = counts.TryGetValue(item, out int n) ? n + 1 : 1;
            foreach (var item in other._items)
            {
                if (!counts.TryGetValue(item, out int n) || n == 0)
                    return false;
                counts[item] = n - 1;
            }
            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikValueList<T> other && Equals(other);

        /// <summary>A hash that does not depend on the order of the items, as <see cref="Equals(TikValueList{T})"/> does not.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _items.Length;
                foreach (var item in _items)
                    hash += item.GetHashCode() * 31 + 7;
                return hash;
            }
        }

        /// <summary>The same items the same number of times, in any order.</summary>
        public static bool operator ==(TikValueList<T>? a, TikValueList<T>? b) => a is null ? b is null : a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikValueList<T>? a, TikValueList<T>? b) => !(a == b);

        /// <summary>The items in the router's spelling, comma-separated: <c>22,8291,1000-2000</c>, <c>!from-client,http</c>.</summary>
        public override string ToString() => string.Join(",", _items.Select(i => i.ToString()));
    }

    /// <summary>The mapper's non-generic view of a <see cref="TikValueList{T}"/>.</summary>
    internal interface ITikValueList
    {
        /// <summary>The items the type cannot hold, comma-separated as the router printed them; else <c>null</c>.</summary>
        string? UnknownItems { get; }

        /// <summary>Whether some item carries its own <c>!</c>.</summary>
        bool HasNegatedItems { get; }
    }
}
