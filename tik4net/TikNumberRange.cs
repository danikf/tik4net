using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// One item of a RouterOS number list: a single number (<c>10</c>) or an inclusive range (<c>20-30</c>), as in
    /// bridge <c>vlan-ids=10,20-30</c> or netwatch <c>http-codes=200,300-399</c>.
    /// </summary>
    /// <remarks>
    /// A <see cref="long"/> (and so an <see cref="int"/>) converts implicitly, so a list reads as it is written:
    /// <c>new TikValueList&lt;TikNumberRange&gt;(10, new TikNumberRange(20, 30))</c>. A number is not negative, and a
    /// range's end is not below its start. The type does not check a field's own limits (a VLAN id is 1–4094): the
    /// router refuses a value out of them. A port list is <see cref="TikPortRange"/>.
    /// </remarks>
    public readonly struct TikNumberRange : IEquatable<TikNumberRange>, IComparable<TikNumberRange>, IComparable
    {
        /// <summary>The first number of the range — the number itself for a single number.</summary>
        public long From { get; }

        /// <summary>The last number of the range — the number itself for a single number.</summary>
        public long To { get; }

        /// <summary>True when this is a range of more than one number.</summary>
        public bool IsRange => To != From;

        /// <summary>A single number.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is negative.</exception>
        public TikNumberRange(long number)
            : this(number, number)
        {
        }

        /// <summary>An inclusive range of numbers.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A number is negative, or <paramref name="to"/> is below <paramref name="from"/>.</exception>
        public TikNumberRange(long from, long to)
        {
            if (from < 0)
                throw new ArgumentOutOfRangeException(nameof(from), from, "A number in a RouterOS number list is not negative.");
            if (to < from)
                throw new ArgumentOutOfRangeException(nameof(to), to, "A range ends at or above its start (" + from.ToString(CultureInfo.InvariantCulture) + ").");
            From = from;
            To = to;
        }

        /// <summary>A single number: <c>vlan.VlanIds = new TikValueList&lt;TikNumberRange&gt;(10, 20)</c>.</summary>
        public static implicit operator TikNumberRange(long number) => new TikNumberRange(number);

        /// <summary>Reads <c>10</c> or <c>20-30</c>.</summary>
        /// <exception cref="FormatException">Not a number or a range of numbers.</exception>
        public static TikNumberRange Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));
            if (TryParse(value, out TikNumberRange result))
                return result;
            throw new FormatException($"'{value}' is not a RouterOS number or number range. Expected 10 or 20-30.");
        }

        /// <summary>Reads <c>10</c> or <c>20-30</c>; false for anything else.</summary>
        public static bool TryParse(string? value, out TikNumberRange result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string text = value!.Trim();
            int dash = text.IndexOf('-');
            string first = dash < 0 ? text : text.Substring(0, dash);
            string last = dash < 0 ? text : text.Substring(dash + 1);
            if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out long from)
                || !long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out long to)
                || to < from)
                return false;
            result = new TikNumberRange(from, to);
            return true;
        }

        /// <summary><c>10</c>, or <c>20-30</c> for a range — the router's spelling.</summary>
        public override string ToString()
            => IsRange
                ? From.ToString(CultureInfo.InvariantCulture) + "-" + To.ToString(CultureInfo.InvariantCulture)
                : From.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc/>
        public bool Equals(TikNumberRange other) => From == other.From && To == other.To;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikNumberRange other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked(From.GetHashCode() * 397 ^ To.GetHashCode());

        /// <summary>By start, then by end.</summary>
        public int CompareTo(TikNumberRange other)
        {
            int byFrom = From.CompareTo(other.From);
            return byFrom != 0 ? byFrom : To.CompareTo(other.To);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikNumberRange other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a TikNumberRange.", nameof(obj));

        /// <summary>Same start and end.</summary>
        public static bool operator ==(TikNumberRange a, TikNumberRange b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikNumberRange a, TikNumberRange b) => !a.Equals(b);
    }
}
