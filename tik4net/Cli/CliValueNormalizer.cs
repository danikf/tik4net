using System;
using System.Collections.Generic;
using System.Globalization;
using tik4net.Connection;

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
        /// by path because the parser is handed words, not a menu — which costs the one duration field called
        /// <c>time</c>: a monitor's, and a read through the table does not come here (<see cref="CliTableParser"/>).
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
        /// Fields as-value renders in decimal where the API prints them in base 16.
        /// </summary>
        /// <remarks>
        /// <c>icmp-rate-mask=0x1AB</c> reads <c>427</c> from as-value and <c>0x1AB</c> over the API and REST:
        /// <c>0x</c>, upper-case digits, no padding (<c>0x19</c>, <c>0x1818</c>). Not every hex field does
        /// this, so the table holds only the fields measured to; the bridge priorities do it on some versions
        /// only and are keyed by menu (<see cref="HexFieldsByMenu"/>).
        /// </remarks>
        private static readonly string[] HexFields = { "icmp-rate-mask" };

        /// <summary>
        /// Fields as-value renders in decimal on some RouterOS versions only, where the API prints them in
        /// base 16 on all of them — keyed by menu, because the field name alone does not decide it.
        /// </summary>
        /// <remarks>
        /// Bridge and bridge-port <c>priority</c>: <c>0x7000</c> / <c>0x70</c> over the API on 6.49.13, 7.21.5 and
        /// 7.24.4; as-value gives <c>28672</c> / <c>112</c> on the first two and the API's hex on 7.24. A
        /// <c>priority</c> elsewhere is decimal over the API too (<c>/interface/vrrp</c>, <c>/queue/tree</c>,
        /// OSPF), so a name-keyed entry in <see cref="HexFields"/> would turn those into hex. Applied after the
        /// parse (<see cref="NormalizeForMenu"/>), the first point that knows the menu.
        /// </remarks>
        private static readonly Dictionary<string, string[]> HexFieldsByMenu =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["/interface/bridge"]      = new[] { "priority" },
                ["/interface/bridge/port"] = new[] { "priority" },
            };

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
        private static readonly string[] JsonEpochDurationFields = { "ttl", "interval", "timeout", "mac-cookie-timeout" };

        /// <summary>
        /// A row id in the API's spelling. RouterOS before 7.20 prints the hex of an id in lowercase on the CLI
        /// (<c>*59b</c>) — in as-value, in the JSON read and in what <c>add</c> answers — where its binary API
        /// prints <c>*59B</c>, as every version from 7.20 on does on both. An id is a key, and one read over a
        /// CLI transport has to match the same row read over the API. Anything not shaped like an id is
        /// returned unchanged.
        /// </summary>
        internal static string NormalizeId(string value)
            => IsRecordId(value) ? value.ToUpperInvariant() : value;

        /// <summary>True when <paramref name="s"/> is a RouterOS row id: <c>*</c> and one or more hex digits.</summary>
        internal static bool IsRecordId(string? s)
        {
            if (string.IsNullOrEmpty(s) || s![0] != '*' || s.Length < 2)
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Rewrites, in place, the fields of <paramref name="rows"/> whose spelling depends on the menu they
        /// were read from as well as on their name (see <see cref="HexFieldsByMenu"/>).
        /// </summary>
        /// <param name="commandText">The read's command path, verb included (<c>/interface/bridge/print</c>).</param>
        /// <param name="rows">The rows the read produced.</param>
        internal static void NormalizeForMenu(string commandText, IList<TikRecordSentence> rows)
        {
            if (rows.Count == 0
                || !HexFieldsByMenu.TryGetValue(TikPath.Parent(commandText), out string[]? fields))
                return;

            foreach (TikRecordSentence row in rows)
                foreach (string field in fields!)
                    if (row.TryGetResponseField(field, out string value)
                        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
                        row.SetField(field, new TikHexNumber(number).ToString());
        }

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

            if (string.Equals(field, TikSpecialProperties.Id, StringComparison.OrdinalIgnoreCase))
                return NormalizeId(value);

            if (fromJson && Contains(JsonEpochDurationFields, field)
                && TryConvertEpochDateToDuration(value, out string? asValueForm))
                value = asValueForm!;

            KeyValuePair<string, string> sentinel;
            if (SentinelFields.TryGetValue(field ?? string.Empty, out sentinel)
                && string.Equals(value, sentinel.Key, StringComparison.Ordinal))
                return sentinel.Value;

            if (Contains(ThousandthsFields, field) && TryScaleDownByThousand(value, out string? scaled))
                return scaled!;

            if (Contains(HexFields, field)
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long hex))
                return new TikHexNumber(hex).ToString();

            // A rate range the terminal abbreviates and the API does not: connection-rate=0-100k over the CLI,
            // 0-100000 over the API and REST (7.24.4). Decimal units, as webfig's types.unit spells them.
            if (string.Equals(field, "connection-rate", StringComparison.OrdinalIgnoreCase)
                && TryExpandUnitRange(value, out string? plainRange))
                return plainRange!;

            // The firewall's limit and dst-limit lead with a rate the terminal abbreviates the same way:
            // limit=1M,5:bit over the CLI, 1000000,5:bit over the API (7.24.5). Only that shape — a queue's limit is
            // a plain number.
            if ((string.Equals(field, "limit", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(field, "dst-limit", StringComparison.OrdinalIgnoreCase))
                && TryExpandLeadingRate(value, out string? plainLimit))
                return plainLimit!;

            // Seconds east of UTC, which the API prints as a signed clock offset. Not shaped like anything
            // else here: "7200" is just a number until you know which field it came from.
            if (string.Equals(field, "gmt-offset", StringComparison.OrdinalIgnoreCase)
                && TryRenderGmtOffset(value, out string? offset))
                return offset!;

            // Shape-recognised, like a duration: an IPv4 address sitting in an IPv6-shaped slot, which the
            // API prints as the bare IPv4.
            if (value.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) && IsDottedQuad(value, 7))
                return value.Substring(7);

            if (Contains(ClockTimeFields, field)) return value;
            // A zero cannot be spelled exactly: the API prints 0s for a second-resolution field and 0ms for a millisecond
            // one, as-value gives 00:00:00 for both, and nothing in the value carries the field's resolution.
            if (!TikDuration.TryParseStrictClock(value, out TikDuration duration))
                return value;
            string apiForm = duration.ToString();
            return apiForm == "0s" && Contains(MillisecondZeroFields, field) ? "0ms" : apiForm;
        }

        /// <summary>
        /// Rewrites the JSON read's <c>1970-01-01</c>-based date into the <c>[Nd]HH:MM:SS</c> as-value
        /// spelling, so one duration parser serves both read formats.
        /// </summary>
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
            asValueForm = (days > 0 ? days.ToString(CultureInfo.InvariantCulture) + "d" : string.Empty)
                        + when.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return true;
        }

        // "!0-100k" -> "!0-100000": each end of a range, a k/M/G suffix multiplied out by 1000 per step. False when
        // an end is not such a number, or when nothing carried a suffix.
        private static bool TryExpandUnitRange(string value, out string? plain)
        {
            plain = null;
            string sign = value.StartsWith("!", StringComparison.Ordinal) ? "!" : string.Empty;
            string[] ends = value.Substring(sign.Length).Split('-');
            if (ends.Length > 2) return false;
            bool changed = false;
            for (int i = 0; i < ends.Length; i++)
            {
                string end = ends[i];
                // Only these spellings: a lower-case m here is a period (1M/1m), not a rate suffix.
                bool suffixed = end.EndsWith("k", StringComparison.Ordinal) || end.EndsWith("M", StringComparison.Ordinal)
                             || end.EndsWith("G", StringComparison.Ordinal);
                if ((!suffixed && !long.TryParse(end, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    || !TikDataRate.TryParseNumber(end, out long n))
                    return false;
                changed |= suffixed;
                ends[i] = n.ToString(CultureInfo.InvariantCulture);
            }
            plain = sign + string.Join("-", ends);
            return changed;
        }

        // "!1M/1m,5:bit" -> "!1000000/1m,5:bit": the rate before the first ',' (and before its '/period'). False when
        // the value has no ',' or the rate carried no suffix.
        private static bool TryExpandLeadingRate(string value, out string? plain)
        {
            plain = null;
            int comma = value.IndexOf(',');
            if (comma < 0) return false;
            string rate = value.Substring(0, comma);
            int slash = rate.IndexOf('/');
            string number = slash < 0 ? rate : rate.Substring(0, slash);
            if (number.IndexOf('-') >= 0 || !TryExpandUnitRange(number, out string? expanded)) return false;
            plain = expanded + (slash < 0 ? string.Empty : rate.Substring(slash)) + value.Substring(comma);
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
    }
}
