using System.Globalization;

namespace tik4net.Winbox
{
    /// <summary>
    /// The RouterOS <c>*HEX</c> record handle (<c>.id</c>) and the M2 record id it stands for: one parse and one format
    /// for every native path that reads or writes it.
    /// </summary>
    internal static class WinboxRecordId
    {
        /// <summary>
        /// Reads a <c>*HEX</c> handle (<c>"*1A"</c> → <c>0x1A</c>). The id is a u32 read by its bits, so a handle with
        /// the high bit set (a route's <c>*80000019</c>, <c>/queue/type</c>'s <c>*FFFFFFFA</c>) parses to a negative
        /// <see cref="int"/>, which is what the M2 layer sends. <c>false</c> for anything else — a friendly name, or no
        /// value at all.
        /// </summary>
        internal static bool TryParse(string? text, out int id)
        {
            id = 0;
            return !string.IsNullOrEmpty(text) && text!.StartsWith("*")
                   && int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        }

        /// <summary>The <c>*HEX</c> handle of an M2 record id.</summary>
        internal static string Format(uint id) => "*" + id.ToString("X", CultureInfo.InvariantCulture);

        /// <summary>The <c>*HEX</c> handle of a record id the decoder read as a (non-negative) 64-bit number.</summary>
        internal static string Format(ulong id) => "*" + id.ToString("X", CultureInfo.InvariantCulture);
    }
}
