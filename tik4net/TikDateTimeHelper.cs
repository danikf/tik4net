using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>
    /// Converts between RouterOS date/time strings and <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RouterOS 7.10 and later print <c>yyyy-MM-dd HH:mm:ss</c> (a date alone as
    /// <c>yyyy-MM-dd</c>) — verified live on 7.23 across <c>/system/clock</c>, <c>/system/resource</c>
    /// <c>build-time</c>, <c>/certificate</c> <c>invalid-before</c>/<c>invalid-after</c> and <c>/log</c>
    /// <c>time</c>. It is also what the WinBox native transport renders from the wire's epoch seconds, on 6.x
    /// too (see <c>WinboxRecordCodec</c>'s <c>dateandtime</c> case).
    /// </para>
    /// <para>
    /// Older RouterOS (6.x, and 7.x below ~7.10) prints <c>MMM/dd/yyyy HH:mm:ss</c> — <c>jul/25/2026
    /// 10:24:52</c>, a date alone <c>jul/25/2026</c>. Both shapes are <b>parsed</b>.
    /// </para>
    /// <para>
    /// <b>A value written to the router is spelled the old way</b> (<see cref="ToTikValue"/>), because that is
    /// the one spelling every version takes as an argument of <c>add</c>/<c>set</c>: 6.49.13 refuses
    /// <c>2026-11-21</c> with <c>invalid date</c>, and 7.24.5 stores <c>nov/21/2026</c> as <c>2026-11-21</c>
    /// (measured on <c>/system/scheduler</c> <c>start-date</c> over the API and Telnet). A date-only field given a
    /// time keeps the date: both versions store <c>mar/05/2029 10:11:12</c> as <c>mar/05/2029</c>.
    /// </para>
    /// <para>
    /// <b>A query filter is different</b>: it compares the text the router prints, so a filter on 7.x needs
    /// <see cref="ToTikDateTime"/> / <see cref="ToTikDate"/>. A filter on <c>jan/01/2020</c> against 7.23 comes
    /// back with the wrong rows rather than a trap.
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
        /// <summary>The date+time format RouterOS 7.10+ prints.</summary>
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>The date-only format RouterOS 7.10+ prints (e.g. <c>/system/clock</c> date).</summary>
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>The date+time format RouterOS 6.x prints, and every version accepts as an argument (month in lowercase).</summary>
        public const string LegacyDateTimeFormat = "MMM/dd/yyyy HH:mm:ss";

        /// <summary>The date-only format RouterOS 6.x prints, and every version accepts as an argument (month in lowercase).</summary>
        public const string LegacyDateFormat = "MMM/dd/yyyy";

        private static readonly string[] AcceptedFormats =
        {
            DateTimeFormat,
            DateFormat,
            "yyyy-MM-dd HH:mm",
            LegacyDateTimeFormat,   //RouterOS 6.x and early 7.x
            "MMM/dd/yyyy HH:mm",
            LegacyDateFormat,
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
        /// Converts a <see cref="DateTime"/> to the spelling every RouterOS version accepts as an argument of
        /// <c>add</c>/<c>set</c>: <c>nov/21/2026</c>, with <c> HH:mm:ss</c> appended when the time of day is not midnight.
        /// This is what the O/R mapper writes for a <see cref="DateTime"/> property.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <returns>The value in <see cref="LegacyDateFormat"/> or <see cref="LegacyDateTimeFormat"/>, month in lowercase.</returns>
        public static string ToTikValue(DateTime value)
        {
            string format = value.TimeOfDay == TimeSpan.Zero ? LegacyDateFormat : LegacyDateTimeFormat;
            return value.ToString(format, CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to the date+time string RouterOS 7.10+ prints — the spelling a query
        /// filter on 7.x compares against. To write a value, use <see cref="ToTikValue"/>.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The value formatted as <see cref="DateTimeFormat"/>.</returns>
        public static string ToTikDateTime(DateTime value)
        {
            return value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to the date-only string RouterOS 7.10+ prints, discarding the time — the
        /// spelling a query filter on 7.x compares against. To write a value, use <see cref="ToTikValue"/>.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The value formatted as <see cref="DateFormat"/>.</returns>
        public static string ToTikDate(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
    }
}
