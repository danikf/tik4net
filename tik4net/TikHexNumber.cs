using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// A whole number RouterOS writes in base 16 — <c>0x8000</c> — such as the spanning-tree priorities of
    /// <c>/interface/bridge</c> and <c>/interface/bridge/port</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain <see cref="int"/> cannot hold these fields, for two reasons measured on the lab routers. The
    /// binary API and REST print them in hex on every version (<c>priority=0x80</c>), while the CLI's
    /// <c>as-value</c> prints decimal before 7.24 (<c>128</c> on 6.49.13 and 7.21.5) and hex from 7.24 — so the
    /// reader has to take both spellings. And a write has to use the hex one: RouterOS 7.24 refuses a decimal
    /// bridge-port priority (<c>input does not match any value of priority</c>) and accepts <c>0x80</c>, which
    /// every version tested accepts for both fields.
    /// </para>
    /// <para>
    /// So this type reads <c>0x80</c>, <c>0X80</c> and <c>128</c> as the same number, and always writes
    /// <c>0x</c> plus upper-case digits with no padding — the spelling the API prints.
    /// </para>
    /// </remarks>
    public readonly struct TikHexNumber : IEquatable<TikHexNumber>, IComparable<TikHexNumber>, IComparable
    {
        /// <summary>The number.</summary>
        public long Value { get; }

        /// <summary>The given number.</summary>
        /// <param name="value">The number.</param>
        public TikHexNumber(long value)
        {
            Value = value;
        }

        /// <summary>
        /// Reads either spelling the router uses: <c>0x8000</c> (base 16, either case of the prefix and the
        /// digits) or <c>32768</c> (base 10).
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not a number in either spelling.</exception>
        public static TikHexNumber Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikHexNumber result))
                return result;

            throw new FormatException($"'{value}' is not a RouterOS number. Expected 0x8000 or 32768.");
        }

        /// <summary><see cref="Parse"/> without the exception.</summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The parsed number.</param>
        public static bool TryParse(string? value, out TikHexNumber result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value!.Trim();
            long number;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // NumberStyles.HexNumber takes no prefix and no sign, and an empty digit run is refused, so
                // '0x' alone and '0x-1' both fail here rather than reading as something.
                if (!long.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number))
                    return false;
            }
            else if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }

            result = new TikHexNumber(number);
            return true;
        }

        /// <summary>
        /// <c>0x</c> and the upper-case hex digits with no padding (<c>0x8000</c>, <c>0x70</c>) — the spelling
        /// the binary API prints, and the one every RouterOS version tested accepts on write.
        /// </summary>
        public override string ToString() => "0x" + Value.ToString("X", CultureInfo.InvariantCulture);

        /// <summary>Reads a string in either spelling — see <see cref="Parse"/>.</summary>
        /// <param name="value">The value as the router writes it.</param>
        public static implicit operator TikHexNumber(string value) => Parse(value);

        /// <summary>The API's hex spelling — see <see cref="ToString"/>.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator string(TikHexNumber value) => value.ToString();

        /// <summary>A number: <c>(TikHexNumber)0x80</c> and <c>(TikHexNumber)128</c> are the same value.</summary>
        /// <param name="value">The number.</param>
        public static implicit operator TikHexNumber(long value) => new TikHexNumber(value);

        /// <summary>The number — see <see cref="Value"/>.</summary>
        /// <param name="value">The value.</param>
        public static explicit operator long(TikHexNumber value) => value.Value;

        /// <summary>Equality on the number, so the two spellings of one value are equal.</summary>
        /// <param name="other">The value to compare with.</param>
        public bool Equals(TikHexNumber other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikHexNumber other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Orders by the number.</summary>
        /// <param name="other">The value to compare with.</param>
        public int CompareTo(TikHexNumber other) => Value.CompareTo(other.Value);

        int IComparable.CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is TikHexNumber other) return CompareTo(other);
            throw new ArgumentException("Object is not a " + nameof(TikHexNumber) + ".", nameof(obj));
        }

        /// <summary>Equality operator — see <see cref="Equals(TikHexNumber)"/>.</summary>
        public static bool operator ==(TikHexNumber left, TikHexNumber right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikHexNumber)"/>.</summary>
        public static bool operator !=(TikHexNumber left, TikHexNumber right) => !left.Equals(right);
    }
}
