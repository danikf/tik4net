using System;
using System.Collections.Generic;
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
            IReadOnlyDictionary<int, WinboxJgField> keyToField)
            => DecodeRecord(rec, keyToName, keyToField, null);

        // The decode proper. collectRefTables != null puts it in COLLECTING mode: reference names are not
        // looked up, the tables they would have needed are noted instead, and the fields it returns are
        // thrown away. See PrimeReferencesAsync — that mode is how the awaited path learns what to fetch.
        private Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName,
            IReadOnlyDictionary<int, WinboxJgField> keyToField,
            Dictionary<string, int[]> collectRefTables)
        {
            // Keys consumed by an owning field, not emitted on their own: a network field's netmask sibling,
            // and the opt/not flag bools of an optional/invertible field (its value rides on the leaf key).
            var consumedKeys = new HashSet<int>();
            if (keyToField != null)
                foreach (var f in keyToField.Values)
                {
                    if (f.UiType == "network" && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
                    if (f.OptKey != 0) consumedKeys.Add(f.OptKey);
                    if (f.NotKey != 0) consumedKeys.Add(f.NotKey);
                }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rec)
            {
                if (consumedKeys.Contains(kv.Key)) continue;
                if (!keyToName.TryGetValue(kv.Key, out var apiName)) continue;
                if (fields.ContainsKey(apiName)) continue;

                if (apiName == TikSpecialProperties.Id)
                {
                    fields[apiName] = FormatId(kv.Value.Item2);
                    continue;
                }

                WinboxJgField jf = null;
                keyToField?.TryGetValue(kv.Key, out jf);
                if (IsUnsetField(jf, kv.Value.Item2, rec)) continue;
                fields[apiName] = FormatTyped(jf, kv.Value.Item1, kv.Value.Item2, rec, collectRefTables);
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
        /// <para>The other is the u32 unset marker on a field that declares it as its default
        /// (<see cref="WinboxJgField.IsUnsetValue"/>): a logging action's <c>Syslog Severity</c> arrives as
        /// 4294967295 on a row where the API prints no <c>syslog-severity</c>.</para>
        /// </remarks>
        private static bool IsUnsetField(WinboxJgField jf, object value,
            Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf == null) return false;
            if (jf.OptKey != 0 && rec.TryGetValue(jf.OptKey, out var opt)
                && opt?.Item2 is bool present && !present)
                return true;
            return jf.Def.HasValue && WinboxFieldResolver.TryToInt64(value, out long n) && jf.IsUnsetValue(n);
        }

        // Format an M2 value to its RouterOS API text using the .jg UI-semantic type: IPs unpack from u32,
        // a network field renders "addr/prefixlen" (pulling the netmask from its maskid sibling key), MACs
        // from raw bytes, static enums back to their string label, dynamic enum references back to the
        // referenced record's name. Falls back to the wire-type formatter.
        private string FormatTyped(WinboxJgField jf, string wireType, object value,
            Dictionary<int, Tuple<string, object>> rec, Dictionary<string, int[]> collectRefTables)
        {
            if (jf != null && value != null)
            {
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
                    case "macaddr":
                        return WinboxFieldResolver.MacFromBytes(value);
                    case "ip6addr":
                        return value is byte[] v6 ? WinboxFieldResolver.IpV6FromBytes(v6) : value.ToString();
                    case "addr":
                        // The compound webfig 'addr' object, read back the way types.addr.tostr renders it:
                        // by SUB-KEY, not by position. The generic nested-message fallback returns the
                        // first member, which is right only when IPv4 is the one present.
                        if (value is Dictionary<int, Tuple<string, object>> addrMsg)
                            return FormatAddr(addrMsg);
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
                    case "set":
                    {
                        // Bitmask flag set → comma-joined labels (.jg map key = bit index). The opt/not flag
                        // keys are consumed separately in DecodeRecord, so only the value rides here. A set
                        // 'not' flag (key NotKey) renders as the RouterOS '!' negation prefix on the whole
                        // value (CLI/API form, e.g. "!established,related").
                        if (jf.EnumMap == null) break;
                        if (!WinboxFieldResolver.TryToInt64(value, out long bits))
                        {
                            // Falls through to the raw wire text, which is visibly not a label list — but a
                            // silent fall-through is how a .jg/router mismatch goes unnoticed for a release
                            // (P2.25), so say so on the trace channel.
                            TraceNonNumeric("set", value);
                            break;
                        }
                        var labels = jf.EnumMap.Where(kv => (bits & (1L << kv.Key)) != 0)
                            .OrderBy(kv => kv.Key).Select(kv => kv.Value);
                        string joined = string.Join(",", labels);
                        bool negated = jf.NotKey != 0 && rec.TryGetValue(jf.NotKey, out var nt)
                            && nt.Item2 is bool nb && nb;
                        return negated ? "!" + joined : joined;
                    }
                }
                // A LIST of dynamic-enum references (webfig 'multinumber' whose element type is an enm): the
                // value is a u32[] of referenced ids, which the API renders as their comma-joined names —
                // e.g. the log's topics u32[9,3] → "script,error". Falls back to the raw text when the
                // referenced table cannot be read, exactly as the scalar case does.
                if (jf.RefHandler != null && IsMultiNumberList(jf.UiType))
                {
                    string joined = ResolveRefNameList(jf.RefHandler, value, collectRefTables);
                    if (joined != null) return joined;
                }
                // The same list shape with LITERAL elements — a number (/ip/proxy 'Port' u32[8080] → "8080")
                // or a static enum (/ip/ssh 'Ciphers' u32[0] → "auto"). Without this the wire form reached the
                // caller verbatim as "[8080]"/"[0]"; the API prints one value per element, comma-joined.
                if (IsMultiNumberList(jf.UiType))
                    return FormatNumberList(value, jf.EnumMap);
                // dynamic enum reference: render the referenced object's name (e.g. interface id → "ether1").
                if (jf.RefHandler != null)
                {
                    string name = ResolveRefName(jf.RefHandler, value, collectRefTables);
                    if (name != null) return name;
                }
                // static enum: map the numeric value back to its API string label.
                if (jf.EnumMap != null)
                {
                    if (WinboxFieldResolver.TryToInt64(value, out long ev))
                    {
                        if (jf.EnumMap.TryGetValue(unchecked((int)ev), out var label)) return label;
                    }
                    else TraceNonNumeric("enum", value);   // falls through to the raw text, see above
                }
            }
            return FormatValue(wireType, value);
        }

        // webfig list types whose ELEMENT is a reference (types.multinumber and everything inheriting it).
        // multinumberrange/numberrangelist are ranges of literal numbers, not references, and are formatted
        // before this point.
        private static bool IsMultiNumberList(string uiType)
            => string.Equals(uiType, "multinumber", StringComparison.OrdinalIgnoreCase);

        // Resolve a u32[] of referenced ids (rendered by M2Message as "[a,b,…]") to comma-joined names.
        // Returns null when nothing resolved, so the caller can fall back to the raw text rather than
        // hand back an empty string that reads like "no topics".
        private string ResolveRefNameList(int[] refHandler, object value,
            Dictionary<string, int[]> collectRefTables)
        {
            var names = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(value?.ToString() ?? "", @"-?\d+"))
            {
                string n = ResolveRefName(refHandler, m.Value, collectRefTables);
                if (n == null) return null;   // table unreadable / id unknown — keep the raw form
                names.Add(n);
            }
            return names.Count > 0 ? string.Join(",", names) : null;
        }

        // Resolve a dynamic-enum reference value (the referenced record's numeric id) back to its name.
        private string ResolveRefName(int[] refHandler, object idValue, Dictionary<string, int[]> collectRefTables)
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
                    map = BuildRefNameMap(kv.Value,
                        await _ops.GetAllAsync(kv.Value, cancellationToken).ConfigureAwait(false));
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
                if (TryReadIdAndName(r, nameKey, out int rowId, out string rowName))
                    map[rowId] = rowName;
            return map;
        }

        /// <summary>
        /// The M2 key of a table's <c>name</c> field, or -1 when it has none.
        /// </summary>
        internal static int NameKeyOf(IReadOnlyDictionary<int, string> keyToApiName)
        {
            if (keyToApiName != null)
                foreach (var kv in keyToApiName)
                    if (kv.Value == "name") return kv.Key;
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
            Dictionary<int, Tuple<string, object>> row, int nameKey, out int id, out string name)
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

        private Dictionary<int, string> CachedRefNames(string key)
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
            return value.ToString();
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

        /// <summary>
        /// Renders a webfig <c>addr</c> compound (a nested message with one member per address form) as the
        /// RouterOS text, following <c>types.addr.tostr</c>: IPv6 wins over IPv4 when both are present, then
        /// the DNS name, then the MAC, with the <c>/prefix</c> appended.
        /// </summary>
        private static string FormatAddr(Dictionary<int, Tuple<string, object>> addr)
        {
            object Get(int subKey) =>
                addr.TryGetValue(subKey, out var t) ? t?.Item2 : null;

            string text = null;
            if (Get(WinboxFieldResolver.AddrV6SubKey) is byte[] v6) text = WinboxFieldResolver.IpV6FromBytes(v6);
            else if (Get(WinboxFieldResolver.AddrV4SubKey) is object v4) text = WinboxFieldResolver.IpFromU32(v4);
            else if (Get(WinboxFieldResolver.AddrDnsSubKey) is object dns) text = dns.ToString();
            else if (Get(WinboxFieldResolver.AddrMacSubKey) is object mac) text = WinboxFieldResolver.MacFromBytes(mac);
            if (text == null) return FormatNestedMessage(addr);

            if (Get(WinboxFieldResolver.AddrPrefixSubKey) is object plen) text += "/" + plen;
            return text;
        }

        private static string FormatValue(string wireType, object value)
        {
            if (value == null) return "";
            if (wireType == "bool") return (value is bool b && b) ? "true" : "false";
            // An FT_ADDR6 value arrives as 16 bytes; without this it renders as "System.Byte[]" whenever the
            // .jg does not also give the field an ip6addr UI type.
            if (wireType == "ip6" && value is byte[] v6) return WinboxFieldResolver.IpV6FromBytes(v6);
            if (value is Dictionary<int, Tuple<string, object>> one) return FormatNestedMessage(one);
            if (value is List<Dictionary<int, Tuple<string, object>>> many)
                return string.Join(",", many.Select(FormatNestedMessage));
            return value.ToString();
        }

        /// <summary>
        /// Renders a nested M2 submessage (a webfig <c>multi</c> element, or a wrapper such as the traceroute
        /// window's per-hop <c>Host</c>) as its first present inner value.
        /// </summary>
        /// <remarks>
        /// This is what webfig itself does, not a guess: <c>types.multi.tostr</c> walks the array and renders
        /// each element through its single child type, and <c>types.union.get</c> with <c>single:1</c> returns
        /// the first child that is present. Without it the value fell through to <c>object.ToString()</c> and
        /// a caller was handed the literal text
        /// <c>System.Collections.Generic.List`1[System.Collections.Generic.Dictionary`2[…]]</c> as if it were
        /// the field's value — a nested field on any handler could do this, not just traceroute (found while
        /// fixing P2.51). An element the router sent empty renders empty, which is the honest answer.
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
        private static string FormatNumberList(object value, IReadOnlyDictionary<int, string> enumMap)
        {
            var parts = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(value?.ToString() ?? "", @"-?\d+"))
            {
                if (enumMap != null && int.TryParse(m.Value, out int n)
                    && enumMap.TryGetValue(n, out var label))
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
