using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    internal sealed partial class WinboxFieldResolver
    {
        // ── webfig 'addr' compound (master.js types.addr) ──────────────────────
        //
        // An 'addr' field is a nested message, and every address FORM has its own sub-key. Which forms a
        // particular field accepts is the .jg 'allow' mask (WinboxJgField.Allow) — the Ping window's target
        // is allow:'46v%Dm', a /ip/route gateway is allow:'46i', and so on.
        internal const int AddrV4SubKey     = 0xFEFF20;   // ufeff20 — IPv4, u32 octet-LSB
        internal const int AddrV6SubKey     = 0xFEFF21;   // afeff21 — IPv6, 16 raw bytes big-endian
        internal const int AddrIfaceSubKey  = 0xFEFF22;   // ufeff22 — '%iface' suffix (dropdown id)

        /// <summary>The dropdown an <c>addr</c>'s <c>%iface</c> qualifier names — the generic interface
        /// table, the same [20,0] every <c>type:'enm'</c> interface reference resolves against.</summary>
        internal static readonly int[] AddrIfaceRefHandler = { 20, 0 };

        private  const int AddrVrfSubKey    = 0xFEFF23;   // ufeff23 — '@vrf' suffix (dropdown id)
        internal const int AddrPrefixSubKey = 0xFEFF25;   // ufeff25 — '/len' prefix length, u32
        internal const int AddrDnsSubKey    = 0xFEFF26;   // sfeff26 — DNS name, string
        private  const int AddrRdSubKey     = 0xFEFF27;   // sfeff27 — route distinguisher, string
        internal const int AddrMacSubKey    = 0xFEFF2F;   // rfeff2f — MAC, 6 raw bytes

        // Fallback mask for the one .jg addr field that carries no 'allow' — webfig itself cannot render that
        // field either (types.addr.tostr returns '' when allow is null), so any choice is ours; the three
        // ordinary forms are the least surprising.
        private const string DefaultAddrAllow = "46D";

        /// <summary>
        /// Encodes an address string into the sub-fields of a webfig <c>addr</c> compound, following
        /// <c>types.addr.fromstr</c> in <c>master*.js</c>: try IPv4, then IPv6, then a DNS name, then a route
        /// distinguisher, then a MAC — each only if <paramref name="allow"/> permits it — and append the
        /// <c>/prefix</c> suffix when present.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every branch has to be encoded, not just IPv4. A value that falls back to a bare string at the
        /// FIELD key is a shape the router does not read: it answers as though the field had never been sent,
        /// so <c>/ping address=example.com</c> comes back "no address was specified" for a host the binary API
        /// pings fine, and so does every IPv6 target. A missing branch is therefore not a missing feature but
        /// a silent wrong-request bug, and the "no address was specified" row makes it look like a router
        /// error (see Docs/winbox-native-m2-protocol.md §23).
        /// </para>
        /// <para>
        /// The DNS branch deliberately sends the WHOLE input, not the part before the first separator —
        /// master.js writes <c>val.sfeff26=str</c> where every other branch takes <c>l[i]</c>.
        /// </para>
        /// </remarks>
        internal static byte[][] EncodeAddr(string value, string? allow, string apiName, string? apiPath)
        {
            allow = allow ?? DefaultAddrAllow;
            var parts = value.Split('/', '%', '@', '&');
            string head = parts[0];
            var sub = new List<byte[]>();

            if (allow.IndexOf('4') >= 0 && PackIpV4(head) is uint v4)
                sub.Add(M2Message.U32Sys(AddrV4SubKey, unchecked((int)v4)));
            else if (allow.IndexOf('6') >= 0 && PackIpV6(head) is byte[] v6)
                sub.Add(M2Message.Addr6Sys(AddrV6SubKey, v6));
            else if (allow.IndexOf('D') >= 0)
                sub.Add(M2Message.StringSys(AddrDnsSubKey, value));
            else if (allow.IndexOf('R') >= 0)
                sub.Add(M2Message.StringSys(AddrRdSubKey, head));
            else if (allow.IndexOf('m') >= 0 && TryParseMac(head, out byte[]? mac))
                sub.Add(M2Message.RawSys(AddrMacSubKey, mac!)); // non-null: TryParseMac only returns true after setting mac
            else
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName}");

            // '/len' — the only suffix with a self-contained value. '%iface' and '@vrf' name a record in a
            // dropdown table ([20,0] / [20,101]) and would need the same reference resolution the enm path
            // does; refuse them loudly rather than dropping the qualifier and silently addressing something
            // else (an fe80:: link-local without its %iface is a different destination).
            int slash = value.IndexOf('/');
            if (slash >= 0 && allow.IndexOf('D') < 0)
            {
                // A prefix on a field that does not accept one changes what the value means, so it is a bad
                // value rather than something to trim off (webfig's fromstr returns null here too).
                if (allow.IndexOf('/') < 0 || !int.TryParse(value.Substring(slash + 1), out int plen))
                    throw new WinboxFieldValueException($"input does not match any value of {apiName}");
                sub.Add(EncodeU32(AddrPrefixSubKey, (uint)plen));
            }
            if (value.IndexOfAny(new[] { '%', '@' }) >= 0)
                throw new WinboxFieldResolutionException(
                    $"WinBox native: address '{value}' for field '{apiName}' on '{apiPath}' carries an " +
                    "interface ('%') or VRF ('@') qualifier, which is not yet encodable over native WinBox M2 " +
                    $"(it would have to resolve the name against the {AddrIfaceSubKey:X}/{AddrVrfSubKey:X} " +
                    "dropdown). Use an Api/REST/CLI connection for this value.");

            return sub.ToArray();
        }

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

        // "AA:BB:CC:DD:EE:FF" → 6 raw bytes (webfig string2macaddr). Returns false when it is not a MAC.
        private static bool TryParseMac(string s, out byte[]? mac)
        {
            mac = null;
            var p = (s ?? "").Split(':');
            if (p.Length != 6) return false;
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
                if (!byte.TryParse(p[i], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out bytes[i]))
                    return false;
            mac = bytes;
            return true;
        }

        private static byte[] EncodeU32(int key, uint n)
            => (n <= 255) ? M2Message.U8Sys(key, (byte)n) : M2Message.U32Sys(key, unchecked((int)n));

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

        // ── decode-side formatting (u32/bytes → text) ──────────────────────────

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
                return string.Join(":", b.Select(x => x.ToString("X2")));

            // The wire form this actually arrives in. M2Message renders an FT_RAW value as unseparated
            // uppercase hex ("AABBCCDDEEFF") rather than a byte[], so every macaddr field — which is a raw
            // on the wire — fell straight through to ToString() and reached the caller as one 12-digit run
            // where RouterOS prints AA:BB:CC:DD:EE:FF. /interface/ethernet, /ip/arp, /ip/neighbor and
            // /tool/romon all read this way; the .jg types them macaddr correctly, so the .jg was never the
            // problem.
            string s = value?.ToString() ?? "";
            if (s.Length >= 2 && s.Length % 2 == 0 && s.IndexOf(':') < 0 && IsHex(s))
                return string.Join(":", Enumerable.Range(0, s.Length / 2)
                                                  .Select(i => s.Substring(i * 2, 2).ToUpperInvariant()));
            return s;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        /// <summary>
        /// Reads an M2 value as a 64-bit integer, reporting failure instead of substituting one. Every numeric
        /// read on the decode path goes through this so the "not a number" case is a decision the caller has to
        /// take, not a value it silently inherits.
        /// </summary>
        internal static bool TryToInt64(object value, out long result)
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

        // ── v4 address RANGE (network + range:1) ──────────────────────────────
        //
        // A range:1 'network' field stores a START address and, at its maskid sibling, an END address — every
        // firewall address field is one. RouterOS renders that pair in three forms, and picks between them by
        // the SPAN, not by how the value was entered (all three verified live on 7.23.2, see
        // Docs/winbox-native-m2-protocol.md §24):
        //   start == end                     → "192.0.2.74"          (bare host, NOT "/32")
        //   span is an aligned CIDR block    → "192.0.2.0/30"        (even when entered as 192.0.2.0-192.0.2.3)
        //   anything else                    → "192.0.2.10-192.0.2.20"
        // Matching that exactly is the whole point: the same record must read identically over every transport.

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
        private static uint MaskFrom(string s)
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

        private static bool ParseBool(string v)
            => v == "true" || v == "yes" || v == "1" ||
               string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);

        // Parse a MAC ("AA:BB:..."/"AABB...") or hex blob into raw bytes.
        private static byte[] ParseRaw(string v)
        {
            string hex = (v ?? "").Replace(":", "").Replace("-", "").Replace(" ", "");
            if (hex.Length % 2 != 0) return Encoding.UTF8.GetBytes(v ?? "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                if (!byte.TryParse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out bytes[i]))
                    return Encoding.UTF8.GetBytes(v ?? "");
            return bytes;
        }
    }
}
