using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// Translates decoded WinBox M2 records (numeric <c>key → (wireType, value)</c>) into RouterOS API field
    /// dictionaries (<c>apiName → stringValue</c>), applying the version-matched <c>.jg</c> UI-semantic types
    /// (IPs, networks, MACs, static enums, dynamic enum references). Pure decode logic split out of
    /// <c>WinboxNativeConnection</c> so it can be reasoned about and unit-tested without the connection.
    /// Depends only on the M2 operations channel (for dynamic-reference name lookups) and the <c>.jg</c> catalog.
    /// </summary>
    internal sealed class WinboxRecordCodec
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog;

        // id → name cache per referenced table, built lazily from one getall. Names are stable enough within a
        // session; this avoids a getall per referenced field per row.
        private readonly Dictionary<string, Dictionary<int, string>> _refNameCache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        // Guards the cache: a multiplexed connection can decode two commands' rows at once, and
        // PrimeReferencesAsync writes from the awaited path while a synchronous decode may be reading.
        private readonly object _refNameCacheLock = new object();

        // The router's uptime clock, the origin every `age` value counts from. Handler and key are webfig's
        // own (fetchBoardInfo posts a get-singleton to [24,2] and reads u1).
        private static readonly int[] BoardInfoHandler = { 24, 2 };
        private const int BoardUptimeKey = 0x1;

        private readonly object _uptimeLock = new object();
        private long? _uptimeAtFetch;
        // Set together with _uptimeAtFetch in RouterUptimeSeconds; only read there, guarded by
        // _uptimeAtFetch.HasValue, so it is never read before that first assignment.
        private Stopwatch _sinceUptimeFetch = null!;
        private bool _uptimeUnreadable;

        // The Unix epoch, the origin every dateandtime/clockdate value on the wire counts from.
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        internal WinboxRecordCodec(WinboxNativeM2Operations ops, WinboxJgCatalog catalog)
        {
            _ops = ops;
            _catalog = catalog;
        }

        /// <summary>
        /// Translates a decoded M2 record (<c>key → (wireType, value)</c>) into a RouterOS API field
        /// dictionary (<c>apiName → stringValue</c>). Unknown keys are dropped; <c>.id</c> is emitted as
        /// the RouterOS <c>*HEX</c> handle form so it round-trips through the O/R mapper.
        /// </summary>
        internal Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName,
            IReadOnlyDictionary<int, WinboxJgField> keyToField,
            IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null)
            => DecodeRecord(rec, keyToName, keyToField, null, derivedBools);

        // The decode proper. collectRefTables != null puts it in COLLECTING mode: reference names are not
        // looked up, the tables they would have needed are noted instead, and the fields it returns are
        // thrown away. See PrimeReferencesAsync — that mode is how the awaited path learns what to fetch.
        private Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName,
            IReadOnlyDictionary<int, WinboxJgField> keyToField,
            Dictionary<string, int[]>? collectRefTables,
            IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null)
        {
            // Keys consumed by an owning field, not emitted on their own: a network field's netmask sibling,
            // and the opt/not flag bools of an optional/invertible field (its value rides on the leaf key).
            var consumedKeys = new HashSet<int>();
            if (keyToField != null)
                foreach (var f in keyToField.Values)
                {
                    if (f.UiType == "network" && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
                    // The port half of an address:port field is not a field of its own to the API.
                    if (f.UiType == WinboxFieldResolver.AddrPortUiType && f.MaskKey != 0)
                        consumedKeys.Add(f.MaskKey);
                    // Nor is the download half of an upload/download pair.
                    if (f.UiType == WinboxFieldResolver.PairUiType && f.MaskKey != 0)
                        consumedKeys.Add(f.MaskKey);
                    // Nor are the negated members of a multitristate — or of a `set`/`multibits` that has a
                    // maskid: they are the '!' half of the ONE value the API prints (tcp-flags="syn,!ack",
                    // policy="read,write,!ftp"), not a field the router names.
                    if (f.MaskKey != 0 && IsBitSetWithMask(f.UiType))
                        consumedKeys.Add(f.MaskKey);
                    // Nor is the parallel array of masks/range ends of a multinetwork: it holds the second
                    // half of every element of the ONE list the API prints.
                    if (f.MaskKey != 0 && IsMultiNetworkList(f.UiType))
                        consumedKeys.Add(f.MaskKey);
                    if (f.OptKey != 0) consumedKeys.Add(f.OptKey);
                    if (f.NotKey != 0) consumedKeys.Add(f.NotKey);
                }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rec)
            {
                // A record may carry one key TWICE — a scalar and a list (see M2Message.ParseAllFields) — so
                // ask the arrayness-qualified registration first and fall back to the plain one. The fallback
                // is the normal path: a window with no such collision registers no qualified key, and a router
                // that sends a field with the arrayness the .jg did NOT predict still decodes as it always did.
                int wireKey = WinboxM2Protocol.TypedKey.WireKeyOf(kv.Key);
                int typedKey = WinboxM2Protocol.TypedKey.Qualify(
                    wireKey, WinboxM2Protocol.TypedKey.IsArrayType(kv.Value.Item1));
                if (consumedKeys.Contains(wireKey)) continue;
                if (!keyToName.TryGetValue(typedKey, out var apiName)
                    && !keyToName.TryGetValue(wireKey, out apiName)) continue;
                if (fields.ContainsKey(apiName)) continue;

                if (apiName == TikSpecialProperties.Id)
                {
                    fields[apiName] = FormatId(kv.Value.Item2);
                    continue;
                }

                WinboxJgField? jf = null;
                if (keyToField != null && !keyToField.TryGetValue(typedKey, out jf))
                    keyToField.TryGetValue(wireKey, out jf);
                if (IsUnsetField(jf, kv.Value.Item2, rec)) continue;
                fields[apiName] = FormatTyped(jf, kv.Value.Item1, kv.Value.Item2, rec, collectRefTables);
            }

            // A bool the API reports and WinBox renders as a wider enum on the same wire field (see
            // FieldAliasSet.DerivedBools). Added AFTER the loop because it reads a decoded field, not a key,
            // and only when the source is actually present: a row that did not carry it gets no bool rather
            // than a false, which would be us asserting something the router did not say.
            if (derivedBools != null)
                foreach (var d in derivedBools)
                {
                    if (fields.ContainsKey(d.Key)) continue;
                    if (!fields.TryGetValue(d.Value.Item1, out string? src)) continue;
                    fields[d.Key] = string.Equals(src, d.Value.Item2, StringComparison.OrdinalIgnoreCase)
                        ? "true" : "false";
                }
            return fields;
        }

        /// <summary>
        /// True when the record carries a key for a field that is NOT SET — which RouterOS's own API answers
        /// by leaving the field out of the row entirely, so emitting anything here would be a value the other
        /// transports do not report.
        /// </summary>
        /// <remarks>
        /// <para>Two ways the router says "not set". An <c>opt</c>-wrapped field has a flag bool that is
        /// <c>false</c> — verified live on 7.23.2: a <c>/ip/proxy/access</c> rule created with only
        /// <c>dst-host</c> and <c>action</c> comes back over the API with no <c>method</c>, <c>src-address</c>,
        /// <c>dst-port</c> or <c>path</c> at all, while the M2 record carries their keys with the flags down.
        /// Decoding those produced <c>method=''</c>, which the O/R mapper then failed to convert to an enum —
        /// the failure was real, but the field should never have been there.</para>
        /// <para>The second is the u32 unset marker on a field that declares it as its default
        /// (<see cref="WinboxJgField.IsUnsetValue"/>): a logging action's <c>Syslog Severity</c> arrives as
        /// 4294967295 on a row where the API prints no <c>syslog-severity</c>.</para>
        /// <para>The third is a static enum with <c>opt:1</c> carrying a value its map has no member for
        /// (<see cref="WinboxJgField.IsUnmappedOptionalEnum"/>) — an unsigned certificate's
        /// <c>digest-algorithm</c> arrives as <c>0</c> where the API prints nothing. This one is webfig's own
        /// rule, read off <c>types.enm.tostr</c>, and it is why the <c>opt</c> ATTRIBUTE has to be carried
        /// separately from the <c>opt</c> WRAPPER's flag key.</para>
        /// </remarks>
        private static bool IsUnsetField(WinboxJgField? jf, object value,
            Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf == null) return false;
            if (jf.OptKey != 0 && rec.TryGetValue(jf.OptKey, out var opt)
                && opt?.Item2 is bool present && !present)
                return true;
            if (IsAnotherKindsField(jf, rec)) return true;
            if (WinboxFieldResolver.TryToInt64(value, out long n))
            {
                if (jf.Def.HasValue && jf.IsUnsetValue(n)) return true;
                if (jf.IsUnmappedOptionalEnum(n)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the field belongs to a <c>deck</c> pane this record is not of — a disk logging action's
        /// file settings on a memory action, a red queue's thresholds on a pcq one.
        /// </summary>
        /// <remarks>
        /// The router sends every pane's keys on every row of the table (a memory action's M2 record carries
        /// both 'Stop on Full' bools, memory's and disk's), while RouterOS's own API reports only the fields
        /// of the record's kind: a <c>memory</c> action prints <c>memory-lines</c> and
        /// <c>memory-stop-on-full</c> and nothing else. Keeping the others would add a dozen fields per row
        /// that no other transport reports — and, for the leftovers of a kind the record is not, values that
        /// mean nothing.
        /// <para>A record that does not carry the selector at all is left alone: without knowing its kind
        /// there is no honest way to say which pane is the live one.</para>
        /// </remarks>
        private static bool IsAnotherKindsField(WinboxJgField jf, Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf.PaneSelectorKey == 0 || jf.PaneValues == null) return false;
            if (!rec.TryGetValue(jf.PaneSelectorKey, out var sel) || sel?.Item2 == null) return false;
            if (!WinboxFieldResolver.TryToInt64(sel.Item2, out long kind)) return false;
            return jf.IsForeignPane(kind);
        }

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
                switch (jf.UiType)
                {
                    case "ipaddr":
                        return WinboxFieldResolver.IpFromU32(value);
                    case "network":
                    {
                        string addr = WinboxFieldResolver.IpFromU32(value);
                        if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var mt) && mt.Item2 != null)
                        {
                            if (jf.IsRange)
                                return WinboxFieldResolver.FormatV4Range(value, mt.Item2);
                            return addr + "/" + WinboxFieldResolver.MaskToPrefix(mt.Item2);
                        }
                        return addr;
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
                        string addr = WinboxFieldResolver.IpFromU32(value);
                        long port = jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var pt)
                                    && WinboxFieldResolver.TryToInt64(pt.Item2, out long p) ? p : 0;
                        return addr + ":" + port.ToString(CultureInfo.InvariantCulture);
                    }
                    case "integer":
                    {
                        // SIGNED, unlike the plain `number` it inherits from: types.integer.get is
                        // `num2int(val)`, and num2int is `v >= 0x80000000 ? v - 0x100000000 : v`. Without it
                        // /system/ntp/client's system-offset read as 4294967276 where the API says a small
                        // negative — a wrong value that looks like a plausible large one.
                        if (!WinboxFieldResolver.TryToInt64(value, out long signed))
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long kib))
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long fp))
                        {
                            TraceNonNumeric("fixedpoint", value);
                            break;
                        }
                        long v = NumToInt(fp);
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long stamp))
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
                    {
                        // Unix epoch seconds. webfig's date2string is
                        // `new Date(val*1000).toISOString().substring(0,10)` — UTC, no local shift — and the
                        // values confirm it exactly: a certificate's 1784975092 is the API's
                        // '2026-07-25 10:24:52' to the second. clockdate is the same value printed as a date
                        // alone (/system/clock 'Date'). Both read as raw second counts before this.
                        if (!WinboxFieldResolver.TryToInt64(value, out long epoch))
                        {
                            TraceNonNumeric(jf.UiType, value);
                            break;
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long offset))
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
                    case "interval":
                    {
                        // types.interval.tostr: `enum2string(attrs.values,val) || interval2string(val,
                        // attrs.scale||1)` — a named value wins, otherwise the number is a DURATION in
                        // 1/scale-second units. Without this every interval reached the caller as its raw
                        // count: /ip/dns cache-max-ttl as 604800 where the API prints 1w, and
                        // /system/watchdog ping-start-after-boot as 30000 (scale:100) where it prints 5m.
                        // webfig's own '1d 02:03:04' rendering is not used — our contract is the text
                        // RouterOS's API prints for the same field.
                        if (!WinboxFieldResolver.TryToInt64(value, out long ticks))
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
                        return WinboxFieldResolver.MacFromBytes(value);
                    case "ip6addr":
                        return value is byte[] v6 ? WinboxFieldResolver.IpV6FromBytes(v6) : value.ToString()!; // value != null (checked above)
                    case "addr":
                        // The compound webfig 'addr' object, read back the way types.addr.tostr renders it:
                        // by SUB-KEY, not by position. The generic nested-message fallback returns the
                        // first member, which is right only when IPv4 is the one present.
                        if (value is Dictionary<int, Tuple<string, object>> addrMsg)
                            return FormatAddr(addrMsg, collectRefTables);
                        break;
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long onBits))
                        {
                            TraceNonNumeric("multitristate", value);
                            break;
                        }
                        long offBits = 0;
                        if (rec.TryGetValue(jf.MaskKey, out var maskT) && maskT?.Item2 != null
                            && WinboxFieldResolver.TryToInt64(maskT.Item2, out long mb))
                            offBits = mb;
                        // Plain members first, then the negated ones, each in bit order — which is how the
                        // router itself prints a mixed list: tcp-flags="!fin,syn,!urg,ack" reads back
                        // "syn,ack,!fin,!urg" (verified on 7.24), not in the order it was written.
                        var plain = jf.EnumMap.Where(kv => (onBits & (1L << kv.Key)) != 0)
                                              .OrderBy(kv => kv.Key).Select(kv => kv.Value);
                        var negated = jf.EnumMap.Where(kv => (onBits & (1L << kv.Key)) == 0
                                                          && (offBits & (1L << kv.Key)) != 0)
                                                .OrderBy(kv => kv.Key).Select(kv => "!" + kv.Value);
                        return string.Join(",", plain.Concat(negated));
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
                        if (!WinboxFieldResolver.TryToInt64(value, out long bits))
                        {
                            // Falls through to the raw wire text, which is visibly not a label list — but a
                            // silent fall-through is how a .jg/router mismatch goes unnoticed for a release
                            // (P2.25), so say so on the trace channel.
                            TraceNonNumeric("set", value);
                            break;
                        }
                        var present = jf.EnumMap.Where(kv => (bits & (1L << kv.Key)) != 0);
                        var labels = (SetPrintedDescending.Contains(jf.ApiName ?? "")
                                ? present.OrderByDescending(kv => kv.Key)
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
                    return FormatNumberList(value, jf.EnumMap);
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
                // dynamic enum reference: render the referenced object's name (e.g. interface id → "ether1").
                if (jf.RefHandler != null)
                {
                    string? name = ResolveRefName(jf.RefHandler, value, collectRefTables);
                    if (name != null) return name;
                }
                // static enum: map the numeric value back to its API string label.
                if (jf.EnumMap != null)
                {
                    if (WinboxFieldResolver.TryToInt64(value, out long ev))
                    {
                        if (jf.EnumMap.TryGetValue(unchecked((int)ev), out var label)) return label;
                        // The map missed, so the value is a plain number — and a `postfix:'s'` says what
                        // KIND of number. webfig renders it bare and paints the unit next to the box; the
                        // API prints a duration. /ip/ipsec/profile dpd-interval, whose only member is
                        // 'disable-dpd' at 0, read as 8 where the API says 8s.
                        if (IsSecondsPostfix(jf.Postfix)) return FormatDuration(ev, jf.Scale);
                        // A number the map has no member for. On an opt field that means "not set" and the
                        // field never reaches here (IsUnsetField drops it); here it means the .jg and the
                        // router disagree about the domain, which webfig itself calls 'unknown'. Fall through
                        // to the raw text as before, but say so — a silent fall-through is how exactly this
                        // class of mismatch survived a release (P2.25).
                        TraceUnmappedEnum(jf, ev);
                    }
                    else TraceNonNumeric("enum", value);   // falls through to the raw text, see above
                }
            }
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
            };

        private static bool TryZeroWord(WinboxJgField jf, object value, out string? word)
        {
            word = null;
            if (!jf.IsOptional || jf.ApiName == null) return false;
            if (!ZeroSpelledAsWord.TryGetValue(jf.ApiName, out word)) { word = null; return false; }
            if (WinboxFieldResolver.TryToInt64(value, out long n) && n == 0) return true;
            word = null;
            return false;
        }

        // The only postfix acted on. 'min', 'MHz', '%' and the rest are units the API spells the same way
        // WinBox does (a bare number), so appending them would invent text the router never prints.
        private static bool IsSecondsPostfix(string? postfix)
            => string.Equals(postfix, "s", StringComparison.Ordinal);

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
        /// <c>secrets</c>, and <c>/ip/dns</c> prints <c>dynamic-servers=192.168.4.1,10.43.94.205,…</c> where
        /// the elements are <c>addr</c> compounds we were rendering as their raw u32
        /// (<c>17082560,3445500682,…</c>).
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
            return string.Join(",", elements.Select(e => FormatListElement(jf.ElementUiType, e)));
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
        /// API re-collapses an exact block to its prefix form, which <see cref="WinboxFieldResolver.
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
                    string mac = WinboxFieldResolver.MacFromBytes(a);
                    rendered.Add(b.Length > 0 ? mac + "/" + WinboxFieldResolver.MacFromBytes(b) : mac);
                    continue;
                }
                if (isV6)
                {
                    // The IPv6 pair, by the same rule the scalar network6 part follows: the second half is
                    // the PREFIX LENGTH itself rather than a mask, and it is printed at every length,
                    // /128 included — RouterOS prints what webfig's tostr hides.
                    byte[]? raw = HexToBytes(a);
                    string addr6 = raw != null ? WinboxFieldResolver.IpV6FromBytes(raw) : a;
                    rendered.Add(long.TryParse(b, out long plen)
                        ? addr6 + "/" + plen.ToString(CultureInfo.InvariantCulture)
                        : addr6);
                    continue;
                }
                if (!uint.TryParse(a, out uint addr)) { rendered.Add(a); continue; }
                if (!uint.TryParse(b, out uint sibling)) { rendered.Add(WinboxFieldResolver.IpFromU32(addr)); continue; }
                rendered.Add(jf.ElementIsRange
                    ? WinboxFieldResolver.FormatV4Range(addr, sibling)
                    : WinboxFieldResolver.IpFromU32(addr) + "/" + WinboxFieldResolver.MaskToPrefix(sibling));
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
            return prefix + string.Join(jf.ElementSeparator ?? "", rendered);
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
            if (part.EnumMap != null && WinboxFieldResolver.TryToInt64(v.Item2, out long ev)
                && part.EnumMap.TryGetValue(unchecked((int)ev), out var label))
                return label;
            // …and a part with a DYNAMIC dropdown is a reference wherever it sits: /snmp's trap-interfaces
            // is a list whose element is one interface id, which the API prints as the interface's name.
            if (part.RefHandler != null)
            {
                string? refName = ResolveRefName(part.RefHandler, v.Item2, collectRefTables);
                if (refName != null) return refName;
            }
            switch ((part.UiType ?? "").ToLowerInvariant())
            {
                case "interval":
                    // The wire carries a count of seconds; the API spells it "5m". A part carries no scale
                    // of its own, and no live element declares one.
                    return WinboxFieldResolver.TryToInt64(v.Item2, out long ticks)
                        ? FormatDuration(ticks, 1)
                        : v.Item2.ToString()!;
                case "network":
                    return WinboxFieldResolver.IpFromU32(v.Item2)
                         + (mask != null ? "/" + WinboxFieldResolver.MaskToPrefix(mask) : "");
                case "network6":
                {
                    // The sibling holds the PREFIX LENGTH itself, not a mask — there is no 128-bit mask to
                    // put in a u32 — and the length is printed at every length INCLUDING 128. webfig's own
                    // tostr hides it there (`if(val[1]==128)return addr`), but that is the GUI's display
                    // rule, not the API's text: a /snmp/community scoped to one host reads back
                    // "2001:db8:3::7/128" on 7.24, and this codec answers in the API's text. Same divergence
                    // as `macnetwork`, whose all-ones mask webfig hides and RouterOS prints.
                    string addr = v.Item2 is byte[] b6 ? WinboxFieldResolver.IpV6FromBytes(b6) : "::";
                    if (mask == null) return addr;
                    long len = WinboxFieldResolver.TryToInt64(mask, out long n) ? n : 0;
                    return addr + "/" + len.ToString(CultureInfo.InvariantCulture);
                }
                case "ipaddr":
                    return WinboxFieldResolver.IpFromU32(v.Item2);
                case "ip6addr":
                    return v.Item2 is byte[] b ? WinboxFieldResolver.IpV6FromBytes(b) : v.Item2.ToString()!; // v.Item2 != null (checked above)
                case "macaddr":
                    return WinboxFieldResolver.MacFromBytes(v.Item2);
                case "macnetwork":
                    // Six address bytes and six mask bytes. RouterOS prints the mask even when it is the
                    // all-ones one (filter-mac-address reads back "AA:BB:CC:DD:EE:FF/FF:FF:FF:FF:FF:FF"),
                    // where webfig's own tostr hides it - the API text is what this codec answers in.
                    return WinboxFieldResolver.MacFromBytes(v.Item2)
                         + (mask != null ? "/" + WinboxFieldResolver.MacFromBytes(mask) : "");
                case "addr":
                    return v.Item2 is Dictionary<int, Tuple<string, object>> a ? FormatAddr(a, collectRefTables) : v.Item2.ToString()!; // v.Item2 != null (checked above)
                default:
                    return v.Item2.ToString()!; // v.Item2 != null (checked above)
            }
        }

        // One element of a scalar list, by the element's .jg type. An ipaddr element rides as the same u32 a
        // scalar ipaddr does, and a macaddr/ip6addr element as the same bytes — which M2Message renders as
        // hex, exactly as it does for the scalar forms. A string/raw/secret element is already its own text.
        private static string FormatListElement(string? elementUiType, string element)
        {
            switch ((elementUiType ?? "").ToLowerInvariant())
            {
                case "ipaddr":
                    return uint.TryParse(element, out uint u) ? WinboxFieldResolver.IpFromU32(u) : element;
                case "macaddr":
                    return WinboxFieldResolver.MacFromBytes(element);
                case "ip6addr":
                {
                    byte[]? raw = HexToBytes(element);
                    return raw != null ? WinboxFieldResolver.IpV6FromBytes(raw) : element;
                }
                default:
                    return element;
            }
        }

        // Hex text (as M2Message renders a raw/addr6 element) back to bytes; null when it is not hex, which
        // leaves the caller with the original text rather than a wrong address.
        private static byte[]? HexToBytes(string hex)
        {
            if (hex.Length == 0 || hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out bytes[i]))
                    return null;
            return bytes;
        }

        /// <summary>
        /// Renders a wire duration as the text RouterOS's own API prints for it: the non-zero units only,
        /// largest first, concatenated without separators — <c>1w</c>, <c>5m</c>, <c>2d17h30m3s</c>,
        /// <c>518w1d18h55m42s</c> — and <c>0s</c> when the whole thing is zero.
        /// </summary>
        /// <param name="ticks">The wire value.</param>
        /// <param name="scale">Wire units per second (the <c>.jg</c> <c>scale</c>, 1 when undeclared).</param>
        /// <remarks>
        /// Deliberately not webfig's <c>interval2string</c>, which renders <c>1d 02:03:04</c>: that is what
        /// the WinBox UI shows, and the API is what every other transport in this library reports. The unit
        /// set and the omit-zeroes rule are read off the API's own output on 7.23.2 (604800 → <c>1w</c>,
        /// 300 → <c>5m</c>, 0 → <c>0s</c>).
        /// </remarks>
        private static string FormatDuration(long ticks, int scale)
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
            // that in milliseconds. Dropping it would report a value the router did not give us.
            if (remainder > 0) sb.Append(remainder * 1000 / scale).Append("ms");

            // Everything was zero — which is a value, and the API prints it as 0s rather than as nothing.
            if (sb.Length == 0 || (negative && sb.Length == 1)) sb.Append("0s");
            return sb.ToString();
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
            if (!WinboxFieldResolver.TryToInt64(value, out long onBits)) return value?.ToString() ?? "";
            long offBits = 0;
            if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var maskT) && maskT?.Item2 != null
                && WinboxFieldResolver.TryToInt64(maskT.Item2, out long mb))
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

        /// <summary>
        /// The router's uptime in seconds, read once per connection and advanced by the local clock — which
        /// is exactly what webfig does (<c>sysres.uptimediff = uptime - getNow()</c>, then
        /// <c>getUptime() = getNow() + uptimediff</c>). Returns <c>null</c> when it cannot be read, so an
        /// <c>age</c> falls back to its raw text rather than to a number computed from a guessed origin.
        /// </summary>
        /// <remarks>
        /// The handler and key are webfig's own: <c>fetchBoardInfo</c> posts a get-singleton to <c>[24,2]</c>
        /// and takes <c>u1</c>. Caching it is not an optimisation but the same design decision webfig made —
        /// an age field appears on many rows of one table, and a round trip per row would make a certificate
        /// list N+1 requests. The drift over a session is the drift of the local clock against the router's,
        /// which for a value printed to the second is irrelevant, and a reconnect re-reads it.
        /// </remarks>
        private long? RouterUptimeSeconds()
        {
            lock (_uptimeLock)
            {
                if (_uptimeAtFetch.HasValue)
                    return _uptimeAtFetch.Value + (long)_sinceUptimeFetch.Elapsed.TotalSeconds;
                if (_uptimeUnreadable) return null;
            }

            long fetched;
            try
            {
                var board = _ops.GetSingleton(BoardInfoHandler);
                if (board == null || !board.TryGetValue(BoardUptimeKey, out var up)
                    || !WinboxFieldResolver.TryToInt64(up.Item2, out fetched))
                {
                    lock (_uptimeLock) _uptimeUnreadable = true;
                    TraceUptimeUnreadable(null);
                    return null;
                }
            }
            catch (Exception ex)
            {
                // Unlike a reference table this IS memoized as unreadable: every age field on the connection
                // would otherwise retry the same singleton, and a handler that has no board info will not
                // grow one mid-session.
                lock (_uptimeLock) _uptimeUnreadable = true;
                TraceUptimeUnreadable(ex);
                return null;
            }

            lock (_uptimeLock)
            {
                _uptimeAtFetch = fetched;
                _sinceUptimeFetch = Stopwatch.StartNew();
                return fetched;
            }
        }

        // webfig's num2int: the wire is unsigned u32, and the types that are signed reinterpret the top half
        // as negative. A value below 0x80000000 is unaffected, which is why only the types whose own
        // get/tostr calls num2int go through this.
        private static long NumToInt(long v)
            => v >= 0x80000000L && v <= 0xFFFFFFFFL ? v - 0x100000000L : v;

        private static void TraceUptimeUnreadable(Exception? ex)
        {
            if (!TikWireTrace.Enabled) return;
            TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                "the board-info singleton [24,2] gave no uptime, so every 'age' field on this connection "
                + "keeps its raw value" + (ex == null ? "" : ": " + ex.Message));
        }

        // Resolve a dynamic-enum reference value (the referenced record's numeric id) back to its name.
        private string? ResolveRefName(int[] refHandler, object idValue, Dictionary<string, int[]>? collectRefTables)
        {
            if (!WinboxFieldResolver.TryToInt64(idValue, out long idl))
            {
                TraceNonNumeric("reference", idValue);
                return null;                       // caller keeps the raw text — never a fabricated name
            }
            int id = unchecked((int)idl);

            string key = string.Join(",", refHandler);
            var map = CachedRefNames(key);
            if (collectRefTables != null)
            {
                // Collecting pass: note the table down (unless it is cached already) and answer nothing. This
                // is the ONLY place that decides a reference needs its table read, so the awaited prefetch and
                // the decode cannot disagree about which tables those are.
                if (map == null) collectRefTables[key] = refHandler;
                return null;
            }
            if (map == null)
            {
                // Blocking: reached when the awaited path did not prime this table — a synchronous monitor
                // round, or a value whose table was still uncached when the collecting pass ran.
                try { map = BuildRefNameMap(refHandler, _ops.GetAll(refHandler)); }
                catch (Exception ex)
                {
                    // The lookup falls back to the numeric id, which reads as a plausible value rather than
                    // as an error — so a single transient getall failure must not be memoized. Leaving the
                    // empty map in the cache turned one hiccup into "every reference on this handler is
                    // numeric for the rest of the connection"; retry on the next value instead.
                    TraceUnreadableRefTable(key, ex);
                    return null;
                }
                StoreRefNames(key, map);
            }
            return map.TryGetValue(id, out var n) ? n : null;
        }

        /// <summary>
        /// Reads, in one awaited pass, every referenced table the given records will actually need, so the
        /// synchronous decode that follows finds each answer in memory instead of blocking on a getall.
        /// </summary>
        /// <remarks>
        /// Which tables those are is decided by running the decoder itself in collecting mode, not by a second
        /// reading of the <c>.jg</c>: a handler routinely declares reference fields whose rows resolve no name
        /// at all — an empty list, a non-numeric value, a UI type that renders before the reference is ever
        /// consulted. Predicting from the field map instead fetched such a table anyway and, because the cache
        /// lives as long as the connection, froze it: a record added later then rendered as its bare numeric id
        /// (caught by <c>AddInterfaceListMemberWillNotFail</c> — printing <c>/interface/list</c>, whose own rows
        /// reference interface lists, poisoned the map for every later read).
        /// <para>A table that cannot be read is left uncached exactly as the blocking path leaves it, so a
        /// transient failure is retried rather than memoized.</para>
        /// </remarks>
        internal async Task PrimeReferencesAsync(
            IEnumerable<Dictionary<int, Tuple<string, object>>> records,
            IReadOnlyDictionary<int, string> keyToName, IReadOnlyDictionary<int, WinboxJgField> keyToField,
            CancellationToken cancellationToken)
        {
            if (records == null || keyToName == null || keyToField == null) return;

            // Cheap exits before the collecting decode: nothing on this handler references anything, or every
            // table it could reference is cached already. Both are the steady state, and neither needs a pass.
            bool anyUncached = false;
            foreach (var jf in keyToField.Values)
                if (jf?.RefHandler != null && CachedRefNames(string.Join(",", jf.RefHandler)) == null)
                { anyUncached = true; break; }
            if (!anyUncached) return;

            var wanted = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (var rec in records)
                DecodeRecord(rec, keyToName, keyToField, wanted);   // result discarded; only the questions matter

            foreach (var kv in wanted)
            {
                Dictionary<int, string> map;
                try
                {
                    var rows = await _ops.GetAllAsync(kv.Value, cancellationToken).ConfigureAwait(false);
                    // The same rows, read as whichever map the key asks for — a dropdown's names or a bit
                    // set's members (see MemberMapSuffix).
                    map = kv.Key.EndsWith(MemberMapSuffix, StringComparison.Ordinal)
                        ? BuildMemberMap(_catalog, kv.Value, rows)
                        : BuildRefNameMap(kv.Value, rows);
                }
                catch (Exception ex)
                {
                    TraceUnreadableRefTable(kv.Key, ex);
                    continue;   // not memoized — the decode that follows falls back to the blocking lookup
                }
                StoreRefNames(kv.Key, map);
            }
        }

        // Build the id → name map of a referenced table from its rows. One place, so the blocking lookup and
        // the awaited prime cannot end up with differently-populated caches.
        private Dictionary<int, string> BuildRefNameMap(
            int[] refHandler, IEnumerable<Dictionary<int, Tuple<string, object>>> rows)
        {
            var refResolver = new WinboxFieldResolver(null, refHandler, _catalog, EmptyOverrides);
            var map = new Dictionary<int, string>();
            int nameKey = NameKeyOf(refResolver.BuildKeyToApiName());
            foreach (var r in rows)
                // TryReadIdAndName only returns true after successfully assigning a non-null name.
                if (TryReadIdAndName(r, nameKey, out int rowId, out string? rowName))
                    map[rowId] = rowName!;
            return map;
        }

        /// <summary>
        /// The M2 key of a table's <c>name</c> field, or -1 when it has none.
        /// </summary>
        internal static int NameKeyOf(IReadOnlyDictionary<int, string> keyToApiName)
        {
            if (keyToApiName != null)
                foreach (var kv in keyToApiName)
                    // Only a WIRE key can be looked up in a record: an arrayness-qualified registration
                    // (WinboxM2Protocol.TypedKey) is the resolver's own bookkeeping and matches nothing.
                    if (kv.Value == "name" && !WinboxM2Protocol.TypedKey.IsQualified(kv.Key)) return kv.Key;
            return -1;
        }

        /// <summary>
        /// Reads a row's numeric record id and its <c>name</c>, the two fields every name ↔ id translation
        /// needs. Returns <c>false</c> when the row carries neither, or an id that is not a number.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT a <c>DecodeRecord</c> — a name and an id need no typed decoding, and running
        /// the full decoder to read them drags in the referenced-table lookups of every OTHER field on the row.
        /// The id lookup then paid round trips for reference tables it never used, and — because the caller
        /// stops at the first matching row — fetched a different set of them depending on where in the table
        /// the match happened to fall, which is not something a lookup's cost should depend on.
        /// <para>A row whose id is not numeric is skipped rather than guessed at, so the value it would have
        /// named stays raw instead of picking up a neighbour's name; traced, because a cached map makes such an
        /// omission permanent and invisible.</para>
        /// </remarks>
        internal static bool TryReadIdAndName(
            Dictionary<int, Tuple<string, object>> row, int nameKey, out int id, out string? name)
        {
            id = 0;
            name = null;
            if (nameKey < 0 || row == null) return false;
            if (!row.TryGetValue(WinboxM2Protocol.RecordKey.Id, out var idt) || idt.Item2 == null) return false;
            if (!row.TryGetValue(nameKey, out var nt) || nt.Item2 == null) return false;
            if (!WinboxFieldResolver.TryToInt64(idt.Item2, out long rowId))
            {
                TraceNonNumeric("reference table id", idt.Item2);
                return false;
            }
            id = unchecked((int)rowId);
            name = nt.Item2.ToString();
            return true;
        }

        /// <summary>
        /// The cache-key suffix under which a referenced table's BIT-SET member map is kept, next to the
        /// ordinary id → name map of the same table.
        /// </summary>
        /// <remarks>
        /// The two are different maps of the same rows. A dropdown shows the row's <c>name</c>; a bit set
        /// shows what the window declares as the row's display value (<c>nameval</c>), and for the policy
        /// table [13,3] those disagree — 'Name' is the sentence "read router configuration" and 'Alias' is
        /// the word the API prints, <c>read</c>.
        /// </remarks>
        private const string MemberMapSuffix = "|members";

        /// <summary>
        /// Builds the member map of a bit set's referenced table: <c>bit index → member name</c>, where the
        /// bit index IS the referenced record's id (webfig's SetView keys its checkboxes by
        /// <c>obj.ufe0001</c> and ORs <c>1&lt;&lt;id</c>).
        /// </summary>
        internal static Dictionary<int, string> BuildMemberMap(WinboxJgCatalog catalog, int[] refHandler,
            IEnumerable<Dictionary<int, Tuple<string, object>>> rows)
        {
            var refResolver = new WinboxFieldResolver(null, refHandler, catalog, EmptyOverrides);
            var keyToName = refResolver.BuildKeyToApiName();
            string? nameVal = catalog.GetNameValField(refHandler);
            int nameKey = -1;
            if (nameVal != null)
                foreach (var kv in keyToName)
                    if (string.Equals(kv.Value, nameVal, StringComparison.OrdinalIgnoreCase)
                        && !WinboxM2Protocol.TypedKey.IsQualified(kv.Key))
                    { nameKey = kv.Key; break; }
            if (nameKey < 0) nameKey = NameKeyOf(keyToName);

            var map = new Dictionary<int, string>();
            foreach (var r in rows)
                if (TryReadIdAndName(r, nameKey, out int rowId, out string? rowName))
                    map[rowId] = rowName!;
            return map;
        }

        /// <summary>
        /// The member map of a bit set whose members come from a TABLE rather than a static <c>.jg</c> map
        /// (<c>/user/group</c> policies, a script's or scheduler entry's policy). Follows
        /// <see cref="ResolveRefName"/>'s collect/cache/blocking structure exactly, so the awaited prefetch
        /// and the decode cannot disagree about which tables get read.
        /// </summary>
        private Dictionary<int, string>? ResolveMemberMap(int[] refHandler,
            Dictionary<string, int[]>? collectRefTables)
        {
            string key = string.Join(",", refHandler) + MemberMapSuffix;
            var map = CachedRefNames(key);
            if (collectRefTables != null)
            {
                if (map == null) collectRefTables[key] = refHandler;
                return null;
            }
            if (map == null)
            {
                try { map = BuildMemberMap(_catalog, refHandler, _ops.GetAll(refHandler)); }
                catch (Exception ex)
                {
                    TraceUnreadableRefTable(key, ex);
                    return null;
                }
                StoreRefNames(key, map);
            }
            return map;
        }

        private Dictionary<int, string>? CachedRefNames(string key)
        {
            lock (_refNameCacheLock)
                return _refNameCache.TryGetValue(key, out var map) ? map : null;
        }

        private void StoreRefNames(string key, Dictionary<int, string> map)
        {
            lock (_refNameCacheLock) _refNameCache[key] = map;
        }

        private static void TraceUnreadableRefTable(string key, Exception ex)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                    "reference table [" + key + "] unreadable, values stay numeric: " + ex.Message);
        }

        // RouterOS .id is the "*HEX" handle form. The M2 record id is a numeric u8/u32.
        private static string FormatId(object value)
        {
            if (value == null) return "*0";
            if (WinboxFieldResolver.TryToInt64(value, out long id) && id >= 0)
                return "*" + ((ulong)id).ToString("X");
            // Handing back the raw text produces an .id that is not a RouterOS handle at all, so every
            // later set/remove addressed by it will fail — noisily, but a long way from here. Trace it.
            TraceNonNumeric(".id", value);
            return value.ToString()!; // value != null (checked above)
        }

        // One place for "the router sent something this decode step cannot read as a number". None of the
        // callers fabricate a value — they fall back to the raw wire text — but a fall-back nobody can see is
        // how a .jg/router mismatch survives a release (P2.25).
        private static void TraceNonNumeric(string what, object value)
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
            if (Get(WinboxFieldResolver.AddrV6SubKey) is byte[] v6) text = WinboxFieldResolver.IpV6FromBytes(v6);
            else if (Get(WinboxFieldResolver.AddrV4SubKey) is object v4) text = WinboxFieldResolver.IpFromU32(v4);
            else if (Get(WinboxFieldResolver.AddrDnsSubKey) is object dns) text = dns.ToString();
            else if (Get(WinboxFieldResolver.AddrMacSubKey) is object mac) text = WinboxFieldResolver.MacFromBytes(mac);
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
            if (wireType == "ip6" && value is byte[] v6) return WinboxFieldResolver.IpV6FromBytes(v6);
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
        // list would read as "the router has fewer of these", which is the P2.25 defect class.
        private static string FormatNumberList(object? value, IReadOnlyDictionary<int, string>? enumMap)
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
