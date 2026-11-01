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
    /// <see cref="Parse(string)"/> and <see cref="TryParse(string, out TikDuration)"/> read what the type knows: a length
    /// of time in either form, or a word <see cref="TikDurationSpecial"/> names. Anything else is refused, and a mapped
    /// field then keeps the router's word as its unparsed value. <see cref="FromToken"/> is how to send a word the library
    /// does not know.
    /// </para>
    /// <para>
    /// <see cref="ToString"/> always renders the compact form, which is what RouterOS accepts on write over
    /// every transport, so a value read over the CLI and written back is not silently reformatted into
    /// something the router would read as a different field shape.
    /// </para>
    /// </remarks>
    public readonly struct TikDuration : IEquatable<TikDuration>
    {
        // Compact form: 1w2d3h4m5s6ms7us, in that order, any part optional — but at least one, and nothing
        // else in the string. The old uptime regex made every part optional AND was used with Match rather
        // than a full-string anchor, so it matched the empty prefix of ANYTHING and reported zero for
        // input it had not understood at all.
        private static readonly Regex CompactPattern = new Regex(
            @"^(?:(?<w>\d+)w)?(?:(?<d>\d+)d)?(?:(?<h>\d+)h)?(?:(?<m>\d+)m(?!s))?(?:(?<s>\d+)s)?(?:(?<ms>\d+)ms)?(?:(?<us>\d+)us)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Clock form as the CLI prints it: an optional week/day prefix in front of hh:mm:ss[.fff].
        private static readonly Regex ClockPattern = new Regex(
            @"^(?:(?<w>\d+)w)?(?:(?<d>\d+)d)?(?<h>\d+):(?<m>\d{1,2}):(?<s>\d{1,2})(?:\.(?<frac>\d{1,7}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // The clock form exactly as as-value prints it: two digits per part and at most three of a fraction. What
        // CliValueNormalizer rewrites, so a value that only resembles a duration (a timestamp, an IPv6 address, a MAC) stays.
        private static readonly Regex StrictClockPattern = new Regex(
            @"^(?:(?<w>[0-9]+)[wW])?(?:(?<d>[0-9]+)[dD])?(?<h>[0-9]{2}):(?<m>[0-9]{2}):(?<s>[0-9]{2})(?:\.(?<frac>[0-9]{1,3}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly TikValueOrToken<TimeSpan> _core;

        private TikDuration(TimeSpan? value, string? token)
        {
            _core = new TikValueOrToken<TimeSpan>(value, token);
        }

        /// <summary>The length of time, or <c>null</c> when the field carries a <see cref="Token"/> instead.</summary>
        public TimeSpan? Value => _core.Value;

        /// <summary>
        /// The word the router used in place of a duration (<c>none</c>, <c>auto</c>, <c>disabled</c>, …), or <c>null</c> when the field carries a real <see cref="Value"/>.
        /// </summary>
        public string? Token => _core.Token;

        /// <summary>Whether this is a real length of time rather than a word.</summary>
        public bool HasValue => _core.Value.HasValue;

        /// <summary>
        /// What this actually holds: a <see cref="Value"/>, a <see cref="Special"/> word the library
        /// recognises, or <see cref="TikValueKind.Unknown"/> text it does not (only <see cref="FromToken"/> makes one).
        /// </summary>
        public TikValueKind Kind => _core.Kind(_core.Token != null && TikSpecialWords.TryReadDuration(_core.Token, out _));

        /// <summary>
        /// The router word this field carries, when it is one the library recognises — otherwise
        /// <c>null</c>. Compare against this rather than against <see cref="Token"/>: the word's spelling
        /// and case vary by transport, the enum member does not.
        /// </summary>
        public TikDurationSpecial? Special
            => _core.Token != null && TikSpecialWords.TryReadDuration(_core.Token, out TikDurationSpecial s)
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
        /// <c>200ms</c>) or clock (<c>00:00:10</c>, <c>1d00:00:00</c>, <c>00:00:00.200</c>), or a word
        /// <see cref="TikDurationSpecial"/> names (<c>none</c>, <c>disabled</c>, …). A bare number is read as seconds,
        /// which is what the router accepts on write.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
        /// <exception cref="FormatException">
        /// <paramref name="value"/> is neither a duration nor a word the library knows. <see cref="FromToken"/> takes such a word.
        /// </exception>
        public static TikDuration Parse(string value)
        {
            Guard.ArgumentNotNullOrEmptyString(value, nameof(value));

            if (TryParse(value, out TikDuration result))
                return result;

            throw new FormatException($"'{value}' is not a RouterOS duration.");
        }

        /// <summary>
        /// A duration of exactly this length.
        /// </summary>
        /// <param name="value">The length of time.</param>
        /// <exception cref="FormatException">
        /// The compact form cannot spell <paramref name="value"/> exactly: it counts whole microseconds.
        /// <see cref="FromTimeSpan"/> and the implicit conversion drop what is finer.
        /// </exception>
        public static TikDuration Parse(TimeSpan value)
        {
            if (TryParse(value, out TikDuration result))
                return result;

            throw new FormatException($"{value} is not a RouterOS duration: the router counts whole microseconds.");
        }

        /// <summary><see cref="Parse(string)"/> without the exception.</summary>
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
                return false;

            result = new TikDuration(null, text);
            return true;
        }

        /// <summary><see cref="Parse(TimeSpan)"/> without the exception.</summary>
        /// <param name="value">The length of time.</param>
        /// <param name="result">The duration.</param>
        public static bool TryParse(TimeSpan value, out TikDuration result)
        {
            result = default;
            if (value == TimeSpan.MinValue || value.Ticks % (TimeSpan.TicksPerMillisecond / 1000) != 0)
                return false;

            result = new TikDuration(value, null);
            return true;
        }

        /// <summary>
        /// Reads the length of time out of either router form, refusing a word: a field that can hold one reads as a
        /// <see cref="TikDuration"/>, which keeps it.
        /// </summary>
        /// <param name="value">The value as the router wrote it.</param>
        /// <returns>The length of time.</returns>
        /// <exception cref="FormatException">
        /// <paramref name="value"/> is not a duration, including the words the router uses in place of one
        /// (<c>none</c>, <c>disabled</c>, …): those are a real state of the field, not a zero.
        /// </exception>
        public static TimeSpan ParseTimeSpan(string value)
        {
            if (TryParseTimeSpan(value, out TimeSpan result))
                return result;

            throw new FormatException(
                $"'{value}' is not a MikroTik duration. Expected either the compact form (10s, 1d2h, 200ms) "
                + "or the clock form the CLI transports report (00:00:10, 1d00:00:00, 00:00:00.200).");
        }

        /// <summary>
        /// Reads <c>[Nw][Nd]HH:MM:SS[.fff]</c> and nothing else: the shape <c>as-value</c> prints, which
        /// <c>CliValueNormalizer</c> rewrites. A loose match there would rewrite values that only resemble a duration.
        /// </summary>
        internal static bool TryParseStrictClock(string value, out TikDuration result)
        {
            result = default;
            var clock = StrictClockPattern.Match(value);
            if (!clock.Success)
                return false;
            try
            {
                result = new TikDuration(TimeSpan.FromTicks(ClockTicks(clock)), null);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
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
            // A number too large for a TimeSpan is not a duration either; a Try method answers that, it does not throw.
            try { return TryParseTimeSpanCore(value, out result); }
            catch (OverflowException) { result = default; return false; }
        }

        private static bool TryParseTimeSpanCore(string? value, out TimeSpan result)
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

            // Counted in ticks, not through TimeSpan.FromMilliseconds(double): on .NET Framework that call rounds to a
            // whole millisecond, which turned a ping's 124us — and the CLI's .000124 — into zero.
            var clock = ClockPattern.Match(text);
            if (clock.Success)
            {
                long ticks = ClockTicks(clock);
                result = TimeSpan.FromTicks(negative ? -ticks : ticks);
                return true;
            }

            // The compact grammar is all-optional, so it also matches the EMPTY string; a non-empty input
            // that matched with no part captured would be a silent zero. Require at least one part.
            var compact = CompactPattern.Match(text);
            if (compact.Success && HasAnyPart(compact))
            {
                long ticks = Ticks(compact, "w", TimeSpan.TicksPerDay * 7)
                           + Ticks(compact, "d", TimeSpan.TicksPerDay)
                           + Ticks(compact, "h", TimeSpan.TicksPerHour)
                           + Ticks(compact, "m", TimeSpan.TicksPerMinute)
                           + Ticks(compact, "s", TimeSpan.TicksPerSecond)
                           + Ticks(compact, "ms", TimeSpan.TicksPerMillisecond)
                           + Ticks(compact, "us", TimeSpan.TicksPerMillisecond / 1000);

                result = TimeSpan.FromTicks(negative ? -ticks : ticks);
                return true;
            }

            return false;
        }

        private static long ClockTicks(Match clock)
        {
            long ticks = Ticks(clock, "w", TimeSpan.TicksPerDay * 7)
                       + Ticks(clock, "d", TimeSpan.TicksPerDay)
                       + Ticks(clock, "h", TimeSpan.TicksPerHour)
                       + Ticks(clock, "m", TimeSpan.TicksPerMinute)
                       + Ticks(clock, "s", TimeSpan.TicksPerSecond);

            // ".200" is 200 MILLISECONDS, not 200 of anything else: the CLI writes the fraction of a
            // second, so its scale depends on how many digits there are (seven are a tick each).
            var frac = clock.Groups["frac"];
            if (frac.Success)
                ticks += long.Parse(frac.Value.PadRight(7, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
            return ticks;
        }

        private static long Ticks(Match match, string name, long ticksPerUnit)
        {
            var group = match.Groups[name];
            return group.Success
                ? checked(long.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture) * ticksPerUnit)
                : 0L;
        }

        private static bool HasAnyPart(Match match)
            => match.Groups["w"].Success || match.Groups["d"].Success || match.Groups["h"].Success
            || match.Groups["m"].Success || match.Groups["s"].Success || match.Groups["ms"].Success
            || match.Groups["us"].Success;

        /// <summary>
        /// The value in the compact form the router accepts on write (<c>1d2h3m4s</c>, <c>200ms</c>,
        /// <c>0s</c>), down to the microseconds the router writes for a ping (<c>1ms52us</c>), or the
        /// <see cref="Token"/> verbatim.
        /// </summary>
        /// <remarks>
        /// Zero renders as <c>0s</c> and never as <c>none</c>: "no time at all" and "this field is turned
        /// off" are different states on the router, and a formatter that collapsed them would write the
        /// second when the caller meant the first.
        /// </remarks>
        public override string ToString()
        {
            if (_core.Token != null) return _core.Token;
            if (!_core.Value.HasValue) return string.Empty;

            TimeSpan t = _core.Value.Value;
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
            long micros = t.Ticks % TimeSpan.TicksPerMillisecond / 10;   // a ping's time and round trips: 1ms52us
            if (micros != 0) sb.Append(micros.ToString(CultureInfo.InvariantCulture)).Append("us");

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
        public bool Equals(TikDuration other) => _core.Equals(other._core);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikDuration other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _core.GetHashCode();

        /// <summary>Equality operator — see <see cref="Equals(TikDuration)"/>.</summary>
        public static bool operator ==(TikDuration left, TikDuration right) => left.Equals(right);

        /// <summary>Inequality operator — see <see cref="Equals(TikDuration)"/>.</summary>
        public static bool operator !=(TikDuration left, TikDuration right) => !left.Equals(right);
    }
}
