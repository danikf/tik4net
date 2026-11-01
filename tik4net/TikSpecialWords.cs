using System;
using System.Collections.Generic;

namespace tik4net
{
    /// <summary>
    /// Maps the words RouterOS writes in place of a value to and from the per-type <c>*Special</c> enums.
    /// </summary>
    /// <remarks>
    /// One table per value type rather than one shared table: <c>none</c> means "no duration" on a timer
    /// and "no rate" on a bandwidth field, and a word one type accepts is not evidence the other does.
    /// Words are matched case-insensitively because the transports do not agree on case.
    /// <para>
    /// <b>Adding a word here is how a gap gets closed.</b> A word that is not here is refused by the types' <c>Parse</c>:
    /// a mapped field keeps it as its unparsed value, writes it back unchanged, and the entity diagnostics report it.
    /// </para>
    /// </remarks>
    internal static class TikSpecialWords
    {
        private static readonly Dictionary<string, TikDurationSpecial> DurationByWord =
            new Dictionary<string, TikDurationSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["none"] = TikDurationSpecial.None,
                ["disabled"] = TikDurationSpecial.Disabled,
                ["auto"] = TikDurationSpecial.Auto,
                ["never"] = TikDurationSpecial.Never,
                ["immediately"] = TikDurationSpecial.Immediately,
                ["forever"] = TikDurationSpecial.Forever,
            };

        private static readonly Dictionary<TikDurationSpecial, string> DurationByValue =
            new Dictionary<TikDurationSpecial, string>
            {
                [TikDurationSpecial.None] = "none",
                [TikDurationSpecial.Disabled] = "disabled",
                [TikDurationSpecial.Auto] = "auto",
                [TikDurationSpecial.Never] = "never",
                [TikDurationSpecial.Immediately] = "immediately",
                [TikDurationSpecial.Forever] = "forever",
            };

        private static readonly Dictionary<string, TikDataRateSpecial> RateByWord =
            new Dictionary<string, TikDataRateSpecial>(StringComparer.OrdinalIgnoreCase)
            {
                ["unlimited"] = TikDataRateSpecial.Unlimited,
                ["auto"] = TikDataRateSpecial.Auto,
                ["none"] = TikDataRateSpecial.None,
            };

        private static readonly Dictionary<TikDataRateSpecial, string> RateByValue =
            new Dictionary<TikDataRateSpecial, string>
            {
                [TikDataRateSpecial.Unlimited] = "unlimited",
                [TikDataRateSpecial.Auto] = "auto",
                [TikDataRateSpecial.None] = "none",
            };

        internal static bool TryReadDuration(string token, out TikDurationSpecial special)
            => DurationByWord.TryGetValue(token, out special);

        internal static string WordFor(TikDurationSpecial special)
            => DurationByValue.TryGetValue(special, out string? word)
                ? word
                : throw new ArgumentOutOfRangeException(nameof(special), special,
                    "No RouterOS word is defined for this TikDurationSpecial member.");

        internal static bool TryReadRate(string token, out TikDataRateSpecial special)
            => RateByWord.TryGetValue(token, out special);

        internal static string WordFor(TikDataRateSpecial special)
            => RateByValue.TryGetValue(special, out string? word)
                ? word
                : throw new ArgumentOutOfRangeException(nameof(special), special,
                    "No RouterOS word is defined for this TikDataRateSpecial member.");
    }
}
