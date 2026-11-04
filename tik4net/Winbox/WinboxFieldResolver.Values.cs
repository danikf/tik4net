using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using static tik4net.Winbox.WinboxValueFormat;

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
        internal static readonly WinboxHandler AddrIfaceRefHandler = new WinboxHandler(20, 0);

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
            else if (allow.IndexOf('m') >= 0 && TikMacText.TryParse(head, lenient: true, out byte[]? mac))
                sub.Add(M2Message.RawSys(AddrMacSubKey, mac!)); // non-null: TikMacText.TryParse only returns true after setting mac
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

        private static byte[] EncodeU32(int key, uint n)
            => (n <= 255) ? M2Message.U8Sys(key, (byte)n) : M2Message.U32Sys(key, unchecked((int)n));

        // ── decode-side formatting (u32/bytes → text) ──────────────────────────

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

        // "1" is a WinBox wire spelling, not a RouterOS one, so it stays here rather than in TikBoolText.
        private static bool ParseBool(string v) => v == "1" || TikBoolText.IsTrue(v);

        // A macaddr value: a MAC in any spelling the router accepts (2:3:4:5:6:7 is 02:03:04:05:06:07, which stripping
        // the separators would send as the three bytes 12 34 56), otherwise the raw reading.
        private static byte[] ParseMacOrRaw(string v)
            => TikMacText.TryParse(v, lenient: true, out byte[]? mac) ? mac! : ParseRaw(v);

        // Parse a MAC ("AA:BB:..."/"AABB...") or hex blob into raw bytes.
        private static byte[] ParseRaw(string v)
        {
            string hex = (v ?? "").Replace(":", "").Replace("-", "").Replace(" ", "");
            if (hex.Length == 0) return new byte[0];
            return TikHexText.TryParse(hex, out byte[]? bytes) ? bytes! : Encoding.UTF8.GetBytes(v ?? "");
        }
    }
}
