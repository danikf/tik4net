using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// Converts between RouterOS date/time strings and <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RouterOS 7.10 and later print and accept <c>yyyy-MM-dd HH:mm:ss</c> (a date alone as
    /// <c>yyyy-MM-dd</c>) — verified live on 7.23 across <c>/system/clock</c>, <c>/system/resource</c>
    /// <c>build-time</c>, <c>/certificate</c> <c>invalid-before</c>/<c>invalid-after</c> and <c>/log</c>
    /// <c>time</c>, and verified as INPUT by filtering <c>/certificate</c> on a supplied date. It is also
    /// what the WinBox native transport renders (see <c>WinboxRecordCodec</c>'s <c>dateandtime</c> case), so
    /// one shape covers every transport.
    /// </para>
    /// <para>
    /// Older RouterOS (6.x, and 7.x below ~7.10) printed <c>MMM/dd/yyyy HH:mm:ss</c> — <c>jul/25/2026
    /// 10:24:52</c>. That shape is still <b>parsed</b>, because a caller may be reading an older router, but
    /// it is never <b>written</b>: 7.23 does not accept it, and — measured, not assumed — it does not
    /// complain either. A query filtered on <c>jan/01/2020</c> comes back with the wrong rows rather than a
    /// trap, so writing the legacy shape to a modern router fails silently.
    /// </para>
    /// <para>
    /// <b>The value carries no time zone and none is invented.</b> Parsing yields
    /// <see cref="DateTimeKind.Unspecified"/>, because what the string means is per field, not per format:
    /// <c>/system/clock</c> is the router's local time, while a certificate's <c>invalid-before</c> is UTC
    /// (proven by the native transport, where the same instant arrives as a unix epoch and renders to the
    /// identical string). Converting either one would corrupt the other.
    /// </para>
    /// </remarks>
    public static class TikDateTimeHelper
    {
        /// <summary>The date+time format RouterOS 7.10+ prints and accepts.</summary>
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>The date-only format RouterOS 7.10+ prints and accepts (e.g. <c>/system/clock</c> date).</summary>
        public const string DateFormat = "yyyy-MM-dd";

        private static readonly string[] AcceptedFormats =
        {
            DateTimeFormat,
            DateFormat,
            "yyyy-MM-dd HH:mm",
            "MMM/dd/yyyy HH:mm:ss",   //RouterOS 6.x and early 7.x
            "MMM/dd/yyyy HH:mm",
            "MMM/dd/yyyy",
        };

        /// <summary>
        /// Converts a RouterOS date/time string to <see cref="DateTime"/>.
        /// </summary>
        /// <param name="value">The value as printed by the router.</param>
        /// <returns>The parsed value, with <see cref="DateTimeKind.Unspecified"/>.</returns>
        /// <exception cref="FormatException">The value is in none of the accepted formats.</exception>
        public static DateTime FromTikDateTime(string value)
        {
            DateTime result;
            if (!TryFromTikDateTime(value, out result))
                throw new FormatException(string.Format("'{0}' is not a RouterOS date/time value.", value));

            return result;
        }

        /// <summary>
        /// Converts a RouterOS date/time string to <see cref="DateTime"/>, reporting failure rather than throwing.
        /// </summary>
        /// <param name="value">The value as printed by the router.</param>
        /// <param name="result">The parsed value, with <see cref="DateTimeKind.Unspecified"/>.</param>
        /// <returns>True when <paramref name="value"/> was in one of the accepted formats.</returns>
        public static bool TryFromTikDateTime(string value, out DateTime result)
        {
            result = default(DateTime);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // The legacy month name arrives lowercase ('jul/25/2026'); ParseExact matches month names
            // case-insensitively, so no normalization is needed. AssumeLocal/AssumeUniversal are both
            // deliberately absent — see the class remarks on why no zone is applied.
            return DateTime.TryParseExact(value.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result);
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to the date+time string RouterOS accepts.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns>The value formatted as <see cref="DateTimeFormat"/>.</returns>
        public static string ToTikDateTime(DateTime value)
        {
            return value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to the date-only string RouterOS accepts, discarding the time.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns>The value formatted as <see cref="DateFormat"/>.</returns>
        public static string ToTikDate(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
    }
}
