using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    /// <summary>
    /// The value formats the native transport reads and writes in both directions: IPv4/IPv6 addresses and masks,
    /// MACs, IPv4 ranges, durations and clock times, scaled and radix numbers, and the one "is this a number" rule
    /// (<see cref="TryToInt64"/>). Pure functions of a value, shared by the encoder (<see cref="WinboxFieldResolver"/>)
    /// and the decoder (<see cref="WinboxRecordCodec"/>), so the two sides of one field cannot drift apart.
    /// </summary>
    internal static class WinboxValueFormat
    {
        // "1:2::3" → 16 bytes big-endian, matching webfig string2ip6addr (including its trailing-IPv4 form,
        // "::ffff:1.2.3.4"). Returns null when the text is not an IPv6 address.
        internal static byte[]? PackIpV6(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(':') < 0) return null;
            var groups = s.Split(':');
            var head = new List<byte>();
            var tail = new List<byte>();
            var cur = head;
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i].Length == 0)
                {
                    // "::" — one run of zero groups, and only in the middle ("::1" splits to ["","","1"]).
                    if (i > 0 && i + 1 < groups.Length)
                    {
                        if (cur == tail) return null;   // a second "::" is not an address
                        cur = tail;
                        continue;
                    }
                    if (i == 0 || i + 1 == groups.Length) continue;
                }
                if (i + 1 == groups.Length && PackIpV4(groups[i]) is uint tail4)
                {
                    // A trailing dotted quad ("::ffff:192.0.2.1"): PackIpV4 packs octet-LSB, so the bytes go
                    // out in that same order — exactly what master.js pushes (a&0xff, a>>8, a>>16, a>>24).
                    for (int b = 0; b < 4; b++) cur.Add((byte)(tail4 >> (8 * b)));
                    break;
                }
                if (!ushort.TryParse(groups[i], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out ushort g))
                    return null;
                cur.Add((byte)(g >> 8));
                cur.Add((byte)(g & 0xff));
            }
            if (head.Count + tail.Count > 16) return null;
            if (cur == head && head.Count != 16) return null;   // no "::" → must be exactly 8 groups
            var bytes = new byte[16];
            head.CopyTo(bytes, 0);
            tail.CopyTo(bytes, 16 - tail.Count);
            return bytes;
        }

        // "a.b.c.d" → u32 packed octet-LSB (a | b<<8 | c<<16 | d<<24), matching webfig string2ipaddr.
        // Returns null when the text is not a dotted IPv4 quad.
        internal static uint? PackIpV4(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var o = s.Split('.');
            if (o.Length != 4) return null;
            uint v = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!byte.TryParse(o[i], out byte b)) return null;
                v |= (uint)b << (8 * i);
            }
            return v;
        }

        /// <summary>u32 packed octet-LSB → "a.b.c.d" (inverse of <see cref="PackIpV4"/>).</summary>
        /// <exception cref="WinboxFieldResolutionException">The value is not numeric — see <see cref="ToU32"/>.</exception>
        internal static string IpFromU32(object value)
        {
            uint v = ToU32(value, "an IPv4 address");
            return $"{v & 0xff}.{(v >> 8) & 0xff}.{(v >> 16) & 0xff}.{(v >> 24) & 0xff}";
        }

        /// <summary>Netmask u32 (octet-LSB) → prefix length (count of set bits).</summary>
        /// <exception cref="WinboxFieldResolutionException">The value is not numeric — see <see cref="ToU32"/>.</exception>
        internal static int MaskToPrefix(object value)
        {
            uint v = ToU32(value, "an IPv4 netmask");
            int n = 0;
            while (v != 0) { n += (int)(v & 1); v >>= 1; }
            return n;
        }

        /// <summary>
        /// Raw 16 bytes → IPv6 text, compressing the longest run of zero groups to "::" (inverse of
        /// <see cref="PackIpV6"/>, following webfig's <c>ip6addr2string</c>).
        /// </summary>
        /// <remarks>
        /// One deliberate difference from <c>ip6addr2string</c>: an IPv4-MAPPED address (<c>::ffff:a.b.c.d</c>)
        /// renders as the bare dotted quad. In webfig that is the job of a per-field <c>allowipv4</c> flag on
        /// the <c>ip6addr</c> node (<c>types.ip6addr.tostr</c>), which sits on nested union members the catalog
        /// does not model — and the fields that carry v4-mapped values are exactly the ones declaring it (the
        /// traceroute hop). The binary API calls such a hop <c>127.0.0.1</c>, so rendering
        /// <c>::ffff:127.0.0.1</c> would make the same record read differently per transport, which is exactly
        /// the divergence this codec exists to prevent. An IPv4-COMPATIBLE address (<c>::a.b.c.d</c>) keeps webfig's form.
        /// </remarks>
        internal static string IpV6FromBytes(byte[] b)
        {
            if (b == null || b.Length != 16) return "";

            bool leading10Zero = true;
            for (int i = 0; i < 10; i++) if (b[i] != 0) { leading10Zero = false; break; }
            if (leading10Zero)
            {
                string quad = $"{b[12]}.{b[13]}.{b[14]}.{b[15]}";
                if (b[10] == 0xFF && b[11] == 0xFF) return quad;                    // ::ffff:a.b.c.d (mapped)
                // ::a.b.c.d (compatible) — only when the zero run stops at byte 12, exactly as webfig's
                // zerosLen==12 test does; ::1 and friends keep the ordinary "::1" form.
                if (b[10] == 0 && b[11] == 0 && (b[12] != 0 || b[13] != 0))
                    return "::" + quad;
            }

            var groups = new int[8];
            for (int i = 0; i < 8; i++) groups[i] = (b[i * 2] << 8) | b[i * 2 + 1];

            int bestStart = -1, bestLen = 1, curStart = -1, curLen = 0;
            for (int i = 0; i < 8; i++)
            {
                if (groups[i] == 0)
                {
                    if (curStart < 0) { curStart = i; curLen = 0; }
                    curLen++;
                    if (curLen > bestLen) { bestStart = curStart; bestLen = curLen; }
                }
                else curStart = -1;
            }

            // The zero run contributes ONE colon and the group after it contributes its own separator, which
            // is what makes "::" — the same two-halves trick ip6addr2string uses. A run that ends the address
            // has no following group, so it closes itself.
            var sb = new StringBuilder();
            for (int i = 0; i < 8; )
            {
                if (i == bestStart)
                {
                    sb.Append(':');
                    i += bestLen;
                    if (i == 8) sb.Append(':');
                    continue;
                }
                if (i > 0) sb.Append(':');
                sb.Append(groups[i].ToString("x"));
                i++;
            }
            return sb.Length == 0 ? "::" : sb.ToString();
        }

        /// <summary>Raw 6-byte MAC → "AA:BB:CC:DD:EE:FF".</summary>
        internal static string MacFromBytes(object value)
        {
            if (value is byte[] b && b.Length > 0)
                return TikMacText.Format(b);

            // The wire form this actually arrives in. M2Message renders an FT_RAW value as unseparated
            // uppercase hex ("AABBCCDDEEFF") rather than a byte[], so every macaddr field — which is a raw
            // on the wire — fell straight through to ToString() and reached the caller as one 12-digit run
            // where RouterOS prints AA:BB:CC:DD:EE:FF. /interface/ethernet, /ip/arp, /ip/neighbor and
            // /tool/romon all read this way; the .jg types them macaddr correctly, so the .jg was never the
            // problem.
            string s = value?.ToString() ?? "";
            if (TikHexText.TryParse(s, out byte[]? bytes))
                return TikMacText.Format(bytes!);
            return s;
        }

        /// <summary>
        /// Reads an M2 value as a 64-bit integer, reporting failure instead of substituting one. Every numeric
        /// read on the decode path goes through this so the "not a number" case is a decision the caller has to
        /// take, not a value it silently inherits.
        /// </summary>
        internal static bool TryToInt64(object? value, out long result)
        {
            // Convert.ToInt64(null) is 0 and does NOT throw — the one substitution the exception list below
            // would not have caught, and the one most likely to be hit (an absent field decodes to null).
            if (value == null) { result = 0; return false; }
            // Otherwise Convert.ToInt64 answers "not a number" with four different exception types depending on
            // what it was handed (a byte[], "x", a value out of range); none of them are caught anywhere else,
            // so listing them keeps a genuine bug in this method from being swallowed as "not numeric".
            try { result = Convert.ToInt64(value); return true; }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            catch (ArgumentNullException) { }
            result = 0;
            return false;
        }

        /// <summary>
        /// Renders a range:1 <c>network</c> pair (start + end, both octet-LSB u32) as the RouterOS API text.
        /// </summary>
        internal static string FormatV4Range(object startValue, object endValue)
        {
            uint start = ToBigEndian(ToU32(startValue, "an IPv4 range start"));
            uint end = ToBigEndian(ToU32(endValue, "an IPv4 range end"));

            if (start == end) return IpFromU32(startValue);
            if (end > start)
            {
                // An aligned block has span 2^n and a start aligned to it; then n bits are the host part.
                // Counted in 64 bits because 0.0.0.0-255.255.255.255 is a legal block whose span is 2^32.
                ulong span = (ulong)end - start + 1;
                if ((span & (span - 1)) == 0 && (start & (span - 1)) == 0)
                {
                    int hostBits = 0;
                    for (ulong s = span; s > 1; s >>= 1) hostBits++;
                    return IpFromU32(startValue) + "/" + (32 - hostBits);
                }
            }
            return IpFromU32(startValue) + "-" + IpFromU32(endValue);
        }

        /// <summary>
        /// Parses the RouterOS text of a range:1 <c>network</c> field into the start/end pair the wire carries,
        /// accepting all three forms <see cref="FormatV4Range"/> emits. Returns <c>false</c> when the text is
        /// not IPv4 (the caller then falls through to the generic encoders).
        /// </summary>
        internal static bool TryParseV4Range(string value, out uint start, out uint end)
        {
            start = end = 0;
            if (string.IsNullOrEmpty(value)) return false;

            int dash = value.IndexOf('-');
            if (dash >= 0)
            {
                uint? lo = PackIpV4(value.Substring(0, dash).Trim());
                uint? hi = PackIpV4(value.Substring(dash + 1).Trim());
                if (lo == null || hi == null) return false;
                start = lo.Value; end = hi.Value;
                return true;
            }

            int slash = value.IndexOf('/');
            uint? addr = PackIpV4((slash >= 0 ? value.Substring(0, slash) : value).Trim());
            if (addr == null) return false;
            start = addr.Value;
            if (slash < 0) { end = addr.Value; return true; }

            // "a.b.c.d/len" → the block's first and last address. The mask is octet-LSB like the address, so
            // the host part is its complement; a /32 collapses to start==end, which is the bare-host form.
            uint mask = MaskFrom(value.Substring(slash + 1).Trim());
            start = addr.Value & mask;
            end = start | ~mask;
            return true;
        }

        // Octet-LSB u32 (the M2/webfig packing) → the same address as a numerically ORDERED value. Comparing
        // or subtracting the packed form directly compares the last octet first, which makes 192.0.2.255 look
        // smaller than 192.0.2.0.
        private static uint ToBigEndian(uint v)
            => (v >> 24) | ((v >> 8) & 0xFF00u) | ((v << 8) & 0xFF0000u) | (v << 24);

        // Netmask as octet-LSB u32: dotted "255.255.255.0" → packed, or prefix length "24" → len2netmask.
        internal static uint MaskFrom(string s)
        {
            uint? dotted = PackIpV4(s);
            if (dotted != null) return dotted.Value;
            if (int.TryParse(s, out int len) && len >= 0 && len <= 32)
            {
                uint v = 0;
                for (int i = 0; i < len; i++)            // set the top `len` bits in big-endian order,
                {                                         // then place each byte at its octet-LSB position
                    int bit = 7 - (i % 8);
                    int octet = i / 8;
                    v |= (uint)(1 << bit) << (8 * octet);
                }
                return v;
            }
            return 0xFFFFFFFFu;
        }

        /// <summary>
        /// Parses a RouterOS duration into the <paramref name="scale"/>-per-second unit the wire carries —
        /// the inverse of <see cref="FormatDuration"/>, which is why the unit table and the
        /// millisecond handling are the same ones.
        /// </summary>
        /// <remarks>
        /// <para>RouterOS spells the same value three ways and accepts all of them, so all three are read
        /// here: the unit form ("5m", "1w2d3h", "1m30s", "500ms"), the clock form ("00:05:00", "1:00:00",
        /// optionally with a "1d " or "1w2d" prefix and a ".500" fraction), and a bare count of seconds.
        /// The clock form is not an exotic input — it is what <c>/system/scheduler</c> prints for
        /// <c>interval</c> and what <c>/ip/hotspot/user</c> prints for <c>limit-uptime</c>.</para>
        /// <para>A bare number means SECONDS, which is what RouterOS means by one. It is scaled like any
        /// other value: on a <c>scale:100</c> field the wire wants hundredths, so sending the number through
        /// raw (what the generic u32 branch did) was already off by the scale factor.</para>
        /// </remarks>
        internal static bool TryParseDuration(string value, int scale, out long ticks)
        {
            ticks = 0;
            if (scale < 1) scale = 1;
            string s = value.Trim();
            if (s.Length == 0) return false;

            bool negative = s[0] == '-';
            if (negative || s[0] == '+') s = s.Substring(1);
            if (s.Length == 0) return false;

            long milliseconds = 0;
            int i = 0;
            bool any = false;
            while (i < s.Length)
            {
                if (s[i] == ' ') { i++; continue; }        // "1d 00:05:00" — the separator RouterOS may print

                int digitsStart = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == digitsStart) return false;                       // a unit with no number in front
                if (!long.TryParse(s.Substring(digitsStart, i - digitsStart),
                        NumberStyles.None, CultureInfo.InvariantCulture, out long n))
                    return false;                                          // overflow

                // A ':' means the rest is a clock group, which is a whole value rather than one component.
                if (i < s.Length && s[i] == ':')
                {
                    if (!TryParseClock(n, s.Substring(i), out long clockMs)) return false;
                    milliseconds += clockMs;
                    any = true;
                    break;
                }

                int unitStart = i;
                while (i < s.Length && !(s[i] >= '0' && s[i] <= '9') && s[i] != ' ') i++;
                string unit = s.Substring(unitStart, i - unitStart).ToLowerInvariant();

                long perUnitMs;
                switch (unit)
                {
                    case "w": perUnitMs = 604800000L; break;
                    case "d": perUnitMs = 86400000L; break;
                    case "h": perUnitMs = 3600000L; break;
                    case "m": perUnitMs = 60000L; break;
                    case "s": perUnitMs = 1000L; break;
                    case "ms": perUnitMs = 1L; break;
                    case "":
                        // A bare number is seconds — but only as the WHOLE value. A unitless component
                        // trailing units ("5m3") is not something RouterOS prints and not something whose
                        // intent is knowable, so it is refused rather than read as "and 3 seconds".
                        if (any) return false;
                        perUnitMs = 1000L;
                        break;
                    default: return false;
                }
                milliseconds += n * perUnitMs;
                any = true;
            }
            if (!any) return false;

            ticks = milliseconds * scale / 1000;
            if (negative) ticks = -ticks;
            return true;
        }

        /// <summary>
        /// The clock half of <see cref="TryParseDuration"/>: <paramref name="hours"/> already read, and
        /// <paramref name="rest"/> starting at the first ':' — <c>:MM:SS</c> with an optional <c>.fff</c>.
        /// </summary>
        /// <remarks>
        /// Only the three-part form is accepted. RouterOS always prints hours:minutes:seconds, and a
        /// two-part "05:00" could as reasonably mean five minutes as five hours — a guess on a value the
        /// caller is writing to the router, so it is refused instead.
        /// </remarks>
        private static bool TryParseClock(long hours, string rest, out long milliseconds)
        {
            milliseconds = 0;
            string[] parts = rest.Split(':');
            // rest starts with ':', so parts[0] is empty and the clock's minutes/seconds are parts[1..2].
            if (parts.Length != 3 || parts[0].Length != 0) return false;

            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long minutes))
                return false;

            string secondsPart = parts[2];
            long fractionMs = 0;
            int dot = secondsPart.IndexOf('.');
            if (dot >= 0)
            {
                string frac = secondsPart.Substring(dot + 1).PadRight(3, '0').Substring(0, 3);
                if (!long.TryParse(frac, NumberStyles.None, CultureInfo.InvariantCulture, out fractionMs))
                    return false;
                secondsPart = secondsPart.Substring(0, dot);
            }
            if (!long.TryParse(secondsPart, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
                return false;

            milliseconds = ((hours * 60 + minutes) * 60 + seconds) * 1000 + fractionMs;
            return true;
        }

        internal static string FormatRadix(long value, int radix, bool withPrefix)
        {
            if (radix != 16) return value.ToString(CultureInfo.InvariantCulture);
            string digits = value < 0
                ? "-" + (-value).ToString("x", CultureInfo.InvariantCulture)
                : value.ToString("x", CultureInfo.InvariantCulture);
            return withPrefix ? "0x" + digits : digits;
        }

        /// <summary>
        /// A scaled integer as the API prints it: the whole part, then the fraction padded to the scale's
        /// digits and trimmed of trailing zeros — the same rule the scalar <c>fixedpoint</c> case follows,
        /// so 2412000/1000 is "2412" and 1500/1000 is "1.5".
        /// </summary>
        internal static string FormatScaled(long value, int scale)
        {
            if (scale < 1) scale = 1;
            string sign = value < 0 ? "-" : "";
            long abs = Math.Abs(value);
            int digits = scale.ToString(CultureInfo.InvariantCulture).Length - 1;
            string frac = (abs % scale).ToString(CultureInfo.InvariantCulture)
                .PadLeft(digits, '0').TrimEnd('0');
            return sign + (abs / scale).ToString(CultureInfo.InvariantCulture)
                 + (frac.Length > 0 ? "." + frac : "");
        }

        /// <summary>
        /// Renders a wire duration as the text RouterOS's own API prints for it: the non-zero units only,
        /// largest first, concatenated without separators — <c>1w</c>, <c>5m</c>, <c>2d17h30m3s</c>,
        /// <c>518w1d18h55m42s</c> — and a zero in <paramref name="zeroUnit"/> when the whole thing is zero.
        /// </summary>
        /// <param name="ticks">The wire value.</param>
        /// <param name="scale">Wire units per second (the <c>.jg</c> <c>scale</c>, 1 when undeclared).</param>
        /// <param name="zeroUnit">
        /// The unit a zero is spelled in — the unit the FIELD is expressed in, not seconds: a bonding
        /// down-delay of nothing reads <c>0ms</c>.
        /// </param>
        /// <remarks>
        /// Deliberately not webfig's <c>interval2string</c>, which renders <c>1d 02:03:04</c>: that is what
        /// the WinBox UI shows, and the API is what every other transport in this library reports. The unit
        /// set and the omit-zeroes rule are read off the API's own output on 7.23.2 (604800 → <c>1w</c>,
        /// 300 → <c>5m</c>, 0 → <c>0s</c>).
        /// </remarks>
        internal static string FormatDuration(long ticks, int scale, string zeroUnit = "s")
        {
            if (scale < 1) scale = 1;
            bool negative = ticks < 0;
            if (negative) ticks = -ticks;

            long seconds = ticks / scale;
            long remainder = ticks % scale;

            var sb = new System.Text.StringBuilder();
            if (negative) sb.Append('-');
            long[] sizes = { 604800, 86400, 3600, 60 };
            string[] units = { "w", "d", "h", "m" };
            for (int i = 0; i < sizes.Length; i++)
            {
                long n = seconds / sizes[i];
                if (n > 0) sb.Append(n).Append(units[i]);
                seconds %= sizes[i];
            }
            if (seconds > 0) sb.Append(seconds).Append('s');

            // A scaled field can carry a sub-second remainder (scale:100 counts hundredths); RouterOS spells
            // that in milliseconds. Dropping it would report a value the router did not give us. A field finer
            // than a millisecond carries microseconds too, and the API writes them: a ping's 105488 (a
            // fixedpoint of milliseconds, scale:1000) is 105ms488us.
            if (remainder > 0 && scale > 1000)
            {
                long micros = remainder * 1000000 / scale;
                if (micros >= 1000) sb.Append(micros / 1000).Append("ms");
                if (micros % 1000 > 0) sb.Append(micros % 1000).Append("us");
            }
            else if (remainder > 0) sb.Append(remainder * 1000 / scale).Append("ms");

            // Everything was zero — which is a value, and the API prints it as a zero in the unit the FIELD
            // is expressed in, not in seconds: a bonding down-delay of nothing reads `0ms`, not `0s`.
            if (sb.Length == 0 || (negative && sb.Length == 1)) sb.Append("0").Append(zeroUnit);
            return sb.ToString();
        }

        // Numeric read on a path whose fallback would be INDISTINGUISHABLE from a real answer: an unreadable
        // ipaddr used to render 0.0.0.0 and an unreadable netmask prefix /0, both of which reach the caller
        // looking like something the router said. There is no honest substitute, so this throws — and
        // it means our .jg UI type and the value the router sent disagree, which is our bug to fix, not the
        // caller's to work around.
        private static uint ToU32(object value, string what)
        {
            if (TryToInt64(value, out long v)) return unchecked((uint)v);
            throw new WinboxFieldResolutionException(
                $"WinBox M2 value '{value}' (CLR type {value?.GetType().Name ?? "null"}) is not a number, " +
                $"so it cannot be decoded as {what}. The field's .jg UI type does not match what the router sent.");
        }
    }
}
