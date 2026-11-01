using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// A RouterOS rate or size in bits per second, written as a plain number, with a decimal suffix, with a
    /// <c>bps</c> unit, or as a word — <c>1000000</c>, <c>1M</c> and <c>1Mbps</c> are the same value, and
    /// <c>unlimited</c> is none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <see cref="TikDuration"/>, it exists because <b>the router writes the same value several ways
    /// depending on how you ask</b>. Verified on RouterOS 7.24: <c>/queue/simple max-limit</c> reads
    /// <c>1000000/2000000</c> over the binary API and <c>1M/2M</c> over the CLI transports, which read
    /// <c>print as-value</c>. A string-typed property hands that difference to the caller.
    /// </para>
    /// <para>
    /// The <c>bps</c> spellings are a second, separate divergence, and they appear only where RouterOS is
    /// rendering a rate for display. Measured on 7.24: <c>/queue/simple print stats</c> writes
    /// <c>rate=0bps/0bps</c> over the CLI where the API writes <c>rate=0/0</c>, and
    /// <c>/interface/ethernet monitor</c> writes <c>rate=1Gbps</c>. The neighbouring single-valued
    /// <c>total-rate</c> on the same CLI read is a bare <c>0</c>, so the unit is not even consistent within
    /// one record — which is exactly the kind of difference a typed property is for.
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
    /// <para>
    /// <see cref="Parse"/> and <see cref="TryParse"/> read what the type knows: a number or a word
    /// <see cref="TikDataRateSpecial"/> names. Anything else is refused, and a mapped field then keeps the router's word as
    /// its unparsed value. <see cref="TryParseNumber"/> is the question "is it a number"; <see cref="FromToken"/> is how
    /// to send a word the library does not know.
    /// </para>
    /// </remarks>
    public readonly struct TikDataRate : IEquatable<TikDataRate>
    {
        private readonly TikValueOrToken<long> _core;

        private TikDataRate(long? value, string? token)
        {
            _core = new TikValueOrToken<long>(value, token);
        }

        /// <summary>
        /// The value, in bits per second — or in bytes, for the fields RouterOS measures that way — or
        /// <c>null</c> when the field carries a <see cref="Token"/> instead.
        /// </summary>
        public long? Value => _core.Value;

        /// <summary>
        /// The word the router used in place of a rate (<c>unlimited</c>, <c>auto</c>, …), or <c>null</c>
        /// when the field carries a real <see cref="Value"/>.
        /// </summary>
        /// <remarks>
        /// <c>/interface/ethernet bandwidth</c> defaults to <c>unlimited/unlimited</c>, so a rate field can
        /// hold a word the same way a duration field holds <c>none</c>. Keeping it verbatim is what lets
        /// such a field be typed at all: the alternative is a <see cref="FormatException"/> that fails the
        /// load of the WHOLE entity, not just the one property.
        /// </remarks>
        public string? Token => _core.Token;

        /// <summary>Whether this is a real rate rather than a word.</summary>
        public bool HasValue => _core.Value.HasValue;

        /// <summary>
        /// What this actually holds: a <see cref="Value"/>, a <see cref="Special"/> word the library
        /// recognises, or <see cref="TikValueKind.Unknown"/> text it does not (only <see cref="FromToken"/> makes one).
        /// </summary>
        public TikValueKind Kind => _core.Kind(_core.Token != null && TikSpecialWords.TryReadRate(_core.Token, out _));

        /// <summary>
        /// The router word this field carries, when it is one the library recognises — otherwise
        /// <c>null</c>. Compare against this rather than against <see cref="Token"/>: the spelling and case
        /// vary by transport, the enum member does not.
        /// </summary>
        public TikDataRateSpecial? Special
            => _core.Token != null && TikSpecialWords.TryReadRate(_core.Token, out TikDataRateSpecial s)
                ? s
                : (TikDataRateSpecial?)null;

        /// <summary>A rate of the given plain value.</summary>
        /// <param name="value">The value with no suffix applied.</param>
        public static TikDataRate FromValue(long value) => new TikDataRate(value, null);

        /// <summary>
        /// A rate field set to one of the router's words, named rather than spelled.
        /// </summary>
        /// <param name="special">The state, e.g. <see cref="TikDataRateSpecial.Unlimited"/>.</param>
        /// <remarks>
        /// Prefer this to <see cref="FromToken"/> when you know which state you mean — the spelling comes
        /// from one table instead of from the call site.
        /// </remarks>
        public static TikDataRate FromSpecial(TikDataRateSpecial special)
            => new TikDataRate(null, TikSpecialWords.WordFor(special));

        /// <summary>
        /// One of the router's non-numeric words for a rate field, kept verbatim.
        /// </summary>
        /// <param name="token">The word, e.g. <c>unlimited</c>.</param>
        public static TikDataRate FromToken(string token)
        {
            Guard.ArgumentNotNullOrEmptyString(token, nameof(token));
            return new TikDataRate(null, token);
        }

        /// <summary>
        /// Reads a rate in any of the forms RouterOS writes: plain (<c>1000000</c>), with a decimal suffix
        /// (<c>1M</c>, <c>500k</c>, <c>2G</c>), with a <c>bps</c> unit (<c>0bps</c>, <c>1Gbps</c>), or a word
        /// <see cref="TikDataRateSpecial"/> names (<c>unlimited</c>, …).
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        /// <exception cref="FormatException">
        /// <paramref name="value"/> is neither a rate nor a word the library knows. <see cref="FromToken"/> takes such a word.
        /// </exception>
        public static TikDataRate Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikDataRate result))
                return result;

            throw new FormatException($"'{value}' is not a RouterOS rate.");
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
            if (TryParseNumber(text, out long number))
                result = new TikDataRate(number, null);
            else if (TikSpecialWords.TryReadRate(text, out _))
                result = new TikDataRate(null, text);
            else
                return false;
            return true;
        }

        /// <summary>
        /// Reads a value that is a <b>number</b>, and fails on one that is not — <c>unlimited</c> included, and a number
        /// too large for a <see cref="long"/>.
        /// </summary>
        /// <remarks>
        /// For callers that have to decide how to put a value ON THE WIRE, because the answer picks the
        /// encoding: `WinboxFieldResolver` encodes a numeric rate as an M2 <c>u64</c> and a word as a string, and
        /// sending <c>auto</c> as a 64-bit integer is accepted by the router and silently ignored.
        /// </remarks>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The number, with its suffix applied.</param>
        public static bool TryParseNumber(string? value, out long result)
        {
            result = 0;
            return !string.IsNullOrWhiteSpace(value) && TryParseNumberCore(value!.Trim(), out result);
        }

        /// <summary>
        /// The numeric grammar, applied whole — no partial match, so <c>1M5</c> is not a rate rather than
        /// being read as one.
        /// </summary>
        private static bool TryParseNumberCore(string text, out long result)
        {
            result = 0;
            if (text.Length == 0)
                return false;

            // The unit comes off first, because it ends in the same letter a suffix could be mistaken for:
            // '1Gbps' ends in 's', which is not a multiplier, and stripping only the last character would
            // leave '1Gbp'. Measured spellings are 'bps', 'kbps', 'Mbps' and 'Gbps'.
            if (text.Length > 3
                && string.Compare(text, text.Length - 3, "bps", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
                text = text.Substring(0, text.Length - 3);
            else if (text.Length == 3 && string.Equals(text, "bps", StringComparison.OrdinalIgnoreCase))
                return false;   // a unit with no number in front of it

            long multiplier = 1;
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

            if (text.Length == 0)
                return false;

            // A fraction is accepted ONLY with a multiplier. RouterOS renders a scaled rate the way it
            // renders any scaled number, so '1.5Mbps' has to read as 1 500 000 — but a bare '0.1' is not a
            // rate at all (it is /queue/simple's bucket-size), and reading it as the 0 it rounds to would
            // be worse than refusing it.
            if (text.IndexOf('.') >= 0)
            {
                if (multiplier == 1)
                    return false;
                if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal fraction))
                    return false;

                decimal scaled = fraction * multiplier;
                if (scaled < long.MinValue || scaled > long.MaxValue)
                    return false;

                result = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
                return true;
            }

            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
                return false;

            // Checked: 99999999999T does not fit, and a number that wrapped would read as a different rate.
            try { result = checked(number * multiplier); }
            catch (OverflowException) { return false; }
            return true;
        }

        /// <summary>
        /// The plain number, with no suffix and no unit — exact, accepted on write everywhere, and the
        /// spelling the binary API uses, so a value read over the CLI is not written back in a different
        /// form than it would have had over the API. A <see cref="Token"/> is written verbatim, because the
        /// word IS the value the router holds.
        /// </summary>
        public override string ToString()
        {
            if (_core.Token != null) return _core.Token;
            if (!_core.Value.HasValue) return string.Empty;
            return _core.Value.Value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a bare string as a rate, so <c>MaxLimit = "1M"</c> keeps working.</summary>
        /// <param name="value">The value as the router writes it.</param>
        public static implicit operator TikDataRate(string value) => Parse(value);

        /// <summary>A plain number is a rate with no suffix.</summary>
        /// <param name="value">The value.</param>
        public static implicit operator TikDataRate(long value) => FromValue(value);

        /// <summary>
        /// The plain-number spelling, or the <see cref="Token"/> — see <see cref="ToString"/>. There is no
        /// implicit conversion to a number, deliberately: a rate field may hold a word rather than a rate,
        /// and "how many bits per second is <c>unlimited</c>" has no answer that is not a guess. Read
        /// <see cref="Value"/>, which says so by being null.
        /// </summary>
        /// <param name="value">The rate.</param>
        public static implicit operator string(TikDataRate value) => value.ToString();

        /// <summary>The value as a number, or <c>null</c> for a <see cref="Token"/>.</summary>
        /// <param name="value">The rate.</param>
        public static explicit operator long?(TikDataRate value) => value.Value;

        /// <summary>
        /// Equality on the <b>value</b>, so <c>1M</c>, <c>1Mbps</c> and <c>1000000</c> are equal — the point
        /// of the type. Two tokens are equal when they are the same word, ignoring case.
        /// </summary>
        /// <param name="other">The rate to compare with.</param>
        public bool Equals(TikDataRate other) => _core.Equals(other._core);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikDataRate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _core.GetHashCode();

        /// <summary>Equality operator — see <see cref="Equals(TikDataRate)"/>.</summary>
        public static bool operator ==(TikDataRate left, TikDataRate right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikDataRate)"/>.</summary>
        public static bool operator !=(TikDataRate left, TikDataRate right) => !left.Equals(right);
    }
}
