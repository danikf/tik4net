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
        // ── Field encode (writes) ──────────────────────────────────────────────

        /// <summary>
        /// Names <paramref name="apiName"/> in an unset: appends its type-tagged field id(s) to
        /// <paramref name="unsetIds"/> and returns any M2 fields that must ride along.
        /// </summary>
        /// <remarks>
        /// WinBox does not clear a field by writing an empty value — it lists the field's id in
        /// <see cref="WinboxM2Protocol.SysKey.UnsetFields"/> (webfig's <c>unset(obj,id)</c>). An
        /// <c>opt</c>-WRAPPED field additionally lowers its present-flag, because <c>types.opt.put</c> with
        /// no value does both: the flag says the option is absent, the list says the value underneath is
        /// gone. A field the catalog does not know, or one it marks read-only, yields nothing and adds no
        /// id — the caller reports it rather than sending a request that would silently do nothing.
        /// </remarks>
        internal List<byte[]> EncodeUnsetField(string apiName, List<int> unsetIds)
        {
            var result = new List<byte[]>();

            // A paired field (max-limit → its upload and download halves) is two real fields wearing one
            // name; clearing it has to clear both, and asking here rather than duplicating the pair table
            // keeps this from drifting away from EncodeField's own idea of what a pair is.
            var halves = PairedHalfNames(apiName);
            if (halves != null)
            {
                result.AddRange(EncodeUnsetField(halves.Item1, unsetIds));
                result.AddRange(EncodeUnsetField(halves.Item2, unsetIds));
                return result;
            }

            WinboxJgField? jg = null;
            if (JgFields == null || !JgFields.TryGetValue(AliasToJg(apiName), out jg) || jg == null)
                return result;
            if (jg.ReadOnly) return result;

            if (jg.OptKey != 0) result.Add(M2Message.BoolSys(jg.OptKey, false));

            unsetIds.Add(jg.TypedFieldId);

            // A network is an address key and a mask key, and a mask left behind is a value the router keeps
            // matching on. The mask has no declaration of its own to read a type from — it rides the same
            // u32 form its sibling does.
            if (jg.MaskKey != 0) unsetIds.Add((1 << 27) | jg.MaskKey);

            return result;
        }

        /// <summary>
        /// Encodes an API field write (<paramref name="apiName"/> = <paramref name="value"/>) into its M2
        /// wire field bytes, driven by the <c>.jg</c> UI-semantic type: IP addresses pack to u32
        /// (<c>ipaddr</c>) or address+netmask u32 pair (<c>network</c>), MACs to 6 raw bytes, enum strings to
        /// their numeric value (static map) or referenced-object <c>.id</c> (dynamic dropdown), bool/u32/
        /// string as their wire type. Returns an empty list when the field is read-only or has no sendable
        /// value; a <c>network</c> field yields two entries (address + mask). <paramref name="resolveRef"/>
        /// resolves a dynamic enum reference (handler, name) → numeric id. Throws
        /// <see cref="WinboxFieldResolutionException"/> when the name cannot be resolved.
        /// </summary>
        internal List<byte[]> EncodeField(string apiName, string value, Func<int[], string, int?>? resolveRef = null,
            bool allowReadOnly = false, Func<int[], IReadOnlyDictionary<int, string>?>? resolveRefTable = null)
        {
            // Normalize a GUI-styled name to its canonical API name up front so both ResolveKey and the typed
            // .jg lookup below agree on it (otherwise a GUI label would resolve a key but miss its typed field).
            apiName = CanonicalInputName(apiName);

            // A paired field has to be split BEFORE anything tries to resolve it: the composite is made of
            // two M2 keys and has none of its own, so ResolveKey would (correctly) fail on it. Each half is
            // then encoded BY ITS OWN NAME through this same method, so every typed encoder below applies
            // to it unchanged — a hand-rolled pair encoder here would be a second place for scale, enums
            // and read-only to be got wrong.
            var halves = PairedHalfNames(apiName);
            if (halves != null)
            {
                var paired = new List<byte[]>();
                if (value.Length == 0) return paired;

                int slash = value.IndexOf('/');
                string up = slash < 0 ? value : value.Substring(0, slash);
                // One side only means upload, download zero — the reading the router itself gives it
                // (max-limit=1M reads back as 1000000/0).
                string down = slash < 0 ? "0" : value.Substring(slash + 1);

                paired.AddRange(EncodeField(halves.Item1, up, resolveRef, allowReadOnly, resolveRefTable));
                paired.AddRange(EncodeField(halves.Item2, down, resolveRef, allowReadOnly, resolveRefTable));
                return paired;
            }

            int key = ResolveKey(apiName);
            var result = new List<byte[]>();
            // Set by the 'enm' case when a dropdown reference could not be resolved to a record; checked once
            // the static enum map has also had its chance, just before the generic encoders.
            bool unresolvedReference = false;

            // Look up the .jg field (wire type, ro, enum map, UI type). Seeds (.id/comment/name) have none —
            // they default to string, which is correct for comment/name. Use the aliased .jg label so a shipped
            // API alias (e.g. ping 'address' → 'ping-to') resolves to its typed field.
            WinboxJgField? jg = null;
            JgFields?.TryGetValue(AliasToJg(apiName), out jg);

            // Read-only fields are unsendable for CRUD writes, but a monitor's request inputs (e.g. ping
            // 'address') are .jg-marked ro as display fields yet must still be sent — allowReadOnly keeps them.
            if (jg != null && jg.ReadOnly && !allowReadOnly) return result;
            value = value ?? "";

            string? uiType = jg?.UiType;

            // The word RouterOS prints for a zero (arp-timeout=auto, mtu=auto, max-sessions=unlimited) is the
            // zero on the way back in. Without it every typed numeric encoder below refused the one value the
            // read side hands out — a field read over native could not be written back unchanged.
            if (jg != null && WinboxRecordCodec.IsZeroWord(jg, value))
            {
                if (jg.OptKey != 0) result.Add(M2Message.BoolSys(jg.OptKey, true));
                result.Add(EncodeU32(key, 0));
                return result;
            }

            // …and the word RouterOS prints for the all-ones unset marker (trust-store=all, certificate=none).
            if (jg != null && WinboxRecordCodec.IsSentinelWord(jg, value))
            {
                if (jg.OptKey != 0) result.Add(M2Message.BoolSys(jg.OptKey, true));
                result.Add(EncodeU32(key, unchecked((uint)WinboxJgField.UnsetSentinel)));
                return result;
            }

            // ── opt/not container flags ──
            //
            // An opt-wrapped field is IGNORED BY THE ROUTER unless its opt bool says the option is present, and
            // a not-wrapped one negates via its own bool (RouterOS's leading '!'). This has to happen before the
            // typed switch below, not after it: 'network', 'ipaddr', 'macaddr' and 'addr' all return from inside
            // the switch, so emitting the flags afterwards skipped exactly them — which is why every firewall
            // address written over native reached the router as a rule matching EVERYTHING.
            //
            // Clearing one is the same bool the other way round: an empty value on an opt-wrapped field means
            // "the option is not present". The flag is emitted ALONGSIDE whatever the encoders below make of
            // the empty value, not instead of it — a string field is cleared by writing it empty, and dropping
            // that write leaves the old value on the router (the unset verb then reports a success that did
            // nothing). For a typed field whose branch sends nothing for an empty value (network, ipaddr, …)
            // this bool is the entire write, which is what makes an unset of one a valid request at all.
            if (jg != null && jg.OptKey != 0 && value.Length == 0)
                result.Add(M2Message.BoolSys(jg.OptKey, false));

            if (jg != null && value.Length > 0)
            {
                // …but NOT on a per-member tri-state list, where '!' negates the MEMBER it precedes rather
                // than the whole field: "!ack,syn" is two members with opposite senses, and stripping the
                // leading '!' here would turn it into "the whole rule negated, ack and syn both plain".
                if (jg.NotKey != 0 && !IsPerMemberNegatedList(uiType))
                {
                    // Sent both ways: the flag is stored on the row, so a value written without '!' over a negated
                    // one kept the negation when only 'true' was ever sent — src-address=10.0.0.0/8 over
                    // !10.0.0.0/8 read back !10.0.0.0/8 (7.24.4).
                    bool negated = value.StartsWith("!");
                    if (negated)
                        value = value.Substring(1);
                    result.Add(M2Message.BoolSys(jg.NotKey, negated));
                }
                if (jg.OptKey != 0 && value.Length > 0)
                    result.Add(M2Message.BoolSys(jg.OptKey, true));
            }

            if (jg != null && value.Length > 0
                && (jg.UiType == FirewallLimitUiType || jg.UiType == FirewallDstLimitUiType || jg.UiType == FirewallTosUiType))
            {
                if (TryEncodeFirewallLimit(jg, value, result)) return result;
                throw new WinboxFieldValueException($"input does not match any value of {apiName}");
            }

            // A `number` whose postfix says s or ms is a DURATION, and the read side has spelled it as one
            // since the seeded audit found `mii-interval` reading 100 where the API says 100ms. The write
            // side has to take that spelling back or the field becomes readable and unwritable: RADIUS's
            // timeout (u8, postfix:'ms') took "500ms" through the generic numeric branch and left 1s100ms
            // on the router — accepted, silently wrong, and invisible to every read-only check.
            if (jg != null && value.Length > 0
                && string.Equals(uiType, "number", StringComparison.OrdinalIgnoreCase)
                && jg.IsTimePostfix
                && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                if (!TryParseDuration(value, jg.TicksPerSecond, out long units))
                    throw new WinboxFieldResolutionException(
                        $"WinBox native: '{value}' is not a valid duration for field '{apiName}' on "
                        + $"'{_apiPath}'. Expected a RouterOS duration (\"500ms\", \"45s\", \"1h\") or a plain "
                        + $"number of {jg.Postfix}.");
                result.Add(EncodeU32(key, unchecked((uint)units)));
                return result;
            }

            // ── typed UI encodings (more specific than the wire type) ──
            switch (uiType)
            {
                case "fixedpoint":
                    if (EncodeFixedPoint(key, value, jg, apiName, result)) return result;
                    break;
                case "clocktime":
                    if (EncodeClockTime(key, value, apiName, result)) return result;
                    break;
                case "clockdate":
                case "dateandtime":
                case "date":
                    if (EncodeDateAndTime(key, value, apiName, uiType, result)) return result;
                    break;
                case "interval":
                    if (EncodeInterval(key, value, jg, apiName, result)) return result;
                    break;
                case "network":
                    if (EncodeNetwork(key, value, jg, result)) return result;
                    break;
                case "ipaddr":
                    if (EncodeIpAddr(key, value, result)) return result;
                    break;
                case NumberNetmaskUiType when jg != null && jg.MaskKey != 0:
                    if (EncodeNumberNetmask(key, value, jg, apiName, result)) return result;
                    break;
                case "netmask":
                    if (EncodeNetmask(key, value, result)) return result;
                    break;
                case "macaddr":
                    if (EncodeMacAddr(key, value, result)) return result;
                    break;
                // A multibits is the same bitmask a `set` is — types.multibits.put ORs (1<<member) over the
                // same bit-indexed map — and only its editor differs. Without this case it fell past the
                // switch into the list/array refusal below, so every /ip/firewall address-type write was
                // rejected as "not yet encodable" for a field that is one u32.
                case "multibits":
                case "set":
                    if (EncodeBitSet(key, value, jg, apiName, resolveRefTable, result)) return result;
                    break;
                case "enm":
                    if (EncodeReference(key, value, jg, resolveRef, ref unresolvedReference, result)) return result;
                    break;
                case "numberrange" when jg != null && jg.MaskKey != 0:
                    if (EncodeNumberRange(key, value, jg, apiName, result)) return result;
                    break;
                case "multinumberrange":
                case "numberrangelist":
                    if (EncodeNumberRangeList(key, value, apiName, result)) return result;
                    break;
                case "multinumber":
                    if (EncodeNumberList(key, value, jg, apiName, resolveRef, result)) return result;
                    break;
                case "multi" when jg?.WireType == M2Type.MessageArray:   // .jg M-prefix: a MESSAGE array
                    if (EncodeMessageList(key, value, jg, apiName, resolveRef, result)) return result;
                    break;
                case "multistring":
                case "multiraw":
                case "multiipaddr":
                case "multiip6addr":
                case "multibignumber":
                    if (EncodeScalarList(key, value, jg, apiName, result)) return result;
                    break;
                case "multinetwork":
                case "multimacnetwork":
                    if (EncodeNetworkList(key, value, jg, apiName, uiType, result)) return result;
                    break;
                case "multinetwork6":
                    if (EncodeNetwork6List(key, value, jg, apiName, result)) return result;
                    break;
                case "multitristate":
                    if (EncodeTriStateList(key, ref value, jg, apiName, resolveRef, result)) return result;
                    break;
                case "multitristatearray":
                    if (EncodeTriStateArrayList(key, value, jg, apiName, resolveRef, result)) return result;
                    break;
            }

            // (opt/not flags for the generic encoders below were emitted before the switch, together with every
            // typed branch's — see the "opt/not container flags" block.)

            // enum static map: encode the API string to its numeric index.
            //
            // EXACT match first, case-insensitive only as a fallback. Case is not always decoration: a
            // wireless security profile's 'MAC Format' lists the same seven formats twice, upper then lower,
            // and the case is what selects how the MAC reaches the RADIUS server. Matching case-insensitively
            // in one pass returns whichever member comes FIRST, so every lowercase value was written as its
            // uppercase twin. The fallback keeps every other map working, where the API's spelling and the
            // label differ only in case.
            if (jg?.EnumMap != null)
            {
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.Ordinal))
                    {
                        // ...at the field's own wire type. A two-member map over a BOOL key is a real shape
                        // (/system/health spells its two bools 'disabled'/'enabled'), and a u32 written to a
                        // bool key is a request the router accepts, answers, and ignores.
                        result.Add(jg.WireType == M2Type.Bool
                            ? M2Message.BoolSys(key, kv.Key != 0)
                            : EncodeU32(key, (uint)kv.Key));
                        return result;
                    }
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(jg.WireType == M2Type.Bool
                            ? M2Message.BoolSys(key, kv.Key != 0)
                            : EncodeU32(key, (uint)kv.Key));
                        return result;
                    }
            }

            // An enm whose element is a clocktime: the map names the word ('startup' on /system/scheduler
            // start-time) and anything else is a time of day, carried as seconds since midnight — the
            // inverse of the decode, which renders a map miss through the element type.
            if (jg != null && value.Length > 0
                && string.Equals(jg.ElementUiType, "clocktime", StringComparison.OrdinalIgnoreCase)
                && TryParseDuration(value, 1, out long elementSeconds))
            {
                result.Add(EncodeU32(key, unchecked((uint)(elementSeconds % 86400))));
                return result;
            }

            // A dropdown reference (ftype 'enm' with a RefHandler) whose value named neither an existing
            // record nor a static enum member. Every remaining encoder below would turn it into a plain
            // string or a dropped field, and the router accepts such a request as if the field were simply
            // not set — so a typo'd interface name would silently "succeed". Fail instead.
            if (unresolvedReference)
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName}");

            M2Type wireType = jg?.WireType ?? SeedWireType(apiName);

            // Loud failure for list/array fields we do NOT yet encode, rather than silently sending a
            // wrong-typed scalar the router quietly drops (silent data loss is worse than an error). The
            // handled list type (multinumberrange) already returned above; anything else with an array wire
            // type (u32[], string[], …) or a 'multi…' UI type (e.g. bridge-vlan 'tagged'/'untagged'
            // interface lists) is not yet supported over native M2 writes.
            if (value.Length > 0 && IsUnsupportedListType(wireType, uiType))
                throw new WinboxFieldResolutionException(
                    $"WinBox native: field '{apiName}' on '{_apiPath}' is a list/array type " +
                    $"('{uiType ?? M2Types.JgName(wireType)}') that is not yet encodable over native WinBox M2 writes. " +
                    "Use an Api/REST/CLI connection for this field (or a FieldOverride to a scalar key).");

            // A scalar tuple is a compound the API prints joined by a separator, and splitting the text back
            // onto the parts is not the inverse of joining it: an IPv6 'Remote' would put its own colons in
            // the way of the tuple's. Refusing beats writing the first part and dropping the rest, which is
            // a request the router accepts and half-obeys.
            // A tuple of plain numbers and durations is the exception: neither part can contain the separator,
            // so the split is exact. The tunnels' keepalive=7s,3 is one — u7d5=7 and u7d9=3 (7.24.2).
            if (value.Length > 0 && jg?.ElementParts != null
                && string.Equals(uiType, WinboxJgCatalog.TupleUiType, StringComparison.OrdinalIgnoreCase)
                && TryEncodeNumericTuple(jg, value, result))
                return result;
            if (value.Length > 0
                && string.Equals(uiType, WinboxJgCatalog.TupleUiType, StringComparison.OrdinalIgnoreCase))
                throw new WinboxFieldResolutionException(
                    $"WinBox native: field '{apiName}' on '{_apiPath}' is a tuple of several WinBox fields " +
                    "joined for display and is not encodable over native WinBox M2 writes. " +
                    "Use an Api/REST/CLI connection for this field.");

            // A base-16 number is read back in the API's own spelling (icmp-rate-mask=0x1818, priority=0x8000), so
            // that spelling is what a caller hands in; the plain decimal is still accepted.
            if (jg != null && jg.Radix == 16 && value.Length > 2
                && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && wireType == M2Type.U32
                && uint.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out uint hex))
            {
                result.Add(EncodeU32(key, hex));
                return result;
            }

            // 'addr' (webfig types.addr) is a compound: the value is a nested message, and each address FORM
            // rides at its own sub-key. Encoding it needs the whole set, not just IPv4 — see EncodeAddr.
            if (wireType == M2Type.Message && value.Length > 0)
            {
                result.Add(M2Message.MessageSys(key, EncodeAddr(value, jg?.Allow, apiName, _apiPath)));
                return result;
            }

            switch (wireType)
            {
                case M2Type.Bool:
                    result.Add(M2Message.BoolSys(key, ParseBool(value)));
                    break;
                case M2Type.U32:
                    if (long.TryParse(value, out long n)) result.Add(EncodeU32(key, (uint)n));
                    else result.Add(M2Message.StringSys(key, value)); // non-numeric (e.g. "auto")
                    break;
                case M2Type.U64:
                    // A u64 must go out as a u64. Narrowing it to the u32 form — which is what this case
                    // used to share — sends the wrong TYPE BYTE, and the router answers success and ignores
                    // the field: /queue/simple's rate fields resolved to their keys, were encoded, and never
                    // moved. Suffixes are accepted here because the API accepts them (max-limit=1M).
                    //
                    // TikDataRate.TryParse is the STRICT half of that type on purpose, and this is the
                    // caller it is strict for: a word like 'auto' or 'unlimited' has to go out as a string,
                    // and a parse that turned it into a token would put a 64-bit integer on the wire for it
                    // — which the router accepts and then ignores, the same silent no-op this whole case
                    // exists to prevent.
                    if (TikDataRate.TryParse(value, out TikDataRate rate) && rate.Value.HasValue)
                        result.Add(M2Message.U64Sys(key, unchecked((ulong)rate.Value.Value)));
                    else
                        result.Add(M2Message.StringSys(key, value)); // non-numeric (e.g. "auto")
                    break;
                case M2Type.Raw:
                    result.Add(M2Message.RawSys(key, ParseRaw(value)));
                    break;
                case M2Type.Addr6:
                {
                    // A standalone IPv6 field (.jg 'a' prefix), as opposed to the '6' member of an addr
                    // compound. Same FT_ADDR6 encoding; a value that is not an address stays text so the
                    // router reports it rather than us guessing 16 bytes.
                    byte[]? v6 = PackIpV6(value.Split('/')[0]);
                    if (v6 != null) result.Add(M2Message.Addr6Sys(key, v6));
                    else result.Add(M2Message.StringSys(key, value));
                    break;
                }
                default: // string, message, unknown and the arrays round-trip as string text
                    result.Add(M2Message.StringSys(key, value));
                    break;
            }
            return result;
        }

        // The typed encoder of "fixedpoint" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeFixedPoint(int key, string value, WinboxJgField? jg, string apiName, List<byte[]> result)
        {
            // The inverse of the decode, and missing for the same reason the ms case above was: a
            // `fixedpoint` renders as value/scale and had no encoder at all, so /caps-man/channel's
            // frequency (scale:1000, the wire holding 2412000) took "2412" through the generic
            // numeric branch and landed 2412 — which the router then printed back as 2.412.
            if (value.Length == 0) return true;
            // A time postfix makes it a duration on the way out, exactly as on the way in.
            if (jg?.IsTimePostfix == true)
            {
                if (!TryParseDuration(value, jg!.TicksPerSecond, out long dticks))
                    throw new WinboxFieldResolutionException(
                        $"WinBox native: '{value}' is not a valid duration for field '{apiName}' "
                        + $"on '{_apiPath}'.");
                result.Add(EncodeU32(key, unchecked((uint)dticks)));
                return true;
            }
            int fpScale = jg == null || jg.Scale < 1 ? 1 : jg.Scale;
            if (!TryParseScaled(value, fpScale, out long scaled))
                throw new WinboxFieldResolutionException(
                    $"WinBox native: '{value}' is not a valid number for field '{apiName}' on "
                    + $"'{_apiPath}'. Expected a decimal with at most "
                    + $"{fpScale.ToString(CultureInfo.InvariantCulture).Length - 1} fraction digit(s).");
            result.Add(EncodeU32(key, unchecked((uint)scaled)));
            return true;
        }

        // The typed encoder of "clocktime" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeClockTime(int key, string value, string apiName, List<byte[]> result)
        {
            // A time of day, as seconds since midnight: webfig types.clocktime.fromstr is
            // string2interval(str), and the API prints and accepts it as 23:20:32. Without this the
            // text reached the generic u32 branch and went out as a string the router ignores.
            if (value.Length == 0) return true;
            if (!TryParseDuration(value, 1, out long secondsOfDay))
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName} (expected a time of day, e.g. 23:20:32)");
            result.Add(EncodeU32(key, unchecked((uint)(secondsOfDay % 86400))));
            return true;
        }

        // The typed encoder of "clockdate" / "dateandtime" / "date" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeDateAndTime(int key, string value, string apiName, string? uiType, List<byte[]> result)
        {
            // Unix epoch seconds, the inverse of WinboxRecordCodec's decode. The API spells the value
            // 2026-11-21 (7.x) or nov/21/2026 (6.x, and what the mapper writes); as text it reached the
            // generic u32 branch and went out as a string the router answers OK to and ignores
            // (/system/scheduler start-date, 7.24.5 and 6.49.13).
            if (value.Length == 0) return true;
            if (!TikDateTimeHelper.TryFromTikDateTime(value, out DateTime when))
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName} (expected a date, e.g. 2026-11-21 or nov/21/2026)");
            if (string.Equals(uiType, "clockdate", StringComparison.OrdinalIgnoreCase))
                when = when.Date;
            long seconds = (long)(DateTime.SpecifyKind(when, DateTimeKind.Utc) - EpochUtc).TotalSeconds;
            result.Add(EncodeU32(key, unchecked((uint)seconds)));
            return true;
        }

        // The typed encoder of "interval" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeInterval(int key, string value, WinboxJgField? jg, string apiName, List<byte[]> result)
        {
            // The inverse of WinboxRecordCodec's interval decode, and it has to exist for the same
            // reason the decode does: the wire carries a COUNT of 1/scale-second units, while the API
            // spells the same value "5m" / "1w" / "500ms". Without this, "5m" failed long.TryParse in
            // the generic u32 branch and went out as the STRING "5m" — which the router accepts,
            // answers with status 0, and ignores, so the write silently did nothing.
            if (value.Length == 0) return true;
            // A named value wins, exactly as on the way back (e.g. 'immediately' / 'never').
            if (jg?.EnumMap != null)
            {
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(EncodeU32(key, unchecked((uint)kv.Key)));
                        return true;
                    }
            }
            if (TryParseDuration(value, jg?.Scale ?? 1, out long ticks))
            {
                result.Add(EncodeU32(key, unchecked((uint)ticks)));
                return true;
            }
            // Not a duration and not a named value. Refused rather than sent as text: text on a
            // numeric key is the silent no-op this case exists to remove.
            throw new WinboxFieldResolutionException(
                $"WinBox native: '{value}' is not a valid interval for field '{apiName}' on "
                + $"'{_apiPath}'. Expected a RouterOS duration (\"5m\", \"1w2d\", \"500ms\"), a plain "
                + "number of seconds"
                + (jg?.EnumMap != null
                    ? ", or one of: " + string.Join(", ", jg.EnumMap.Values) + "."
                    : "."));
        }

        // The typed encoder of "network" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNetwork(int key, string value, WinboxJgField? jg, List<byte[]> result)
        {
            // Empty → unset (send nothing).
            if (value.Length == 0) return true;
            // jg cannot be null here: uiType only reaches this switch as jg?.UiType's value, so a
            // non-null "network" case implies jg itself is non-null.
            if (jg!.IsRange)
            {
                // range:1 → the maskid sibling is the range-END address, not a netmask. All three
                // RouterOS forms are accepted ("a", "a-b", "a/len"); sending end=start for a host
                // avoids the router storing an open-ended range (which is what a /32 netmask sent as
                // the "end" produced).
                if (!TryParseV4Range(value, out uint start, out uint end))
                    return false; // not v4 — fall through to generic encoders
                result.Add(EncodeU32(key, start));
                if (jg.MaskKey != 0) result.Add(EncodeU32(jg.MaskKey, end));
                return true;
            }
            // "addr/mask" → address u32 (key) + netmask u32 (maskid).
            var parts = value.Split('/');
            uint? addr = PackIpV4(parts[0]);
            if (addr == null) return false; // not v4 — fall through to generic encoders
            result.Add(EncodeU32(key, addr.Value));
            if (jg.MaskKey != 0)
            {
                uint mask = parts.Length > 1 ? MaskFrom(parts[1]) : 0xFFFFFFFFu;
                result.Add(EncodeU32(jg.MaskKey, mask));
            }
            return true;
        }

        // The typed encoder of "ipaddr" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeIpAddr(int key, string value, List<byte[]> result)
        {
            if (value.Length == 0) return true;
            uint? ip = PackIpV4(value.Split('/')[0]);
            if (ip == null) return false;
            result.Add(EncodeU32(key, ip.Value));
            return true;
        }

        // The typed encoder of NumberNetmaskUiType when jg != null && jg.MaskKey != 0 (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNumberNetmask(int key, string value, WinboxJgField jg, string apiName, List<byte[]> result)
        {
            // "10,24": the number on the field's key, the prefix as a mask on its netmask key. The API
            // always prints both; a bare "10" leaves the netmask as the router has it.
            if (value.Length == 0) return true;
            int comma = value.IndexOf(',');
            string numberText = comma < 0 ? value : value.Substring(0, comma);
            if (!uint.TryParse(numberText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
                throw new WinboxFieldValueException($"'{value}' is not number,netmask for '{apiName}' (expected 10,32).");
            result.Add(EncodeU32(key, number));
            if (comma >= 0)
                result.Add(EncodeU32(jg.MaskKey, MaskFrom(value.Substring(comma + 1).Trim())));
            return true;
        }

        // The typed encoder of "netmask" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNetmask(int key, string value, List<byte[]> result)
        {
            // The read side answers in prefix lengths (types.netmask.tostr), so the write side has
            // to accept one. MaskFrom takes both spellings — "32" and "255.255.255.255" — which is
            // also what types.netmask.fromstr does.
            if (value.Length == 0) return true;
            result.Add(EncodeU32(key, MaskFrom(value)));
            return true;
        }

        // The typed encoder of "macaddr" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeMacAddr(int key, string value, List<byte[]> result)
        {
            if (value.Length == 0) return true;
            result.Add(M2Message.RawSys(key, ParseRaw(value)));
            return true;
        }

        // The typed encoder of "multibits" / "set" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeBitSet(int key, string value, WinboxJgField? jg, string apiName, Func<int[], IReadOnlyDictionary<int, string>?>? resolveRefTable, List<byte[]> result)
        {
            // Bitmask flag set (e.g. connection-state "established,related"). Empty → unset (send nothing).
            // The value rides as a u32 of OR'd (1<<bitIndex) per the .jg bit map; the opt/not flags and
            // the leading '!' were handled above.
            if (value.Length == 0) return true;

            // The members: a static .jg map, or — for /user/group's policies and the script and
            // scheduler policy fields — a TABLE, where the bit index is the referenced row's id.
            // A member list that cannot be read is refused rather than encoded as far as it goes:
            // without it every token misses, and the field would go out as a clean, well-formed
            // ZERO, which the router accepts as "this row is allowed nothing".
            IReadOnlyDictionary<int, string>? memberMap = jg!.EnumMap;
            if (memberMap == null && jg.RefHandler != null)
            {
                memberMap = resolveRefTable?.Invoke(jg.RefHandler);
                if (memberMap == null)
                    throw new WinboxFieldResolutionException(
                        $"WinBox native: field '{apiName}' on '{_apiPath}' is a bit set whose members "
                        + $"live in table [{string.Join(",", jg.RefHandler)}], which could not be read, "
                        + "so the value cannot be encoded without silently clearing the field.");
            }

            long bits = 0;
            long negatedBits = 0;
            if (memberMap != null)
                foreach (var tok in value.Split(','))
                {
                    string t = tok.Trim();
                    bool negated = t.StartsWith("!");
                    if (negated) t = t.Substring(1).Trim();
                    if (t.Length == 0) continue;
                    bool matched = false;
                    foreach (var kv in memberMap)
                        if (string.Equals(kv.Value, t, StringComparison.OrdinalIgnoreCase))
                        {
                            if (negated) negatedBits |= 1L << kv.Key; else bits |= 1L << kv.Key;
                            matched = true;
                            break;
                        }
                    // A member nobody knows is a wrong value, not a member to skip: dropping it sends
                    // a bit set the router accepts with that permission simply absent.
                    if (!matched && jg.RefHandler != null)
                        throw new WinboxFieldValueException(
                            $"input does not match any value of {apiName} (member '{t}')");
                }
            // The second half of the value: the members this write says NO to. webfig's editor
            // sends both — SetView.save builds `set` from the ticked boxes and `unset` from the
            // unticked ones and calls put([set,unset]) — so the mask is the complement WITHIN the
            // member list, not ~bits over all 32 bits (types.multibits.put, whose editor has no
            // unticked-vs-absent distinction, does use the plain complement).
            //
            // It only rides when the field declares somewhere to put it: a maskid sibling for a
            // scalar, or the second element of a two-element u32[] for a `set` on an array key
            // (types.set.put's 'U' branch). A u32 written to an array key is the wrong TYPE BYTE,
            // which RouterOS answers with success and ignores.
            // The second half of the value: the members this write explicitly DENIES. The router
            // applies the members the request mentions — the union of the two words — and leaves the
            // rest of the row as it found it, which is exactly how the binary API behaves for the
            // same command: `set policy=read,write,winbox` on a group that already had `test` keeps
            // test (measured on 7.24, both directions). Sending "every member not named" as denied
            // instead — which is what WinBox's own editor does, because it always has the whole
            // checkbox state in hand — turns a set of three members into a rewrite of all
            // seventeen. On an ADD the router fills the rest in itself, so the row still comes out
            // spelled exactly as the API's own add spells it.
            long setMask = negatedBits;
            if (jg.WireType.IsArray())
            {
                result.Add(M2Message.U32ArraySys(key,
                    unchecked((int)bits), unchecked((int)setMask)));
                return true;
            }
            result.Add(EncodeU32(key, unchecked((uint)bits)));
            if (jg.MaskKey != 0) result.Add(EncodeU32(jg.MaskKey, unchecked((uint)setMask)));
            return true;
        }

        // The typed encoder of "enm" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeReference(int key, string value, WinboxJgField? jg, Func<int[], string, int?>? resolveRef, ref bool unresolvedReference, List<byte[]> result)
        {
            // dynamic dropdown → referenced object's .id; resolve the name against that table.
            // jg non-null: uiType == "enm" only when jg.UiType produced it.
            if (jg!.RefHandler != null && resolveRef != null && value.Length > 0
                && !long.TryParse(value, out _))
            {
                // Every table the dropdown draws from, in the order webfig's enm.pair tries them — a
                // queue tree's parent is an interface OR another queue, and only the first was searched.
                foreach (int[] table in jg.RefHandlers ?? new[] { jg.RefHandler })
                {
                    int? id = resolveRef(table, value);
                    if (id.HasValue) { result.Add(EncodeU32(key, (uint)id.Value)); return true; }
                }

                // The name is not a record in the referenced table. It may still be a static enum
                // member ("none", "all", …), so try those below — but if nothing matches, the value
                // is simply wrong and must not be swallowed. Silently dropping it sends a request the
                // router happily accepts with the field missing, so a typo'd interface name looks
                // like success. Remember to fail at the end of this method instead.
                unresolvedReference = true;
            }
            return false; // fall through to static-map / numeric handling below
        }

        // The typed encoder of "numberrange" when jg != null && jg.MaskKey != 0 (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNumberRange(int key, string value, WinboxJgField jg, string apiName, List<byte[]> result)
        {
            // One range on two keys (webfig types.numberrange.fromstr/put): "100-200" is low 100 on the
            // field's key and high 200 on its highid, a bare "100" is [100,100]. Only the low end was
            // written before, as a plain number, and "100-200" did not parse at all.
            if (value.Length == 0) return true;
            int dash = value.IndexOf('-');
            string lowText = dash < 0 ? value : value.Substring(0, dash);
            string highText = dash < 0 ? value : value.Substring(dash + 1);
            if (!uint.TryParse(lowText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint lo)
                || !uint.TryParse(highText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint hi))
                throw new WinboxFieldValueException($"'{value}' is not a number range for '{apiName}' (expected 100 or 100-200).");
            result.Add(EncodeU32(key, lo));
            result.Add(EncodeU32(jg.MaskKey, hi));
            return true;
        }

        // The typed encoder of "multinumberrange" / "numberrangelist" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNumberRangeList(int key, string value, string apiName, List<byte[]> result)
        {
            // A list of number ranges: bridge-vlan 'vlan-ids', firewall 'dst-port'/'dscp'/'pcp', … .
            // webfig types.multinumberrange.put (no id2) and types.numberrangelist (inherits def) both
            // store a flat u32[] of [lo0,hi0,lo1,hi1,…]; a bare "10" is the range [10,10]. An invertible
            // (not-wrapped) field negates via its not-flag bool (RouterOS "!80"), handled above along
            // with the opt flag. Empty → unset.
            if (value.Length == 0) return true;
            var nums = ParseNumberRangeList(value, apiName);
            if (nums.Count == 0) return true;
            result.Add(M2Message.U32ArraySys(key, nums.ToArray()));
            return true;
        }

        // The typed encoder of "multinumber" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNumberList(int key, string value, WinboxJgField? jg, string apiName, Func<int[], string, int?>? resolveRef, List<byte[]> result)
        {
            // A list whose ELEMENTS are what the scalar encoders handle one at a time: bridge-vlan
            // 'tagged'/'untagged' (interface references), the log rule's 'topics' (a static enum),
            // /ip/proxy 'port' (plain numbers). webfig types.multinumber stores them as a flat u32[]
            // in the order given, so the whole field is one array write — and encoding each element
            // by the same three rules the decode reads them back with (reference → static map →
            // literal) is what makes the round trip agree.
            if (value.Length == 0) return true;
            var items = new List<int>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                // The ELEMENT's own scale, the inverse of the decode: "2412" on a
                // {fixedpoint,scale:1000} element is 2412000 on the wire. Without it the write
                // landed 2412 and the router printed it back as 2.412. Gated on the scale alone —
                // a scale above 1 IS the statement that the wire counts in smaller units than the
                // API prints, whatever the element's ui type is spelled as.
                // jg non-null: uiType == "multinumber" only when jg.UiType produced it.
                if (jg!.ElementScale > 1)
                {
                    if (!TryParseScaled(t, jg.ElementScale, out long scaledElem))
                        throw new WinboxFieldValueException(
                            $"input does not match any value of {apiName} (element '{t}')");
                    items.Add(unchecked((int)scaledElem));
                }
                else items.Add(EncodeListElement(jg!, t, apiName, resolveRef));
            }
            if (items.Count == 0) return true;
            result.Add(M2Message.U32ArraySys(key, items.ToArray()));
            return true;
        }

        // The typed encoder of "multi" when jg?.WireType == M2Type.MessageArray (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeMessageList(int key, string value, WinboxJgField? jg, string apiName, Func<int[], string, int?>? resolveRef, List<byte[]> result)
        {
            // A list whose elements are whole SUBMESSAGES. What one element is varies — an `addr`
            // compound (/ip/dns servers), a single addressable leaf (/snmp trap-interfaces, one
            // interface id), or a tuple/union of several parts — and each is encoded by the same
            // rules its scalar counterpart is, so the allow-mask handling, the enum maps and the
            // reference resolution all stay in one place.
            var elements = new List<byte[][]>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length > 0) elements.Add(EncodeMessageElement(jg!, t, apiName, resolveRef));
            }
            result.Add(M2Message.MessageArraySys(key, elements));
            return true;
        }

        // The typed encoder of "multistring" / "multiraw" / "multiipaddr" / "multiip6addr" / "multibignumber" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeScalarList(int key, string value, WinboxJgField? jg, string apiName, List<byte[]> result)
        {
            // The rest of the webfig list family. All five `inherit(types.multinumber)` and store one
            // ARRAY under one key, differing only in what an element is — text, bytes, an IPv4 packed
            // into a u32, sixteen IPv6 bytes — so the element type decides the conversion and the
            // field's WIRE type decides the array's TLV form.
            //
            // An empty value is the EMPTY ARRAY, not a dropped field: a key the router is not told
            // about keeps whatever it already holds, so clearing a list has to be said out loud.
            var items = new List<string>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length > 0) items.Add(t);
            }
            result.Add(EncodeScalarArray(jg!, key, items, apiName));
            return true;
        }

        // The typed encoder of "multinetwork" / "multimacnetwork" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNetworkList(int key, string value, WinboxJgField? jg, string apiName, string? uiType, List<byte[]> result)
        {
            // webfig types.multinetwork: a list of (address, sibling) PAIRS. With a `maskid` the two
            // halves ride in two PARALLEL arrays, one entry per element; without one the type is a
            // plain multinumberrange and the pairs are FLATTENED into a single [a0,b0,a1,b1,...]
            // array (types.multinetwork.put delegates to multinumberrange.put when maskid is null).
            //
            // What the second half MEANS is the element's business, exactly as on a scalar: the range
            // END when the element declares range:1 (/ip/pool "192.168.251.10-192.168.251.20"), a
            // netmask otherwise. An empty value is the empty array, not a dropped field.
            bool isMac = string.Equals(uiType, "multimacnetwork", StringComparison.OrdinalIgnoreCase);
            // jg non-null: uiType only reaches this switch as jg?.UiType's value.
            if (isMac && jg!.MaskKey == 0) return false;   // not the shape webfig describes - refuse below
            var addrs = new List<int>();
            var masks = new List<int>();
            var macs = new List<byte[]>();
            var macMasks = new List<byte[]>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                if (isMac)
                {
                    var mp = t.Split('/');
                    if (!TryParseMac(mp[0], out byte[]? mac)) throw NotThisPart(apiName, t);
                    byte[] mask = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                    if (mp.Length > 1 && !TryParseMac(mp[1], out mask!)) throw NotThisPart(apiName, t);
                    macs.Add(mac!);       // non-null: TryParseMac sets it before returning true
                    macMasks.Add(mask);
                    continue;
                }
                uint lo, hi;
                if (jg!.ElementIsRange)
                {
                    if (!TryParseV4Range(t, out lo, out hi)) throw NotThisPart(apiName, t);
                }
                else
                {
                    var np = t.Split('/');
                    uint? packed = PackIpV4(np[0]);
                    if (packed == null) throw NotThisPart(apiName, t);
                    lo = packed.Value;
                    hi = np.Length > 1 ? MaskFrom(np[1]) : 0xFFFFFFFFu;
                }
                addrs.Add(unchecked((int)lo));
                masks.Add(unchecked((int)hi));
            }
            if (isMac)
            {
                result.Add(M2Message.RawArraySys(key, macs));
                result.Add(M2Message.RawArraySys(jg!.MaskKey, macMasks));
                return true;
            }
            if (jg!.MaskKey != 0)
            {
                result.Add(M2Message.U32ArraySys(key, addrs.ToArray()));
                result.Add(M2Message.U32ArraySys(jg.MaskKey, masks.ToArray()));
                return true;
            }
            var flat = new List<int>();
            for (int i = 0; i < addrs.Count; i++) { flat.Add(addrs[i]); flat.Add(masks[i]); }
            result.Add(M2Message.U32ArraySys(key, flat.ToArray()));
            return true;
        }

        // The typed encoder of "multinetwork6" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeNetwork6List(int key, string value, WinboxJgField? jg, string apiName, List<byte[]> result)
        {
            // `types.multinetwork6 = inherit(types.multinetwork)` — the same list of pairs, over
            // sixteen-byte addresses. Only the parallel-array form can exist here: a 128-bit address
            // does not fit the flattened u32 array the maskid-less multinetwork falls back to, and
            // both fields the 7.24 catalog declares carry a maskid.
            //
            // The sibling holds the PREFIX LENGTH itself, not a mask, and a bare address means /128 —
            // the same rule the scalar network6 part follows.
            if (jg!.MaskKey == 0) return false;   // not the shape webfig describes - refuse below
            var v6Addrs = new List<byte[]>();
            var v6Lens = new List<int>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                var np = t.Split('/');
                byte[]? packed6 = PackIpV6(np[0]);
                if (packed6 == null) throw NotThisPart(apiName, t);
                int plen = 128;
                if (np.Length > 1 && (!int.TryParse(np[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out plen) || plen < 0 || plen > 128))
                    throw NotThisPart(apiName, t);
                v6Addrs.Add(packed6);
                v6Lens.Add(plen);
            }
            result.Add(M2Message.Addr6ArraySys(key, v6Addrs));
            result.Add(M2Message.U32ArraySys(jg.MaskKey, v6Lens.ToArray()));
            return true;
        }

        // The typed encoder of "multitristate" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeTriStateList(int key, ref string value, WinboxJgField? jg, string apiName, Func<int[], string, int?>? resolveRef, List<byte[]> result)
        {
            // A bitmask whose members can each be negated, with the negated ones in a SECOND key:
            // types.multitristate.put ORs the plain members into `id` and the '!' ones into `maskid`
            // (contrast multitristatearray, which does the same across two ARRAYS, and a scalar's
            // NotKey, which negates the whole value). /ip/firewall/filter tcp-flags="syn,!ack".
            //
            // Both keys are always sent once anything is: the router keeps whatever it already has
            // for a key it is not told about, so writing only the plain half would leave a stale '!'
            // member behind and report success.
            if (jg!.MaskKey == 0) return false;   // no second key: not the shape webfig describes — refuse below
            if (value.Length == 0) return true;
            // The whole-list '!' is a bare leading element (tcp-flags="!,syn,!ack", as the API prints it) and
            // the field's own `not` flag, written both ways: the flag is stored on the row, and the router's
            // text set rewrites it every time (!,syn,!ack + set rst → rst,!ack, 7.24.5).
            bool wholeNegated = value == "!" || value.StartsWith("!,", StringComparison.Ordinal);
            if (wholeNegated)
            {
                value = value.Length > 1 ? value.Substring(2) : "";
                if (value.Length == 0)
                    // RouterOS refuses a bare '!' too ("ambiguous value of flag"): there is nothing to negate.
                    throw new WinboxFieldValueException($"input does not match any value of {apiName} (a '!' with no members)");
                if (jg.NotKey == 0)
                    throw new WinboxFieldValueException($"input does not match any value of {apiName} (the field takes no '!' on the whole list)");
            }
            if (jg.NotKey != 0)
                result.Add(M2Message.BoolSys(jg.NotKey, wholeNegated));
            long onBits = 0, offBits = 0;
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                bool negated = t.StartsWith("!");
                if (negated) t = t.Substring(1).Trim();
                if (t.Length == 0) continue;
                int bit = EncodeListElement(jg, t, apiName, resolveRef);
                if (negated) offBits |= 1L << bit; else onBits |= 1L << bit;
            }
            result.Add(EncodeU32(key, unchecked((uint)onBits)));
            result.Add(EncodeU32(jg.MaskKey, unchecked((uint)offBits)));
            return true;
        }

        // The typed encoder of "multitristatearray" (a case of EncodeField). True when the value
        // is encoded (EncodeField returns what is in result); false to fall through to the generic encoders.
        private bool EncodeTriStateArrayList(int key, string value, WinboxJgField? jg, string apiName, Func<int[], string, int?>? resolveRef, List<byte[]> result)
        {
            // A list whose elements may each be negated — /system/logging 'topics', where the API
            // writes "info,!debug". webfig types.multitristatearray.put splits the one list into TWO
            // arrays by that flag and writes both keys, so the '!' is a different KEY here, not a
            // value prefix (contrast the NotKey flag on a scalar). Both arrays are always sent,
            // including as empty, because that is the only way to CLEAR the other half.
            // jg non-null: uiType == "multitristatearray" only when jg.UiType produced it.
            if (jg!.OffKey == 0) return false;   // no second key: not the shape webfig describes — refuse below
            if (value.Length == 0) return true;
            var on = new List<int>();
            var off = new List<int>();
            foreach (var tok in value.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                bool negated = t.StartsWith("!");
                if (negated) t = t.Substring(1).Trim();
                if (t.Length == 0) continue;
                (negated ? off : on).Add(EncodeListElement(jg, t, apiName, resolveRef));
            }
            if (on.Count == 0 && off.Count == 0) return true;
            result.Add(M2Message.U32ArraySys(key, on.ToArray()));
            result.Add(M2Message.U32ArraySys(jg.OffKey, off.ToArray()));
            return true;
        }

        /// <summary>
        /// Parses a decimal into the <paramref name="scale"/>-per-unit integer the wire carries — "2412"
        /// with scale 1000 is 2412000, "1.5" is 1500. The inverse of the codec's fixedpoint rendering,
        /// which trims trailing zeros, so a value written back as "1.5" must read as "1.5" again.
        /// </summary>
        private static bool TryParseScaled(string value, int scale, out long scaled)
        {
            scaled = 0;
            if (scale < 1) scale = 1;
            string text = value.Trim();
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative) text = text.Substring(1);
            int dot = text.IndexOf('.');
            string whole = dot < 0 ? text : text.Substring(0, dot);
            string frac = dot < 0 ? "" : text.Substring(dot + 1);
            if (whole.Length == 0) whole = "0";
            if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out long w)) return false;
            int digits = scale.ToString(CultureInfo.InvariantCulture).Length - 1;
            if (frac.Length > digits) return false;
            if (frac.Length > 0 && !long.TryParse(frac, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;
            long f = 0;
            if (frac.Length > 0) f = long.Parse(frac.PadRight(digits, '0'), CultureInfo.InvariantCulture);
            scaled = w * scale + f;
            if (negative) scaled = -scaled;
            return true;
        }

        /// <summary>
        /// The firewall's ipsec-policy: the window's 'IPsec Policy' opt (0x1C5) over a tuple of two static enums,
        /// direction u84 (in, out) and policy u85 (none, ipsec). The window joins them with ':' where the API prints
        /// and takes <c>out,none</c>, so the catalog's own field reads <c>out:none</c> and cannot split a write.
        /// </summary>
        private static WinboxJgField FirewallIpsecPolicy()
            => new WinboxJgField("ipsec-policy", 0x84, M2Type.U32, false, uiType: WinboxJgCatalog.TupleUiType,
                optKey: 0x1C5, elementSeparator: ",",
                elementParts: new[]
                {
                    new WinboxJgElementPart(0x84, "enm", 0, enumMap: new Dictionary<int, string> { [0] = "in", [1] = "out" }),
                    new WinboxJgElementPart(0x85, "enm", 0, enumMap: new Dictionary<int, string> { [0] = "none", [1] = "ipsec" }),
                });

        /// <summary>UI type of <see cref="FirewallLimit"/>: one API field over the window's 'Limit' group.</summary>
        // The origin a clockdate/dateandtime value counts from (see the encode in EncodeField).
        private static readonly DateTime EpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal const string FirewallLimitUiType = "tik4net-fw-limit";

        /// <summary>UI type of <see cref="FirewallDstLimit"/>: one API field over the window's 'Dst. Limit' group.</summary>
        internal const string FirewallDstLimitUiType = "tik4net-fw-dst-limit";

        /// <summary>
        /// The firewall's limit, <c>[!]rate[/period],burst:mode</c> (<c>!10,5:packet</c>, <c>7/1h,3:packet</c>): the
        /// window's 'Limit' group (opt 0x1A0) of four boxes - rate q87 under the not 0xE4, period u11 in seconds (1 is
        /// left out of the text), burst u12, mode u86 (0 packet, 1 bit). The catalog reads them as four fields named
        /// after the boxes, and the API has one (7.24.5, filter, raw and mangle alike).
        /// </summary>
        private static WinboxJgField FirewallLimit()
            => new WinboxJgField("limit", 0x87, M2Type.U64, false, uiType: FirewallLimitUiType, optKey: 0x1A0, notKey: 0xE4);

        /// <summary>
        /// The firewall's dst-limit, <c>rate[/period],burst,limit-by/expire</c> (<c>30,5,dst-address/1m</c>): the window's
        /// 'Dst. Limit' group (opt 0x1A4) - rate u36, period u39 in seconds (1 left out), burst u37, limit-by u38 and
        /// expire u3b in hundredths of a second, which the API always prints (7.24.5).
        /// </summary>
        private static WinboxJgField FirewallDstLimit()
            => new WinboxJgField("dst-limit", 0x36, M2Type.U32, false, uiType: FirewallDstLimitUiType, optKey: 0x1A4);

        /// <summary>UI type of <see cref="FirewallTos"/>: one API field over the window's 'TOS' group.</summary>
        internal const string FirewallTosUiType = "tik4net-fw-tos";

        /// <summary>
        /// The firewall's tos, <c>[!]value[/mask]</c> in upper-case hex (<c>!0xBC/0xF0</c>, <c>0xA</c>): the window's 'TOS'
        /// group (opt 0x1C7) — 'Tos Value' u1c9 under the not 0x1C8 and 'Tos Mask' u1ca, left out of the text at its
        /// default 0xFF (7.24.5). The catalog reads tos and tos-mask, in lower case.
        /// </summary>
        private static WinboxJgField FirewallTos()
            => new WinboxJgField("tos", 0x1C9, M2Type.U32, false, uiType: FirewallTosUiType, optKey: 0x1C7, notKey: 0x1C8);

        /// <summary>
        /// The keys of a firewall group field other than the field's own (and ipsec-policy's second part), or none for
        /// any other field. The window's own fields for them would otherwise take the name whenever the frame lists
        /// such a key first.
        /// </summary>
        internal static int[] FirewallLimitBoxKeys(WinboxJgField f)
            => f.UiType == FirewallLimitUiType ? new[] { 0x11, 0x12, 0x86 }
             : f.UiType == FirewallDstLimitUiType ? new[] { 0x39, 0x37, 0x38, 0x3B }
             : f.UiType == FirewallTosUiType ? new[] { 0x1CA }
             : f.Key == 0x84 && f.ElementSeparator == "," && string.Equals(f.ApiName, "ipsec-policy", StringComparison.OrdinalIgnoreCase)
                ? new[] { 0x85 }
             : Array.Empty<int>();

        /// <summary>A byte in hex (<c>0xBC</c>) or decimal, as the tos field takes it.</summary>
        private static bool TryParseTosByte(string text, out uint n)
        {
            n = 0;
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            return (hex ? uint.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out n)
                        : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                   && n <= 255;
        }

        /// <summary>limit-by of <see cref="FirewallDstLimit"/>, u38: the window's labels in the API's spelling.</summary>
        internal static readonly IReadOnlyDictionary<int, string> FirewallDstLimitBy = new Dictionary<int, string>
        {
            [1] = "dst-address", [3] = "dst-address-and-port", [4] = "src-address", [5] = "src-and-dst-addresses",
            [7] = "addresses-and-dst-port",
        };

        /// <summary>
        /// Encodes <see cref="FirewallLimit"/> or <see cref="FirewallDstLimit"/> (the flags are already sent): the text
        /// split on the API's separators, each box written. False, adding nothing, when the text does not have the shape.
        /// </summary>
        private static bool TryEncodeFirewallLimit(WinboxJgField jg, string value, List<byte[]> result)
        {
            if (jg.UiType == FirewallTosUiType)
            {
                int cut = value.IndexOf('/');
                if (!TryParseTosByte(cut < 0 ? value : value.Substring(0, cut), out uint tos)) return false;
                uint mask = 255;
                if (cut >= 0 && !TryParseTosByte(value.Substring(cut + 1), out mask)) return false;
                result.Add(EncodeU32(0x1C9, tos));
                result.Add(EncodeU32(0x1CA, mask));
                return true;
            }
            bool dst = jg.UiType == FirewallDstLimitUiType;
            string[] parts = value.Split(',');
            if (parts.Length != (dst ? 3 : 2)) return false;

            string rateText = parts[0];
            long period = 1;
            int slash = rateText.IndexOf('/');
            if (slash >= 0)
            {
                if (!TryParseDuration(rateText.Substring(slash + 1), 1, out period) || period < 1 || period > uint.MaxValue) return false;
                rateText = rateText.Substring(0, slash);
            }
            if (!TikDataRate.TryParse(rateText, out TikDataRate rate) || !rate.Value.HasValue || rate.Value.Value < 0) return false;

            var encoded = new List<byte[]>();
            if (dst)
            {
                if (!uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint burst)) return false;
                string by = parts[2], expire = "10s";
                int at = by.IndexOf('/');
                if (at >= 0) { expire = by.Substring(at + 1); by = by.Substring(0, at); }
                var member = FirewallDstLimitBy.FirstOrDefault(kv => string.Equals(kv.Value, by, StringComparison.OrdinalIgnoreCase));
                if (member.Value == null || !TryParseDuration(expire, 100, out long expireTicks)
                    || expireTicks < 0 || expireTicks > uint.MaxValue || rate.Value.Value > uint.MaxValue)
                    return false;
                encoded.Add(EncodeU32(0x36, (uint)rate.Value.Value));
                encoded.Add(EncodeU32(0x39, (uint)period));
                encoded.Add(EncodeU32(0x37, burst));
                encoded.Add(EncodeU32(0x38, (uint)member.Key));
                encoded.Add(EncodeU32(0x3B, (uint)expireTicks));
            }
            else
            {
                int colon = parts[1].IndexOf(':');
                if (colon < 0) return false;
                string mode = parts[1].Substring(colon + 1);
                int modeValue = string.Equals(mode, "packet", StringComparison.OrdinalIgnoreCase) ? 0
                              : string.Equals(mode, "bit", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                if (modeValue < 0
                    || !uint.TryParse(parts[1].Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out uint burst))
                    return false;
                encoded.Add(M2Message.U64Sys(0x87, unchecked((ulong)rate.Value.Value)));
                encoded.Add(EncodeU32(0x11, (uint)period));
                encoded.Add(EncodeU32(0x12, burst));
                encoded.Add(EncodeU32(0x86, (uint)modeValue));
            }
            result.AddRange(encoded);
            return true;
        }

        /// <summary>
        /// A firewall bridge-port matcher as the filter window declares it: an interface, or one of the 'all …' groups,
        /// under an opt and a not.
        /// </summary>
        private static WinboxJgField FirewallBridgePort(string apiName, int key, int optKey, int notKey)
            => new WinboxJgField(apiName, key, M2Type.U32, false, uiType: "enm", refHandler: new[] { 20, 0 },
                optKey: optKey, notKey: notKey,
                enumMap: new Dictionary<int, string>
                {
                    [16777216] = "all ethernet", [16777217] = "all wireless", [16777218] = "all vlan", [16777219] = "all ppp",
                });

        /// <summary>
        /// Encodes a scalar tuple whose every part is a <c>number</c>, an <c>interval</c> or a static enum — one u32 per part,
        /// the value split on the tuple's separator. Returns <c>false</c>, adding nothing, for any other part
        /// type or when the value does not have one token per part.
        /// </summary>
        private static bool TryEncodeNumericTuple(WinboxJgField jg, string value, List<byte[]> result)
        {
            var parts = jg.ElementParts!;
            string sep = string.IsNullOrEmpty(jg.ElementSeparator) ? "/" : jg.ElementSeparator!;
            string[] tokens = value.Split(new[] { sep }, StringSplitOptions.None);
            if (tokens.Length != parts.Count) return false;

            var encoded = new List<byte[]>();
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Alternatives != null || part.Key == 0) return false;
                long n;
                if (string.Equals(part.UiType, "interval", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseDuration(tokens[i], 1, out n)) return false;
                }
                else if (part.EnumMap != null)
                {
                    // A word of the part's own static map (mangle new-ttl=increment:3, 'TTL Action' 1).
                    string word = tokens[i].Trim();
                    var member = part.EnumMap.FirstOrDefault(kv => string.Equals(kv.Value, word, StringComparison.OrdinalIgnoreCase));
                    if (member.Value == null) return false;
                    n = member.Key;
                }
                else if (string.Equals(part.UiType, "number", StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(tokens[i].Trim(), out n)) return false;
                }
                else return false;
                if (n < 0 || n > uint.MaxValue) return false;
                encoded.Add(EncodeU32(part.Key, (uint)n));
            }
            result.AddRange(encoded);
            return true;
        }

        // Parse a RouterOS number-range list ("10,20-30,4000") into the flat [lo,hi,lo,hi,…] u32 array webfig
        // sends for a multinumberrange field. A bare "n" is the range [n,n]. Throws (loud) on a non-numeric
        // token rather than dropping it — a malformed vlan-id list should fail, not silently truncate.
        private List<int> ParseNumberRangeList(string value, string apiName)
        {
            var nums = new List<int>();
            foreach (var part in value.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int dash = p.IndexOf('-', 1); // skip a leading '-' so a negative low bound is not mis-split
                string loS = dash > 0 ? p.Substring(0, dash) : p;
                string hiS = dash > 0 ? p.Substring(dash + 1) : p;
                if (!int.TryParse(loS.Trim(), out int lo) || !int.TryParse(hiS.Trim(), out int hi))
                    throw new WinboxFieldResolutionException(
                        $"WinBox native: cannot parse number-range token '{p}' for field '{apiName}' on " +
                        $"'{_apiPath}'. Expected a number or a 'low-high' range (e.g. \"10,20-30\").");
                nums.Add(lo); nums.Add(hi);
            }
            return nums;
        }

        /// <summary>
        /// Encodes ONE element of a <c>multinumber</c> list to its u32, by the same three rules the decode
        /// reads an element back with: a dynamic dropdown resolves to the referenced record's id, a static
        /// map to its numeric member, and anything else must already be a number.
        /// </summary>
        /// <remarks>
        /// An element that matches none of the three is an error, never a dropped element. Dropping one sends
        /// a SHORTER list the router accepts without complaint, so "tagged=ether1,typo" would quietly tag
        /// ether1 alone and report success — the list equivalent of the silent scalar drop the <c>enm</c> case
        /// refuses for the same reason.
        /// </remarks>
        private int EncodeListElement(WinboxJgField jg, string token, string apiName,
            Func<int[], string, int?>? resolveRef)
        {
            if (jg.RefHandler != null && resolveRef != null && !long.TryParse(token, out _))
            {
                int? id = resolveRef(jg.RefHandler, token);
                if (id.HasValue) return id.Value;
            }
            if (jg.EnumMap != null)
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, token, StringComparison.OrdinalIgnoreCase))
                        return kv.Key;
            if (long.TryParse(token, out long literal)) return unchecked((int)literal);

            throw new WinboxFieldValueException(
                $"input does not match any value of {apiName} (element '{token}')");
        }

        /// <summary>
        /// Encodes ONE element of a message-array list into the fields of its submessage — the write side of
        /// <c>WinboxRecordCodec.FormatMultiList</c>'s element rendering.
        /// </summary>
        /// <remarks>
        /// Three element shapes, in the order the <c>.jg</c> distinguishes them: an <c>addr</c> compound,
        /// which has its own encoder; a <c>tuple</c>, whose parts are split by the tuple's separator and
        /// encoded left to right; and everything else, which is one addressable leaf (including a
        /// <c>union</c>, carried as a single part holding its alternatives). An element shape with no parts
        /// at all is refused rather than sent as something else — a submessage the router cannot read is a
        /// field it accepts and ignores.
        /// </remarks>
        private byte[][] EncodeMessageElement(WinboxJgField jg, string element, string apiName,
            Func<int[], string, int?>? resolveRef)
        {
            var fields = new List<byte[]>();

            // A `not`-wrapped element carries its own negation flag INSIDE the submessage, and the '!' is a
            // prefix on this element alone (/tool/sniffer filter-ip-address="!10.0.0.0/8,192.168.0.0/16").
            // The flag is written both ways round, never only when true: the router keeps whatever it holds
            // for a key it is not told about, so an omitted `false` would leave a stale '!' on an element
            // being rewritten as plain - and false is what the router itself sends for a plain element.
            if (jg.ElementNotKey != 0)
            {
                bool negated = element.StartsWith("!", StringComparison.Ordinal);
                if (negated) element = element.Substring(1).Trim();
                fields.Add(M2Message.BoolSys(jg.ElementNotKey, negated));
            }

            if (string.Equals(jg.ElementUiType, "addr", StringComparison.OrdinalIgnoreCase))
            {
                fields.AddRange(EncodeAddr(element, jg.Allow, apiName, _apiPath));
                return fields.ToArray();
            }

            var parts = jg.ElementParts;
            if (parts == null || parts.Count == 0)
                throw new WinboxFieldResolutionException(
                    $"WinBox native: field '{apiName}' on '{_apiPath}' is a list whose element type "
                    + $"('{jg.ElementUiType ?? "?"}') has no addressable parts in the catalog, so an element "
                    + "cannot be encoded. Use an Api/REST/CLI connection for this field.");

            if (parts.Count == 1)
            {
                fields.AddRange(EncodeElementPart(parts[0], element, apiName, resolveRef));
                return fields.ToArray();
            }

            // A tuple: its parts in .jg order, joined by the tuple's separator. Fewer pieces than parts is
            // accepted because types.tuple.tostr omits a part that renders empty (an optional port, a
            // missing range end); MORE is a value this element cannot hold, and is refused rather than
            // truncated.
            string sep = jg.ElementSeparator ?? "/";
            string[] pieces = sep.Length > 0
                ? element.Split(new[] { sep }, StringSplitOptions.None)
                : new[] { element };
            if (pieces.Length > parts.Count)
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName} (element '{element}' has "
                    + $"{pieces.Length} parts, the field holds {parts.Count})");
            for (int i = 0; i < pieces.Length; i++)
            {
                string piece = pieces[i].Trim();
                if (piece.Length == 0) continue;
                fields.AddRange(EncodeElementPart(parts[i], piece, apiName, resolveRef));
            }
            return fields.ToArray();
        }

        /// <summary>
        /// Encodes one PART of a list element at its own sub-key, by the part's own <c>.jg</c> type — the
        /// write side of <c>WinboxRecordCodec.FormatElementPart</c>, and deliberately the same rules the
        /// scalar encoders above apply to a field of that type.
        /// </summary>
        private List<byte[]> EncodeElementPart(WinboxJgElementPart part, string value, string apiName,
            Func<int[], string, int?>? resolveRef)
        {
            var fields = new List<byte[]>();

            // A union carries one logical value under a per-family key and the element holds exactly ONE of
            // them, so the alternatives are tried in .jg order and the first that can hold the value wins —
            // the mirror of types.union.get, which reads back the first one present.
            if (part.Alternatives != null)
            {
                foreach (var alt in part.Alternatives)
                {
                    try { return EncodeElementPart(alt, value, apiName, resolveRef); }
                    catch (WinboxFieldValueException) { /* not this family — try the next */ }
                }
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName} (element '{value}')");
            }

            // A static map or a dropdown decides the value before its wire type does, exactly as on the way
            // back: the API writes the WORD, and the number under it is an index or a record id.
            if (part.EnumMap != null)
                foreach (var kv in part.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        fields.Add(EncodeU32(part.Key, unchecked((uint)kv.Key)));
                        return fields;
                    }
            if (part.RefHandler != null && resolveRef != null && !long.TryParse(value, out _))
            {
                int? id = resolveRef(part.RefHandler, value);
                if (id == null)
                    throw new WinboxFieldValueException(
                        $"input does not match any value of {apiName} (element '{value}')");
                fields.Add(EncodeU32(part.Key, (uint)id.Value));
                return fields;
            }

            switch ((part.UiType ?? "").ToLowerInvariant())
            {
                case "ipaddr":
                {
                    uint? ip = PackIpV4(value.Split('/')[0]);
                    if (ip == null) throw NotThisPart(apiName, value);
                    fields.Add(EncodeU32(part.Key, ip.Value));
                    return fields;
                }
                case "ip6addr":
                {
                    byte[]? v6 = PackIpV6(value.Split('/')[0]);
                    if (v6 == null) throw NotThisPart(apiName, value);
                    fields.Add(M2Message.Addr6Sys(part.Key, v6));
                    return fields;
                }
                case "macaddr":
                {
                    if (!TryParseMac(value, out byte[]? mac)) throw NotThisPart(apiName, value);
                    fields.Add(M2Message.RawSys(part.Key, mac!)); // non-null: TryParseMac sets it before true
                    return fields;
                }
                case "macnetwork":
                {
                    // Six bytes and six mask bytes (types.macnetwork.put writes val[0] to id and val[1] to
                    // maskid). A bare MAC is the all-ones mask - the exact address - which is also the form
                    // RouterOS prints it back in.
                    var mp = value.Split('/');
                    if (!TryParseMac(mp[0], out byte[]? mac)) throw NotThisPart(apiName, value);
                    fields.Add(M2Message.RawSys(part.Key, mac!)); // non-null: TryParseMac sets it before true
                    if (part.MaskKey != 0)
                    {
                        byte[] mask = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                        if (mp.Length > 1 && !TryParseMac(mp[1], out mask!)) throw NotThisPart(apiName, value);
                        fields.Add(M2Message.RawSys(part.MaskKey, mask));
                    }
                    return fields;
                }
                case "network":
                {
                    var np = value.Split('/');
                    uint? addr = PackIpV4(np[0]);
                    if (addr == null) throw NotThisPart(apiName, value);
                    fields.Add(EncodeU32(part.Key, addr.Value));
                    if (part.MaskKey != 0)
                        fields.Add(EncodeU32(part.MaskKey,
                            np.Length > 1 ? MaskFrom(np[1]) : 0xFFFFFFFFu));
                    return fields;
                }
                case "network6":
                {
                    // The sibling of a network6 holds the PREFIX LENGTH itself, not a netmask
                    // (types.network6.tostr: addr + '/' + (val[1]||0)), and a bare address means /128.
                    var np = value.Split('/');
                    byte[]? v6 = PackIpV6(np[0]);
                    if (v6 == null) throw NotThisPart(apiName, value);
                    fields.Add(M2Message.Addr6Sys(part.Key, v6));
                    if (part.MaskKey != 0)
                    {
                        if (np.Length > 1 && !int.TryParse(np[1], out int declared))
                            throw NotThisPart(apiName, value);
                        else
                            fields.Add(EncodeU32(part.MaskKey,
                                np.Length > 1 ? unchecked((uint)int.Parse(np[1], CultureInfo.InvariantCulture)) : 128u));
                    }
                    return fields;
                }
                case "interval":
                {
                    if (!TryParseDuration(value, 1, out long ticks)) throw NotThisPart(apiName, value);
                    fields.Add(EncodeU32(part.Key, unchecked((uint)ticks)));
                    return fields;
                }
                case "string":
                case "secret":
                    fields.Add(M2Message.StringSys(part.Key, value));
                    return fields;
                default:
                {
                    // number / enm / numberrange low bound / tristate — all plain numbers on the wire once
                    // the map and the dropdown above have had their turn.
                    if (!long.TryParse(value, out long n)) throw NotThisPart(apiName, value);
                    fields.Add(EncodeU32(part.Key, unchecked((uint)n)));
                    return fields;
                }
            }
        }

        // A part that cannot hold this text. Thrown as a VALUE error because a union catches it to try the
        // next family, and because a caller's typo is what it usually is.
        private static WinboxFieldValueException NotThisPart(string apiName, string value)
            => new WinboxFieldValueException($"input does not match any value of {apiName} (element '{value}')");

        /// <summary>
        /// The handler of the table a field takes its bit-set MEMBERS from, or <c>null</c> when the field is
        /// not a table-backed bit set. Lets a caller read that table before encoding instead of blocking
        /// inside the encoder (see <c>WinboxReferenceTables.PrimeMemberTableAsync</c>).
        /// </summary>
        internal int[]? BitSetMemberTable(string apiName)
        {
            if (JgFields == null) return null;
            if (!JgFields.TryGetValue(AliasToJg(CanonicalInputName(apiName)), out var jg) || jg == null)
                return null;
            if (jg.EnumMap != null || jg.RefHandler == null) return null;
            switch ((jg.UiType ?? "").ToLowerInvariant())
            {
                case "set":
                case "multibits":
                case "multitristate":
                    return jg.RefHandler;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Encodes the elements of a scalar list field into the one array TLV its wire type calls for, each
        /// element converted by the ELEMENT's <c>.jg</c> type — the write side of
        /// <c>WinboxRecordCodec.FormatMultiList</c>.
        /// </summary>
        /// <remarks>
        /// An element that cannot be converted is an error, never a dropped element: a shorter list is a
        /// request the router accepts without complaint, so "dns-server=8.8.8.8,typo" would set one server
        /// and report success.
        /// </remarks>
        private byte[] EncodeScalarArray(WinboxJgField jg, int key, List<string> items, string apiName)
        {
            switch (jg.WireType)
            {
                case M2Type.StringArray:
                    return M2Message.StringArraySys(key, items);
                case M2Type.RawArray:
                    return M2Message.RawArraySys(key, items.Select(ParseRaw).ToList());
                case M2Type.Addr6Array:
                    return M2Message.Addr6ArraySys(key, items.Select(t =>
                        PackIpV6(t.Split('/')[0]) ?? throw new WinboxFieldValueException(
                            $"input does not match any value of {apiName} (element '{t}')")).ToList());
                case M2Type.U64Array:
                {
                    // webfig `types.multibignumber = inherit(types.multinumber)`: the same flat array, one
                    // element width wider. Its elements are numbers (`bignumber` and `bigbitrate` both sit on
                    // types.number), so there is no reference or enum rule to apply — and a value that is not
                    // one is an error rather than a dropped element, as everywhere else in this method.
                    var big = new List<ulong>();
                    foreach (string t in items)
                    {
                        if (!ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u))
                            throw new WinboxFieldValueException(
                                $"input does not match any value of {apiName} (element '{t}')");
                        big.Add(u);
                    }
                    return M2Message.U64ArraySys(key, big);
                }
                case M2Type.U32Array:
                {
                    var nums = new List<int>();
                    foreach (string t in items)
                    {
                        // An 'ipaddr' element is the same u32 a scalar ipaddr is; anything else goes through
                        // the ordinary element rules (reference → static map → literal).
                        if (string.Equals(jg.ElementUiType, "ipaddr", StringComparison.OrdinalIgnoreCase))
                        {
                            uint? ip = PackIpV4(t.Split('/')[0]);
                            if (ip == null)
                                throw new WinboxFieldValueException(
                                    $"input does not match any value of {apiName} (element '{t}')");
                            nums.Add(unchecked((int)ip.Value));
                        }
                        else nums.Add(EncodeListElement(jg, t, apiName, null));
                    }
                    return M2Message.U32ArraySys(key, nums.ToArray());
                }
                default:
                    throw new WinboxFieldResolutionException(
                        $"WinBox native: field '{apiName}' on '{_apiPath}' is a list of '{M2Types.JgName(jg.WireType)}', " +
                        "which is not yet encodable over native WinBox M2 writes. Use an Api/REST/CLI " +
                        "connection for this field.");
            }
        }

        // True for a list/array field that EncodeField has no specific encoder for (so it would otherwise be
        // silently mis-sent as a scalar string). An array wire type, or a 'multi…' UI type — the
        // WinBox multi-value controls (multitristatearray, …). multinumberrange, numberrangelist and
        // multinumber are handled before this check, so they never reach here.
        private static bool IsUnsupportedListType(M2Type wireType, string? uiType)
            => wireType.IsArray()
               || (uiType != null && uiType.StartsWith("multi", StringComparison.OrdinalIgnoreCase)
                   && !IsScalarDespiteMultiPrefix(uiType));

        // The one 'multi…' UI type that is NOT a list: webfig declares
        // `types.multilinestring = inherit(types.string)` and overrides only its VIEW (a text area instead of
        // a one-line input) — every other multi* inherits `types.multi`. Reading the prefix as "list" refused
        // /system/note's 'note' field as unencodable when it is a plain string.
        // The list types whose members carry their own '!' (webfig multitristate / multitristatearray), as
        // opposed to a scalar whose leading '!' is the field-wide `not` container flag.
        internal static bool IsPerMemberNegatedList(string? uiType)
            => string.Equals(uiType, "multitristate", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uiType, "multitristatearray", StringComparison.OrdinalIgnoreCase);

        private static bool IsScalarDespiteMultiPrefix(string uiType)
            => string.Equals(uiType, "multilinestring", StringComparison.OrdinalIgnoreCase);
    }
}
