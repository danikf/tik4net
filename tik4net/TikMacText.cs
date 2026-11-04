using System;
using System.Globalization;

namespace tik4net
{
    /// <summary>A MAC address as RouterOS spells it: six hex pairs, upper case, separated by colons.</summary>
    /// <remarks>
    /// The strict reader is what decides whether a bare string is an address (<see cref="TikRouterAddress.Parse"/>), so it
    /// takes only the two spellings an address is written in. The lenient one is for a value already known to be a MAC,
    /// and takes what the router itself accepts for one: single-digit groups (<c>2:3:4:5:6:7</c> is set as
    /// <c>02:03:04:05:06:07</c>), either separator, and a bare run of twelve digits.
    /// </remarks>
    internal static class TikMacText
    {
        /// <summary>Six hex pairs separated by <c>:</c> or <c>-</c>, the same separator throughout.</summary>
        internal static bool TryParse(string? text, out byte[]? mac)
        {
            mac = null;
            if (text == null || text.Length != 17) return false;

            char sep = text[2];
            if (sep != ':' && sep != '-') return false;

            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                int p = i * 3;
                if (i > 0 && text[p - 1] != sep) return false;
                if (!TikHexText.IsHexDigit(text[p]) || !TikHexText.IsHexDigit(text[p + 1])) return false;
                bytes[i] = (byte)(TikHexText.ValueOf(text[p]) << 4 | TikHexText.ValueOf(text[p + 1]));
            }

            mac = bytes;
            return true;
        }

        /// <summary>
        /// <see cref="TryParse(string, out byte[])"/>, or with <paramref name="lenient"/> six groups of one or two hex
        /// digits separated by <c>:</c> or <c>-</c> (the same separator throughout), or twelve digits with none.
        /// </summary>
        internal static bool TryParse(string? text, bool lenient, out byte[]? mac)
        {
            if (!lenient) return TryParse(text, out mac);

            mac = null;
            if (text == null) return false;
            if (text.Length == 12) return TikHexText.TryParse(text, out mac);

            var groups = text.Split(text.IndexOf(':') >= 0 ? ':' : '-');
            if (groups.Length != 6) return false;

            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                string g = groups[i];
                if (g.Length == 0 || g.Length > 2 || !TikHexText.IsHex(g)) return false;
                bytes[i] = (byte)(g.Length == 1 ? TikHexText.ValueOf(g[0]) : TikHexText.ValueOf(g[0]) << 4 | TikHexText.ValueOf(g[1]));
            }

            mac = bytes;
            return true;
        }

        /// <summary>The bytes as upper-case hex pairs joined by <c>:</c>.</summary>
        internal static string Format(byte[] mac)
            => string.Join(":", Array.ConvertAll(mac, b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
