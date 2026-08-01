using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    /// <summary>
    /// Resolves between RouterOS API field names and numeric WinBox M2 field keys for one handler.
    /// Stability split (per the design): the volatile <c>key↔type</c> mapping comes from the live
    /// <c>.jg</c> catalog (<see cref="WinboxJgCatalog"/>); the stable <c>label↔apiName</c> mapping comes
    /// from a normalizer plus protocol-constant seeds plus session overrides.
    /// </summary>
    /// <remarks>
    /// For the F1 read milestone the important direction is <c>key → apiName</c> (decoding records back
    /// to API field names). The forward direction (<c>apiName → key</c>, for writes) is also exposed for F2.
    /// Ambiguity (an API name that maps to two different keys) throws a clear exception telling the caller
    /// to add a session field override or use a <c>WinboxCli</c> connection instead.
    /// </remarks>
    internal sealed class WinboxFieldResolver
    {
        private readonly int[] _handler;
        private readonly string _apiPath;
        private readonly WinboxJgCatalog _catalog;
        // session overrides apiName → key (highest priority)
        private readonly IReadOnlyDictionary<string, int> _overrides;
        // when true, a field name that does not resolve verbatim is retried through NormalizeLabel
        // (GUI-name addressing: "MAC Address"/"MAC_Address" → "mac-address"). Opt-in per connection.
        private readonly bool _useGuiNames;

        internal WinboxFieldResolver(string apiPath, int[] handler, WinboxJgCatalog catalog,
            IReadOnlyDictionary<string, int> overrides, bool useGuiNames = false)
        {
            _apiPath = apiPath;
            _handler = handler;
            _catalog = catalog;
            _overrides = overrides ?? new Dictionary<string, int>();
            _useGuiNames = useGuiNames;
        }

        // ── Protocol-constant seeds (stable, hardcoded) ────────────────────────
        // Universal system record keys — authoritative for every table (the .jg never lists them as
        // fields). These win over the catalog.
        private static readonly Dictionary<string, int> SystemSeed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [".id"]     = WinboxM2Protocol.RecordKey.Id,      // 0xFE0001
            ["comment"] = WinboxM2Protocol.RecordKey.Comment, // 0xFE0009
        };

        // Common-but-not-universal fallback: most config tables key 'name' at 0x10006, but some (e.g.
        // /ip/hotspot/user) use a different key. The .jg is authoritative, so this only fills in when the
        // catalog has no 'name' field for the handler.
        private static readonly Dictionary<string, int> FallbackSeed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"]     = WinboxM2Protocol.RecordKey.Name,     // 0x10006
            ["disabled"] = WinboxM2Protocol.RecordKey.Disabled, // 0xFE000A (bool, 1=disabled)
        };

        // Wire type for seed fields without a .jg entry (so EncodeField types them correctly).
        private static string SeedWireType(string apiName)
            => string.Equals(apiName, "disabled", StringComparison.OrdinalIgnoreCase) ? "bool" : "string";

        // ── Shipped field aliases (stable API-name ↔ .jg-label text) ───────────
        // Some WinBox windows label fields differently from the RouterOS API (e.g. the Ping window's API
        // 'address' is WinBox 'ping-to'). Only the stable name↔label text is shipped here — the label↔key
        // mapping still comes live from the .jg — exactly the stability split the class doc describes, and the
        // field-level analogue of WinboxHandlerMap.ShippedAlias for paths.
        private sealed class FieldAliasSet
        {
            public readonly IReadOnlyDictionary<string, string> ApiToJg; // API field name → .jg label (encode/resolve)
            public readonly IReadOnlyDictionary<string, string> JgToApi; // .jg label → API field name (decode)
            public readonly IReadOnlyDictionary<int, string> KeyToApi;   // M2 key → API name, for .jg-unnamed fields
            public readonly IReadOnlyDictionary<int, string> KeyUiType;  // M2 key → UI type (e.g. ipaddr), for decode formatting
            public FieldAliasSet(IReadOnlyDictionary<string, string> apiToJg, IReadOnlyDictionary<string, string> jgToApi,
                IReadOnlyDictionary<int, string> keyToApi = null, IReadOnlyDictionary<int, string> keyUiType = null)
            {
                ApiToJg = apiToJg; JgToApi = jgToApi;
                KeyToApi = keyToApi ?? new Dictionary<int, string>();
                KeyUiType = keyUiType ?? new Dictionary<int, string>();
            }
        }

        private static Dictionary<string, string> Ci(params (string, string)[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        private static readonly Dictionary<string, FieldAliasSet> ShippedFieldAliases =
            new Dictionary<string, FieldAliasSet>(StringComparer.OrdinalIgnoreCase)
            {
                // /ping (ToolPing, top-level /ping). 'address'/'host' both ride the WinBox 'ping-to' field; the
                // result row decodes it to the API 'host'. count/size/seq/min/avg/max are likewise relabelled.
                ["/ping"] = new FieldAliasSet(
                    apiToJg: Ci(("address", "ping-to"), ("count", "packet-count"),
                               ("size", "packet-size"), ("seq", "seq-#"),
                               ("min-rtt", "min"), ("avg-rtt", "avg"), ("max-rtt", "max")),
                    jgToApi: Ci(("packet-count", "count"), ("packet-size", "size"),
                               ("seq-#", "seq"), ("min", "min-rtt"), ("avg", "avg-rtt"), ("max", "max-rtt")),
                    // The reply's responder address rides at key 0x1 (u32 ipaddr), which the .jg leaves unnamed —
                    // so its name and ipaddr formatting are supplied here (the value is a packed-u32 IPv4).
                    keyToApi: new Dictionary<int, string> { [0x1] = "host" },
                    keyUiType: new Dictionary<int, string> { [0x1] = "ipaddr" }),

                // /tool/traceroute (ToolTraceroute). The .jg window (type:'query', path:[26]) labels the target
                // 'Traceroute To' and the per-hop responder 'Host'; the API calls both 'address'.
                // 'count'/'max-hops' need no alias — the window labels them exactly that.
                ["/tool/traceroute"] = new FieldAliasSet(
                    apiToJg: Ci(("address", "traceroute-to"), ("size", "packet-size")),
                    jgToApi: Ci(("host", "address"), ("packet-size", "size"))),

                // /tool/wol (ToolWol, standalone 'Wake on LAN' doit window): the API sends 'mac', WinBox labels
                // the same field 'MAC Address'. 'interface' matches verbatim, so only the one alias is needed.
                ["/tool/wol"] = new FieldAliasSet(
                    apiToJg: Ci(("mac", "mac-address")),
                    jgToApi: Ci(("mac-address", "mac"))),

                // /system/identity: the singleton's one field is labelled 'Identity' in WinBox (.jg
                // {title:'Identity',type:'item',path:[24,1],c:[{name:'Identity',id:'sc'},…]}), while the API
                // calls it 'name'. Without the alias a read returned {"identity":…} — so LoadSingle
                // <SystemIdentity> threw "Missing field 'name'" — and a write resolved 'name' through the
                // FallbackSeed to key 0x10006, a key this handler does not have.
                ["/system/identity"] = new FieldAliasSet(
                    apiToJg: Ci(("name", "identity")),
                    jgToApi: Ci(("identity", "name"))),

                // /interface: the .jg 'type' field is the numeric type id (key 0x10001), but RouterOS API exposes
                // 'type' as the type *name* string — which the record also carries at key 0x1001E (e.g. "ether",
                // "loopback"). Map the string key to 'type' and rename the numeric one so they don't collide.
                //
                // The traffic block is mapped BY KEY, because the WinBox labels and the API names disagree in a
                // way that silently swaps a rate for a counter. The window shows two families: 'Rx'/'Tx' and
                // 'Rx Packet'/'Tx Packet' are LIVE RATES (.jg bigbitrate / decimal p/s — what
                // /interface/monitor-traffic reports), while 'Rx Bytes'/'Rx Packets' are the CUMULATIVE
                // counters (.jg bigbytes / bigdecimal — what /interface print reports as rx-byte/rx-packet).
                // Normalizing the labels put the RATE under the API's counter name: ether1 read back
                // rx-byte=5536 where the API says rx-byte=76024833 for the same record, and the real counter
                // arrived as "rx-bytes", a name the API never uses. Naming them by key fixes both directions,
                // and gives /interface/monitor-traffic its field names for free (see TryRunMonitor).
                ["/interface"] = new FieldAliasSet(
                    apiToJg: Ci(),
                    jgToApi: Ci(),
                    keyToApi: new Dictionary<int, string>
                    {
                        [0x1001E] = "type",      [0x10001] = "type-id",
                        // cumulative counters — /interface print
                        [0x100FC] = "rx-byte",   [0x100FD] = "tx-byte",
                        [0x100FE] = "rx-packet", [0x100FF] = "tx-packet",
                        [0x100F8] = "rx-drop",   [0x100F9] = "tx-drop",
                        [0x100FA] = "rx-error",  [0x100FB] = "tx-error",
                        [0x10104] = "tx-queue-drop",
                        // live rates — /interface/monitor-traffic
                        [0x100D3] = "rx-bits-per-second",       [0x100D4] = "tx-bits-per-second",
                        [0x100CB] = "rx-packets-per-second",    [0x100CD] = "tx-packets-per-second",
                        [0x100D5] = "fp-rx-bits-per-second",    [0x100D6] = "fp-tx-bits-per-second",
                        [0x100D7] = "fp-rx-packets-per-second", [0x100D8] = "fp-tx-packets-per-second",
                    }),
            };

        /// <summary>
        /// The shipped alias set for this path, or the nearest ANCESTOR path's set when the path itself has
        /// none.
        /// </summary>
        /// <remarks>
        /// The walk up matters because several API paths read the SAME handler and must therefore agree on
        /// what its keys are called: every interface subtype (<c>/interface/ethernet</c>, <c>/interface/vlan</c>,
        /// …) is the generic <c>[20,0]</c> window with a type filter, and <c>/interface/monitor-traffic</c> is
        /// that window read for one interface. Without the walk, only reads spelled exactly <c>/interface</c>
        /// got the corrected traffic-field names and an <c>EthernetInterface</c> still reported a bit rate as
        /// <c>rx-byte</c>.
        /// </remarks>
        private FieldAliasSet Aliases
        {
            get
            {
                string path = WinboxHandlerMap.Normalize(_apiPath ?? "");
                while (!string.IsNullOrEmpty(path))
                {
                    if (ShippedFieldAliases.TryGetValue(path, out var s)) return s;
                    int cut = path.LastIndexOf('/');
                    if (cut <= 0) break;
                    path = path.Substring(0, cut);
                }
                return null;
            }
        }

        // Rewrite an API field name to its .jg label (encode/resolve direction); identity when no alias.
        private string AliasToJg(string apiName)
        {
            var a = Aliases;
            return (a != null && a.ApiToJg.TryGetValue(apiName, out var jg)) ? jg : apiName;
        }

        // Rewrite a .jg label to its API field name (decode direction); identity when no alias.
        private string AliasToApi(string jgLabel)
        {
            var a = Aliases;
            return (a != null && a.JgToApi.TryGetValue(jgLabel, out var api)) ? api : jgLabel;
        }

        // ── key → apiName (decode records) ─────────────────────────────────────

        /// <summary>
        /// Builds the <c>key → apiName</c> map for this handler by inverting the seed table, the
        /// <c>.jg</c> catalog fields, and the session overrides (overrides and seeds win over the catalog).
        /// </summary>
        internal IReadOnlyDictionary<int, string> BuildKeyToApiName()
        {
            // First-wins in descending priority: session overrides → universal system keys (.id/comment) →
            // catalog (.jg) → name/disabled fallback. First-wins also resolves the .jg's own duplicate-key
            // fields (e.g. /system/resource has both 'freq' and 'CPU Frequency' at u5) deterministically.
            var map = new Dictionary<int, string>();
            void Put(int key, string apiName) { if (!map.ContainsKey(key)) map[key] = apiName; }

            foreach (var kv in _overrides) Put(kv.Value, kv.Key);
            // Shipped numeric key→apiName aliases for fields the .jg leaves unnamed (e.g. ping reply 'host' @0x1).
            var aliasSet = Aliases;
            if (aliasSet != null)
                foreach (var kv in aliasSet.KeyToApi) Put(kv.Key, kv.Value);
            foreach (var kv in SystemSeed) Put(kv.Value, kv.Key);
            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null)
                foreach (var f in jg.Values) Put(f.Key, AliasToApi(f.ApiName));
            foreach (var kv in FallbackSeed) Put(kv.Value, kv.Key);

            return map;
        }

        /// <summary>
        /// Returns the catalog's <c>key → field</c> map for this handler (typed metadata for decode-side
        /// value formatting: IP/MAC/enum). Empty when the handler has no <c>.jg</c> entry.
        /// </summary>
        internal IReadOnlyDictionary<int, WinboxJgField> BuildKeyToField()
        {
            var map = new Dictionary<int, WinboxJgField>();
            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null)
                foreach (var f in jg.Values)
                    if (!map.ContainsKey(f.Key)) map[f.Key] = f;
            // Synthesize typed fields for shipped key aliases the .jg leaves unnamed (collide on empty apiName),
            // so decode formats them correctly (e.g. ping reply 'host' @0x1 as an ipaddr u32).
            var aliasSet = Aliases;
            if (aliasSet != null)
                foreach (var kv in aliasSet.KeyUiType)
                    if (!map.ContainsKey(kv.Key))
                    {
                        aliasSet.KeyToApi.TryGetValue(kv.Key, out var nm);
                        map[kv.Key] = new WinboxJgField(nm ?? "", kv.Key, "u32", true, null, kv.Value);
                    }
            return map;
        }

        // ── apiName → key (forward; for writes / filters) ──────────────────────

        /// <summary>
        /// Resolves an API field name to its M2 key. Throws <see cref="WinboxFieldResolutionException"/>
        /// when the name is unknown or ambiguous, with guidance to add a session override or use WinboxCli.
        /// </summary>
        internal int ResolveKey(string apiName)
        {
            if (TryResolveKey(apiName, out int key)) return key;

            // GUI-name addressing (opt-in): retry with the label normalizer so a name copied straight from the
            // WinBox GUI ("MAC Address" / "MAC_Address" / "Dst. Address", any case) resolves to its API field.
            if (_useGuiNames)
            {
                string norm = NormalizeLabel(apiName);
                if (!string.Equals(norm, apiName, StringComparison.OrdinalIgnoreCase)
                    && TryResolveKey(norm, out int guiKey))
                    return guiKey;
            }

            throw new WinboxFieldResolutionException(
                $"WinBox native: cannot resolve API field '{apiName}' on '{_apiPath}' to an M2 key. " +
                $"Add a session field override (connection.FieldOverride(\"{_apiPath}\", \"{apiName}\", key)) " +
                $"or use a WinboxCli connection instead.");
        }

        /// <summary>
        /// Single resolution attempt for a field name, in priority order: session override → shipped API alias
        /// (e.g. ping 'address' → 'ping-to') → universal system keys (.id/comment) → live .jg catalog →
        /// name/disabled fallback. Returns <c>false</c> (rather than throwing) when none match, so the public
        /// <see cref="ResolveKey"/> can retry with the GUI-name normalizer.
        /// </summary>
        private bool TryResolveKey(string apiName, out int key)
        {
            if (_overrides.TryGetValue(apiName, out key)) return true;
            // Rewrite a shipped API alias to its .jg label (e.g. ping 'address' → 'ping-to') before catalog lookup.
            string jgName = AliasToJg(apiName);
            // universal system keys (.id/comment) are authoritative; name and other fields come from the .jg.
            if (SystemSeed.TryGetValue(jgName, out key)) return true;

            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null && jg.TryGetValue(jgName, out var f)) { key = f.Key; return true; }

            // fallback only when the catalog has no such field (e.g. name → 0x10006 on tables w/o a .jg name).
            if (FallbackSeed.TryGetValue(jgName, out key)) return true;

            key = 0;
            return false;
        }

        /// <summary>
        /// Maps a possibly GUI-styled field name to the canonical name the catalog/seeds actually know — identity
        /// when GUI-names is off or the input already resolves, otherwise the label-normalized form when THAT
        /// resolves. Lets <see cref="EncodeField"/>'s typed <c>.jg</c> lookup and <see cref="ResolveKey"/> agree on
        /// one name so typed encodings (IP/MAC/enum) still apply for a GUI-named field.
        /// </summary>
        private string CanonicalInputName(string apiName)
        {
            if (!_useGuiNames || TryResolveKey(apiName, out _)) return apiName;
            string norm = NormalizeLabel(apiName);
            if (!string.Equals(norm, apiName, StringComparison.OrdinalIgnoreCase) && TryResolveKey(norm, out _))
                return norm;
            return apiName;
        }

        // ── Field encode (writes) ──────────────────────────────────────────────

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
        internal List<byte[]> EncodeField(string apiName, string value, Func<int[], string, int?> resolveRef = null,
            bool allowReadOnly = false)
        {
            // Normalize a GUI-styled name to its canonical API name up front so both ResolveKey and the typed
            // .jg lookup below agree on it (otherwise a GUI label would resolve a key but miss its typed field).
            apiName = CanonicalInputName(apiName);
            int key = ResolveKey(apiName);
            var result = new List<byte[]>();
            // Set by the 'enm' case when a dropdown reference could not be resolved to a record; checked once
            // the static enum map has also had its chance, just before the generic encoders.
            bool unresolvedReference = false;

            // Look up the .jg field (wire type, ro, enum map, UI type). Seeds (.id/comment/name) have none —
            // they default to string, which is correct for comment/name. Use the aliased .jg label so a shipped
            // API alias (e.g. ping 'address' → 'ping-to') resolves to its typed field.
            WinboxJgField jg = null;
            _catalog?.GetHandlerFields(_handler)?.TryGetValue(AliasToJg(apiName), out jg);

            // Read-only fields are unsendable for CRUD writes, but a monitor's request inputs (e.g. ping
            // 'address') are .jg-marked ro as display fields yet must still be sent — allowReadOnly keeps them.
            if (jg != null && jg.ReadOnly && !allowReadOnly) return result;
            value = value ?? "";

            string uiType = jg?.UiType;

            // ── typed UI encodings (more specific than the wire type) ──
            switch (uiType)
            {
                case "network":
                {
                    // Empty → unset (send nothing).
                    if (value.Length == 0) return result;
                    if (jg.IsRange)
                    {
                        // range:1 → the maskid sibling is the range-END address, not a netmask. "a" (host) →
                        // start=end=a; "a-b" → start=a,end=b. Sending end=start for a host avoids the router
                        // storing an open-ended range (the bug when a /32 netmask was sent as the "end").
                        var rp = value.Split('-');
                        uint? start = PackIpV4(rp[0].Trim());
                        if (start == null) break; // not v4 — fall through to generic encoders
                        uint end = (rp.Length > 1 ? PackIpV4(rp[1].Trim()) : start) ?? start.Value;
                        result.Add(EncodeU32(key, start.Value));
                        if (jg.MaskKey != 0) result.Add(EncodeU32(jg.MaskKey, end));
                        return result;
                    }
                    // "addr/mask" → address u32 (key) + netmask u32 (maskid).
                    var parts = value.Split('/');
                    uint? addr = PackIpV4(parts[0]);
                    if (addr == null) break; // not v4 — fall through to generic encoders
                    result.Add(EncodeU32(key, addr.Value));
                    if (jg.MaskKey != 0)
                    {
                        uint mask = parts.Length > 1 ? MaskFrom(parts[1]) : 0xFFFFFFFFu;
                        result.Add(EncodeU32(jg.MaskKey, mask));
                    }
                    return result;
                }
                case "ipaddr":
                {
                    if (value.Length == 0) return result;
                    uint? ip = PackIpV4(value.Split('/')[0]);
                    if (ip == null) break;
                    result.Add(EncodeU32(key, ip.Value));
                    return result;
                }
                case "macaddr":
                {
                    if (value.Length == 0) return result;
                    result.Add(M2Message.RawSys(key, ParseRaw(value)));
                    return result;
                }
                case "set":
                {
                    // Bitmask flag set (e.g. connection-state "established,related"). Empty → unset (send nothing).
                    // A leading '!' negates (API "!established") → set the not-flag. The opt-flag marks the option
                    // present; the value rides as a u32 of OR'd (1<<bitIndex) per the .jg bit map.
                    if (value.Length == 0) return result;
                    bool negate = value.StartsWith("!");
                    string body = negate ? value.Substring(1) : value;
                    long bits = 0;
                    if (jg.EnumMap != null)
                        foreach (var tok in body.Split(','))
                        {
                            string t = tok.Trim();
                            if (t.Length == 0) continue;
                            foreach (var kv in jg.EnumMap)
                                if (string.Equals(kv.Value, t, StringComparison.OrdinalIgnoreCase))
                                { bits |= 1L << kv.Key; break; }
                        }
                    if (jg.OptKey != 0) result.Add(M2Message.BoolSys(jg.OptKey, true));
                    if (jg.NotKey != 0 && negate) result.Add(M2Message.BoolSys(jg.NotKey, true));
                    result.Add(EncodeU32(key, unchecked((uint)bits)));
                    return result;
                }
                case "enm":
                {
                    // dynamic dropdown → referenced object's .id; resolve the name against that table.
                    if (jg.RefHandler != null && resolveRef != null && value.Length > 0
                        && !long.TryParse(value, out _))
                    {
                        int? id = resolveRef(jg.RefHandler, value);
                        if (id.HasValue) { result.Add(EncodeU32(key, (uint)id.Value)); return result; }

                        // The name is not a record in the referenced table. It may still be a static enum
                        // member ("none", "all", …), so try those below — but if nothing matches, the value
                        // is simply wrong and must not be swallowed. Silently dropping it sends a request the
                        // router happily accepts with the field missing, so a typo'd interface name looks
                        // like success. Remember to fail at the end of this method instead.
                        unresolvedReference = true;
                    }
                    break; // fall through to static-map / numeric handling below
                }
                case "multinumberrange":
                case "numberrangelist":
                {
                    // A list of number ranges: bridge-vlan 'vlan-ids', firewall 'dst-port'/'dscp'/'pcp', … .
                    // webfig types.multinumberrange.put (no id2) and types.numberrangelist (inherits def) both
                    // store a flat u32[] of [lo0,hi0,lo1,hi1,…]; a bare "10" is the range [10,10]. An invertible
                    // (not-wrapped) field negates via its not-flag bool (RouterOS "!80"). Empty → unset.
                    bool negate = value.StartsWith("!");
                    string body = negate ? value.Substring(1) : value;
                    if (body.Length == 0) return result;
                    var nums = ParseNumberRangeList(body, apiName);
                    if (nums.Count == 0) return result;
                    if (jg.OptKey != 0) result.Add(M2Message.BoolSys(jg.OptKey, true));
                    if (jg.NotKey != 0 && negate) result.Add(M2Message.BoolSys(jg.NotKey, true));
                    result.Add(M2Message.U32ArraySys(key, nums.ToArray()));
                    return result;
                }
            }

            // opt/not-wrapped scalar (e.g. firewall 'protocol' = opt→not→number, with a static proto-name map):
            // mark the option present via its opt-flag bool and a leading '!' via its not-flag bool — otherwise
            // the router IGNORES the value (e.g. "ports can be specified if proto is tcp,…" when protocol's opt
            // flag is missing). Shared by the enum-static-map and generic scalar encoders below. The 'set' and
            // multinumberrange/numberrangelist UI types emit their own opt/not flags and return before this.
            if (jg != null && jg.NotKey != 0 && value.StartsWith("!"))
            {
                value = value.Substring(1);
                result.Add(M2Message.BoolSys(jg.NotKey, true));
            }
            if (jg != null && jg.OptKey != 0 && value.Length > 0)
                result.Add(M2Message.BoolSys(jg.OptKey, true));

            // enum static map: encode the API string to its numeric index.
            if (jg?.EnumMap != null)
            {
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(EncodeU32(key, (uint)kv.Key));
                        return result;
                    }
            }

            // A dropdown reference (ftype 'enm' with a RefHandler) whose value named neither an existing
            // record nor a static enum member. Every remaining encoder below would turn it into a plain
            // string or a dropped field, and the router accepts such a request as if the field were simply
            // not set — so a typo'd interface name would silently "succeed". Fail instead.
            if (unresolvedReference)
                throw new WinboxFieldValueException(
                    $"input does not match any value of {apiName}");

            string wireType = jg?.WireType ?? SeedWireType(apiName);

            // Loud failure for list/array fields we do NOT yet encode, rather than silently sending a
            // wrong-typed scalar the router quietly drops (silent data loss is worse than an error). The
            // handled list type (multinumberrange) already returned above; anything else with an array wire
            // type ("u32[]"/"string[]"/…) or a 'multi…' UI type (e.g. bridge-vlan 'tagged'/'untagged'
            // interface lists) is not yet supported over native M2 writes.
            if (value.Length > 0 && IsUnsupportedListType(wireType, uiType))
                throw new WinboxFieldResolutionException(
                    $"WinBox native: field '{apiName}' on '{_apiPath}' is a list/array type " +
                    $"('{uiType ?? wireType}') that is not yet encodable over native WinBox M2 writes. " +
                    "Use an Api/REST/CLI connection for this field (or a FieldOverride to a scalar key).");

            // 'addr' (webfig types.addr) is a compound: the value is a nested message, and each address FORM
            // rides at its own sub-key. Encoding it needs the whole set, not just IPv4 — see EncodeAddr.
            if (wireType == "addr" && value.Length > 0)
            {
                result.Add(M2Message.MessageSys(key, EncodeAddr(value, jg?.Allow, apiName, _apiPath)));
                return result;
            }

            switch (wireType)
            {
                case "bool":
                    result.Add(M2Message.BoolSys(key, ParseBool(value)));
                    break;
                case "u32":
                case "u64":
                case "i32":
                case "dur":
                case "time":
                    if (long.TryParse(value, out long n)) result.Add(EncodeU32(key, (uint)n));
                    else result.Add(M2Message.StringSys(key, value)); // non-numeric (e.g. "auto")
                    break;
                case "raw":
                    result.Add(M2Message.RawSys(key, ParseRaw(value)));
                    break;
                case "ip6":
                {
                    // A standalone IPv6 field (.jg 'a' prefix), as opposed to the '6' member of an addr
                    // compound. Same FT_ADDR6 encoding; a value that is not an address stays text so the
                    // router reports it rather than us guessing 16 bytes.
                    byte[] v6 = PackIpV6(value.Split('/')[0]);
                    if (v6 != null) result.Add(M2Message.Addr6Sys(key, v6));
                    else result.Add(M2Message.StringSys(key, value));
                    break;
                }
                default: // "string", "addr" and unknowns round-trip as string text
                    result.Add(M2Message.StringSys(key, value));
                    break;
            }
            return result;
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

        // True for a list/array field that EncodeField has no specific encoder for (so it would otherwise be
        // silently mis-sent as a scalar string). Array wire types end in "[]"; 'multi…' UI types are the
        // WinBox multi-value controls (multinumber interface lists, etc.). multinumberrange is handled before
        // this check, so it never reaches here.
        private static bool IsUnsupportedListType(string wireType, string uiType)
            => (wireType != null && wireType.EndsWith("[]", StringComparison.Ordinal))
               || (uiType != null && uiType.StartsWith("multi", StringComparison.OrdinalIgnoreCase)
                   && !IsScalarDespiteMultiPrefix(uiType));

        // The one 'multi…' UI type that is NOT a list: webfig declares
        // `types.multilinestring = inherit(types.string)` and overrides only its VIEW (a text area instead of
        // a one-line input) — every other multi* inherits `types.multi`. Reading the prefix as "list" refused
        // /system/note's 'note' field as unencodable when it is a plain string.
        private static bool IsScalarDespiteMultiPrefix(string uiType)
            => string.Equals(uiType, "multilinestring", StringComparison.OrdinalIgnoreCase);

        // ── webfig 'addr' compound (master.js types.addr) ──────────────────────
        //
        // An 'addr' field is a nested message, and every address FORM has its own sub-key. Which forms a
        // particular field accepts is the .jg 'allow' mask (WinboxJgField.Allow) — the Ping window's target
        // is allow:'46v%Dm', a /ip/route gateway is allow:'46i', and so on.
        internal const int AddrV4SubKey     = 0xFEFF20;   // ufeff20 — IPv4, u32 octet-LSB
        internal const int AddrV6SubKey     = 0xFEFF21;   // afeff21 — IPv6, 16 raw bytes big-endian
        private  const int AddrIfaceSubKey  = 0xFEFF22;   // ufeff22 — '%iface' suffix (dropdown id)
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
        /// Only the IPv4 branch existed before P2.53, and anything else fell back to a bare string at the
        /// FIELD key. The router does not read that shape: it answers as though the field had never been sent
        /// — <c>/ping address=example.com</c> came back "no address was specified" for a host the binary API
        /// pings fine, and so did every IPv6 target. So the missing branches were not a missing feature but a
        /// silent wrong-request bug, which the "no address was specified" row then made look like a router
        /// error (see Docs/winbox-native-m2-protocol.md §23).
        /// </para>
        /// <para>
        /// The DNS branch deliberately sends the WHOLE input, not the part before the first separator —
        /// master.js writes <c>val.sfeff26=str</c> where every other branch takes <c>l[i]</c>.
        /// </para>
        /// </remarks>
        internal static byte[][] EncodeAddr(string value, string allow, string apiName, string apiPath)
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
            else if (allow.IndexOf('m') >= 0 && TryParseMac(head, out byte[] mac))
                sub.Add(M2Message.RawSys(AddrMacSubKey, mac));
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
        internal static byte[] PackIpV6(string s)
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
        private static bool TryParseMac(string s, out byte[] mac)
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
        internal static string IpFromU32(object value)
        {
            uint v = ToU32(value);
            return $"{v & 0xff}.{(v >> 8) & 0xff}.{(v >> 16) & 0xff}.{(v >> 24) & 0xff}";
        }

        /// <summary>Netmask u32 (octet-LSB) → prefix length (count of set bits).</summary>
        internal static int MaskToPrefix(object value)
        {
            uint v = ToU32(value);
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
        /// <c>::ffff:127.0.0.1</c> would make the same record read differently per transport, which is the
        /// defect class P2.33 is about. An IPv4-COMPATIBLE address (<c>::a.b.c.d</c>) keeps webfig's form.
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
            return value?.ToString() ?? "";
        }

        private static uint ToU32(object value)
        {
            try { return unchecked((uint)Convert.ToInt64(value)); }
            catch { return 0; }
        }

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

        // ── label normalizer (stable text) ─────────────────────────────────────

        // Irregular WinBox labels whose plain normalization does not match the API field name.
        private static readonly Dictionary<string, string> LabelOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mac address"]     = "mac-address",
            ["mtu"]             = "mtu",
            ["actual mtu"]      = "actual-mtu",
            ["l2 mtu"]          = "l2mtu",
            ["arp"]             = "arp",
            ["tx"]              = "tx-byte",
            ["rx"]              = "rx-byte",
        };

        /// <summary>
        /// Normalizes a WinBox UI label to a RouterOS API field name: trims, lower-cases, collapses
        /// whitespace to single '-', and applies a small irregular-label override map.
        /// </summary>
        internal static string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            string trimmed = label.Trim();
            if (LabelOverride.TryGetValue(trimmed, out var ovr)) return ovr;

            var sb = new StringBuilder(trimmed.Length);
            bool lastDash = false;
            foreach (char c in trimmed)
            {
                if (c == '.')
                {
                    // Abbreviation dot in a UI label ("Dst. Address" → "dst-address"); API names carry no dots.
                    continue;
                }
                if (char.IsWhiteSpace(c) || c == '_')
                {
                    if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastDash = false;
                }
            }
            return sb.ToString().Trim('-');
        }
    }

    /// <summary>
    /// Thrown when the WinBox native field resolver cannot unambiguously map an API field name to an
    /// M2 key. The message tells the caller how to recover (session override or a WinboxCli connection).
    /// </summary>
    public sealed class WinboxFieldResolutionException : TikConnectionException
    {
        /// <summary>.ctor</summary>
        public WinboxFieldResolutionException(string message) : base(message) { }
    }

    /// <summary>
    /// A field was mapped fine but the <b>value</b> is not valid for it — today: a dropdown reference naming
    /// no existing record and no fixed enum member. Internal on purpose: the owning connection converts it
    /// into a <c>TikCommandTrapException</c>, so consumers catch the same exception here as on the transports
    /// where the router itself rejects the value. The message deliberately mirrors RouterOS's own wording.
    /// </summary>
    internal sealed class WinboxFieldValueException : Exception
    {
        internal WinboxFieldValueException(string message) : base(message) { }
    }
}
