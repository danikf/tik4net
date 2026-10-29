using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

using static tik4net.Winbox.WinboxValueFormat;

namespace tik4net.Winbox
{
    internal sealed partial class WinboxRecordCodec
    {
        // Format an M2 value to its RouterOS API text using the .jg UI-semantic type: IPs unpack from u32,
        // a network field renders "addr/prefixlen" (pulling the netmask from its maskid sibling key), MACs
        // from raw bytes, static enums back to their string label, dynamic enum references back to the
        // referenced record's name. Falls back to the wire-type formatter.
        private string FormatTyped(WinboxJgField? jf, string wireType, object value,
            Dictionary<int, Tuple<string, object>> rec, Dictionary<string, int[]>? collectRefTables)
        {
            if (jf != null && value != null)
            {
                // TryZeroWord only returns true when it found a word (non-null by construction).
                if (TryZeroWord(jf, value, out string? zeroWord)) return zeroWord!;
                if (TrySentinelWord(jf, value, out string? sentinelWord)) return sentinelWord!;
                switch (jf.UiType)
                {
                    case WinboxFieldResolver.FirewallLimitUiType:
                    case WinboxFieldResolver.FirewallDstLimitUiType:
                        return FormatFirewallLimit(jf, value, rec);
                    case WinboxFieldResolver.FirewallTosUiType:
                    {
                        WinboxValueFormat.TryToInt64(value, out long tos);
                        long mask = rec.TryGetValue(0x1CA, out var mt) && WinboxValueFormat.TryToInt64(mt.Item2, out long m) ? m : 255;
                        return "0x" + tos.ToString("X", CultureInfo.InvariantCulture)
                             + (mask == 255 ? "" : "/0x" + mask.ToString("X", CultureInfo.InvariantCulture));
                    }
                    case "ipaddr":
                        return WinboxValueFormat.IpFromU32(value);
                    case "ipaddrandport":
                    {
                        // types.ipaddrandport.tostr: ipaddr2string(addr) + (port ? ':' + port : '') — the port at
                        // 'portid' (MaskKey), left out when absent or 0, as the 6.x API prints a connection.
                        string addr = WinboxValueFormat.IpFromU32(value);
                        long port = jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var pt)
                                    && WinboxValueFormat.TryToInt64(pt.Item2, out long p) ? p : 0;
                        return port == 0 ? addr : addr + ":" + port.ToString(CultureInfo.InvariantCulture);
                    }
                    case "netmask":
                        // types.netmask.tostr is netmask2len(val).toString() — the LENGTH, not the mask,
                        // and RouterOS agrees: a pcq queue type prints pcq-src-address-mask=32 for the
                        // 4294967295 on the wire. Without this the raw u32 reached the caller.
                        return WinboxValueFormat.MaskToPrefix(value)
                                   .ToString(CultureInfo.InvariantCulture);
                    case "network":
                    {
                        string addr = WinboxValueFormat.IpFromU32(value);
                        if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var mt) && mt.Item2 != null)
                        {
                            if (jf.IsRange)
                                return WinboxValueFormat.FormatV4Range(value, mt.Item2);
                            return addr + "/" + WinboxValueFormat.MaskToPrefix(mt.Item2);
                        }
                        return addr;
                    }
                    case "network6":
                    {
                        // The IPv6 twin of the case above, and the sibling is not the same thing: it holds
                        // the PREFIX LENGTH itself (types.network6.tostr: addr + '/' + (val[1]||0)), there
                        // being no 128-bit mask to put in a u32. RouterOS prints the length at every value
                        // including 128 — see FormatElementPart's network6, which is where the same rule
                        // already lived for a LIST element.
                        //
                        // Only a scalar network6 reaches here, and until a union registered its other
                        // families (WinboxJgField.ExtraRegistrations) none ever did: /ip/ipsec/policy's
                        // 'Src. Address' is a union whose IPv4 member was the only one in the catalog, so
                        // the v6 value the router actually sends fell through to the wire formatter and
                        // read '::' where the API says '::/0'.
                        string addr6 = value is byte[] b6
                            ? WinboxValueFormat.IpV6FromBytes(b6) : value.ToString()!;
                        if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var m6) && m6.Item2 != null)
                            return addr6 + "/" + (WinboxValueFormat.TryToInt64(m6.Item2, out long n6)
                                ? n6 : 0).ToString(CultureInfo.InvariantCulture);
                        return addr6;
                    }
                    case WinboxFieldResolver.PairUiType:
                    {
                        // One API field out of two M2 scalars (see WinboxFieldResolver.PairUiType). Each
                        // half is formatted by ITS OWN typed field, which is what PairHalves carries: the
                        // two halves of bucket-size are fixedpoints whose wire 100 is the API's 0.1, and
                        // formatting the second with the first's rules would silently rescale it.
                        if (jf.PairHalves == null) break;   // registered without halves — generic formatter
                        var upField = jf.PairHalves.Item1;
                        var downField = jf.PairHalves.Item2;

                        string up = FormatTyped(upField, upField.WireType, value, rec, collectRefTables);
                        string down = "0";
                        if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var other) && other.Item2 != null)
                            down = FormatTyped(downField, downField.WireType, other.Item2, rec, collectRefTables);
                        return up + "/" + down;
                    }
                    case WinboxFieldResolver.AddrPortUiType:
                    {
                        // Two WinBox boxes, one API field (see WinboxFieldResolver.AddrPortUiType). The port
                        // rides on MaskKey and is consumed in DecodeRecord, so it appears only here.
                        string addr = WinboxValueFormat.IpFromU32(value);
                        long port = jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var pt)
                                    && WinboxValueFormat.TryToInt64(pt.Item2, out long p) ? p : 0;
                        return addr + ":" + port.ToString(CultureInfo.InvariantCulture);
                    }
                    case "integer":
                    {
                        // SIGNED, unlike the plain `number` it inherits from: types.integer.get is
                        // `num2int(val)`, and num2int is `v >= 0x80000000 ? v - 0x100000000 : v`. Without it
                        // /system/ntp/client's system-offset read as 4294967276 where the API says a small
                        // negative — a wrong value that looks like a plausible large one.
                        if (!WinboxValueFormat.TryToInt64(value, out long signed))
                        {
                            TraceNonNumeric("integer", value);
                            break;
                        }
                        return NumToInt(signed).ToString(CultureInfo.InvariantCulture);
                    }
                    case "kbytes":
                    {
                        // The wire value is in KIBIBYTES and the API prints BYTES. webfig hides the
                        // difference behind a unit — `types.kbytes.tostr` renders "N KiB" straight from the
                        // number, with no scale multiply, unlike `types.bytes` which is already in bytes —
                        // so nothing in the .jg says the two transports are counting different things.
                        //
                        // Measured on 7.24, three fields of /system/resource, exact to the byte:
                        // total-memory 4194304 -> 4294967296, free-hdd-space 60152 -> 61595648,
                        // total-hdd-size 91372 -> 93564928, each the API's own number. It went unnoticed
                        // because every kbytes field on that path but one has a volatile name (free-,
                        // total-memory) and the audit does not compare those.
                        if (!WinboxValueFormat.TryToInt64(value, out long kib))
                        {
                            TraceNonNumeric("kbytes", value);
                            break;
                        }
                        return (kib * 1024L).ToString(CultureInfo.InvariantCulture);
                    }
                    case "fixedpoint":
                    {
                        // types.fixedpoint.tostr: num2int first, then floor(|v|/scale) '.' the remainder
                        // padded to the scale's digit count (fraction2string). /system/ntp/client freq-drift
                        // is scale:1000, so 4294925573 is -41723 thousandths = -41.723 — the API's value
                        // exactly.
                        //
                        // Trailing zeros are then TRIMMED, which is where this departs from webfig on
                        // purpose: webfig pads to the scale's width and shows -39.730, RouterOS prints
                        // -39.73. Padding therefore disagreed with the API on exactly the values whose last
                        // digit is zero — roughly one reading in ten, which is why it was recorded as
                        // agreeing exactly. A fraction that trims away entirely takes the '.' with it.
                        if (!WinboxValueFormat.TryToInt64(value, out long fp))
                        {
                            TraceNonNumeric("fixedpoint", value);
                            break;
                        }
                        long v = NumToInt(fp);
                        // A postfix of s or ms says this fixedpoint is a TIME, and RouterOS prints a time in
                        // its own compound form rather than as a decimal: /interface/vrrp's interval
                        // (scale:100, postfix:'s') reads 1s at 100 and 1s500ms at 150 — measured by moving
                        // the value, since 1.5 and 1s500ms are the same quantity spelled two ways and only
                        // the router says which spelling is its own.
                        if (jf.IsSecondsPostfix || jf.IsMillisecondsPostfix)
                            return FormatDuration(v, jf.TicksPerSecond, jf.DurationZeroUnit);
                        int scale = jf.Scale < 1 ? 1 : jf.Scale;
                        if (scale == 1) return v.ToString(CultureInfo.InvariantCulture);
                        string sign = v < 0 ? "-" : "";
                        long abs = Math.Abs(v);
                        int digits = scale.ToString(CultureInfo.InvariantCulture).Length - 1;
                        string frac = (abs % scale).ToString(CultureInfo.InvariantCulture)
                            .PadLeft(digits, '0').TrimEnd('0');
                        return sign + (abs / scale).ToString(CultureInfo.InvariantCulture)
                             + (frac.Length > 0 ? "." + frac : "");
                    }
                    case "age":
                    {
                        // NOT a duration, though it wears a duration's clothes. types.age.tostr:
                        //     var uptime = getUptime();
                        //     if (val > 0x7fffffff) val = uptime + Math.abs(val - 0xffffffff);
                        //     else                  val = Math.abs(val - uptime);
                        //     return interval2string(val, 1);
                        // — i.e. the value is a timestamp on the router's UPTIME clock, and the duration is
                        // its distance from now. Measured before the JS was read and agreeing with it:
                        // /certificate and /ip/dhcp-client expires-after both read exactly one uptime higher
                        // than the API (89828 s, with uptime 89828 s at that moment).
                        if (!WinboxValueFormat.TryToInt64(value, out long stamp))
                        {
                            TraceNonNumeric("age", value);
                            break;
                        }
                        if (jf.Scale > 1) stamp /= jf.Scale;
                        long? uptime = RouterUptimeSeconds();
                        if (uptime == null) break;   // no uptime, no honest answer — fall through to raw text
                        long distance = stamp > int.MaxValue
                            ? uptime.Value + Math.Abs(stamp - 0xFFFFFFFFL)
                            : Math.Abs(stamp - uptime.Value);
                        return FormatDuration(distance, 1);
                    }
                    case "dateandtime":
                    case "clockdate":
                    case "date":
                    {
                        // Unix epoch seconds. webfig's date2string is
                        // `new Date(val*1000).toISOString().substring(0,10)` — UTC, no local shift — and the
                        // values confirm it exactly: a certificate's 1784975092 is the API's
                        // '2026-07-25 10:24:52' to the second. clockdate is the same value printed as a date
                        // alone (/system/clock 'Date'). 'date' is the 6.x catalog's name for dateandtime: on
                        // 6.49.13 a certificate's 1789921669 is the API's 'sep/20/2026 16:27:49'.
                        if (!WinboxValueFormat.TryToInt64(value, out long epoch))
                        {
                            TraceNonNumeric(jf.UiType, value);
                            break;
                        }
                        // ... unless the declaration says `relative:1`, in which case the same wire type
                        // carries a moment on the router's UPTIME clock instead (see WinboxJgField.Relative).
                        // /interface's last-link-up-time read 1970-02-28 before this: the value is a distance
                        // from boot in hundredths (scale:100), not seconds since 1970.
                        if (jf.Relative)
                        {
                            long? bootHundredths = RouterBootEpochHundredths();
                            // No uptime and no timezone means no honest answer — the raw value, exactly as an
                            // `age` does, rather than a plausible-looking date computed from a guessed origin.
                            if (bootHundredths == null) break;
                            int relScale = jf.Scale < 1 ? 1 : jf.Scale;
                            long whenHundredths = bootHundredths.Value + (epoch * 100 / relScale);
                            return Epoch.AddSeconds(whenHundredths / 100)
                                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        }
                        var when = Epoch.AddSeconds(epoch);
                        return string.Equals(jf.UiType, "clockdate", StringComparison.OrdinalIgnoreCase)
                            ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    case "timezone":
                    {
                        // types.timezone.tostr → timezone2string: a signed SECOND offset rendered ±HH:MM.
                        // /system/clock gmt-offset read as the bare 7200.
                        if (!WinboxValueFormat.TryToInt64(value, out long offset))
                        {
                            TraceNonNumeric("timezone", value);
                            break;
                        }
                        // The wire carries it as an unsigned u32, so a negative offset arrives wrapped.
                        if (offset > int.MaxValue) offset -= 4294967296L;
                        char sign = offset < 0 ? '-' : '+';
                        long minutes = Math.Abs(offset) / 60;
                        return $"{sign}{minutes / 60:00}:{minutes % 60:00}";
                    }
                    case "clocktime":
                    {
                        // A time of day: types.clocktime.tostr is interval2string(getTime(val)), getTime being
                        // val % 86400. Measured on 7.24.2: /system/clock time 84043 where the API prints
                        // 23:20:43.
                        if (!WinboxValueFormat.TryToInt64(value, out long tod))
                        {
                            TraceNonNumeric("clocktime", value);
                            break;
                        }
                        return FormatClockTime(tod);
                    }
                    case "interval":
                    {
                        // types.interval.tostr: `enum2string(attrs.values,val) || interval2string(val,
                        // attrs.scale||1)` — a named value wins, otherwise the number is a DURATION in
                        // 1/scale-second units. Without this every interval reached the caller as its raw
                        // count: /ip/dns cache-max-ttl as 604800 where the API prints 1w, and
                        // /system/watchdog ping-start-after-boot as 30000 (scale:100) where it prints 5m.
                        // webfig's own '1d 02:03:04' rendering is not used — our contract is the text
                        // RouterOS's API prints for the same field.
                        if (!WinboxValueFormat.TryToInt64(value, out long ticks))
                        {
                            TraceNonNumeric("interval", value);
                            break;
                        }
                        if (jf.EnumMap != null
                            && jf.EnumMap.TryGetValue(unchecked((int)ticks), out var named))
                            return named;
                        return FormatDuration(ticks, jf.Scale);
                    }
                    case "macaddr":
                        return WinboxValueFormat.MacFromBytes(value);
                    case "ip6addr":
                        return value is byte[] v6 ? WinboxValueFormat.IpV6FromBytes(v6) : value.ToString()!; // value != null (checked above)
                    case "addr":
                        // The compound webfig 'addr' object, read back the way types.addr.tostr renders it:
                        // by SUB-KEY, not by position. The generic nested-message fallback returns the
                        // first member, which is right only when IPv4 is the one present.
                        if (value is Dictionary<int, Tuple<string, object>> addrMsg)
                            return FormatAddr(addrMsg, collectRefTables);
                        break;
                    case WinboxFieldResolver.NumberNetmaskUiType:
                    {
                        // connection-limit=10,24: the number and the netmask's prefix length, as the API prints it.
                        if (!WinboxValueFormat.TryToInt64(value, out long number)) break;
                        string limitText = number.ToString(CultureInfo.InvariantCulture);
                        return rec.TryGetValue(jf.MaskKey, out var mt) && mt?.Item2 != null
                            ? limitText + "," + WinboxValueFormat.MaskToPrefix(mt.Item2).ToString(CultureInfo.InvariantCulture)
                            : limitText;
                    }
                    case "numberrange":
                    {
                        // webfig types.numberrange.tostr: the low end alone when both ends agree, else low-high —
                        // the API's packet-size=100-200. The high end rides MaskKey (WinboxJgCatalog.HighKeyOf).
                        if (jf.MaskKey == 0 || !WinboxValueFormat.TryToInt64(value, out long low)) break;
                        long high = rec.TryGetValue(jf.MaskKey, out var ht) && ht?.Item2 != null
                                    && WinboxValueFormat.TryToInt64(ht.Item2, out long h) ? h : low;
                        return high == low ? low.ToString(CultureInfo.InvariantCulture)
                            : low.ToString(CultureInfo.InvariantCulture) + "-" + high.ToString(CultureInfo.InvariantCulture);
                    }
                    case "multinumberrange":
                    case "numberrangelist":
                    {
                        // u32[] of flat [lo0,hi0,lo1,hi1,…] (webfig multinumberrange / numberrangelist) → "lo"
                        // when lo==hi, else "lo-hi", comma-joined — the RouterOS API form (e.g. "10,20-30").
                        // A not-wrapped field renders the RouterOS '!' negation prefix (e.g. firewall "!80").
                        string list = FormatNumberRangeList(value);
                        bool negated = jf.NotKey != 0 && rec.TryGetValue(jf.NotKey, out var nt)
                            && nt.Item2 is bool nb && nb;
                        return negated ? "!" + list : list;
                    }
                    // A `multitristate` is a multibits whose members can each be NEGATED, and the
                    // negation rides in a SECOND key: types.multitristate.get reads bmap = id|maskid and
                    // reports each set bit as negated when it is absent from `id` — so `id` holds the plain
                    // members and `maskid` the '!' ones. /ip/firewall/filter's tcp-flags="syn,!ack" is
                    // u56=2 (syn) plus u57=16 (ack); reading `id` alone printed the bare number 2.
                    case "multitristate":
                    {
                        if (jf.EnumMap == null || jf.MaskKey == 0) break;
                        if (!WinboxValueFormat.TryToInt64(value, out long onBits))
                        {
                            TraceNonNumeric("multitristate", value);
                            break;
                        }
                        long offBits = 0;
                        if (rec.TryGetValue(jf.MaskKey, out var maskT) && maskT?.Item2 != null
                            && WinboxValueFormat.TryToInt64(maskT.Item2, out long mb))
                            offBits = mb;
                        // Plain members first, then the negated ones, each in bit order — which is how the
                        // router itself prints a mixed list: tcp-flags="!fin,syn,!urg,ack" reads back
                        // "syn,ack,!fin,!urg" (verified on 7.24), not in the order it was written.
                        //
                        // A member in BOTH halves is printed twice, as the API prints it: a text set rewrites only
                        // the half it names, so syn,!ack + set !syn leaves u56=2 and u57=2, which the API prints
                        // "syn,!syn" (7.24.5). Webfig's own getter shows the bit once; that hid one of the two.
                        var plain = jf.EnumMap.Where(kv => (onBits & (1L << kv.Key)) != 0)
                                              .OrderBy(kv => kv.Key).Select(kv => kv.Value);
                        var negated = jf.EnumMap.Where(kv => (offBits & (1L << kv.Key)) != 0)
                                                .OrderBy(kv => kv.Key).Select(kv => "!" + kv.Value);
                        string members = string.Join(",", plain.Concat(negated));
                        // The field-wide 'not' flag is the whole-list '!', which the API spells as a bare leading
                        // element: tcp-flags="!,ack,!syn" for 0xD3=true (7.24.5). Negated() leaves per-member lists
                        // alone, so it is rendered here or not at all.
                        bool whole = jf.NotKey != 0 && rec.TryGetValue(jf.NotKey, out var notT) && notT?.Item2 is bool wn && wn;
                        return whole && members.Length > 0 ? "!," + members : members;
                    }
                    // A `multibits` is a `set` under another name: types.multibits.get is
                    // `for(i=0..31) if(val&(1<<i)) push(i)` over the same bit-indexed map, and only its
                    // EDITOR differs (a list of checkboxes rather than one field). It has an EnumMap, so it
                    // was falling through to the SCALAR enum branch and reading the whole bitmask as one
                    // member: /ip/neighbor's system-caps of 0 — no bits, which the API prints as nothing —
                    // came back as 'other', the member the map happens to hold at index 0.
                    case "multibits":
                    case "set":
                    {
                        // Bitmask flag set → comma-joined labels (.jg map key = bit index). The opt/not flag
                        // keys are consumed separately in DecodeRecord, so only the value rides here. A set
                        // 'not' flag (key NotKey) renders as the RouterOS '!' negation prefix on the whole
                        // value (CLI/API form, e.g. "!established,related").
                        //
                        // A set whose members come from a TABLE instead of a static map is the same bitmask
                        // with the member names one round trip away: /user/group's policies, a script's or a
                        // scheduler entry's policy, all reading the policy table [13,3] where the bit index
                        // is the row's id. Without this they reached the caller as the raw number 654958.
                        var members = jf.EnumMap;
                        if (members == null && jf.RefHandler != null)
                        {
                            members = ResolveMemberMap(jf.RefHandler, collectRefTables);
                            if (members != null) return FormatBitSet(jf, value, members, rec);
                        }
                        if (jf.EnumMap == null) break;
                        if (!WinboxValueFormat.TryToInt64(value, out long bits))
                        {
                            // Falls through to the raw wire text, which is visibly not a label list — but a
                            // silent fall-through is how a .jg/router mismatch goes unnoticed for a release,
                            // so say so on the trace channel.
                            TraceNonNumeric("set", value);
                            break;
                        }
                        var present = jf.EnumMap.Where(kv => (bits & (1L << kv.Key)) != 0);
                        var labels = (SetPrintedDescending.Contains(jf.ApiName ?? "")
                                ? present.OrderByDescending(kv => kv.Key)
                                : SetPrintedBySpeed.Contains(jf.ApiName ?? "")
                                    ? present.OrderBy(kv => LinkSpeedMbps(kv.Value)).ThenBy(kv => kv.Key)
                                    : present.OrderBy(kv => kv.Key))
                            .Select(kv => kv.Value);
                        string joined = string.Join(",", labels);
                        bool negated = jf.NotKey != 0 && rec.TryGetValue(jf.NotKey, out var nt)
                            && nt.Item2 is bool nb && nb;
                        return negated ? "!" + joined : joined;
                    }
                }
                // A list whose elements may each be negated (webfig 'multitristatearray', today only
                // /system/logging 'topics'): the plain members ride in this key and the negated ones in the
                // OffKey sibling, and the API prints them as one comma-joined list with a '!' on the negated
                // ones. Without this the field reached the caller as the raw handle list "[1]".
                if (jf.OffKey != 0 && IsTriStateList(jf.UiType))
                {
                    string on = FormatListElements(jf, value, collectRefTables);
                    rec.TryGetValue(jf.OffKey, out var offT);
                    string off = FormatListElements(jf, offT?.Item2, collectRefTables);
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(on)) parts.Add(on);
                    if (!string.IsNullOrEmpty(off))
                        parts.AddRange(off.Split(',').Select(t => "!" + t));
                    if (parts.Count > 0) return string.Join(",", parts);
                }
                // A LIST of dynamic-enum references (webfig 'multinumber' whose element type is an enm): the
                // value is a u32[] of referenced ids, which the API renders as their comma-joined names —
                // e.g. the log's topics u32[9,3] → "script,error". Falls back to the raw text when the
                // referenced table cannot be read, exactly as the scalar case does.
                if (jf.RefHandler != null && IsMultiNumberList(jf.UiType))
                {
                    string? joined = ResolveRefNameList(jf.RefHandler, value, collectRefTables);
                    if (joined != null) return joined;
                }
                // The same list shape with LITERAL elements — a number (/ip/proxy 'Port' u32[8080] → "8080")
                // or a static enum (/ip/ssh 'Ciphers' u32[0] → "auto"). Without this the wire form reached the
                // caller verbatim as "[8080]"/"[0]"; the API prints one value per element, comma-joined.
                if (IsMultiNumberList(jf.UiType))
                    return FormatNumberList(value, jf.EnumMap, jf.ElementScale);
                // Every OTHER member of the webfig list family. types.multistring/multiipaddr/multiip6addr/
                // multiraw all `inherit(types.multinumber)`, and types.multi.tostr renders each element
                // through the ELEMENT's own type — so the list's own name says nothing about how an element
                // reads, and none of these had a case at all: an empty one reached the caller as the literal
                // "[]" where the API prints nothing, and /ip/dns dynamic-servers as raw u32s.
                // webfig types.multinetwork (and multimacnetwork, which inherits it): a list of PAIRS, in
                // two parallel arrays when the field has a maskid and flattened into one when it has not.
                if (IsMultiNetworkList(jf.UiType))
                    return FormatMultiNetwork(jf, value, rec);
                if (IsMultiList(jf.UiType))
                    return FormatMultiList(jf, value, collectRefTables);
                // A scalar tuple: one field the API prints as its parts joined by the .jg's own separator.
                // The parts live in the ROW rather than in a nested element, which is the only difference
                // from a list element of the same shape — hence the same formatter, given the row.
                if (jf.ElementParts != null
                    && string.Equals(jf.UiType, WinboxJgCatalog.TupleUiType, StringComparison.OrdinalIgnoreCase))
                    return FormatCompoundElement(jf, rec, collectRefTables);
                // dynamic enum reference: render the referenced object's name (e.g. interface id → "ether1").
                if (jf.RefHandler != null)
                {
                    // Each table the dropdown draws from, in order (webfig enm.pair.toString). In the collecting
                    // pass every lookup answers null, so the loop notes down all of them.
                    string? name = null;
                    foreach (int[] table in jf.RefHandlers ?? new[] { jf.RefHandler })
                    {
                        name = ResolveRefName(table, value, collectRefTables);
                        if (name != null) break;
                    }
                    if (name != null) return name;
                    // No row answers to it, and no static member names it either — which is what an
                    // all-ones "not set" looks like on a reference the .jg wraps in no defenum. RouterOS
                    // prints the raw id token there rather than a word: /caps-man/provisioning's
                    // master-configuration reads *FFFFFFFF, not 4294967295 and not 'none'. A reference that
                    // DOES have a static member for the value (see §32.6) is answered by that member below.
                    if (collectRefTables == null
                        && (jf.EnumMap == null
                            || !(WinboxValueFormat.TryToInt64(value, out long rv)
                                 && jf.EnumMap.ContainsKey(unchecked((int)rv))))
                        && WinboxValueFormat.TryToInt64(value, out long idv))
                        return FormatId(idv);
                }
                // static enum: map the numeric value back to its API string label.
                if (jf.EnumMap != null)
                {
                    if (WinboxValueFormat.TryToInt64(value, out long ev))
                    {
                        if (jf.EnumMap.TryGetValue(unchecked((int)ev), out var label))
                            return PrintedMember(jf.ApiName, label);
                        // The map missed, so the value is a plain number — and a `postfix:'s'` says what
                        // KIND of number. webfig renders it bare and paints the unit next to the box; the
                        // API prints a duration. /ip/ipsec/profile dpd-interval, whose only member is
                        // 'disable-dpd' at 0, read as 8 where the API says 8s.
                        if (jf.IsSecondsPostfix) return FormatDuration(ev, jf.Scale);
                        // …or the ELEMENT says what kind: /system/scheduler start-time is
                        // {enm,map:{4294967295:'startup'},c:[{type:'clocktime'}]}, and a time of day read as
                        // 84032 where the API prints 23:20:32 (7.24.2).
                        if (string.Equals(jf.ElementUiType, "clocktime", StringComparison.OrdinalIgnoreCase))
                            return FormatClockTime(ev);
                        // A number the map has no member for. On an opt field that means "not set" and the
                        // field never reaches here (IsUnsetField drops it); here it means the .jg and the
                        // router disagree about the domain, which webfig itself calls 'unknown'. Fall through
                        // to the raw text as before, but say so — a silent fall-through is how exactly this
                        // class of mismatch survived a release.
                        TraceUnmappedEnum(jf, ev);
                    }
                    else TraceNonNumeric("enum", value);   // falls through to the raw text, see above
                }
            }
            // A number the window writes in hexadecimal. webfig renders the digits BARE and paints 'hex'
            // beside the box; RouterOS prints the 0x as part of the value, in lower case
            // (/interface/bridge priority set to 0xA000 reads back 0xa000). The 0x is taken from the
            // declaration rather than assumed: a scalar says `postfix:'hex'`, and a hex part inside a
            // compound says nothing, because there the 0x belongs to the tuple's own prefix.
            if (jf != null && value != null && jf.Radix == 16
                && WinboxValueFormat.TryToInt64(value, out long hx))
                return FormatRadix(hx, jf.Radix, jf.IsHexPostfix);

            // A plain number whose postfix says seconds or milliseconds is a duration too — nothing above
            // catches it, because the .jg types it `number` and only the unit beside the box says otherwise.
            // Seconds, measured on 7.24.2: /system/watchdog ping-timeout reads 60 on the wire where the API
            // prints 1m, and /queue/tree burst-time 0 where it prints 0s.
            if (jf != null && value != null
                && (jf.IsMillisecondsPostfix || (jf.IsSecondsPostfix && !IsPrintedBare(jf)))
                && string.Equals(jf.UiType, "number", StringComparison.OrdinalIgnoreCase)
                && WinboxValueFormat.TryToInt64(value, out long ms))
                return FormatDuration(ms, jf.TicksPerSecond, jf.DurationZeroUnit);

            return FormatValue(wireType, value);
        }

        // webfig list types whose ELEMENT is a reference (types.multinumber and everything inheriting it).
        // multinumberrange/numberrangelist are ranges of literal numbers, not references, and are formatted
        // before this point.
        /// <summary>
        /// Set fields whose API text runs from the HIGHEST <c>.jg</c> bit down. Everything else, and anything
        /// not listed, runs upwards — which is both webfig's own order (<c>types.set.tostr</c> loops
        /// <c>i = 0..31</c>) and RouterOS's for most fields.
        /// </summary>
        /// <remarks>
        /// <para>The order is a real property of the value, not an accident of how it was written: setting
        /// <c>authentication=mschap2,pap</c> reads back as <c>pap,mschap2</c>, so RouterOS NORMALISES rather
        /// than preserving insertion order. That is what makes the order reproducible at all — had the API
        /// echoed what was written, a bitmask on the wire could not have carried the information.</para>
        /// <para>What it is not is derivable. WinBox and RouterOS simply number these two fields in opposite
        /// directions (WinBox lists the strongest first, RouterOS the weakest), and nothing in the
        /// <c>.jg</c>, in <c>master*.js</c> or in tab completion says which — completion answers
        /// <c>chap mschap1 mschap2 pap</c>, alphabetical, matching neither. So the direction is measured, one
        /// field at a time, against the router:</para>
        /// <list type="bullet">
        /// <item><c>dh-group</c> written as <c>modp768,modp1024,modp2048,ecp256,x25519</c> reads back
        /// <c>x25519,ecp256,modp2048,modp1024,modp768</c> — bits 22,19,14,2,1, strictly down.</item>
        /// <item><c>authentication</c> written as all four reads back <c>pap,chap,mschap1,mschap2</c> —
        /// bits 4,3,2,1, strictly down.</item>
        /// <item>The control: <c>connection-state</c> written as <c>new,untracked,invalid,related,established</c>
        /// reads back <c>invalid,established,related,new,untracked</c> — bits 0,1,2,3,8, strictly UP. This is
        /// why the direction cannot simply be flipped for every set field.</item>
        /// </list>
        /// <para>A field that is not listed keeps the ascending order it has always had, so a wrong entry can
        /// only affect the field it names, and the path-map audit compares every one of them against the API.</para>
        /// </remarks>
        private static readonly HashSet<string> SetPrintedDescending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "authentication", "dh-group",
                // Measured the same way, on the stock rows of a 7.24 router: /ip/ipsec/profile prints its
                // enc-algorithm as "aes-128,3des" where the window's bits run 3des,aes-128, and
                // /ip/ipsec/proposal prints "aes-256-cbc,aes-192-cbc,aes-128-cbc" where they run the other
                // way. Strongest first on both, as with dh-group.
                //
                // Spelled as the WINDOW names them, not as the API does — this set is looked up by the .jg
                // field's own name, which is why the two entries below read 'encryption-algorithm' and
                // 'encr-algorithms' rather than the API's 'enc-algorithm'/'enc-algorithms'. The first two
                // entries hide the distinction by being spelled the same on both sides.
                "encryption-algorithm", "encr-algorithms",
            };

        /// <summary>
        /// Set fields RouterOS prints slowest link mode first, whatever bit the window gives each mode.
        /// </summary>
        /// <remarks>
        /// RouterOS 6's ethernet <c>advertise</c> (a <c>set</c> there; 7.x's is a list and keeps its own order):
        /// the window numbers 10G as bit 12 and 2.5G/5G as bits 30/31, and 6.49.13 prints
        /// <c>…,1000M-full,2500M-full,5000M-full,10000M-full</c>. Within one speed the bit order stands (half
        /// before full).
        /// </remarks>
        private static readonly HashSet<string> SetPrintedBySpeed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "advertise" };

        // The leading quantity of a link-mode member in Mbit/s ('2500M-full' → 2500, '10G-full' → 10000);
        // a member with none sorts last.
        private static double LinkSpeedMbps(string member)
        {
            int i = 0;
            while (i < member.Length && (char.IsDigit(member[i]) || member[i] == '.')) i++;
            if (i == 0 || !double.TryParse(member.Substring(0, i), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double n))
                return double.MaxValue;
            char unit = i < member.Length ? char.ToUpperInvariant(member[i]) : 'M';
            return unit == 'G' ? n * 1000 : n;
        }

        /// <summary>
        /// Fields where RouterOS spells a zero as a WORD, and the <c>.jg</c> gives that word nowhere: the
        /// number is simply outside the field's declared domain (<c>MRRU</c> declares <c>min:1500</c> with
        /// <c>def:0</c>), so there is no member to map and no sentinel <c>def</c> to recognise.
        /// </summary>
        /// <remarks>
        /// <para>Every word here is the ROUTER's own, not a name chosen here. Three came from its tab
        /// completion, which completes each of these to exactly one value and nothing else:
        /// <c>set mrru=</c> → <c>mrru=disabled</c>, <c>set max-sessions=</c> → <c>max-sessions=unlimited</c>,
        /// <c>set ddns-update-interval=</c> → <c>ddns-update-interval=none</c>. <c>watch-address</c> completes
        /// to nothing (it is a free-form address), so its word comes from the API's own print of the row the
        /// wire carries as <c>0.0.0.0</c>.</para>
        /// <para>Keyed by FIELD NAME rather than by path on purpose: these are RouterOS-wide vocabulary, and
        /// an <c>mrru</c> means the same thing on all four PPP server menus. Gated on
        /// <see cref="WinboxJgField.IsOptional"/> so it cannot fire on some unrelated non-optional field
        /// that happens to share a label, and on the value being exactly zero — a real count is untouched.</para>
        /// </remarks>
        private static readonly Dictionary<string, string> ZeroSpelledAsWord =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mrru"]                  = "disabled",
                ["max-sessions"]          = "unlimited",
                ["ddns-update-interval"]  = "none",
                ["watch-address"]         = "none",
                // An MTU of zero is no MTU: every window declaring it says min 32 or 64, so the value is
                // outside the field's own domain and RouterOS prints the word instead. Bridges and the
                // EoIP/GRE/IPIP tunnels all read 0 on a stock row and the API prints 'auto' for each.
                ["mtu"]                   = "auto",
                // A bridge port's split horizon: 0 is not horizon zero, it is no horizon.
                ["horizon"]               = "none",
                // Same again for a DHCP relay's delay threshold, where 0 formatted as the interval '0s'.
                ["delay-threshold"]       = "none",
                // An interface's ARP timeout: 0 is 'auto'. Measured on 7.24.2 on an ethernet row: 30s rides
                // as 30, and arp-timeout=0s is accepted but stored as auto — the API reads it back as auto
                // and the key as 0 — so a zero here can never be a real zero-second timeout.
                ["arp-timeout"]           = "auto",
            };

        /// <summary>
        /// The write side of <see cref="ZeroSpelledAsWord"/>: whether <paramref name="value"/> is the word this
        /// field's zero is printed as, so an encoder sends 0 for it instead of refusing a non-number.
        /// </summary>
        internal static bool IsZeroWord(WinboxJgField jf, string value)
        {
            if ((!jf.IsOptional && jf.Min <= 0) || jf.ApiName == null) return false;
            return ZeroSpelledAsWord.TryGetValue(jf.ApiName, out string? word)
                && string.Equals(word, value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryZeroWord(WinboxJgField jf, object value, out string? word)
        {
            word = null;
            // Either the window says the value may be absent, or it declares a minimum that excludes zero:
            // both are the window saying a zero here is not a quantity. /interface's own MTU declaration is
            // the second kind — no `opt:1`, but `min:64` — and without it the generic menu went on reporting
            // 0 for a bridge row the API prints `auto` for, while every subtype menu had been corrected.
            if ((!jf.IsOptional && jf.Min <= 0) || jf.ApiName == null) return false;
            if (!ZeroSpelledAsWord.TryGetValue(jf.ApiName, out word)) { word = null; return false; }
            if (WinboxValueFormat.TryToInt64(value, out long n) && n == 0) return true;
            word = null;
            return false;
        }

        /// <summary>
        /// Fields where the all-ones unset marker is a VALUE RouterOS prints as a word, and the <c>.jg</c> names
        /// it nowhere — so <see cref="WinboxJgField.IsUnsetValue"/> would drop a field the API reports.
        /// </summary>
        /// <remarks>
        /// Measured on 7.24.2. <c>/caps-man/manager</c>'s Certificate and CA Certificate declare
        /// <c>def:4294967295</c> with a <c>defenum</c> naming only 0 (<c>auto</c>): the stock singleton carries
        /// 4294967295 on both and the API prints <c>none</c>; certificate=auto moved each key to 0.
        /// <c>/certificate</c>'s Trust Store is a <c>set</c> with the same default, and both stock certificates
        /// carry it while the API prints <c>trust-store=all</c>. Keyed by field name, gated on the field
        /// declaring the marker as its default.
        /// </remarks>
        private static readonly Dictionary<string, string> SentinelSpelledAsWord =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certificate"]    = "none",
                ["ca-certificate"] = "none",
                ["trust-store"]    = "all",
                // A logging action's Syslog Severity: the stock remote action carries the marker and the API
                // prints `auto` for it — on 6.49.13 always, on 7.24 once remote-log-format=syslog (the
                // window's on:'bsd' condition, which 7.24's API applies to the field and 6.49.13's does not;
                // native applies it to none of the remote pane's fields, syslog-facility included).
                ["syslog-severity"] = "auto",
                // The 6.49.13 OSPF instance's BGP and Other OSPF metrics (the API's metric-bgp,
                // metric-other-ospf): the stock instance carries the marker on 0xD and 0x12, the API prints auto.
                ["bgp-routes-metric"]        = "auto",
                ["other-ospf-routes-metric"] = "auto",
            };

        private static bool TrySentinelWord(WinboxJgField? jf, object value, out string? word)
        {
            word = null;
            if (jf?.ApiName == null || jf.Def != WinboxJgField.UnsetSentinel) return false;
            if (!WinboxValueFormat.TryToInt64(value, out long n) || n != WinboxJgField.UnsetSentinel) return false;
            return SentinelSpelledAsWord.TryGetValue(jf.ApiName, out word);
        }

        /// <summary>
        /// The write side of <see cref="SentinelSpelledAsWord"/>: whether <paramref name="value"/> is the word this
        /// field's unset marker is printed as, so an encoder sends 4294967295 for it.
        /// </summary>
        internal static bool IsSentinelWord(WinboxJgField jf, string value)
            => jf.ApiName != null && jf.Def == WinboxJgField.UnsetSentinel
               && SentinelSpelledAsWord.TryGetValue(jf.ApiName, out string? word)
               && string.Equals(word, value, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Members RouterOS ACCEPTS under the catalog's word but PRINTS as something else — here, as
        /// nothing at all.
        /// </summary>
        /// <remarks>
        /// Distinct from <c>WinboxFieldResolver.AliasEnumMembers</c>, which replaces a member's word in
        /// BOTH directions because the catalog's is one the router would refuse (<c>ip-encap</c>). This one
        /// is a print-only difference and must not reach the write side: <c>/interface/vrrp</c> completes
        /// <c>group-authority=</c> to an empty token, <c>none</c> and <c>self</c>, and setting either of the
        /// first two lands the same 0 on the wire — so <c>none</c> is a spelling the router takes and never
        /// gives back.
        /// </remarks>
        private static readonly Dictionary<string, Dictionary<string, string>> MemberPrintedAs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["group-authority"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["none"] = "",
                },
            };

        private static string PrintedMember(string? apiName, string label)
            => apiName != null && MemberPrintedAs.TryGetValue(apiName, out var table)
               && table.TryGetValue(label, out string? printed) && printed != null
                ? printed : label;

        /// <summary>
        /// True when the wire put a STRING where the field is declared numeric, or a number where it is
        /// declared a string. Only that contradiction: every other pairing (u32 against u64, a bool read as
        /// a number) is a width or a rendering question, not a different kind of thing, and the router is
        /// entitled to it.
        /// </summary>
        // (name key, kind id) → the kind's name, learned from the router's own rows (see WinboxJgField.KindIdKey).
        private readonly Dictionary<long, string> _kindWords = new Dictionary<long, string>();
        private readonly object _kindWordsLock = new object();

        private static long KindWordKey(int nameKey, long kindId) => ((long)nameKey << 32) ^ (kindId & 0xFFFFFFFFL);

        private void Learn(WinboxJgField jf, Dictionary<int, Tuple<string, object>> rec, object value)
        {
            if (!(value is string word) || word.Length == 0) return;
            if (!rec.TryGetValue(jf.KindIdKey, out var id) || !WinboxValueFormat.TryToInt64(id.Item2, out long kindId)) return;
            lock (_kindWordsLock) _kindWords[KindWordKey(jf.Key, kindId)] = word;
        }

        private bool TryLearnedKindWord(WinboxJgField jf, Dictionary<int, Tuple<string, object>> rec, out string? word)
        {
            word = null;
            if (jf.KindIdKey == 0 || !rec.TryGetValue(jf.KindIdKey, out var id)
                || !WinboxValueFormat.TryToInt64(id.Item2, out long kindId)) return false;
            lock (_kindWordsLock) return _kindWords.TryGetValue(KindWordKey(jf.Key, kindId), out word);
        }

        /// <summary>
        /// Learns every kind name a batch of records carries before any of them is decoded, so a row whose name
        /// key holds something else (an enslaved interface's master id) is named by its kind id even when its
        /// peers come later in the same answer.
        /// </summary>
        internal void LearnKindWords(IEnumerable<Dictionary<int, Tuple<string, object>>> records,
            IReadOnlyDictionary<int, WinboxJgField>? keyToField)
        {
            if (keyToField == null) return;
            WinboxJgField? kindField = null;
            foreach (var f in keyToField.Values)
                if (f != null && f.KindIdKey != 0) { kindField = f; break; }
            if (kindField == null) return;
            foreach (var rec in records)
                if (rec.TryGetValue(kindField.Key, out var v))
                    Learn(kindField, rec, v.Item2);
        }

        private static bool IsWireKindContradiction(WinboxJgField? jf, string wireType)
        {
            if (jf == null || jf.WireType == null) return false;
            bool fieldIsText = IsTextWire(jf.WireType);
            bool wireIsText = IsTextWire(wireType);
            if (fieldIsText == wireIsText) return false;
            // Only a scalar declaration says anything this strong: a list, a message or an address is
            // carried in shapes this comparison does not model.
            return IsScalarKind(jf.WireType) && IsScalarKind(wireType);
        }

        private static bool IsTextWire(string wireType)
            => string.Equals(wireType, "string", StringComparison.OrdinalIgnoreCase);

        private static bool IsScalarKind(string wireType)
        {
            switch ((wireType ?? "").ToLowerInvariant())
            {
                case "string":
                case "u32":
                case "u64":
                case "u8":
                    return true;
                default:
                    return false;
            }
        }

        // The postfixes acted on: the two that mean TIME. 'min', 'MHz', '%' and the rest are units the API
        // spells the same way WinBox does (a bare number), so appending them would invent text the router
        // never prints — but a duration RouterOS does spell out, and in its own compound form.
        //
        // 'ms' was on the wrong side of that line until a seeded audit could see a bonding row: webfig paints
        // `100` beside a box labelled ms, and the API prints `100ms`. Not an appended unit either — setting
        // mii-interval=1500ms reads back `1s500ms`, so the value is a DURATION, in milliseconds.
        /// <summary>
        /// Fields declared <c>{number,postfix:'s'}</c> that the API nonetheless prints as the bare number.
        /// </summary>
        /// <remarks>
        /// The declaration cannot tell them apart: <c>/queue/type</c>'s Perturb is <c>{number,def:5,postfix:'s'}</c>
        /// like the watchdog's Ping Timeout, and the API prints <c>sfq-perturb=5</c> against
        /// <c>ping-timeout=1m</c> (7.24.2). Measured the other way on the same router: ipv6 nd ra-delay 3s,
        /// ra-lifetime 30m, neighbor age 28s, queue tree burst-time 0s. Keyed by the name the API reports.
        /// </remarks>
        private static readonly HashSet<string> SecondsPrintedBare =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sfq-perturb" };

        private static bool IsPrintedBare(WinboxJgField jf)
            => jf.ApiName != null
               && (SecondsPrintedBare.Contains(jf.ApiName)
                   || (jf.PaneKind != null && SecondsPrintedBare.Contains(WinboxFieldResolver.PrefixWithKind(jf.PaneKind, jf.ApiName))));

        // webfig interval2string(val % 86400) for a time of day — always HH:MM:SS, as the API prints it.
        private static string FormatClockTime(long secondsOfDay)
        {
            long s = ((secondsOfDay % 86400) + 86400) % 86400;
            return (s / 3600).ToString("00", CultureInfo.InvariantCulture) + ":"
                 + (s / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":"
                 + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // webfig `types.multibignumber = inherit(types.multinumber)` — the same list, elements eight bytes
        // wide instead of four. Both its element types (`bignumber`, `bigbitrate`) sit on types.number, so an
        // element renders exactly as the scalar of the same type does: as the number itself.
        private static bool IsMultiNumberList(string? uiType)
            => string.Equals(uiType, "multinumber", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uiType, "multibignumber", StringComparison.OrdinalIgnoreCase);

        // The rest of the webfig list family, taken from the inheritance chain in master*.js:
        //   types.multiipaddr = types.multiip6addr = types.multistring = types.multiraw
        //       = inherit(types.multinumber) = inherit(types.multi)
        // plus the plain `multi` whose elements are nested messages. Deliberately NOT multibits (a bitmask
        // that inherits types.multi directly), nor multinumberrange/numberrangelist/multitristatearray,
        // which have their own shapes and are formatted before this point.
        private static bool IsMultiList(string? uiType)
        {
            if (uiType == null) return false;
            switch (uiType.ToLowerInvariant())
            {
                case "multi":
                case "multistring":
                case "multiraw":
                case "multiipaddr":
                case "multiip6addr":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Renders a webfig list field the way <c>types.multi.tostr</c> does — every element through the
        /// ELEMENT type's own <c>tostr</c>, comma-joined — with the empty list rendering empty.
        /// </summary>
        /// <remarks>
        /// Verified against the API on 7.23.2: a <c>/ppp/profile</c> with no address list prints
        /// <c>address-list=</c> (we sent the literal <c>[]</c>), <c>/tool/romon</c> the same for
        /// <c>secrets</c>, and <c>/ip/dns</c> prints <c>dynamic-servers=192.0.2.1,198.51.100.7,…</c> where
        /// the elements are <c>addr</c> compounds we were rendering as their raw u32
        /// (<c>16908480,124007366,…</c>).
        /// </remarks>
        private string FormatMultiList(WinboxJgField jf, object value,
            Dictionary<string, int[]>? collectRefTables)
        {
            // A message array (webfig `multi`): each element is a submessage of the element's own type.
            if (value is List<Dictionary<int, Tuple<string, object>>> msgs)
                return string.Join(",", msgs.Select(m =>
                    jf.ElementParts != null
                        ? FormatCompoundElement(jf, m, collectRefTables)
                        : string.Equals(jf.ElementUiType, "addr", StringComparison.OrdinalIgnoreCase)
                            ? FormatAddr(m, collectRefTables)
                            : FormatNestedMessage(m)));

            // A scalar array, which M2Message renders as the text "[a,b,…]" (and "[]" when empty).
            var elements = SplitListElements(value);
            if (elements.Count == 0) return "";
            return string.Join(",", elements.Select(e => FormatListElement(jf.ElementUiType, e, jf.ElementScale)));
        }

        // webfig list types whose element is an address PAIR: types.multinetwork and the two that inherit it,
        // types.multimacnetwork and types.multinetwork6.
        private static bool IsMultiNetworkList(string? uiType)
            => string.Equals(uiType, "multinetwork", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiType, "multimacnetwork", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiType, "multinetwork6", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Renders a webfig <c>multinetwork</c> / <c>multimacnetwork</c> list: pairs of (address, sibling),
        /// each rendered the way the scalar of the element's type renders one.
        /// </summary>
        /// <remarks>
        /// <para>Where the pairs live is the FIELD's business and what the second half means is the
        /// ELEMENT's. With a <c>maskid</c> the halves ride in two parallel arrays, one entry per element;
        /// without one <c>types.multinetwork</c> is a plain <c>multinumberrange</c> and the pairs are
        /// flattened into a single <c>[a0,b0,a1,b1,…]</c> array. The second half is the range END when the
        /// element declares <c>range:1</c> and a netmask otherwise, exactly as <c>types.network.tostr</c>
        /// reads <c>attrs.range</c> for a scalar.</para>
        /// <para>Live on 7.24: <c>/ip/pool</c>'s <c>ranges</c> is the flattened, ranged form — a pool of
        /// <c>192.168.251.10-192.168.251.20,192.168.252.5,192.168.253.0/24</c> arrives as six u32s — and the
        /// API re-collapses an exact block to its prefix form, which <see cref="WinboxValueFormat.
        /// FormatV4Range"/> does too. Without this the whole field reached the caller as the raw
        /// <c>[184264896,352037056,…]</c>.</para>
        /// </remarks>
        private string FormatMultiNetwork(WinboxJgField jf, object value,
            Dictionary<int, Tuple<string, object>> rec)
        {
            var first = SplitListElements(value);
            List<string>? second = null;
            if (jf.MaskKey != 0)
            {
                if (!rec.TryGetValue(jf.MaskKey, out var maskT) || maskT?.Item2 == null) return "";
                second = SplitListElements(maskT.Item2);
            }
            bool isMac = string.Equals(jf.UiType, "multimacnetwork", StringComparison.OrdinalIgnoreCase);
            bool isV6 = string.Equals(jf.UiType, "multinetwork6", StringComparison.OrdinalIgnoreCase);
            var rendered = new List<string>();
            int count = second != null ? first.Count : first.Count / 2;
            for (int i = 0; i < count; i++)
            {
                string a = second != null ? first[i] : first[i * 2];
                string b = second != null ? (i < second.Count ? second[i] : "") : first[i * 2 + 1];
                if (isMac)
                {
                    string mac = WinboxValueFormat.MacFromBytes(a);
                    rendered.Add(b.Length > 0 ? mac + "/" + WinboxValueFormat.MacFromBytes(b) : mac);
                    continue;
                }
                if (isV6)
                {
                    // The IPv6 pair, by the same rule the scalar network6 part follows: the second half is
                    // the PREFIX LENGTH itself rather than a mask, and it is printed at every length,
                    // /128 included — RouterOS prints what webfig's tostr hides.
                    byte[]? raw = HexToBytes(a);
                    string addr6 = raw != null ? WinboxValueFormat.IpV6FromBytes(raw) : a;
                    rendered.Add(long.TryParse(b, out long plen)
                        ? addr6 + "/" + plen.ToString(CultureInfo.InvariantCulture)
                        : addr6);
                    continue;
                }
                if (!uint.TryParse(a, out uint addr)) { rendered.Add(a); continue; }
                if (!uint.TryParse(b, out uint sibling)) { rendered.Add(WinboxValueFormat.IpFromU32(addr)); continue; }
                rendered.Add(jf.ElementIsRange
                    ? WinboxValueFormat.FormatV4Range(addr, sibling)
                    : WinboxValueFormat.IpFromU32(addr) + "/" + WinboxValueFormat.MaskToPrefix(sibling));
            }
            return string.Join(",", rendered);
        }

        /// <summary>
        /// One element of a list whose element is a <c>tuple</c> or a <c>union</c>: each part rendered
        /// through its own type and joined by the tuple's separator, exactly as <c>types.tuple.tostr</c>
        /// does — including its rule that a part rendering EMPTY contributes no separator.
        /// </summary>
        /// <remarks>
        /// A <c>network</c> part's sibling holds a NETMASK and a <c>network6</c> part's holds the PREFIX
        /// LENGTH itself (<c>types.network6.tostr</c>: <c>addr + '/' + (val[1]||0)</c>) — which is why the
        /// two cannot share one formatter. Both live shapes on 7.23.2:
        /// <c>/snmp/community</c>'s one row is the IPv6 any-address (<c>a16</c> = 16 zero bytes,
        /// <c>u17 = 0</c>) which the API prints <c>::/0</c>, and <c>/certificate</c>'s subject-alt-name is
        /// <c>{u7f = 1, u7d = 3959728320}</c> which it prints <c>IP:192.168.4.236</c>.
        /// </remarks>
        private string FormatCompoundElement(WinboxJgField jf, Dictionary<int, Tuple<string, object>> element,
            Dictionary<string, int[]>? collectRefTables)
        {
            var rendered = new List<string>();
            // Only called when jf.ElementParts != null (see FormatMultiList's caller check).
            foreach (var part in jf.ElementParts!)
            {
                string s = FormatElementPart(part, element, collectRefTables);
                if (!string.IsNullOrEmpty(s)) rendered.Add(s);
            }
            // Nothing addressable in the element — webfig's union.get returns [def,null] here. Fall back to
            // the generic dump rather than inventing a value.
            if (rendered.Count == 0) return FormatNestedMessage(element);
            // A `not`-wrapped element negates THIS element alone: types.not.tostr renders
            // (flag ? '!' : '') + the inner type's own text, which is the '!' RouterOS prints in front of one
            // entry of /tool/sniffer's filter lists.
            string prefix = jf.ElementNotKey != 0 && element.TryGetValue(jf.ElementNotKey, out var nf)
                            && nf?.Item2 is bool nb && nb ? "!" : "";
            // …and a compound's own `prefix` goes in front of the whole joined value (the STP ids' '0x').
            return prefix + (jf.Prefix ?? "") + string.Join(jf.ElementSeparator ?? "", rendered);
        }

        // One element's status, in declaration order, joined by spaces. A prefix stands when its own leaf, or a
        // name:'' leaf continuing it, is present — 6.49.13 prints "192.168.4.1 reachable via  ether1"
        // for {u1, u5=1, U8=[1]} (no U6: the via-addresses are empty, the interfaces are not) and
        // "ether1 reachable" for a connected route's {u3, u5=1}, which has neither.
        private string FormatElementStatus(IReadOnlyList<WinboxJgElementPart> parts,
            Dictionary<int, Tuple<string, object>> element, Dictionary<string, int[]>? collectRefTables)
        {
            var tokens = new List<string>();
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                bool present = element.TryGetValue(part.Key, out var v) && v?.Item2 != null;
                string text = "";
                if (present)
                {
                    // A list leaf arrives as "[a,b]" — "[]" when empty, which is no word at all.
                    var items = SplitListElements(v!.Item2);
                    bool isList = v.Item1.EndsWith("[]", StringComparison.Ordinal)
                        || (v.Item2 is string sv && sv.StartsWith("[", StringComparison.Ordinal));
                    IEnumerable<string> words = items.Count > 0 || isList ? items : new List<string> { v.Item2.ToString()! };
                    text = string.Join(",", words.Select(w => FormatStatusWord(part, w, collectRefTables)));
                }
                if (part.Prefix != null)
                {
                    if (present || parts.Skip(i + 1).TakeWhile(p => p.Continues).Any(p => element.ContainsKey(p.Key)))
                        tokens.Add(part.Prefix + text);
                }
                else if (present) tokens.Add(text);
            }
            return tokens.Count == 0 ? "" : " " + string.Join(" ", tokens);
        }

        private string FormatStatusWord(WinboxJgElementPart part, string word, Dictionary<string, int[]>? collectRefTables)
        {
            if (part.EnumMap != null && WinboxValueFormat.TryToInt64(word, out long ev)
                && part.EnumMap.TryGetValue(unchecked((int)ev), out var label))
                return label;
            if (part.RefHandler != null)
                return ResolveRefName(part.RefHandler, word, collectRefTables) ?? word;
            if (string.Equals(part.UiType, "multiipaddr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part.UiType, "ipaddr", StringComparison.OrdinalIgnoreCase))
                return WinboxValueFormat.IpFromU32(word);
            return word;
        }

        private string FormatElementPart(WinboxJgElementPart part, Dictionary<int, Tuple<string, object>> element,
            Dictionary<string, int[]>? collectRefTables)
        {
            if (part.Alternatives != null)
            {
                foreach (var alt in part.Alternatives)
                {
                    string s = FormatElementPart(alt, element, collectRefTables);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                return "";
            }
            if (!element.TryGetValue(part.Key, out var v) || v?.Item2 == null) return "";
            object? mask = part.MaskKey != 0 && element.TryGetValue(part.MaskKey, out var mt) ? mt?.Item2 : null;
            // A part with a static map is an enum wherever it sits — the API prints the word, not the index.
            if (part.EnumMap != null && WinboxValueFormat.TryToInt64(v.Item2, out long ev)
                && part.EnumMap.TryGetValue(unchecked((int)ev), out var label))
                return label;
            // …and a part with a DYNAMIC dropdown is a reference wherever it sits: /snmp's trap-interfaces
            // is a list whose element is one interface id, which the API prints as the interface's name.
            if (part.RefHandler != null)
            {
                string? refName = ResolveRefName(part.RefHandler, v.Item2, collectRefTables);
                if (refName != null) return refName;
            }
            // A hex part renders its digits bare: the 0x in front of an STP identifier belongs to the
            // compound, and only its FIRST part is hexadecimal — port-id is 0x80.1, not 0x80.0x1.
            if (part.Radix == 16 && WinboxValueFormat.TryToInt64(v.Item2, out long ph))
                return FormatRadix(ph, part.Radix, withPrefix: false);
            switch ((part.UiType ?? "").ToLowerInvariant())
            {
                case "interval":
                    // The wire carries a count of seconds; the API spells it "5m". A part carries no scale
                    // of its own, and no live element declares one.
                    return WinboxValueFormat.TryToInt64(v.Item2, out long ticks)
                        ? FormatDuration(ticks, 1)
                        : v.Item2.ToString()!;
                case "network":
                    return WinboxValueFormat.IpFromU32(v.Item2)
                         + (mask != null ? "/" + WinboxValueFormat.MaskToPrefix(mask) : "");
                case "network6":
                {
                    // The sibling holds the PREFIX LENGTH itself, not a mask — there is no 128-bit mask to
                    // put in a u32 — and the length is printed at every length INCLUDING 128. webfig's own
                    // tostr hides it there (`if(val[1]==128)return addr`), but that is the GUI's display
                    // rule, not the API's text: a /snmp/community scoped to one host reads back
                    // "2001:db8:3::7/128" on 7.24, and this codec answers in the API's text. Same divergence
                    // as `macnetwork`, whose all-ones mask webfig hides and RouterOS prints.
                    string addr = v.Item2 is byte[] b6 ? WinboxValueFormat.IpV6FromBytes(b6) : "::";
                    if (mask == null) return addr;
                    long len = WinboxValueFormat.TryToInt64(mask, out long n) ? n : 0;
                    return addr + "/" + len.ToString(CultureInfo.InvariantCulture);
                }
                case "ipaddr":
                    return WinboxValueFormat.IpFromU32(v.Item2);
                case "ip6addr":
                    return v.Item2 is byte[] b ? WinboxValueFormat.IpV6FromBytes(b) : v.Item2.ToString()!; // v.Item2 != null (checked above)
                case "macaddr":
                    return WinboxValueFormat.MacFromBytes(v.Item2);
                case "macnetwork":
                    // Six address bytes and six mask bytes. RouterOS prints the mask even when it is the
                    // all-ones one (filter-mac-address reads back "AA:BB:CC:DD:EE:FF/FF:FF:FF:FF:FF:FF"),
                    // where webfig's own tostr hides it - the API text is what this codec answers in.
                    return WinboxValueFormat.MacFromBytes(v.Item2)
                         + (mask != null ? "/" + WinboxValueFormat.MacFromBytes(mask) : "");
                case "addr":
                    return v.Item2 is Dictionary<int, Tuple<string, object>> a ? FormatAddr(a, collectRefTables) : v.Item2.ToString()!; // v.Item2 != null (checked above)
                default:
                    return v.Item2.ToString()!; // v.Item2 != null (checked above)
            }
        }

        // One element of a scalar list, by the element's .jg type. An ipaddr element rides as the same u32 a
        // scalar ipaddr does, and a macaddr/ip6addr element as the same bytes — which M2Message renders as
        // hex, exactly as it does for the scalar forms. A string/raw/secret element is already its own text.
        private static string FormatListElement(string? elementUiType, string element, int elementScale = 1)
        {
            switch ((elementUiType ?? "").ToLowerInvariant())
            {
                case "fixedpoint":
                {
                    // The element's own scale, not the field's: /caps-man/channel's frequency is a
                    // multinumber of {fixedpoint,scale:1000}, so the wire's 2412000 is the API's 2412. The
                    // list carried the raw count until a WRITE probe on an empty field made it visible —
                    // no read could, because a stock row has no frequency to compare.
                    if (elementScale <= 1
                        || !long.TryParse(element, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                          out long fp))
                        return element;
                    return FormatScaled(fp, elementScale);
                }
                case "ipaddr":
                    return uint.TryParse(element, out uint u) ? WinboxValueFormat.IpFromU32(u) : element;
                case "macaddr":
                    return WinboxValueFormat.MacFromBytes(element);
                case "ip6addr":
                {
                    byte[]? raw = HexToBytes(element);
                    return raw != null ? WinboxValueFormat.IpV6FromBytes(raw) : element;
                }
                default:
                    return element;
            }
        }

        /// <summary>
        /// The firewall's limit (<c>rate[/period],burst:mode</c>) or dst-limit (<c>rate[/period],burst,limit-by/expire</c>)
        /// from the boxes of its WinBox group, a box the record leaves out read as the window's default.
        /// </summary>
        private static string FormatFirewallLimit(WinboxJgField jf, object rate, Dictionary<int, Tuple<string, object>> rec)
        {
            long Box(int key, long def)
                => rec.TryGetValue(key, out var t) && WinboxValueFormat.TryToInt64(t.Item2, out long n) ? n : def;
            bool dst = jf.UiType == WinboxFieldResolver.FirewallDstLimitUiType;
            WinboxValueFormat.TryToInt64(rate, out long r);
            long period = Box(dst ? 0x39 : 0x11, 1);
            string text = r.ToString(CultureInfo.InvariantCulture)
                        + (period == 1 ? "" : "/" + FormatDuration(period, 1));
            if (!dst)
                return text + "," + Box(0x12, 5).ToString(CultureInfo.InvariantCulture)
                            + ":" + (Box(0x86, 0) == 1 ? "bit" : "packet");
            long by = Box(0x38, 1);
            string byText = WinboxFieldResolver.FirewallDstLimitBy.TryGetValue(unchecked((int)by), out var word)
                ? word : by.ToString(CultureInfo.InvariantCulture);
            return text + "," + Box(0x37, 5).ToString(CultureInfo.InvariantCulture) + "," + byText
                 + "/" + FormatDuration(Box(0x3B, 1000), 100);
        }

        // "[a,b,c]" → [a,b,c]; "[]" → empty. The brackets are M2Message's rendering of an array, not part of
        // any element, and an element is never itself comma-bearing on the wire.
        private static List<string> SplitListElements(object value)
        {
            string s = value?.ToString() ?? "";
            if (s.Length >= 2 && s[0] == '[' && s[s.Length - 1] == ']')
                s = s.Substring(1, s.Length - 2);
            if (s.Length == 0) return new List<string>();
            return s.Split(',').Select(p => p.Trim()).ToList();
        }

        /// <summary>
        /// Renders a bit set whose members come from a referenced table, in the form the router prints:
        /// the members whose bit is set, then the ones its <c>maskid</c> sibling marks as explicitly
        /// denied with a <c>!</c> in front — <c>/user/group</c>'s
        /// <c>policy="read,write,!local,!telnet,…"</c>.
        /// </summary>
        /// <remarks>
        /// Verified against 7.24: <c>/user/group add policy=read,write</c> over the binary API stores
        /// <c>u2 = 192</c> (bits 6 and 7) and <c>u3 = 655166</c> — every OTHER member of the table. So the
        /// mask is not "the complement of 32 bits" but "the members this row says no to".
        /// </remarks>
        private static string FormatBitSet(WinboxJgField jf, object value,
            IReadOnlyDictionary<int, string> members, Dictionary<int, Tuple<string, object>> rec)
        {
            if (!WinboxValueFormat.TryToInt64(value, out long onBits)) return value?.ToString() ?? "";
            long offBits = 0;
            if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var maskT) && maskT?.Item2 != null
                && WinboxValueFormat.TryToInt64(maskT.Item2, out long mb))
                offBits = mb;

            var plain = members.Where(kv => (onBits & (1L << kv.Key)) != 0)
                               .OrderBy(kv => kv.Key).Select(kv => kv.Value);
            var negated = members.Where(kv => (onBits & (1L << kv.Key)) == 0
                                           && (offBits & (1L << kv.Key)) != 0)
                                 .OrderBy(kv => kv.Key).Select(kv => "!" + kv.Value);
            return string.Join(",", plain.Concat(negated));
        }

        // The bitmask UI types whose maskid sibling holds the NEGATED members rather than a value of its own.
        private static bool IsBitSetWithMask(string? uiType)
        {
            switch ((uiType ?? "").ToLowerInvariant())
            {
                case "multitristate":
                case "multibits":
                case "set":
                    return true;
                default:
                    return false;
            }
        }

        // The one list type whose elements carry their own negation flag, so it rides on TWO keys.
        private static bool IsTriStateList(string? uiType)
            => string.Equals(uiType, "multitristatearray", StringComparison.OrdinalIgnoreCase);

        // One half of a tri-state list, rendered by the same rules a multinumber element is: a reference
        // resolves to the referenced record's name, a static map to its label, anything else to the number.
        private string FormatListElements(WinboxJgField jf, object? value,
            Dictionary<string, int[]>? collectRefTables)
        {
            if (value == null) return "";
            if (jf.RefHandler != null)
            {
                string? joined = ResolveRefNameList(jf.RefHandler, value, collectRefTables);
                if (joined != null) return joined;
            }
            return FormatNumberList(value, jf.EnumMap);
        }

        // Resolve a u32[] of referenced ids (rendered by M2Message as "[a,b,…]") to comma-joined names.
        // Returns null when nothing resolved, so the caller can fall back to the raw text rather than
        // hand back an empty string that reads like "no topics".
        private string? ResolveRefNameList(int[] refHandler, object? value,
            Dictionary<string, int[]>? collectRefTables)
        {
            var names = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(value?.ToString() ?? "", @"-?\d+"))
            {
                string? n = ResolveRefName(refHandler, m.Value, collectRefTables);
                if (n == null) return null;   // table unreadable / id unknown — keep the raw form
                names.Add(n);
            }
            return names.Count > 0 ? string.Join(",", names) : null;
        }

        // RouterOS .id is the "*HEX" handle form. The M2 record id is a numeric u8/u32.
        private static string FormatId(object value)
        {
            if (value == null) return "*0";
            if (WinboxValueFormat.TryToInt64(value, out long id) && id >= 0)
                return WinboxRecordId.Format((ulong)id);
            // Handing back the raw text produces an .id that is not a RouterOS handle at all, so every
            // later set/remove addressed by it will fail — noisily, but a long way from here. Trace it.
            TraceNonNumeric(".id", value);
            return value.ToString()!; // value != null (checked above)
        }

        // One place for "the router sent something this decode step cannot read as a number". None of the
        // callers fabricate a value — they fall back to the raw wire text — but a fall-back nobody can see is
        // how a .jg/router mismatch survives a release.
        internal static void TraceNonNumeric(string what, object value)
        {
            if (!TikWireTrace.Enabled) return;
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                $"{what} value '{value}' ({value?.GetType().Name ?? "null"}) is not numeric, left as raw text");
        }

        // A numeric enum value the .jg map has no member for, on a field that is NOT opt (where it would mean
        // "not set"). The catalog and the router disagree about this field's domain — usually a RouterOS
        // upgrade against a stale cached .jg.
        private static void TraceUnmappedEnum(WinboxJgField jf, long value)
        {
            if (!TikWireTrace.Enabled) return;
            // Only called from the "static enum" branch, entered when jf.EnumMap != null.
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                $"enum '{jf.ApiName}' (key 0x{jf.Key:X}) value {value} has no member in the .jg map "
                + $"[{string.Join(",", jf.EnumMap!.Keys)}], left as raw text");
        }

        // A key whose wire value contradicts the field's declared kind — a string where a number is declared
        // or the reverse. The row is not carrying this field, and a silent drop is how a wrong value gets
        // reported for a release, so say so.
        private static void TraceWireKindContradiction(WinboxJgField jf, string wireType)
        {
            if (!TikWireTrace.Enabled) return;
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                $"'{jf.ApiName}' (key 0x{jf.Key:X}) is declared {jf.WireType} and the row carries "
                + $"{wireType}; not reported on this row");
        }

        /// <summary>
        /// Renders a webfig <c>addr</c> compound (a nested message with one member per address form) as the
        /// RouterOS text, following <c>types.addr.tostr</c>: IPv6 wins over IPv4 when both are present, then
        /// the DNS name, then the MAC, with the <c>/prefix</c> appended.
        /// </summary>
        private string FormatAddr(Dictionary<int, Tuple<string, object>> addr,
            Dictionary<string, int[]>? collectRefTables)
        {
            object? Get(int subKey) =>
                addr.TryGetValue(subKey, out var t) ? t?.Item2 : null;

            // The '%iface' qualifier names a row of the interface table, exactly as a dynamic enum does, so
            // it is resolved the same way. It is not only a suffix: a value whose ONLY member is the
            // interface IS that interface — a connected route's gateway is `ether1`, with no address at all.
            // Without this the compound fell through to FormatNestedMessage and rendered the family tag
            // (ufeff1f=9, 'this is an interface') as the whole value, so every connected route's gateway
            // read as the literal '9'.
            string? iface = Get(WinboxFieldResolver.AddrIfaceSubKey) is object ifid
                ? ResolveRefName(WinboxFieldResolver.AddrIfaceRefHandler, ifid, collectRefTables)
                : null;

            string? text = null;
            if (Get(WinboxFieldResolver.AddrV6SubKey) is byte[] v6) text = WinboxValueFormat.IpV6FromBytes(v6);
            else if (Get(WinboxFieldResolver.AddrV4SubKey) is object v4) text = WinboxValueFormat.IpFromU32(v4);
            else if (Get(WinboxFieldResolver.AddrDnsSubKey) is object dns) text = dns.ToString();
            else if (Get(WinboxFieldResolver.AddrMacSubKey) is object mac) text = WinboxValueFormat.MacFromBytes(mac);
            if (text == null) return iface ?? FormatNestedMessage(addr);

            if (Get(WinboxFieldResolver.AddrPrefixSubKey) is object plen) text += "/" + plen;
            if (iface != null) text += "%" + iface;
            return text;
        }

        private static string FormatValue(string wireType, object? value)
        {
            if (value == null) return "";
            if (wireType == "bool") return (value is bool b && b) ? "true" : "false";
            // An FT_ADDR6 value arrives as 16 bytes; without this it renders as "System.Byte[]" whenever the
            // .jg does not also give the field an ip6addr UI type.
            if (wireType == "ip6" && value is byte[] v6) return WinboxValueFormat.IpV6FromBytes(v6);
            if (value is Dictionary<int, Tuple<string, object>> one) return FormatNestedMessage(one);
            if (value is List<Dictionary<int, Tuple<string, object>>> many)
                return string.Join(",", many.Select(FormatNestedMessage));
            return value.ToString()!; // value != null (checked above)
        }

        /// <summary>
        /// Renders a nested M2 submessage (a webfig <c>multi</c> element, or a wrapper such as the traceroute
        /// window's per-hop <c>Host</c>) as its first present inner value.
        /// </summary>
        /// <remarks>
        /// This is what webfig itself does, not a guess: <c>types.multi.tostr</c> walks the array and renders
        /// each element through its single child type, and <c>types.union.get</c> with <c>single:1</c> returns
        /// the first child that is present. Without it the value falls through to <c>object.ToString()</c> and
        /// the caller is handed the literal text
        /// <c>System.Collections.Generic.List`1[System.Collections.Generic.Dictionary`2[…]]</c> as if it were
        /// the field's value — which a nested field on any handler can do, not just traceroute. An element the
        /// router sent empty renders empty, which is the honest answer.
        /// </remarks>
        private static string FormatNestedMessage(Dictionary<int, Tuple<string, object>> msg)
        {
            if (msg == null || msg.Count == 0) return "";
            foreach (var kv in msg.OrderBy(k => k.Key))
            {
                if (kv.Value?.Item2 == null) continue;
                return FormatValue(kv.Value.Item1, kv.Value.Item2);
            }
            return "";
        }

        // Render a webfig 'multinumber' value (a u32[] parsed by M2Message to the text "[a,b,…]") as the
        // comma-joined API form: each element through the element's static enum map when it has one, else the
        // number itself. An element the map does not name stays numeric rather than being dropped — a shorter
        // list would read as "the router has fewer of these", a wrong answer that looks like data.
        private static string FormatNumberList(object? value, IReadOnlyDictionary<int, string>? enumMap,
            int elementScale = 1)
        {
            var parts = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(value?.ToString() ?? "", @"-?\d+"))
            {
                // long, then unchecked to int — the same two steps the SCALAR enum path takes, and the
                // reason matters: a u32 member above int.MaxValue (0xFFFFFFFF is a real member on several
                // maps — 'passthrough' on eap-methods, 'all' on traffic-flow interfaces) does not parse as
                // an int at all, so the lookup never ran and the raw number reached the caller.
                if (enumMap != null && long.TryParse(m.Value, out long n)
                    && enumMap.TryGetValue(unchecked((int)n), out var label))
                    parts.Add(label);
                // The ELEMENT's own scale, which is not the field's: /caps-man/channel's frequency is a
                // multinumber of {fixedpoint,scale:1000}, so the wire's 2412000 is the API's 2412. It read
                // as the raw count until a WRITE probe made it visible — no read could, because a stock row
                // carries no frequency to compare.
                else if (elementScale > 1 && long.TryParse(m.Value, NumberStyles.Integer,
                                                           CultureInfo.InvariantCulture, out long scaled))
                    parts.Add(FormatScaled(scaled, elementScale));
                else
                    parts.Add(m.Value);
            }
            return string.Join(",", parts);
        }

        // Render a webfig multinumberrange value (a u32[] parsed by M2Message to the text "[lo0,hi0,lo1,hi1,…]")
        // back to the RouterOS API form: each [lo,hi] pair becomes "lo" (when lo==hi) or "lo-hi", comma-joined.
        private static string FormatNumberRangeList(object value)
        {
            var nums = new List<int>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(value?.ToString() ?? "", @"-?\d+"))
                if (int.TryParse(m.Value, out int n)) nums.Add(n);
            if (nums.Count == 0) return "";

            var parts = new List<string>();
            int i = 0;
            for (; i + 1 < nums.Count; i += 2)
                parts.Add(nums[i] == nums[i + 1] ? nums[i].ToString() : nums[i] + "-" + nums[i + 1]);
            if (i < nums.Count) parts.Add(nums[i].ToString()); // odd trailing value (defensive)
            return string.Join(",", parts);
        }
    }
}
