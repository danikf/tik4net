using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// One item of a RouterOS port list: a single port (<c>22</c>) or an inclusive range (<c>1000-2000</c>), as in
    /// <c>dst-port=22,8291,1000-2000</c>.
    /// </summary>
    /// <remarks>
    /// An <see cref="int"/> converts implicitly, so a port list reads as it is written:
    /// <c>new TikValueList&lt;TikPortRange&gt;(22, 8291, new TikPortRange(1000, 2000))</c>. A port is 0–65535; a range's
    /// end is not below its start.
    /// </remarks>
    public readonly struct TikPortRange : IEquatable<TikPortRange>, IComparable<TikPortRange>, IComparable
    {
        /// <summary>The first port of the range — the port itself for a single port.</summary>
        public int From { get; }

        /// <summary>The last port of the range — the port itself for a single port.</summary>
        public int To { get; }

        /// <summary>True when this is a range of more than one port.</summary>
        public bool IsRange => To != From;

        /// <summary>A single port.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not 0–65535.</exception>
        public TikPortRange(int port)
            : this(port, port)
        {
        }

        /// <summary>An inclusive range of ports.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A port is not 0–65535, or <paramref name="to"/> is below <paramref name="from"/>.</exception>
        public TikPortRange(int from, int to)
        {
            if (from < 0 || from > 65535)
                throw new ArgumentOutOfRangeException(nameof(from), from, "A port is 0-65535.");
            if (to < 0 || to > 65535)
                throw new ArgumentOutOfRangeException(nameof(to), to, "A port is 0-65535.");
            if (to < from)
                throw new ArgumentOutOfRangeException(nameof(to), to, "A range ends at or above its start (" + from.ToString(CultureInfo.InvariantCulture) + ").");
            From = from;
            To = to;
        }

        /// <summary>A single port: <c>rule.DstPort = new TikValueList&lt;TikPortRange&gt;(22, 8291)</c>.</summary>
        public static implicit operator TikPortRange(int port) => new TikPortRange(port);

        /// <summary>Reads <c>22</c> or <c>1000-2000</c>.</summary>
        /// <exception cref="FormatException">Not a port or a range of ports.</exception>
        public static TikPortRange Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));
            if (TryParse(value, out TikPortRange result))
                return result;
            throw new FormatException($"'{value}' is not a RouterOS port or port range. Expected 22 or 1000-2000.");
        }

        /// <summary>Reads <c>22</c> or <c>1000-2000</c>; false for anything else.</summary>
        public static bool TryParse(string? value, out TikPortRange result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string text = value!.Trim();
            int dash = text.IndexOf('-');
            string first = dash < 0 ? text : text.Substring(0, dash);
            string last = dash < 0 ? text : text.Substring(dash + 1);
            if (!int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int from)
                || !int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out int to)
                || from > 65535 || to > 65535 || to < from)
                return false;
            result = new TikPortRange(from, to);
            return true;
        }

        /// <summary><c>22</c>, or <c>1000-2000</c> for a range — the router's spelling.</summary>
        public override string ToString()
            => IsRange
                ? From.ToString(CultureInfo.InvariantCulture) + "-" + To.ToString(CultureInfo.InvariantCulture)
                : From.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc/>
        public bool Equals(TikPortRange other) => From == other.From && To == other.To;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikPortRange other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked(From * 65537 + To);

        /// <summary>By start, then by end.</summary>
        public int CompareTo(TikPortRange other)
        {
            int byFrom = From.CompareTo(other.From);
            return byFrom != 0 ? byFrom : To.CompareTo(other.To);
        }

        int IComparable.CompareTo(object? obj)
            => obj is TikPortRange other ? CompareTo(other)
             : obj == null ? 1
             : throw new ArgumentException("Not a TikPortRange.", nameof(obj));

        /// <summary>Same start and end.</summary>
        public static bool operator ==(TikPortRange a, TikPortRange b) => a.Equals(b);

        /// <summary>The negation of <c>a == b</c>.</summary>
        public static bool operator !=(TikPortRange a, TikPortRange b) => !a.Equals(b);
    }
}
