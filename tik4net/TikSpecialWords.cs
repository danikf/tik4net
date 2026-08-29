using System;
using System.Collections.Generic;
using tik4net.Diagnostics;

namespace tik4net
{
    /// <summary>
    /// Maps the words RouterOS writes in place of a value to and from the per-type <c>*Special</c> enums,
    /// and reports the ones nothing recognises.
    /// </summary>
    /// <remarks>
    /// One table per value type rather than one shared table: <c>none</c> means "no duration" on a timer
    /// and "no rate" on a bandwidth field, and a word one type accepts is not evidence the other does.
    /// Words are matched case-insensitively because the transports do not agree on case.
    /// <para>
    /// <b>Adding a word here is how a gap gets closed.</b> An unrecognised word is not an error — it is
    /// kept verbatim and written back unchanged — but it is traced on <c>value.token</c>, so the way to
    /// find out which words are missing is to run against a real router with a trace sink installed.
    /// </para>
    /// </remarks>
    internal static class TikSpecialWords
    {
        /// <summary>The channel the unrecognised words are reported on.</summary>
        internal const string TraceChannel = "value.token";

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

        /// <summary>
        /// Reports text that is neither a value nor a recognised word, so the gap is findable instead of
        /// silently surviving as a token.
        /// </summary>
        internal static void TraceUnknown(string type, string token)
            => TikWireTrace.Emit(TraceChannel, TikWireDir.Note,
                $"{type} could not read '{token}' as a value and does not recognise it as a RouterOS word — "
                + "kept verbatim and written back unchanged; if the router really uses it, it belongs in "
                + "TikSpecialWords");
    }
}
