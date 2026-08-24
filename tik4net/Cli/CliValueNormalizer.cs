using System;
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
    /// <para><b>Only durations are re-spelled here, because only a duration says what it is.</b> The other
    /// renderings as-value differs on need to know which FIELD they belong to — <c>mtu=0</c> is
    /// <c>auto</c> and <c>mrru=0</c> is <c>disabled</c> while a <c>0</c> elsewhere is a zero, and
    /// <c>bucket-size=100</c> is <c>0.1</c> only because that field is scaled by a thousand. Guessing those
    /// from the value would corrupt every field that legitimately holds the number.</para>
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
        /// The value <paramref name="field"/> should carry, given what as-value put in it.
        /// </summary>
        internal static string Normalize(string field, string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (IsClockTimeField(field)) return value;
            return TryParseAsValueDuration(value, out string? apiForm) ? apiForm! : value;
        }

        private static bool IsClockTimeField(string field)
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
