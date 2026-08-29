using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net
{
    /// <summary>
    /// A RouterOS duration field: either a length of time or one of the words the router uses in place of
    /// one (<c>none</c>, <c>auto</c>, <c>disabled</c>, …).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because <b>the router writes the same duration two different ways depending on how you
    /// ask</b>. The binary API, REST and native WinBox answer in the compact form — <c>10s</c>, <c>5m</c>,
    /// <c>1d</c>, <c>200ms</c>, <c>21h16m40s</c> — while the CLI transports, which read
    /// <c>print as-value</c>, answer in clock form — <c>00:00:10</c>, <c>00:05:00</c>, <c>1d00:00:00</c>,
    /// <c>00:00:00.200</c>, <c>21:16:40</c>. Same router, same field, same moment. A property typed
    /// <see cref="string"/> hands that difference straight to the caller, so code that compares, parses or
    /// round-trips a duration gets a different answer per transport. Parsing both forms into one value is
    /// what makes a duration mean the same thing everywhere.
    /// </para>
    /// <para>
    /// A plain <see cref="TimeSpan"/> is not enough on its own: several duration fields also accept words
    /// (<c>lease-time=none</c>, <c>keepalive-timeout=disabled</c>, <c>enabled=auto</c>), and a type that
    /// cannot hold those would turn one of the router's states into the wrong one rather than into an
    /// error. <see cref="Token"/> keeps them, verbatim.
    /// </para>
    /// <para>
    /// <see cref="ToString"/> always renders the compact form, which is what RouterOS accepts on write over
    /// every transport, so a value read over the CLI and written back is not silently reformatted into
    /// something the router would read as a different field shape.
    /// </para>
    /// </remarks>
    public readonly struct TikDuration : IEquatable<TikDuration>
    {
        // Compact form: 1w2d3h4m5s6ms, in that order, any part optional — but at least one, and nothing
        // else in the string. The old uptime regex made every part optional AND was used with Match rather
        // than a full-string anchor, so it matched the empty prefix of ANYTHING and reported zero for
        // input it had not understood at all.
        private static readonly Regex CompactPattern = new Regex(
            @"^(?:(?<w>\d+)w)?(?:(?<d>\d+)d)?(?:(?<h>\d+)h)?(?:(?<m>\d+)m(?!s))?(?:(?<s>\d+)s)?(?:(?<ms>\d+)ms)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Clock form as the CLI prints it: an optional week/day prefix in front of hh:mm:ss[.fff].
        private static readonly Regex ClockPattern = new Regex(
            @"^(?:(?<w>\d+)w)?(?:(?<d>\d+)d)?(?<h>\d+):(?<m>\d{1,2}):(?<s>\d{1,2})(?:\.(?<frac>\d{1,7}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly TimeSpan? _value;
        private readonly string? _token;

        private TikDuration(TimeSpan? value, string? token)
        {
            _value = value;
            _token = token;
        }

        /// <summary>The length of time, or <c>null</c> when the field carries a <see cref="Token"/> instead.</summary>
        public TimeSpan? Value => _value;

        /// <summary>
        /// The word the router used in place of a duration (<c>none</c>, <c>auto</c>, <c>disabled</c>,
        /// <c>any</c>, …), or <c>null</c> when the field carries a real <see cref="Value"/>.
        /// </summary>
        public string? Token => _token;

        /// <summary>Whether this is a real length of time rather than a word.</summary>
        public bool HasValue => _value.HasValue;

        /// <summary>
        /// What this actually holds: a <see cref="Value"/>, a <see cref="Special"/> word the library
        /// recognises, or <see cref="TikValueKind.Unknown"/> text it does not.
        /// </summary>
        /// <remarks>
        /// <b><see cref="TikValueKind.Unknown"/> is a gap in tik4net, not a state of the router.</b> The
        /// value still survives verbatim and still writes back unchanged, so nothing breaks — but it is
        /// also reported on the <c>value.token</c> trace channel, because a token that behaves perfectly
        /// is exactly how a missing word stays missing.
        /// </remarks>
        public TikValueKind Kind
        {
            get
            {
                if (_value.HasValue) return TikValueKind.Value;
                if (_token == null) return TikValueKind.Empty;
                return TikSpecialWords.TryReadDuration(_token, out _)
                    ? TikValueKind.Special
                    : TikValueKind.Unknown;
            }
        }

        /// <summary>
        /// The router word this field carries, when it is one the library recognises — otherwise
        /// <c>null</c>. Compare against this rather than against <see cref="Token"/>: the word's spelling
        /// and case vary by transport, the enum member does not.
        /// </summary>
        public TikDurationSpecial? Special
            => _token != null && TikSpecialWords.TryReadDuration(_token, out TikDurationSpecial s)
                ? s
                : (TikDurationSpecial?)null;

        /// <summary>A duration of the given length.</summary>
        /// <param name="value">The length of time.</param>
        public static TikDuration FromTimeSpan(TimeSpan value) => new TikDuration(value, null);

        /// <summary>
        /// A duration field set to one of the router's words, named rather than spelled.
        /// </summary>
        /// <param name="special">The state, e.g. <see cref="TikDurationSpecial.None"/>.</param>
        /// <remarks>
        /// Prefer this to <see cref="FromToken"/> when you know which state you mean: the spelling comes
        /// from one table instead of from the call site, so a typo is a compile error. The router still
        /// refuses a word the particular field does not accept — the type knows what durations use, not
        /// what this menu takes.
        /// </remarks>
        public static TikDuration FromSpecial(TikDurationSpecial special)
            => new TikDuration(null, TikSpecialWords.WordFor(special));

        /// <summary>
        /// One of the router's non-duration words for a duration field, kept verbatim.
        /// </summary>
        /// <param name="token">The word, e.g. <c>none</c> or <c>disabled</c>.</param>
        public static TikDuration FromToken(string token)
        {
            Guard.ArgumentNotNullOrEmptyString(token, nameof(token));
            return new TikDuration(null, token);
        }

        /// <summary>
        /// Reads a duration in <b>either</b> of the forms RouterOS writes: compact (<c>10s</c>, <c>1d2h</c>,
        /// <c>200ms</c>) or clock (<c>00:00:10</c>, <c>1d00:00:00</c>, <c>00:00:00.200</c>). A bare number is
        /// read as seconds, which is what the router accepts on write. Anything else is kept as a
        /// <see cref="Token"/>.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        public static TikDuration Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikDuration result))
                return result;

            // Unreachable in practice — TryParse only fails on null/empty, which Guard has ruled out —
            // but stated rather than assumed, so a change to TryParse cannot make this return a wrong value.
            throw new FormatException($"'{value}' is not a RouterOS duration.");
        }

        /// <summary>
        /// <see cref="Parse"/> without the exception. Fails only on a null or empty <paramref name="value"/>:
        /// a non-empty value the duration grammars do not match is a <see cref="Token"/>, not a failure,
        /// because that is what the router does with <c>none</c> and its relatives.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The parsed duration.</param>
        public static bool TryParse(string? value, out TikDuration result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value!.Trim();

            if (TryParseTimeSpan(text, out TimeSpan span))
            {
                result = new TikDuration(span, null);
                return true;
            }

            if (!TikSpecialWords.TryReadDuration(text, out _))
                TikSpecialWords.TraceUnknown(nameof(TikDuration), text);

            result = new TikDuration(null, text);
            return true;
        }

        /// <summary>
        /// Reads the length of time out of either router form. Returns <c>false</c> for a word such as
        /// <c>none</c>, and for anything the two grammars do not fully match — no partial match, which is
        /// what made the previous parser answer "zero" for input it had not read.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <param name="result">The parsed length of time.</param>
        public static bool TryParseTimeSpan(string? value, out TimeSpan result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = value!.Trim();

            // ToString() writes a leading '-' for a negative TimeSpan, so Parse has to read one back or the
            // type cannot round-trip its own output. The sign is stripped here and re-applied at the end,
            // which keeps the three grammars below free of it.
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative)
            {
                text = text.Substring(1).TrimStart();
                if (text.Length == 0)
                    return false;
            }

            // A bare number is seconds. RouterOS accepts it on write and several fields report it that way.
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            {
                result = TimeSpan.FromSeconds(negative ? -seconds : seconds);
                return true;
            }

            var clock = ClockPattern.Match(text);
            if (clock.Success)
            {
                double ms = Part(clock, "w") * 604800000d
                          + Part(clock, "d") * 86400000d
                          + Part(clock, "h") * 3600000d
                          + Part(clock, "m") * 60000d
                          + Part(clock, "s") * 1000d;

                // ".200" is 200 MILLISECONDS, not 200 of anything else: the CLI writes the fraction of a
                // second, so its scale depends on how many digits there are.
                var frac = clock.Groups["frac"];
                if (frac.Success)
                    ms += double.Parse("0." + frac.Value, CultureInfo.InvariantCulture) * 1000d;

                result = TimeSpan.FromMilliseconds(negative ? -ms : ms);
                return true;
            }

            // The compact grammar is all-optional, so it also matches the EMPTY string; a non-empty input
            // that matched with no part captured would be a silent zero. Require at least one part.
            var compact = CompactPattern.Match(text);
            if (compact.Success && HasAnyPart(compact))
            {
                double ms = Part(compact, "w") * 604800000d
                          + Part(compact, "d") * 86400000d
                          + Part(compact, "h") * 3600000d
                          + Part(compact, "m") * 60000d
                          + Part(compact, "s") * 1000d
                          + Part(compact, "ms");

                result = TimeSpan.FromMilliseconds(negative ? -ms : ms);
                return true;
            }

            return false;
        }

        private static double Part(Match match, string name)
        {
            var group = match.Groups[name];
            return group.Success ? double.Parse(group.Value, CultureInfo.InvariantCulture) : 0d;
        }

        private static bool HasAnyPart(Match match)
            => match.Groups["w"].Success || match.Groups["d"].Success || match.Groups["h"].Success
            || match.Groups["m"].Success || match.Groups["s"].Success || match.Groups["ms"].Success;

        /// <summary>
        /// The value in the compact form the router accepts on write (<c>1d2h3m4s</c>, <c>200ms</c>,
        /// <c>0s</c>), or the <see cref="Token"/> verbatim.
        /// </summary>
        /// <remarks>
        /// Zero renders as <c>0s</c> and never as <c>none</c>: "no time at all" and "this field is turned
        /// off" are different states on the router, and a formatter that collapsed them would write the
        /// second when the caller meant the first.
        /// </remarks>
        public override string ToString()
        {
            if (_token != null) return _token;
            if (!_value.HasValue) return string.Empty;

            TimeSpan t = _value.Value;
            bool negative = t < TimeSpan.Zero;
            if (negative) t = t.Negate();

            long weeks = (long)t.TotalDays / 7;
            t -= TimeSpan.FromDays(weeks * 7);

            var sb = new StringBuilder();
            if (negative) sb.Append('-');
            if (weeks != 0) sb.Append(weeks.ToString(CultureInfo.InvariantCulture)).Append('w');
            if (t.Days != 0) sb.Append(t.Days.ToString(CultureInfo.InvariantCulture)).Append('d');
            if (t.Hours != 0) sb.Append(t.Hours.ToString(CultureInfo.InvariantCulture)).Append('h');
            if (t.Minutes != 0) sb.Append(t.Minutes.ToString(CultureInfo.InvariantCulture)).Append('m');
            if (t.Seconds != 0) sb.Append(t.Seconds.ToString(CultureInfo.InvariantCulture)).Append('s');
            if (t.Milliseconds != 0) sb.Append(t.Milliseconds.ToString(CultureInfo.InvariantCulture)).Append("ms");

            // Every part was zero, so the value is zero — and something has to be written.
            if (sb.Length == 0 || (negative && sb.Length == 1))
                sb.Append("0s");

            return sb.ToString();
        }

        /// <summary>A duration of the given length.</summary>
        /// <param name="value">The length of time.</param>
        public static implicit operator TikDuration(TimeSpan value) => FromTimeSpan(value);

        /// <summary>
        /// Reads a bare string, so <c>Timeout = "10s"</c> and <c>Timeout = "none"</c> keep working —
        /// including the clock spelling the CLI transports report.
        /// </summary>
        /// <param name="value">The value as the router writes it.</param>
        public static implicit operator TikDuration(string value) => Parse(value);

        /// <summary>
        /// A plain number is seconds, which is how the router reads one in a duration field.
        /// </summary>
        /// <param name="value">The number of seconds.</param>
        public static implicit operator TikDuration(long value) => FromTimeSpan(TimeSpan.FromSeconds(value));

        /// <summary>
        /// The compact spelling, or the <see cref="Token"/> — see <see cref="ToString"/>. There is no
        /// conversion to a number, deliberately: a duration field may hold a word rather than a length,
        /// and "how many seconds is <c>none</c>" has no answer that is not a guess. Read
        /// <see cref="Value"/>, which says so by being null.
        /// </summary>
        /// <param name="value">The duration.</param>
        public static implicit operator string(TikDuration value) => value.ToString();

        /// <summary>The length of time, or <c>null</c> for a <see cref="Token"/>.</summary>
        /// <param name="value">The duration.</param>
        public static explicit operator TimeSpan?(TikDuration value) => value.Value;

        /// <summary>
        /// Equality on the <b>value</b>, so the two forms the router writes the same duration in compare
        /// equal — which is the whole point of the type.
        /// </summary>
        /// <param name="other">The duration to compare with.</param>
        public bool Equals(TikDuration other)
            => Nullable.Equals(_value, other._value)
            && string.Equals(_token, other._token, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikDuration other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = _value.HasValue ? _value.Value.GetHashCode() : 0;
                return (h * 397) ^ (_token == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_token));
            }
        }

        /// <summary>Equality operator — see <see cref="Equals(TikDuration)"/>.</summary>
        public static bool operator ==(TikDuration left, TikDuration right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikDuration)"/>.</summary>
        public static bool operator !=(TikDuration left, TikDuration right) => !left.Equals(right);
    }
}
