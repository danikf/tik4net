using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// Re-spells the values RouterOS's <c>as-value</c> output gives in its INTERNAL form into the form the
    /// binary API prints, so a field read over a CLI transport says what the same field says over the API.
    /// </summary>
    /// <remarks>
    /// <para>A CLI read is <c>:put [/path print as-value]</c>, and as-value is the router talking to a
    /// script rather than to a person: a duration comes out as <c>00:00:15</c> where <c>print</c> and the
    /// binary API both say <c>15s</c>. That is not a formatting preference — the library's own contract is
    /// that a transport is interchangeable, entity <c>DefaultValue</c>s are written in the API's spelling,
    /// and a duration that reads back as <c>00:00:15</c> never compares equal to a default of <c>15s</c>,
    /// so the change tracker sees a field that is permanently dirty.</para>
    /// <para>Two kinds of rule live here. A <b>duration</b> and an <b>IPv4-mapped IPv6 address</b> say
    /// what they are, so they are recognised by SHAPE. Everything else as-value renders differently is
    /// keyed by FIELD NAME, because the value alone cannot carry it: <c>mtu=0</c> is <c>auto</c> and
    /// <c>mrru=0</c> is <c>disabled</c> while a <c>0</c> elsewhere is a zero, and
    /// <c>bucket-size=5000</c> is <c>5</c> only because that field is scaled by a thousand. The field
    /// name is exactly what the parser is handed, so those tables are keyed by it — and every entry was
    /// measured on the router by setting a NON-sentinel value and reading it back both ways, rather
    /// than inferred from a default.</para>
    /// </remarks>
    internal static class CliValueNormalizer
    {
        /// <summary>
        /// The two fields whose <c>HH:MM:SS</c> is a clock TIME rather than a duration.
        /// </summary>
        /// <remarks>
        /// The API spells a duration in units, so a field whose API value is a bare <c>HH:MM:SS</c> is
        /// telling the time — and as-value spells both the same way, which is the one distinction a reader
        /// cannot make by looking. Measured across the 154 audited paths (the audit reports the set as
        /// <c>CLOCK-SHAPED api values</c>), the whole list is these two. Matched by field NAME rather than
        /// by path because the parser is handed words, not a menu — the risk that costs is a duration field
        /// literally called <c>time</c>, and RouterOS has none.
        /// </remarks>
        private static readonly string[] ClockTimeFields = { "time", "start-time" };

        /// <summary>
        /// Fields whose numeric sentinel the API prints as a WORD, with the exact number that means it.
        /// </summary>
        /// <remarks>
        /// Each entry was pinned by setting a real value and reading it back on both transports, so it says
        /// "this number, on this field" rather than "this field is special":
        /// <list type="bullet">
        /// <item><c>mtu=1400</c> reads <c>1400</c> both ways — only <c>0</c> is <c>auto</c>.</item>
        /// <item><c>horizon=5</c> reads <c>5</c> both ways, and <c>horizon=0</c> reads <c>none</c> over the
        /// API — so 0 and <c>none</c> are one state, with no third case to lose.</item>
        /// <item><c>dscp=0</c> reads <c>0</c> both ways: a real DSCP class. The sentinel is <c>256</c>,
        /// outside the 0..63 range — mapping the zero here, as the other fields do, would have corrupted
        /// it.</item>
        /// <item><c>mrru=1600</c> and <c>max-sessions=10</c> read back unchanged, and <c>mrru</c> cannot be
        /// SET to 0 at all (range 1500..16384), which is what makes 0 unambiguously the disabled state.</item>
        /// </list>
        /// </remarks>
        private static readonly Dictionary<string, KeyValuePair<string, string>> SentinelFields =
            new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "mtu",          new KeyValuePair<string, string>("0",   "auto") },
                { "ttl",          new KeyValuePair<string, string>("0",   "auto") },
                { "horizon",      new KeyValuePair<string, string>("0",   "none") },
                { "mrru",         new KeyValuePair<string, string>("0",   "disabled") },
                { "max-sessions", new KeyValuePair<string, string>("0",   "unlimited") },
                { "dscp",         new KeyValuePair<string, string>("256", "inherit") },
            };

        /// <summary>
        /// Fields as-value renders as a whole number a thousand times the API's.
        /// </summary>
        /// <remarks>
        /// A scale, not a sentinel: <c>bucket-size=5</c> comes back from as-value as <c>5000</c>, and the
        /// router's own range for the field is 0..10 — so every value is affected, not one special number.
        /// </remarks>
        private static readonly string[] ThousandthsFields = { "bucket-size", "freq-drift" };

        /// <summary>
        /// Duration fields whose ZERO the API spells <c>0ms</c> rather than <c>0s</c>.
        /// </summary>
        /// <remarks>
        /// as-value gives a plain <c>00:00:00</c> for both and nothing in the value carries the field's
        /// resolution; this list supplies it. Kept to the fields the audit actually reports reading
        /// <c>0ms</c> over the API, so it grows with evidence rather than by guesswork.
        /// </remarks>
        private static readonly string[] MillisecondZeroFields = { "down-delay", "up-delay" };

        /// <summary>
        /// Duration fields that <c>:serialize to=json</c> renders as a DATE counted from the Unix epoch.
        /// </summary>
        /// <remarks>
        /// The JSON read is used for entities holding free-form text, and it renders a duration as
        /// <c>1970-01-01</c> plus the duration: <c>ttl=1d</c> arrives as <c>1970-01-02 00:00:00</c> and
        /// <c>52w1d</c> as <c>1971-01-01 00:00:00</c> — which is the SAME shape a real timestamp has
        /// (<c>last-link-up-time</c> reads <c>2026-08-25 00:27:44</c> through the same serialiser). Nothing
        /// in the value separates them, so the conversion is done only for fields measured to be durations
        /// and every other date-shaped value is left exactly as the router sent it. An unlisted duration
        /// field shows up in the transport audit as a difference; an unlisted TIMESTAMP silently becomes a
        /// nonsense duration, so the list only ever grows on evidence.
        /// </remarks>
        private static readonly string[] JsonEpochDurationFields = { "ttl", "interval", "timeout" };

        /// <summary>
        /// The value <paramref name="field"/> should carry, given what the CLI read put in it.
        /// </summary>
        /// <param name="field">The field name the value arrived under.</param>
        /// <param name="value">The value as the read format rendered it.</param>
        /// <param name="fromJson">
        /// Whether the value came from <c>:serialize to=json</c> rather than from <c>as-value</c>. The two
        /// render the same field differently, and only the caller knows which it ran.
        /// </param>
        internal static string Normalize(string? field, string value, bool fromJson = false)
        {
            if (string.IsNullOrEmpty(value)) return value;

            if (fromJson && Contains(JsonEpochDurationFields, field)
                && TryConvertEpochDateToDuration(value, out string? asValueForm))
                value = asValueForm!;

            KeyValuePair<string, string> sentinel;
            if (SentinelFields.TryGetValue(field ?? string.Empty, out sentinel)
                && string.Equals(value, sentinel.Key, StringComparison.Ordinal))
                return sentinel.Value;

            if (Contains(ThousandthsFields, field) && TryScaleDownByThousand(value, out string? scaled))
                return scaled!;

            // Seconds east of UTC, which the API prints as a signed clock offset. Not shaped like anything
            // else here: "7200" is just a number until you know which field it came from.
            if (string.Equals(field, "gmt-offset", StringComparison.OrdinalIgnoreCase)
                && TryRenderGmtOffset(value, out string? offset))
                return offset!;

            // Shape-recognised, like a duration: an IPv4 address sitting in an IPv6-shaped slot, which the
            // API prints as the bare IPv4.
            if (value.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) && IsDottedQuad(value, 7))
                return value.Substring(7);

            if (IsClockTimeField(field)) return value;
            return TryParseAsValueDuration(value, out string? apiForm)
                ? (apiForm == "0s" && Contains(MillisecondZeroFields, field) ? "0ms" : apiForm!)
                : value;
        }

        /// <summary>
        /// Rewrites the JSON read's <c>1970-01-01</c>-based date into the <c>[Nd]HH:MM:SS</c> as-value
        /// spelling, so one duration parser serves both read formats.
        /// </summary>
        /// <remarks>
        /// The day count is folded into weeks here, because the duration renderer emits the components it
        /// is given rather than normalising them: handing it 365 days produced <c>365d</c> where the API
        /// says <c>52w1d</c>.
        /// </remarks>
        private static bool TryConvertEpochDateToDuration(string value, out string? asValueForm)
        {
            asValueForm = null;
            DateTime when;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out when))
                return false;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            if (when < epoch) return false;
            int days = (when.Date - epoch.Date).Days;
            asValueForm = (days / 7 > 0 ? (days / 7).ToString(CultureInfo.InvariantCulture) + "w" : string.Empty)
                        + (days % 7 > 0 ? (days % 7).ToString(CultureInfo.InvariantCulture) + "d" : string.Empty)
                        + when.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return true;
        }

        private static bool Contains(string[] names, string? field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            foreach (string name in names)
                if (string.Equals(field, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Renders <paramref name="value"/>/1000, trimming trailing zeros the way the API does.</summary>
        private static bool TryScaleDownByThousand(string value, out string? scaled)
        {
            scaled = null;
            long raw;
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out raw))
                return false;
            string sign = raw < 0 ? "-" : string.Empty;
            long abs = Math.Abs(raw);
            string frac = (abs % 1000).ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');
            scaled = sign + (abs / 1000).ToString(CultureInfo.InvariantCulture)
                   + (frac.Length == 0 ? string.Empty : "." + frac);
            return true;
        }

        /// <summary>Seconds east of UTC as the API's signed <c>+HH:MM</c>.</summary>
        private static bool TryRenderGmtOffset(string value, out string? offset)
        {
            offset = null;
            long seconds;
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out seconds))
                return false;
            string sign = seconds < 0 ? "-" : "+";
            long abs = Math.Abs(seconds);
            offset = sign + (abs / 3600).ToString("00", CultureInfo.InvariantCulture)
                   + ":" + ((abs % 3600) / 60).ToString("00", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Whether <paramref name="value"/> from <paramref name="start"/> on is a.b.c.d.</summary>
        private static bool IsDottedQuad(string value, int start)
        {
            int parts = 0, digits = 0, acc = 0;
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= '0' && c <= '9')
                {
                    if (++digits > 3) return false;
                    acc = acc * 10 + (c - '0');
                    if (acc > 255) return false;
                }
                else if (c == '.')
                {
                    if (digits == 0) return false;
                    parts++; digits = 0; acc = 0;
                }
                else return false;
            }
            return parts == 3 && digits > 0;
        }

        private static bool IsClockTimeField(string? field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            foreach (string f in ClockTimeFields)
                if (string.Equals(field, f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Parses <c>[Nw][Nd]HH:MM:SS[.fff]</c> and renders it the way the API prints a duration.
        /// </summary>
        /// <remarks>
        /// Hand-parsed rather than by regex, and strict about it: the whole string must be consumed, the
        /// clock part must be exactly two digits per component, and anything else is left untouched. A
        /// loose match here would rewrite values that only resemble a duration — a timestamp
        /// (<c>2026-08-24 00:27:30</c>), an IPv6 address, a MAC.
        /// </remarks>
        internal static bool TryParseAsValueDuration(string value, out string? apiForm)
        {
            apiForm = null;
            int pos = 0, len = value.Length;
            long weeks = 0, days = 0;

            if (!TryTakeUnit(value, ref pos, 'w', out weeks)) return false;
            if (!TryTakeUnit(value, ref pos, 'd', out days)) return false;

            // HH:MM:SS — the clock part is mandatory, which is what keeps "1w" (already an API form) and a
            // bare number out.
            if (pos + 8 > len) return false;
            if (!TryTakeTwoDigits(value, ref pos, out long hours)) return false;
            if (pos >= len || value[pos] != ':') return false;
            pos++;
            if (!TryTakeTwoDigits(value, ref pos, out long minutes)) return false;
            if (pos >= len || value[pos] != ':') return false;
            pos++;
            if (!TryTakeTwoDigits(value, ref pos, out long seconds)) return false;

            long millis = 0;
            if (pos < len && value[pos] == '.')
            {
                pos++;
                int start = pos;
                while (pos < len && value[pos] >= '0' && value[pos] <= '9') pos++;
                if (pos == start || pos - start > 3) return false;
                // '.1' is a tenth of a second, not one millisecond — pad on the right, not the left.
                string frac = value.Substring(start, pos - start).PadRight(3, '0');
                millis = long.Parse(frac, CultureInfo.InvariantCulture);
            }

            if (pos != len) return false;

            apiForm = Render(weeks, days, hours, minutes, seconds, millis);
            return true;
        }

        // A leading "<digits><unit>" if present; absent is success with zero. Only fails on digits that are
        // NOT followed by the unit letter, which means the string is something else entirely.
        private static bool TryTakeUnit(string value, ref int pos, char unit, out long count)
        {
            count = 0;
            int start = pos, len = value.Length;
            int i = start;
            while (i < len && value[i] >= '0' && value[i] <= '9') i++;
            if (i == start || i >= len || char.ToLowerInvariant(value[i]) != unit) return true;   // not this unit
            // A two-digit run followed by ':' is the hour, not a count of weeks.
            count = long.Parse(value.Substring(start, i - start), CultureInfo.InvariantCulture);
            pos = i + 1;
            return true;
        }

        private static bool TryTakeTwoDigits(string value, ref int pos, out long number)
        {
            number = 0;
            if (pos + 2 > value.Length) return false;
            char a = value[pos], b = value[pos + 1];
            if (a < '0' || a > '9' || b < '0' || b > '9') return false;
            number = (a - '0') * 10 + (b - '0');
            pos += 2;
            return true;
        }

        /// <summary>
        /// The API's spelling: every non-zero component largest first, each with its unit and no separators
        /// (<c>1d1h9m51s</c>), and <c>0s</c> when there is nothing to say.
        /// </summary>
        /// <remarks>
        /// A zero cannot be spelled exactly. The API prints <c>0s</c> for a second-resolution field and
        /// <c>0ms</c> for a millisecond one (<c>/interface/bonding</c> <c>down-delay</c>), and as-value
        /// gives plain <c>00:00:00</c> for both — the resolution is a property of the field, and nothing in
        /// the value carries it. <c>0s</c> is the common case; the handful of millisecond fields read
        /// <c>0s</c> where the API says <c>0ms</c>, which is the same duration spelled in a different unit
        /// rather than a different value.
        /// </remarks>
        private static string Render(long weeks, long days, long hours, long minutes, long seconds, long millis)
        {
            var sb = new StringBuilder();
            Append(sb, weeks, "w");
            Append(sb, days, "d");
            Append(sb, hours, "h");
            Append(sb, minutes, "m");
            Append(sb, seconds, "s");
            Append(sb, millis, "ms");
            return sb.Length == 0 ? "0s" : sb.ToString();
        }

        private static void Append(StringBuilder sb, long n, string unit)
        {
            if (n != 0) sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }
}
