using System;

namespace tik4net
{
    /// <summary>
    /// The <c>upload/download</c> pair RouterOS uses for the rate fields of <c>/queue/simple</c> —
    /// <c>max-limit</c>, <c>limit-at</c>, <c>burst-limit</c>, <c>burst-threshold</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pair is why these fields need a type of their own rather than plain numbers, and it is also
    /// where the transports disagree: verified on RouterOS 7.24, <c>max-limit</c> reads
    /// <c>1000000/2000000</c> over the binary API and <c>1M/2M</c> over the CLI transports. The
    /// single-valued form of the same field on <c>/queue/tree</c> reads <c>1000000</c> on both and stays a
    /// plain <see cref="long"/> — the pairing is what changes the spelling, not the magnitude.
    /// </para>
    /// <para>
    /// A value written with one side only means <b>upload, with download zero</b> — measured by setting
    /// <c>max-limit=1M</c> and reading back <c>1000000/0</c>. It does not mean "the same on both sides",
    /// which is the assumption that would silently halve a caller's configuration.
    /// </para>
    /// <para>
    /// There is deliberately no conversion to a single number: a pair holds two, and picking one of them
    /// for the caller would be a guess. Read <see cref="Upload"/> or <see cref="Download"/>.
    /// </para>
    /// </remarks>
    public readonly struct TikRatePair : IEquatable<TikRatePair>
    {
        /// <summary>The upload side — the first of the two.</summary>
        public TikDataRate Upload { get; }

        /// <summary>The download side — the second of the two.</summary>
        public TikDataRate Download { get; }

        /// <summary>A pair of the two given rates.</summary>
        /// <param name="upload">The upload side.</param>
        /// <param name="download">The download side.</param>
        public TikRatePair(TikDataRate upload, TikDataRate download)
        {
            Upload = upload;
            Download = download;
        }

        /// <summary>
        /// Reads a pair in either spelling the router uses — <c>1M/2M</c> or <c>1000000/2000000</c>. A bare
        /// <b>number</b> with no separator is the upload side with download zero, which is what the router
        /// does with it; a bare <b>word</b> (<c>unlimited</c>) applies to both sides, because it describes
        /// the field rather than one half of it.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> is not a rate or a pair of them.</exception>
        public static TikRatePair Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikRatePair result))
                return result;

            throw new FormatException(
                $"'{value}' is not a RouterOS rate pair. Expected 'upload/download' (1M/2M, 1000000/2000000) "
                + "or a single rate.");
        }

        /// <summary><see cref="Parse"/> without the exception.</summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The parsed pair.</param>
        public static bool TryParse(string? value, out TikRatePair result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value!.Trim();
            int slash = text.IndexOf('/');

            // Exactly one separator, or none. '1//2' is not a pair with a strange half — it is not a pair,
            // and the half-tolerant reading below would otherwise take '/2' for a word.
            if (slash >= 0 && text.IndexOf('/', slash + 1) >= 0)
                return false;

            if (slash < 0)
            {
                TikDataRate single = ParseHalf(text);

                // A bare NUMBER is the upload side with download zero — measured: writing 'max-limit=1M'
                // reads back '1000000/0'. A bare WORD is not: 'unlimited' says the whole field is
                // unlimited, and pairing it with a zero download would write back 'unlimited/0', which is
                // a different configuration from the one the router reported. A word applies to both.
                result = single.HasValue
                    ? new TikRatePair(single, TikDataRate.FromValue(0))
                    : new TikRatePair(single, single);
                return true;
            }

            string upText = text.Substring(0, slash);
            string downText = text.Substring(slash + 1);
            if (upText.Length == 0 || downText.Length == 0)
                return false;

            result = new TikRatePair(ParseHalf(upText), ParseHalf(downText));
            return true;
        }

        /// <summary>
        /// One side of the pair, which may be a number or one of the router's words.
        /// </summary>
        /// <remarks>
        /// This uses <see cref="TikDataRate.Parse"/> rather than its strict <c>TryParse</c>, because a half
        /// is genuinely allowed to be a word: <c>/interface/ethernet bandwidth</c> defaults to
        /// <c>unlimited/unlimited</c>. Refusing that would leave the field a <c>string</c> forever, which is
        /// where it sat on the convention backlog until the type learned words.
        /// </remarks>
        private static TikDataRate ParseHalf(string text) => TikDataRate.Parse(text);

        /// <summary>
        /// <c>upload/download</c> in plain numbers — the spelling the binary API uses, accepted on write by
        /// every transport. Always both sides, because that is how the router stores the field: a value
        /// that arrived as <c>1M</c> reads back as <c>1000000/0</c> from the router itself.
        /// </summary>
        public override string ToString() => Upload.ToString() + "/" + Download.ToString();

        /// <summary>Reads a bare string as a pair, so <c>MaxLimit = "1M/2M"</c> keeps working.</summary>
        /// <param name="value">The value as the router writes it.</param>
        public static implicit operator TikRatePair(string value) => Parse(value);

        /// <summary>The <c>upload/download</c> spelling — see <see cref="ToString"/>.</summary>
        /// <param name="value">The pair.</param>
        public static implicit operator string(TikRatePair value) => value.ToString();

        /// <summary>
        /// A single number is the upload side with download zero — the same reading the router gives it.
        /// </summary>
        /// <param name="upload">The upload side.</param>
        public static implicit operator TikRatePair(long upload)
            => new TikRatePair(TikDataRate.FromValue(upload), TikDataRate.FromValue(0));

        /// <summary>Equality on both values, so the two spellings of one pair are equal.</summary>
        /// <param name="other">The pair to compare with.</param>
        public bool Equals(TikRatePair other) => Upload.Equals(other.Upload) && Download.Equals(other.Download);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikRatePair other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked { return (Upload.GetHashCode() * 397) ^ Download.GetHashCode(); }
        }

        /// <summary>Equality operator — see <see cref="Equals(TikRatePair)"/>.</summary>
        public static bool operator ==(TikRatePair left, TikRatePair right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikRatePair)"/>.</summary>
        public static bool operator !=(TikRatePair left, TikRatePair right) => !left.Equals(right);
    }
}
