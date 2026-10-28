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
    internal sealed partial class WinboxRecordCodec
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog;

        // id → name cache per referenced table, built lazily from one getall — this avoids a getall per
        // referenced field per row. Names are stable between WRITES, not for a whole session: a row added or
        // renamed through this same connection is a row the cache has never seen, and the reference decoded
        // to the raw id instead of the name. Every write drops the cache (see ForgetReferenceNames), which
        // is the only event that can make it wrong.
        private readonly Dictionary<string, Dictionary<int, string>> _refNameCache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        // Guards the cache: a multiplexed connection can decode two commands' rows at once, and
        // PrimeReferencesAsync writes from the awaited path while a synchronous decode may be reading.
        private readonly object _refNameCacheLock = new object();

        // The router's uptime clock, the origin every `age` value counts from. Handler and key are webfig's
        // own (fetchBoardInfo posts a get-singleton to [24,2] and reads u1).
        private static readonly int[] BoardInfoHandler = { 24, 2 };
        private const int BoardUptimeKey = 0x1;
        // The same singleton's hundredths counter - see RouterBootEpochHundredths.
        private const int BoardJiffiesKey = 0x2E;

        // The router's own wall clock: the clock singleton [24,0] carries the current time as epoch seconds
        // at key 0x7, already in the router's timezone - the same frame every absolute dateandtime on the
        // wire rides in. Read from the ROUTER rather than taken from the local machine on purpose: a client
        // whose clock is wrong would otherwise date every relative field by its own error.
        private static readonly int[] ClockHandler = { 24, 0 };
        private const int ClockNowKey = 0x7;

        private readonly object _uptimeLock = new object();
        private long? _uptimeAtFetch;
        private long? _routerNowSeconds;
        private DateTime _clockFetchedAt;
        private bool _routerClockUnreadable;
        private long? _jiffiesAtFetch;
        private DateTime _boardFetchedAt;
        private long? _bootEpochHundredths;
        // Set together with _uptimeAtFetch in RouterUptimeSeconds; only read there, guarded by
        // _uptimeAtFetch.HasValue, so it is never read before that first assignment.
        private Stopwatch _sinceUptimeFetch = null!;
        private bool _uptimeUnreadable;

        // The Unix epoch, the origin every dateandtime/clockdate value on the wire counts from.
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        // The per-row note RouterOS prints as .about; see DecodeRecord.
        internal const int AboutKey = 0xFE001C;
        internal const string AboutField = ".about";

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
            IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? numFlags = null,
            IReadOnlyDictionary<string, string[]>? extraSpellings = null)
            => DecodeRecord(rec, keyToName, keyToField, null, derivedBools, numFlags, extraSpellings);

        // The decode proper. collectRefTables != null puts it in COLLECTING mode: reference names are not
        // looked up, the tables they would have needed are noted instead, and the fields it returns are
        // thrown away. See PrimeReferencesAsync — that mode is how the awaited path learns what to fetch.
        private Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName,
            IReadOnlyDictionary<int, WinboxJgField> keyToField,
            Dictionary<string, int[]>? collectRefTables,
            IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? numFlags = null,
            IReadOnlyDictionary<string, string[]>? extraSpellings = null)
        {
            // Keys consumed by an owning field, not emitted on their own: a network field's netmask sibling,
            // and the opt/not flag bools of an optional/invertible field (its value rides on the leaf key).
            var consumedKeys = new HashSet<int>();
            if (keyToField != null)
                foreach (var f in keyToField.Values)
                {
                    if (f.UiType == "network" && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
                    // The port half of an address:port field is not a field of its own to the API.
                    if ((f.UiType == WinboxFieldResolver.AddrPortUiType || f.UiType == "ipaddrandport") && f.MaskKey != 0)
                        consumedKeys.Add(f.MaskKey);
                    // Nor is the netmask half of a number,netmask field.
                    if (f.UiType == WinboxFieldResolver.NumberNetmaskUiType && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
                    // Nor is the high end of a scalar number range (WinboxJgCatalog.HighKeyOf).
                    if (f.UiType == "numberrange" && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
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
                    // Nor are the other boxes of a firewall limit group, which the catalog files under fields named
                    // after the boxes: a row native wrote carries the period u11, and the window's 'limit' tuple took
                    // the name from it when the frame listed u11 first.
                    foreach (int box in WinboxFieldResolver.FirewallLimitBoxKeys(f))
                        consumedKeys.Add(box);
                    // An opt/not flag is consumed only when no OTHER field owns its key. Two windows share a
                    // handler, and a field of the one this path does not address can still sit in the map for
                    // its own key: /ip/upnp's interface list declares 'Forced External IP' {opt b3, ipaddr u4},
                    // and its flag key swallowed the settings singleton's 'Show Dummy Rule' b3 (7.24.2).
                    if (f.OptKey != 0 && !OwnedByAnotherField(keyToField, keyToName, f, f.OptKey)) consumedKeys.Add(f.OptKey);
                    if (f.NotKey != 0 && !OwnedByAnotherField(keyToField, keyToName, f, f.NotKey)) consumedKeys.Add(f.NotKey);
                }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rec)
            {
                // A record may carry one key TWICE — a scalar and a list (see M2Message.ParseAllFields) — so
                // ask the arrayness-qualified registration first and fall back to the plain one. The fallback
                // is the normal path: a window with no such collision registers no qualified key, and a router
                // that sends a field with the arrayness the .jg did NOT predict still decodes as it always did.
                int wireKey = WinboxM2Protocol.TypedKey.WireKeyOf(kv.Key);
                int typedKey = WinboxM2Protocol.TypedKey.Qualify(wireKey, kv.Value.Item1);
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
                // One key, two kinds, and this entry is not the kind the field is: the other entry, filed under its
                // qualified key, is the field's value. /interface/vrrp sends a bool and the connection-tracking-mode
                // u32 both on 0x17, and which one the parser meets first depends on the frame.
                if (jf != null && WinboxM2Protocol.TypedKey.KindOf(jf.WireType) != WinboxM2Protocol.TypedKey.KindOf(kv.Value.Item1)
                    && kv.Key == wireKey && rec.ContainsKey(WinboxM2Protocol.TypedKey.Qualify(wireKey, jf.WireType)))
                    continue;
                if (IsUnsetField(jf, kv.Value.Item2, rec)) continue;
                // A key the router filled with a different KIND of value is not this field on this row.
                // /interface's 0x1001E carries the type NAME as a string on almost every row — and on an
                // ENSLAVED one it carries its master's numeric id instead, because WinBox nests a slave
                // under its master in the list. Reported as `type` that read `474` where the API says
                // `eoip`. A row whose value contradicts the field's declared wire type gets no value at
                // all, which is the honest answer: we do not know this row's type, and saying nothing is
                // not the same as saying a number.
                if (IsWireKindContradiction(jf, kv.Value.Item1))
                {
                    if (TryLearnedKindWord(jf!, rec, out string? kindWord)) { fields[apiName] = kindWord!; continue; }
                    TraceWireKindContradiction(jf!, kv.Value.Item1);
                    continue;
                }
                if (jf != null && jf.KindIdKey != 0) Learn(jf, rec, kv.Value.Item2);
                fields[apiName] = Negated(jf, rec, FormatTyped(jf, kv.Value.Item1, kv.Value.Item2, rec, collectRefTables));
            }

            // The comment key rides every record, empty on a row with no comment, where the API prints nothing.
            if (fields.TryGetValue(TikEmptyComment.Field, out string? comment) && TikEmptyComment.IsNoComment(TikEmptyComment.Field, comment))
                fields.Remove(TikEmptyComment.Field);

            // The router's note on the row — the API's .about ("No IP address on interface" on a DHCP server
            // whose interface has none, 7.24.2). It rides every record as the string list 0xFE001C, empty on a
            // row with nothing to say, and no window names it.
            if (!fields.ContainsKey(AboutField) && rec.TryGetValue(AboutKey, out var about)
                && about.Item2 is string aboutText)
            {
                string note = aboutText.StartsWith("[", StringComparison.Ordinal) && aboutText.EndsWith("]", StringComparison.Ordinal)
                    ? aboutText.Substring(1, aboutText.Length - 2) : aboutText;
                if (note.Length > 0) fields[AboutField] = note;
            }

            // A bool the API reports and WinBox renders as a wider enum on the same wire field (see
            // FieldAliasSet.DerivedBools). Added AFTER the loop because it reads a decoded field, not a key,
            // and only when the source is actually present: a row that did not carry it gets no bool rather
            // than a false, which would be us asserting something the router did not say.
            // A numflag's key carries a SET of row flags, and the row IS the member its value names — the
            // API prints that one (`connect=true`) and nothing for the others, so neither does this. The
            // members come from the version-matched catalog, so a flag a RouterOS version does not have is
            // one this decode cannot invent; which member names are API flags at all is the path's own list
            // (see WinboxFieldResolver.BuildNumFlags).
            if (numFlags != null)
                foreach (var nf in numFlags)
                {
                    if (!rec.TryGetValue(nf.Key, out var flagVal)) continue;
                    if (!WinboxFieldResolver.TryToInt64(flagVal.Item2, out long n)) continue;
                    if (n < int.MinValue || n > int.MaxValue) continue;
                    if (!nf.Value.TryGetValue((int)n, out string? flagName)) continue;
                    if (!fields.ContainsKey(flagName)) fields[flagName] = "true";
                }

            // The read-only status half of a list element, which the 6.x API prints as a field of its own
            // (see WinboxJgField.ElementStatusParts): gateway-status=192.168.4.1 reachable via  ether1.
            if (keyToField != null)
                foreach (var kf in keyToField)
                {
                    if (kf.Value.ElementStatusParts == null || kf.Value.ElementParts == null) continue;
                    if (!keyToName.TryGetValue(kf.Key, out string? listName)) continue;
                    string statusName = listName + "-status";
                    if (fields.ContainsKey(statusName)) continue;
                    if (!rec.TryGetValue(kf.Key, out var listVal)
                        || !(listVal.Item2 is List<Dictionary<int, Tuple<string, object>>> elements)) continue;
                    fields[statusName] = string.Join(",", elements.Select(m =>
                        FormatCompoundElement(kf.Value, m, collectRefTables)
                        + FormatElementStatus(kf.Value.ElementStatusParts, m, collectRefTables)));
                }

            if (derivedBools != null)
                foreach (var d in derivedBools)
                {
                    if (fields.ContainsKey(d.Key)) continue;
                    if (!fields.TryGetValue(d.Value.Item1, out string? src)) continue;
                    fields[d.Key] = string.Equals(src, d.Value.Item2, StringComparison.OrdinalIgnoreCase)
                        ? "true" : "false";
                }

            // The other word RouterOS uses for a field it renamed between versions — reported beside the
            // catalog's own name, because the .jg says the same thing on both (FieldAliasSet.AlsoKnownAs).
            if (extraSpellings != null)
                foreach (var e in extraSpellings)
                {
                    if (!fields.TryGetValue(e.Key, out string? value)) continue;
                    foreach (string other in e.Value)
                        if (!fields.ContainsKey(other)) fields[other] = value;
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
        /// (<see cref="WinboxJgField.IsUnsetValue"/>) — unless the field is one RouterOS prints that marker for
        /// as a word (<see cref="SentinelSpelledAsWord"/>: a logging action's unset Syslog Severity is
        /// <c>auto</c>).</para>
        /// <para>The third is a static enum with <c>opt:1</c> carrying a value its map has no member for
        /// (<see cref="WinboxJgField.IsUnmappedOptionalEnum"/>) — an unsigned certificate's
        /// <c>digest-algorithm</c> arrives as <c>0</c> where the API prints nothing. This one is webfig's own
        /// rule, read off <c>types.enm.tostr</c>, and it is why the <c>opt</c> ATTRIBUTE has to be carried
        /// separately from the <c>opt</c> WRAPPER's flag key.</para>
        /// <para>The fourth is a window condition (<c>on:</c>) that is false on this row, for the conditions the
        /// API is measured to honour (<see cref="WinboxJgCondition"/>): a hotspot profile with RADIUS off prints
        /// no <c>radius-*</c> field.</para>
        /// </remarks>
        /// <summary>
        /// Whether <paramref name="key"/> — a flag key of <paramref name="consumer"/> — is the value key of a
        /// different field this path decodes under a name of its own.
        /// </summary>
        private static bool OwnedByAnotherField(IReadOnlyDictionary<int, WinboxJgField> keyToField,
            IReadOnlyDictionary<int, string> keyToName, WinboxJgField consumer, int key)
            => keyToName != null && keyToName.ContainsKey(key)
               && keyToField.TryGetValue(key, out var owner) && owner != null
               && !ReferenceEquals(owner, consumer) && owner.Key == key;

        // A not-wrapped field's own bool is RouterOS's leading '!' on the whole value, whatever the value's type:
        // /ip/firewall/filter src-address=!10.0.0.0/8, in-interface=!ether1, src-address-list=!x. Only the set and
        // number-list cases rendered it, so every negated address, interface or list name read back as its
        // opposite. A per-member list carries its '!'s on the members instead, and a value that already starts
        // with one was negated by its own case.
        private static string Negated(WinboxJgField? jf, Dictionary<int, Tuple<string, object>> rec, string text)
            => jf != null && jf.NotKey != 0 && text.Length > 0 && !text.StartsWith("!", StringComparison.Ordinal)
               && !WinboxFieldResolver.IsPerMemberNegatedList(jf.UiType)
               && rec.TryGetValue(jf.NotKey, out var flag) && flag.Item2 is bool negated && negated
                ? "!" + text : text;

        /// <summary>
        /// Whether a <c>macaddr</c> field's value is a MAC at all: six raw bytes, as a <c>byte[]</c>, as the
        /// twelve hex digits M2Message renders an FT_RAW in, or already separated by colons.
        /// </summary>
        /// <remarks>
        /// The generic <c>/interface</c> window's 'MAC Address' key is shared by every interface type, and a
        /// type without a MAC puts something else under it: a WireGuard interface its public key (a string), a
        /// GRE or IPIP tunnel a u32 <c>0</c> (7.24.4). The API prints no <c>mac-address</c> on those rows, and
        /// decoding the key anyway reported the public key as the interface's MAC. A value that is not a MAC is
        /// the router saying the field does not apply.
        /// </remarks>
        private static bool IsSixByteRaw(object value)
        {
            if (value is byte[] b) return b.Length == 6;
            string s = (value as string ?? "").Replace(":", "");   // an already separated MAC is a MAC too
            if (s.Length != 12) return false;
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        private static bool IsUnsetField(WinboxJgField? jf, object value,
            Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf == null) return false;
            if (jf.OptKey != 0 && rec.TryGetValue(jf.OptKey, out var opt)
                && opt?.Item2 is bool present && !present)
                return true;
            if (IsAnotherKindsField(jf, rec)) return true;
            if (jf.Condition != null && jf.Condition.IsFalseOn(rec)) return true;
            if (jf.UiType == "macaddr" && !IsSixByteRaw(value)) return true;
            if (WinboxFieldResolver.TryToInt64(value, out long n))
            {
                // A marker RouterOS prints as a word is a value, whichever of the two rules below would drop it.
                if (TrySentinelWord(jf, value, out _)) return false;
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
        /// <para>A record that does not carry the selector at all has no kind, and no pane is live on it: a
        /// mangle rule with no <c>action</c> prints no <c>passthrough</c> over the API or the CLI (7.24.4), while
        /// its M2 record carries the key. A selector that is present but not a number is left alone — there is
        /// no honest way to say which pane it selects.</para>
        /// </remarks>
        private static bool IsAnotherKindsField(WinboxJgField jf, Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf.PaneSelectorKey == 0 || jf.PaneValues == null || jf.PaneValues.Length == 0) return false;
            if (!rec.TryGetValue(jf.PaneSelectorKey, out var sel) || sel?.Item2 == null) return true;
            if (!WinboxFieldResolver.TryToInt64(sel.Item2, out long kind)) return false;
            return jf.IsForeignPane(kind);
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
            {
                if (jf?.RefHandler == null) continue;
                foreach (int[] table in jf.RefHandlers ?? new[] { jf.RefHandler })
                    if (CachedRefNames(string.Join(",", table)) == null) { anyUncached = true; break; }
                if (anyUncached) break;
            }
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
            var map = new Dictionary<int, string>();
            var (nameKey, nameValKey) = RefNameKeys(_catalog, refHandler);
            foreach (var r in rows)
                // TryReadIdAndName only returns true after successfully assigning a non-null name.
                if (TryReadIdAndName(r, nameKey, out int rowId, out string? rowName)
                    || TryReadIdAndName(r, nameValKey, out rowId, out rowName))
                    map[rowId] = rowName!;
            return map;
        }

        /// <summary>
        /// The two keys a referenced row's name can sit at: the table's <c>name</c> field, and the field its
        /// window declares as <c>nameval</c> (<c>-1</c> for either it has none).
        /// </summary>
        /// <remarks>
        /// <c>name</c> is tried first, so every reference that resolved before resolves the same way. The
        /// <c>nameval</c> key is the fallback for a row that does not carry it — and it cannot be decided from
        /// the catalog alone, because the resolver seeds <c>name</c> at <c>0x10006</c> for a table whose window
        /// has no Name box. <c>/queue/type</c> is one: its rows are named by 'Type Name' at <c>0x16</c>, the
        /// seeded key was never in them, and <c>/queue/simple</c>'s queue read back as <c>*FFFFFFFA</c> where the
        /// API says pcq-upload-default (7.24.2). The bit-set member map (<see cref="BuildMemberMap"/>) already
        /// read <c>nameval</c>.
        /// </remarks>
        internal static (int nameKey, int nameValKey) RefNameKeys(WinboxJgCatalog catalog, int[] refHandler)
        {
            var keyToApiName = new WinboxFieldResolver(null, refHandler, catalog, EmptyOverrides).BuildKeyToApiName();
            int nameKey = NameKeyOf(keyToApiName);
            int nameValKey = -1;
            string? nameVal = catalog.GetNameValField(refHandler);
            if (nameVal != null)
                foreach (var kv in keyToApiName)
                    if (string.Equals(kv.Value, nameVal, StringComparison.OrdinalIgnoreCase)
                        && !WinboxM2Protocol.TypedKey.IsQualified(kv.Key))
                    { nameValKey = kv.Key; break; }
            return (nameKey, nameValKey);
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

        /// <summary>
        /// Drops the id → name cache. Called after any write: an add, a rename or a remove changes what the
        /// referenced tables contain, and a stale entry does not read as an error — the reference decodes to
        /// the bare id, which looks like a value.
        /// </summary>
        internal void ForgetReferenceNames()
        {
            lock (_refNameCacheLock) _refNameCache.Clear();
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
    }
}
