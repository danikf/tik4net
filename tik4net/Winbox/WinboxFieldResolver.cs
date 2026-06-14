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

        internal WinboxFieldResolver(string apiPath, int[] handler, WinboxJgCatalog catalog,
            IReadOnlyDictionary<string, int> overrides)
        {
            _apiPath = apiPath;
            _handler = handler;
            _catalog = catalog;
            _overrides = overrides ?? new Dictionary<string, int>();
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

                // /interface: the .jg 'type' field is the numeric type id (key 0x10001), but RouterOS API exposes
                // 'type' as the type *name* string — which the record also carries at key 0x1001E (e.g. "ether",
                // "loopback"). Map the string key to 'type' and rename the numeric one so they don't collide.
                ["/interface"] = new FieldAliasSet(
                    apiToJg: Ci(),
                    jgToApi: Ci(),
                    keyToApi: new Dictionary<int, string> { [0x1001E] = "type", [0x10001] = "type-id" }),
            };

        private FieldAliasSet Aliases =>
            ShippedFieldAliases.TryGetValue(WinboxHandlerMap.Normalize(_apiPath ?? ""), out var s) ? s : null;

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
            if (_overrides.TryGetValue(apiName, out int ov)) return ov;
            // Rewrite a shipped API alias to its .jg label (e.g. ping 'address' → 'ping-to') before catalog lookup.
            apiName = AliasToJg(apiName);
            // universal system keys (.id/comment) are authoritative; name and other fields come from the .jg.
            if (SystemSeed.TryGetValue(apiName, out int sys)) return sys;

            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null && jg.TryGetValue(apiName, out var f)) return f.Key;

            // fallback only when the catalog has no such field (e.g. name → 0x10006 on tables w/o a .jg name).
            if (FallbackSeed.TryGetValue(apiName, out int fb)) return fb;

            throw new WinboxFieldResolutionException(
                $"WinBox native: cannot resolve API field '{apiName}' on '{_apiPath}' to an M2 key. " +
                $"Add a session field override (connection.FieldOverride(\"{_apiPath}\", \"{apiName}\", key)) " +
                $"or use a WinboxCli connection instead.");
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
            int key = ResolveKey(apiName);
            var result = new List<byte[]>();

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
                    }
                    break; // fall through to static-map / numeric handling below
                }
            }

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

            string wireType = jg?.WireType ?? SeedWireType(apiName);

            // 'addr' (webfig types.addr) is a compound: the field value is a nested object, IPv4 riding as a u32
            // at sub-key 0xFEFF20 (master.js: val.ufeff20=string2ipaddr(str)). Send it as a nested message field.
            if (wireType == "addr" && value.Length > 0)
            {
                uint? v4 = PackIpV4(value.Split('/', '%')[0]);
                if (v4 != null)
                {
                    result.Add(M2Message.MessageSys(key, M2Message.U32Sys(AddrV4SubKey, unchecked((int)v4.Value))));
                    return result;
                }
                // non-IPv4 (hostname / IPv6) → fall through to plain string at the field key.
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
                default: // "string", "addr", "ip6" and unknowns round-trip as string text
                    result.Add(M2Message.StringSys(key, value));
                    break;
            }
            return result;
        }

        // The IPv4 sub-key inside a webfig 'addr' compound object (master.js property 'ufeff20' = u32@0xFEFF20).
        private const int AddrV4SubKey = 0xFEFF20;

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
}
