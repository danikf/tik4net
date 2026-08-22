using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// A RouterOS rate or size in bits per second, written either as a plain number or with a decimal
    /// suffix — <c>1000000</c> and <c>1M</c> are the same value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <see cref="TikDuration"/>, it exists because <b>the router writes the same value two ways
    /// depending on how you ask</b>. Verified on RouterOS 7.24: <c>/queue/simple max-limit</c> reads
    /// <c>1000000/2000000</c> over the binary API and <c>1M/2M</c> over the CLI transports, which read
    /// <c>print as-value</c>. A string-typed property hands that difference to the caller.
    /// </para>
    /// <para>
    /// The suffixes are <b>decimal, not binary</b>: <c>500k</c> is 500 000, not 512 000 — measured by
    /// setting <c>limit-at=500k</c> and reading back <c>500000</c>. <see cref="ToString"/> writes the plain
    /// number, which is exact, accepted on write by every transport, and the form the API itself uses.
    /// </para>
    /// <para>
    /// Not every RouterOS field that looks like a rate is one: <c>rate-limit</c> on a PPP profile packs six
    /// values into one string, <c>dst-limit</c> on a firewall rule packs a count, a burst and a mode, and
    /// <c>rates</c> on a CapsMan configuration is a comma-separated list. Those stay strings.
    /// </para>
    /// </remarks>
    public readonly struct TikDataRate : IEquatable<TikDataRate>
    {
        /// <summary>The value, in bits per second — or in bytes, for the fields RouterOS measures that way.</summary>
        public long Value { get; }

        private TikDataRate(long value)
        {
            Value = value;
        }

        /// <summary>A rate of the given plain value.</summary>
        /// <param name="value">The value with no suffix applied.</param>
        public static TikDataRate FromValue(long value) => new TikDataRate(value);

        /// <summary>
        /// Reads a rate written either plainly (<c>1000000</c>) or with a decimal suffix (<c>1M</c>,
        /// <c>500k</c>, <c>2G</c>).
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not a rate.</exception>
        public static TikDataRate Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikDataRate result))
                return result;

            throw new FormatException(
                $"'{value}' is not a RouterOS rate. Expected a plain number (1000000) or one with a "
                + "decimal suffix (1k, 1M, 1G, 1T).");
        }

        /// <summary><see cref="Parse"/> without the exception.</summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The parsed rate.</param>
        public static bool TryParse(string? value, out TikDataRate result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value!.Trim();
            long multiplier = 1;

            // The suffix is case-sensitive in neither direction on the router, and only the last character
            // can carry one — "1M5" is not a rate and must not read as 1 000 000.
            char last = text[text.Length - 1];
            switch (last)
            {
                case 'k': case 'K': multiplier = 1000L; break;
                case 'M': case 'm': multiplier = 1000000L; break;
                case 'G': case 'g': multiplier = 1000000000L; break;
                case 'T': case 't': multiplier = 1000000000000L; break;
            }

            if (multiplier != 1)
                text = text.Substring(0, text.Length - 1);

            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
                return false;

            result = new TikDataRate(number * multiplier);
            return true;
        }

        /// <summary>
        /// The plain number, with no suffix — exact, accepted on write everywhere, and the spelling the
        /// binary API uses, so a value read over the CLI is not written back in a different form than it
        /// would have had over the API.
        /// </summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Reads a bare string as a rate, so <c>MaxLimit = "1M"</c> keeps working.</summary>
        /// <param name="value">The value as the router writes it.</param>
        public static implicit operator TikDataRate(string value) => Parse(value);

        /// <summary>A plain number is a rate with no suffix.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator TikDataRate(long value) => FromValue(value);

        /// <summary>The plain-number spelling — see <see cref="ToString"/>.</summary>
        /// <param name="value">The rate.</param>
        public static implicit operator string(TikDataRate value) => value.ToString();

        /// <summary>The value as a number.</summary>
        /// <param name="value">The rate.</param>
        public static implicit operator long(TikDataRate value) => value.Value;

        /// <summary>Equality on the value, so <c>1M</c> and <c>1000000</c> are equal — the point of the type.</summary>
        /// <param name="other">The rate to compare with.</param>
        public bool Equals(TikDataRate other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikDataRate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality operator — see <see cref="Equals(TikDataRate)"/>.</summary>
        public static bool operator ==(TikDataRate left, TikDataRate right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikDataRate)"/>.</summary>
        public static bool operator !=(TikDataRate left, TikDataRate right) => !left.Equals(right);
    }
}
