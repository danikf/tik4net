using System;

namespace tik4net
{
    /// <summary>The RouterOS boolean word: <c>true</c>/<c>yes</c> and <c>false</c>/<c>no</c>, in any case.</summary>
    /// <remarks>
    /// The router prints some booleans as <c>true</c>/<c>false</c> and accepts <c>yes</c>/<c>no</c> for every one of them,
    /// so a reader takes all four. A caller with a refusal of its own throws it on <see langword="false"/> from
    /// <see cref="TryParse"/>, naming what it was reading.
    /// </remarks>
    internal static class TikBoolText
    {
        internal static bool TryParse(string? value, out bool result)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                return result = true;
            result = false;
            return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether the value is a true word; an absent value, or any other word, is not.</summary>
        internal static bool IsTrue(string? value) => TryParse(value, out bool result) && result;

        /// <summary>The spelling every boolean argument accepts, and the only one a CLI expression does.</summary>
        internal static string ToCli(bool value) => value ? "yes" : "no";
    }
}
