using System.Globalization;

namespace tik4net
{
    /// <summary>The text of a <see cref="TikPortRange"/> and a <see cref="TikNumberRange"/>: <c>22</c> or <c>1000-2000</c>.</summary>
    internal static class TikRangeText
    {
        internal static bool TryParse(string? value, out long from, out long to)
        {
            from = to = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string text = value!.Trim();
            int dash = text.IndexOf('-');
            string first = dash < 0 ? text : text.Substring(0, dash);
            string last = dash < 0 ? text : text.Substring(dash + 1);
            return long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out from)
                && long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out to)
                && to >= from;
        }

        internal static string Format(long from, long to)
            => to != from
                ? from.ToString(CultureInfo.InvariantCulture) + "-" + to.ToString(CultureInfo.InvariantCulture)
                : from.ToString(CultureInfo.InvariantCulture);
    }
}
