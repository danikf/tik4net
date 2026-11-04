using System;
using System.Globalization;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// The compact RouterOS duration as the router writes it: the non-zero units only, largest first, with no separators —
    /// <c>1w</c>, <c>5m</c>, <c>2d17h30m3s</c>, <c>105ms488us</c> — and a zero in the field's own unit.
    /// </summary>
    /// <remarks>
    /// Every transport reports a duration in this spelling, so the same record reads identically over all of them. The
    /// unit set and the omit-zeroes rule are read off the API's own output on 7.23.2 (604800 → <c>1w</c>, 300 →
    /// <c>5m</c>, 0 → <c>0s</c>). Zero is written as a zero and never as <c>none</c>: "no time at all" and "this field
    /// is turned off" are different states on the router. WinBox native deliberately does not use webfig's
    /// <c>interval2string</c> (<c>1d 02:03:04</c>): that is what the WinBox UI shows, not what the API reports.
    /// </remarks>
    internal static class TikDurationText
    {
        /// <param name="value">The length of time; anything under a microsecond is dropped.</param>
        /// <param name="zeroUnit">
        /// The unit a zero is spelled in — the unit the FIELD is expressed in, not seconds: a bonding down-delay of nothing
        /// reads <c>0ms</c>.
        /// </param>
        internal static string Format(TimeSpan value, string zeroUnit = "s")
        {
            bool negative = value < TimeSpan.Zero;
            long ticks = negative ? value.Negate().Ticks : value.Ticks;
            return Format(negative, ticks / TimeSpan.TicksPerSecond, ticks % TimeSpan.TicksPerSecond, zeroUnit);
        }

        /// <summary>A count of <c>1/scale</c> seconds, as a wire field carries it; any length, past what a <see cref="TimeSpan"/> holds.</summary>
        /// <param name="value">The count.</param>
        /// <param name="scale">Counts per second; 1 when below that.</param>
        /// <param name="zeroUnit">See <see cref="Format(TimeSpan, string)"/>.</param>
        internal static string Format(long value, int scale, string zeroUnit = "s")
        {
            if (scale < 1) scale = 1;
            bool negative = value < 0;
            if (negative) value = -value;
            return Format(negative, value / scale, value % scale * TimeSpan.TicksPerSecond / scale, zeroUnit);
        }

        // fraction: the part under a second, in 100 ns ticks.
        private static string Format(bool negative, long seconds, long fraction, string zeroUnit)
        {
            var sb = new StringBuilder();
            if (negative) sb.Append('-');
            Append(sb, seconds / 604800, "w");
            Append(sb, seconds % 604800 / 86400, "d");
            Append(sb, seconds % 86400 / 3600, "h");
            Append(sb, seconds % 3600 / 60, "m");
            Append(sb, seconds % 60, "s");
            Append(sb, fraction / TimeSpan.TicksPerMillisecond, "ms");
            Append(sb, fraction % TimeSpan.TicksPerMillisecond / 10, "us");

            // Every part was zero, so the value is zero — and something has to be written.
            if (sb.Length == 0 || (negative && sb.Length == 1)) sb.Append('0').Append(zeroUnit);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, long n, string unit)
        {
            if (n != 0) sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }
}
