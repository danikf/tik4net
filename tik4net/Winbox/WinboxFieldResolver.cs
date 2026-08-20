using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly string? _apiPath;
        private readonly WinboxJgCatalog _catalog;
        // session overrides apiName → key (highest priority)
        private readonly IReadOnlyDictionary<string, int> _overrides;
        // when true, a field name that does not resolve verbatim is retried through NormalizeLabel
        // (GUI-name addressing: "MAC Address"/"MAC_Address" → "mac-address"). Opt-in per connection.
        private readonly bool _useGuiNames;

        // The derived menu-label path of the WINDOW this path resolves to, when it has one of its own —
        // interface subtypes (EoIP Tunnel, L2TP Client, …) share handler [20,0] but declare their own fields.
        private readonly string? _windowKey;
        // The arguments of the ACTION being invoked (WinboxJgCatalog.GetActionFields), laid over everything
        // else — an action's argument and a record column of the same name are different fields on the same
        // handler. Null for every ordinary (non-action) command.
        private readonly IReadOnlyDictionary<string, WinboxJgField>? _actionFields;
        // Lazily computed by the JgFields getter; null means "not yet computed", not "no fields".
        private IReadOnlyDictionary<string, WinboxJgField>? _fields;

        internal WinboxFieldResolver(string? apiPath, int[] handler, WinboxJgCatalog catalog,
            IReadOnlyDictionary<string, int> overrides, bool useGuiNames = false, string? windowKey = null,
            IReadOnlyDictionary<string, WinboxJgField>? actionFields = null)
        {
            _apiPath = apiPath;
            _handler = handler;
            _catalog = catalog;
            _overrides = overrides ?? new Dictionary<string, int>();
            _useGuiNames = useGuiNames;
            _windowKey = windowKey;
            _actionFields = actionFields;
        }

        /// <summary>
        /// The <c>.jg</c> fields in force for this path: the handler's map, with the window's own fields laid
        /// over it and the invoked action's arguments laid over those. The window overlay is what makes an
        /// interface subtype addressable — every subtype reads handler <c>[20,0]</c>, but 'Remote Address' is a
        /// different key on EoIP, GRE and IPIP, so the window has the last word. The action overlay is the same
        /// rule one level further in: 'Key Size' is a read-only column of the IPsec 'Keys' list and a writable
        /// enum argument of its 'Generate Key' doit. Computed once per resolver.
        /// </summary>
        private IReadOnlyDictionary<string, WinboxJgField>? JgFields
        {
            get
            {
                if (_fields != null) return _fields;
                var handlerFields = _catalog?.GetHandlerFields(_handler);
                var windowFields = _catalog?.GetWindowFields(_windowKey);
                var synthetic = Aliases?.SyntheticFields;
                bool hasWindow = windowFields != null && windowFields.Count > 0;
                bool hasAction = _actionFields != null && _actionFields.Count > 0;
                bool hasSynthetic = synthetic != null && synthetic.Count > 0;
                if (!hasWindow && !hasAction && !hasSynthetic) return _fields = handlerFields;

                var merged = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                if (handlerFields != null)
                    foreach (var kv in handlerFields) merged[kv.Key] = kv.Value;
                // hasWindow/hasAction already established windowFields/_actionFields non-null; the extra
                // null check just gives the compiler the same fact it cannot carry through the bool.
                if (hasWindow && windowFields != null)
                    foreach (var kv in windowFields) merged[kv.Key] = kv.Value;   // the window wins
                if (hasAction && _actionFields != null)
                    foreach (var kv in _actionFields) merged[kv.Key] = kv.Value;  // the action wins over both
                // A shipped synthetic field wins over all of them: it exists precisely because the catalog
                // does not describe the key, so there is nothing here it can be overriding by accident.
                if (hasSynthetic && synthetic != null)
                    foreach (var kv in synthetic) merged[kv.Key] = kv.Value;
                return _fields = merged;
            }
        }

        /// <summary>
        /// The same three maps, but enumerated MOST SPECIFIC FIRST (action → window → handler) for the
        /// consumers that resolve by KEY rather than by name.
        /// </summary>
        /// <remarks>
        /// <see cref="JgFields"/> answers "what is this name?" and so lets the specific map overwrite the
        /// general one. Key → name is the other direction and is first-wins, which the merged dictionary
        /// cannot express: two windows on one handler give key 1 two names ('Enabled' on the UPnP settings
        /// singleton, 'Interface' on the UPnP interface list) under DIFFERENT dictionary entries, so nothing
        /// overwrites anything and the handler's answer — whichever window the catalog parsed first — wins for
        /// both. Enumerating the window's own fields first is what makes an interface row decode as
        /// <c>interface</c> and not as <c>enabled</c>.
        /// </remarks>
        private IEnumerable<KeyValuePair<string, WinboxJgField>> JgFieldsSpecificFirst()
        {
            // Most specific of all: the catalog does not name these keys at all, so nothing else can.
            var synthetic = Aliases?.SyntheticFields;
            if (synthetic != null)
                foreach (var kv in synthetic) yield return kv;
            if (_actionFields != null)
                foreach (var kv in _actionFields) yield return kv;
            var windowFields = _catalog?.GetWindowFields(_windowKey);
            if (windowFields != null)
                foreach (var kv in windowFields) yield return kv;
            var handlerFields = _catalog?.GetHandlerFields(_handler);
            if (handlerFields != null)
                foreach (var kv in handlerFields) yield return kv;
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

        // ── Deck panes: the kind prefix (stable text) ──────────────────────────

        /// <summary>
        /// The name a pane field is filed under: the kind and the field's own label joined with '-', unless
        /// the label already begins with the kind (WinBox writes 'Remote Port' inside the remote pane and
        /// 'PFIFO Queue Size' inside the pfifo one, where the API says <c>remote-port</c> and
        /// <c>pfifo-limit</c> — not <c>remote-remote-port</c>).
        /// </summary>
        internal static string PrefixWithKind(string? kind, string apiName)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(apiName)) return apiName;
            if (apiName.Equals(kind, StringComparison.OrdinalIgnoreCase)
                || apiName.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase))
                return apiName;
            return kind + "-" + apiName;
        }

        /// <summary>
        /// Which <c>deck</c> panes RouterOS's API spells with the kind prefix, per path — <c>"*"</c> for every
        /// pane of that window.
        /// </summary>
        /// <remarks>
        /// <para>It is NOT derivable, which is why it is shipped and why the default is "leave the name
        /// alone". <c>/queue/type</c> prefixes every pane without exception (<c>pcq-rate</c>,
        /// <c>red-burst</c>, <c>codel-limit</c>, <c>fq-codel-limit</c>), while <c>/system/logging/action</c>
        /// prefixes memory, disk and email but leaves the remote, echo and script panes alone — the API calls
        /// those <c>src-address</c>, <c>syslog-facility</c>, <c>remember</c>, <c>script</c>. Both lists were
        /// read off the live router with tab completion (<c>/queue/type add ?</c>,
        /// <c>/system/logging/action add ?</c>), not inferred.</para>
        /// <para>The catalog files every pane field under BOTH names regardless (see
        /// <c>WinboxJgCatalog.AddField</c>), so a caller can always WRITE the prefixed spelling; this table
        /// only decides which of the two a READ reports. Leaving a path out therefore costs nothing that
        /// worked before — the ~70 deck windows in the 7.23.2 catalog keep the names they have always
        /// decoded to.</para>
        /// </remarks>
        private static readonly Dictionary<string, HashSet<string>> PanePrefixedPaths =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/queue/type"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" },
                ["/system/logging/action"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "memory", "disk", "email" },
            };

        // True when the API spells THIS path's fields of THIS pane kind with the kind prefix.
        private bool PanePrefixed(string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string path = WinboxHandlerMap.Normalize(_apiPath ?? "");
            return PanePrefixedPaths.TryGetValue(path, out var kinds)
                   && (kinds.Contains("*") || kinds.Contains(kind!)); // IsNullOrEmpty above already excluded null
        }

        /// <summary>
        /// Which of a field's catalog registrations is the one a read reports it under — the kind-prefixed
        /// spelling where the API uses it, the plain label otherwise. (The shipped label alias is applied on
        /// top by the caller, so this stays comparable with the catalog's own keys.)
        /// </summary>
        private string RegisteredNameToReport(WinboxJgField f)
            => PanePrefixed(f.PaneKind) ? PrefixWithKind(f.PaneKind, f.ApiName) : f.ApiName;

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

            /// <summary>
            /// Address field label → PORT field label, for the fields RouterOS prints as one
            /// <c>address:port</c> where WinBox has two boxes. See <see cref="AddrPortUiType"/>.
            /// </summary>
            public readonly IReadOnlyDictionary<string, string>? AddrPortPairs;

            /// <summary>
            /// API bool field → (the decoded field it is derived from, the value that means <c>true</c>).
            /// For the case where the API reports a BOOL and WinBox reports the same wire field as an
            /// enum with more members than the API distinguishes.
            /// </summary>
            /// <remarks>
            /// <c>/ip/route</c>'s <c>active</c> is the live one: the API prints <c>active=true|false</c>,
            /// while the route window renders u22 as 'Contribution'
            /// (filtered/unreachable/candidate/best candidate/active) — and the base window's own
            /// <c>numflag</c> on that key says which member the flag stands for (<c>4:['active','A']</c>).
            /// The derived field is ADDED, not substituted: the enum keeps its own name, so native still
            /// reports the finer answer to anyone who wants it.
            /// </remarks>
            public readonly IReadOnlyDictionary<string, Tuple<string, string>>? DerivedBools;

            /// <summary>
            /// Fields the ROUTER sends but no <c>.jg</c> window names, supplied here so the resolver can
            /// read, resolve and write them like any catalogued field. Keyed by API name.
            /// </summary>
            /// <remarks>
            /// Different from <see cref="KeyToApi"/>, which only names a key for decode: a synthetic field
            /// carries its wire type and enum map too, so it also resolves for a WRITE. Ship one only where
            /// the router has been observed sending the key AND the pairing has been confirmed by changing
            /// the value and watching the key move — a name guessed onto a key writes to whatever that key
            /// really is.
            /// </remarks>
            public readonly IReadOnlyDictionary<string, WinboxJgField>? SyntheticFields;

            public FieldAliasSet(IReadOnlyDictionary<string, string> apiToJg, IReadOnlyDictionary<string, string> jgToApi,
                IReadOnlyDictionary<int, string>? keyToApi = null, IReadOnlyDictionary<int, string>? keyUiType = null,
                IReadOnlyDictionary<string, string>? addrPortPairs = null,
                IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null,
                IReadOnlyDictionary<string, WinboxJgField>? syntheticFields = null)
            {
                ApiToJg = apiToJg; JgToApi = jgToApi;
                KeyToApi = keyToApi ?? new Dictionary<int, string>();
                KeyUiType = keyUiType ?? new Dictionary<int, string>();
                AddrPortPairs = addrPortPairs;
                DerivedBools = derivedBools;
                SyntheticFields = syntheticFields;
            }
        }

        private static Dictionary<string, string> Ci(params (string, string)[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // /system/health's two states, as RouterOS spells them.
        private static readonly Dictionary<int, string> HealthStateMap = new Dictionary<int, string>
        {
            [0] = "disabled",
            [1] = "enabled",
        };

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

                // /file: the Files window labels the record's name 'File Name' (it is the window's
                // nameval), while the API calls it 'name'. Without the alias every file row came back
                // without a 'name' and the entity read threw "Missing field 'name'".
                ["/file"] = new FieldAliasSet(
                    apiToJg: Ci(("name", "file-name")),
                    jgToApi: Ci(("file-name", "name"))),

                // /system/logging/action: the window's field is {name:'Type',title:'Target'} — the API name
                // is 'target', and 'type' is not an API field of this table at all.
                //
                // The rest are deck-pane leaves whose label the API spells differently. The memory/disk/email
                // panes are kind-prefixed (see PanePrefixedPaths) and their labels then match the API exactly
                // — memory-lines, disk-file-count, email-cc — so only three remain, plus the echo pane's one
                // field. Checked against `/system/logging/action add ?` on 7.23.2.
                ["/system/logging/action"] = new FieldAliasSet(
                    apiToJg: Ci(("target", "type"),
                               ("remote", "remote-address"),          // remote pane, 'Remote Address'
                               ("remote-protocol", "remote-log-protocol"),
                               ("email-to", "email"),                 // email pane, 'Email' (already kind-named)
                               ("remember", "save")),                 // echo pane, 'Save'
                    jgToApi: Ci(("type", "target"),
                               ("remote-address", "remote"),
                               ("remote-log-protocol", "remote-protocol"),
                               ("email", "email-to"),
                               ("save", "remember"))),
                // NOT aliased here, deliberately: the remote pane's 'Timestamp Format' (u13) looks like the
                // API's syslog-time-format, but the API reports that field only when the action's log format
                // is BSD syslog (the .jg marks it `on:'timestamp'`, a condition this catalog does not model),
                // and it spells the value 'bsd-syslog' where the window's enum says 'BSD'. Naming it would
                // hand the mapper a field the API does not report for the row, with a value it cannot convert
                // — which is exactly what it did. Same for 'Syslog Facility'/'Add Topics'. An alias is only
                // shipped when the NAME and the VALUE both match what the router prints.

                // /queue/type: every pane IS kind-prefixed, so the prefix is derived and only the leaf text
                // differs — WinBox calls the queue depth 'Queue Size' where the API says 'limit', and the RED
                // pane's 'Avg. Packet Size' is 'avg-packet'. The kind-carrying labels ('BFIFO Queue Size')
                // keep their own prefix, so the alias is on the whole name. Checked against
                // `/queue/type add ?` on 7.23.2.
                // ('Type Name' is the window's nameval, the same shape as /file's 'File Name' — without it
                // every row came back without a 'name' and the entity read threw "Missing field 'name'".)
                ["/queue/type"] = new FieldAliasSet(
                    apiToJg: Ci(("name", "type-name"),
                               ("bfifo-limit", "bfifo-queue-size"),
                               ("pfifo-limit", "pfifo-queue-size"),
                               ("mq-pfifo-limit", "mq-pfifo-mq-queue-size"),
                               ("red-limit", "red-queue-size"),
                               ("red-avg-packet", "red-avg-packet-size"),
                               ("pcq-limit", "pcq-queue-size"),
                               ("pcq-total-limit", "pcq-total-queue-size")),
                    jgToApi: Ci(("type-name", "name"),
                               ("bfifo-queue-size", "bfifo-limit"),
                               ("pfifo-queue-size", "pfifo-limit"),
                               ("mq-pfifo-mq-queue-size", "mq-pfifo-limit"),
                               ("red-queue-size", "red-limit"),
                               ("red-avg-packet-size", "red-avg-packet"),
                               ("pcq-queue-size", "pcq-limit"),
                               ("pcq-total-queue-size", "pcq-total-limit"))),

                // /interface/ethernet: the window carries the auto-negotiation SETTING and the link's
                // negotiation STATUS, and the .jg labels both of them 'Auto Negotiation'. The setting
                // declares name:'autoneg' (b3f3, writable) and so normalizes to 'autoneg'; the status is a
                // read-only enm on the Status tab (u44d, values incomplete/done/no negotiation/failed/
                // restarted/disabled/not available) with no name of its own, so it took the label.
                //
                // The result was a read that reported auto-negotiation='not-available' on a CHR's virtual
                // NIC where the API says 'true' — not two transports disagreeing, but us answering a
                // different question with the API's field name. The setting is the API's
                // 'auto-negotiation'; the status keeps a name of its own, since the API reports it only
                // from /interface/ethernet/monitor and not on this table at all.
                ["/interface/ethernet"] = new FieldAliasSet(
                    apiToJg: Ci(("auto-negotiation", "autoneg")),
                    jgToApi: Ci(("autoneg", "auto-negotiation"),
                               ("auto-negotiation", "auto-negotiation-status"))),

                // /ip/route: the API's `active` bool is the route window's 'Contribution' enum — one wire
                // field (u22), two vocabularies. The base 'All Routes' window's numflag on that key
                // (4:['active','A']) is what says which member the API's true stands for. Derived rather
                // than renamed, so `contribution` survives alongside it.
                ["/ip/route"] = new FieldAliasSet(
                    apiToJg: Ci(),
                    jgToApi: Ci(),
                    derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["active"] = Tuple.Create("contribution", "active"),
                    }),

                // /system/health: the router sends both of the API's fields and the catalog names neither.
                //
                // [24,14] hosts two .jg windows — 'Settings' (fan control) and the x86-gated 'System Health'
                // (voltages and temperatures, plus `caps` at uf) — and nothing in either declares keys 8 or
                // 9. A getall answers with them anyway: on the lab CHR (7.24, no hardware sensors, so the
                // catalogued half is empty) the reply is `0x8=bool:False 0x9=bool:True` against the API's
                // `state=disabled state-after-reboot=enabled`. The decoder drops keys nothing names, so the
                // path read as `caps` alone and this was recorded as 'state/state-after-reboot are API-only
                // fields with no WinBox equivalent'. They are not; we were not listening.
                //
                // The pairing was CONFIRMED, not inferred from one consistent sample: setting
                // state-after-reboot=disabled over the API moved 0x9 True -> False and left 0x8 alone.
                // 0x8 is the read-only one, which matches the router — `/system/health set` tab-completes to
                // state-after-reboot and nothing else.
                //
                // Two bools the API spells as words, so they carry a two-member map rather than a bool type;
                // the encoder writes a mapped value at the field's own wire type.
                ["/system/health"] = new FieldAliasSet(
                    apiToJg: Ci(),
                    jgToApi: Ci(),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["state"] = new WinboxJgField("state", 0x8, "bool", true, enumMap: HealthStateMap),
                        ["state-after-reboot"] = new WinboxJgField("state-after-reboot", 0x9, "bool", false,
                                                                   enumMap: HealthStateMap),
                    }),

                // /ip/upnp: the settings singleton's second field is labelled 'Allow To Disable External
                // Interface' in WinBox and 'allow-disable-external-interface' in the API — one stray "to".
                // Without the alias the entity read back a field name RouterOS never uses.
                ["/ip/upnp"] = new FieldAliasSet(
                    apiToJg: Ci(("allow-disable-external-interface", "allow-to-disable-external-interface")),
                    jgToApi: Ci(("allow-to-disable-external-interface", "allow-disable-external-interface"))),

                // /interface/wifi/security: WinBox writes the label with the ID spelled out — 'FT Preserve
                // VLAN ID' normalizes to ft-preserve-vlan-id, where the API says ft-preserve-vlanid. Same
                // field (wave2 b2023), one hyphen apart, so an add carrying it could not resolve a key at all.
                ["/interface/wifi/security"] = new FieldAliasSet(
                    apiToJg: Ci(("ft-preserve-vlanid", "ft-preserve-vlan-id")),
                    jgToApi: Ci(("ft-preserve-vlan-id", "ft-preserve-vlanid"))),

                // /ip/hotspot/profile: the RADIUS tab drops the 'radius-' prefix the API spells out — the
                // checkbox is just 'Accounting' (b8e, def:1, on:'radius') where the API says
                // radius-accounting, as does `/ip/hotspot/profile add ?` on 7.23.2. Without the alias an add
                // carrying the field could not resolve a key at all. 'Rate Limit (rx/tx)' is the same shape
                // with the units written into the label.
                //
                // 'Interim Update' (u8f) is the same shape and was held back until the VALUE could be
                // measured too — the rule at /system/logging/action. The API prints the field only on a
                // profile with use-radius=yes, so it was measured on one: with radius-interim-update=5m the
                // API says '5m' and the .jg interval decodes to '5m'. Shipped on that.
                //   The one value that still differs is the sentinel: at 0 the API prints 'received' where
                // native prints '0s'. That is RouterOS's own rendering of the zero interval, not a decode
                // gap — the .jg declares a plain interval with no values map, so webfig shows 0s too. It is
                // recorded here rather than in the audit's KnownValueGaps because the audit cannot see the
                // field at all: the API prints nothing radius-related on a profile with use-radius=no, which
                // is how the test router sits, and an entry that never fires is reported as a stale gap.
                //
                // 'HTTP Proxy' (u83) IS handled, by addrPortPairs below rather than by a name alias: the
                // label already normalizes to the API's http-proxy, and what differed was the value.
                ["/ip/hotspot/profile"] = new FieldAliasSet(
                    addrPortPairs: Ci(("http-proxy", "http-proxy-port")),
                    apiToJg: Ci(("radius-accounting", "accounting"),
                               ("radius-default-domain", "default-domain"),
                               ("radius-interim-update", "interim-update"),
                               ("radius-location-id", "location-id"),
                               ("radius-location-name", "location-name"),
                               ("radius-mac-format", "mac-format"),
                               ("rate-limit", "rate-limit-(rx/tx)")),
                    jgToApi: Ci(("accounting", "radius-accounting"),
                               ("default-domain", "radius-default-domain"),
                               ("interim-update", "radius-interim-update"),
                               ("location-id", "radius-location-id"),
                               ("location-name", "radius-location-name"),
                               ("mac-format", "radius-mac-format"),
                               ("rate-limit-(rx/tx)", "rate-limit"))),

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
        private FieldAliasSet? Aliases
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

        /// <summary>
        /// The bool fields this path derives from another decoded field — see
        /// <see cref="FieldAliasSet.DerivedBools"/>. Empty for all but a handful of paths.
        /// </summary>
        internal IReadOnlyDictionary<string, Tuple<string, string>>? DerivedBoolFields => Aliases?.DerivedBools;

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
        /// The M2 keys that TWO fields in force for this path claim with different <b>arrayness</b> — a
        /// scalar and a list sharing one key. Empty for all but a handful of windows.
        /// </summary>
        /// <remarks>
        /// <para>Inverting key → name is first-wins, and where two windows disagree the window overlay
        /// decides. Neither helps here, because these two fields are in the SAME window: <c>/ip/dhcp-client</c>
        /// declares 'Add Default Route' as <c>u12</c> and 'DHCP Options' as <c>U12</c>. Only the wire type
        /// separates them, and the router does send both — see <c>M2Message.ParseAllFields</c>, which files the
        /// second under the qualified key these registrations answer at.</para>
        /// <para>Scoped to the CONTESTED keys rather than applied to every field, so a window with no such
        /// collision — which is nearly all of them — builds exactly the map it built before.</para>
        /// </remarks>
        private HashSet<int> ContestedKeys()
        {
            var arrayness = new Dictionary<int, bool>();
            var contested = new HashSet<int>();
            foreach (var kv in JgFieldsSpecificFirst())
            {
                bool isArray = WinboxM2Protocol.TypedKey.IsArrayType(kv.Value.WireType);
                if (arrayness.TryGetValue(kv.Value.Key, out bool seen))
                {
                    if (seen != isArray) contested.Add(kv.Value.Key);
                }
                else arrayness[kv.Value.Key] = isArray;
            }
            return contested;
        }

        // A field's key qualified by the arrayness of its .jg wire type ('U12' → array, 'u12' → scalar).
        private static int TypedKeyOf(WinboxJgField f)
            => WinboxM2Protocol.TypedKey.Qualify(f.Key, WinboxM2Protocol.TypedKey.IsArrayType(f.WireType));

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
            // First-wins on the KEY, and — because the enumeration is most-specific-first — first-wins on the
            // NAME too. The second guard is what makes the window overlay reach the decode: two windows share
            // handler [47,1], 'Enabled' is b4 on NTP Client and b6 on NTP Server, and the singleton record
            // carries BOTH. Naming each key separately left two keys called 'enabled' and let the RECORD's
            // field order pick between them — /system/ntp/server read the client's flag and reported true
            // where the API says false. A name belongs to the most specific window that claims it.
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Put(int key, string apiName)
            {
                if (map.ContainsKey(key)) return;
                // A qualified registration is the SAME field as its plain one (see ContestedKeys), so it must
                // not be the thing that claims the name away from it.
                if (!WinboxM2Protocol.TypedKey.IsQualified(key) && !claimed.Add(apiName)) return;
                map[key] = apiName;
            }

            var contested = ContestedKeys();
            foreach (var kv in _overrides) Put(kv.Value, kv.Key);
            // Shipped numeric key→apiName aliases for fields the .jg leaves unnamed (e.g. ping reply 'host' @0x1).
            var aliasSet = Aliases;
            if (aliasSet != null)
                foreach (var kv in aliasSet.KeyToApi) Put(kv.Key, kv.Value);
            foreach (var kv in SystemSeed) Put(kv.Value, kv.Key);
            foreach (var kv in JgFieldsSpecificFirst())
            {
                // A key two fields of DIFFERENT arrayness both claim also gets an arrayness-qualified
                // registration, which is the only thing that can tell them apart on the wire.
                if (contested.Contains(kv.Value.Key)
                    && string.Equals(kv.Key, RegisteredNameToReport(kv.Value), StringComparison.OrdinalIgnoreCase))
                    Put(TypedKeyOf(kv.Value), AliasToApi(kv.Key));
                // A pane field is registered TWICE (plain + kind-prefixed), and only the spelling this
                // path actually reports may contribute a name — otherwise both spellings would claim the
                // field's key, and on a path that does NOT prefix, the second pane's field (which owns no
                // plain registration, its label having been taken by the first pane) would start
                // answering under the first pane's name. Skipping the other registration keeps such a
                // field out of the decode exactly as it was before panes were harvested.
                if (!string.Equals(kv.Key, RegisteredNameToReport(kv.Value), StringComparison.OrdinalIgnoreCase))
                    continue;
                Put(kv.Value.Key, AliasToApi(kv.Key));
            }
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
            var contested = ContestedKeys();
            // Most specific first, for the same reason BuildKeyToApiName does it: a key claimed by two
            // windows on one handler must be typed by the window this path addresses.
            foreach (var kv in JgFieldsSpecificFirst())
            {
                if (contested.Contains(kv.Value.Key))
                {
                    int typed = TypedKeyOf(kv.Value);
                    if (!map.ContainsKey(typed)) map[typed] = kv.Value;
                }
                if (!map.ContainsKey(kv.Value.Key)) map[kv.Value.Key] = kv.Value;
            }
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
            ApplyAddrPortPairs(map, aliasSet);
            return map;
        }

        /// <summary>
        /// The synthetic UI type of an address field RouterOS prints joined to a PORT that WinBox keeps in a
        /// field of its own. The port key rides in <see cref="WinboxJgField.MaskKey"/> — the same
        /// "my value needs a sibling" slot a <c>network</c>'s netmask uses — and the codec consumes it so the
        /// port does not also surface as a field the API never reports.
        /// </summary>
        /// <remarks>
        /// Shipped per path rather than derived, because nothing in the <c>.jg</c> says the two boxes are one
        /// API field: <c>/ip/hotspot/profile</c> has 'HTTP Proxy' (<c>u83</c>) beside 'HTTP Proxy Port'
        /// (<c>u84</c>) exactly as it has 'SMTP Server' (<c>u87</c>) beside nothing, and the API prints
        /// <c>http-proxy=0.0.0.0:0</c> against <c>smtp-server=0.0.0.0</c>.
        /// </remarks>
        internal const string AddrPortUiType = "addrport";

        private void ApplyAddrPortPairs(Dictionary<int, WinboxJgField> map, FieldAliasSet? aliasSet)
        {
            if (aliasSet?.AddrPortPairs == null) return;
            var jg = JgFields;
            if (jg == null) return;
            foreach (var pair in aliasSet.AddrPortPairs)
            {
                if (!jg.TryGetValue(pair.Key, out var addr) || !jg.TryGetValue(pair.Value, out var port))
                    continue;
                map[addr.Key] = new WinboxJgField(addr.ApiName, addr.Key, addr.WireType, addr.ReadOnly,
                    uiType: AddrPortUiType, maskKey: port.Key);
            }
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

            var jg = JgFields;
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
        internal List<byte[]> EncodeField(string apiName, string value, Func<int[], string, int?>? resolveRef = null,
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
            WinboxJgField? jg = null;
            JgFields?.TryGetValue(AliasToJg(apiName), out jg);

            // Read-only fields are unsendable for CRUD writes, but a monitor's request inputs (e.g. ping
            // 'address') are .jg-marked ro as display fields yet must still be sent — allowReadOnly keeps them.
            if (jg != null && jg.ReadOnly && !allowReadOnly) return result;
            value = value ?? "";

            string? uiType = jg?.UiType;

            // ── opt/not container flags ──
            //
            // An opt-wrapped field is IGNORED BY THE ROUTER unless its opt bool says the option is present, and
            // a not-wrapped one negates via its own bool (RouterOS's leading '!'). This has to happen before the
            // typed switch below, not after it: 'network', 'ipaddr', 'macaddr' and 'addr' all return from inside
            // the switch, so emitting the flags afterwards skipped exactly them — which is why every firewall
            // address written over native reached the router as a rule matching EVERYTHING (P2.33).
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
                if (jg.NotKey != 0 && value.StartsWith("!"))
                {
                    value = value.Substring(1);
                    result.Add(M2Message.BoolSys(jg.NotKey, true));
                }
                if (jg.OptKey != 0 && value.Length > 0)
                    result.Add(M2Message.BoolSys(jg.OptKey, true));
            }

            // ── typed UI encodings (more specific than the wire type) ──
            switch (uiType)
            {
                case "interval":
                {
                    // The inverse of WinboxRecordCodec's interval decode, and it has to exist for the same
                    // reason the decode does: the wire carries a COUNT of 1/scale-second units, while the API
                    // spells the same value "5m" / "1w" / "500ms". Without this, "5m" failed long.TryParse in
                    // the generic u32 branch and went out as the STRING "5m" — which the router accepts,
                    // answers with status 0, and ignores, so the write silently did nothing.
                    if (value.Length == 0) return result;
                    // A named value wins, exactly as on the way back (e.g. 'immediately' / 'never').
                    if (jg?.EnumMap != null)
                    {
                        foreach (var kv in jg.EnumMap)
                            if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(EncodeU32(key, unchecked((uint)kv.Key)));
                                return result;
                            }
                    }
                    if (TryParseDuration(value, jg?.Scale ?? 1, out long ticks))
                    {
                        result.Add(EncodeU32(key, unchecked((uint)ticks)));
                        return result;
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
                case "network":
                {
                    // Empty → unset (send nothing).
                    if (value.Length == 0) return result;
                    // jg cannot be null here: uiType only reaches this switch as jg?.UiType's value, so a
                    // non-null "network" case implies jg itself is non-null.
                    if (jg!.IsRange)
                    {
                        // range:1 → the maskid sibling is the range-END address, not a netmask. All three
                        // RouterOS forms are accepted ("a", "a-b", "a/len"); sending end=start for a host
                        // avoids the router storing an open-ended range (which is what a /32 netmask sent as
                        // the "end" produced).
                        if (!TryParseV4Range(value, out uint start, out uint end))
                            break; // not v4 — fall through to generic encoders
                        result.Add(EncodeU32(key, start));
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
                    // The value rides as a u32 of OR'd (1<<bitIndex) per the .jg bit map; the opt/not flags and
                    // the leading '!' were handled above.
                    if (value.Length == 0) return result;
                    long bits = 0;
                    if (jg!.EnumMap != null) // jg non-null: uiType == "set" only when jg.UiType produced it
                        foreach (var tok in value.Split(','))
                        {
                            string t = tok.Trim();
                            if (t.Length == 0) continue;
                            foreach (var kv in jg.EnumMap)
                                if (string.Equals(kv.Value, t, StringComparison.OrdinalIgnoreCase))
                                { bits |= 1L << kv.Key; break; }
                        }
                    result.Add(EncodeU32(key, unchecked((uint)bits)));
                    return result;
                }
                case "enm":
                {
                    // dynamic dropdown → referenced object's .id; resolve the name against that table.
                    // jg non-null: uiType == "enm" only when jg.UiType produced it.
                    if (jg!.RefHandler != null && resolveRef != null && value.Length > 0
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
                    // (not-wrapped) field negates via its not-flag bool (RouterOS "!80"), handled above along
                    // with the opt flag. Empty → unset.
                    if (value.Length == 0) return result;
                    var nums = ParseNumberRangeList(value, apiName);
                    if (nums.Count == 0) return result;
                    result.Add(M2Message.U32ArraySys(key, nums.ToArray()));
                    return result;
                }
                case "multinumber":
                {
                    // A list whose ELEMENTS are what the scalar encoders handle one at a time: bridge-vlan
                    // 'tagged'/'untagged' (interface references), the log rule's 'topics' (a static enum),
                    // /ip/proxy 'port' (plain numbers). webfig types.multinumber stores them as a flat u32[]
                    // in the order given, so the whole field is one array write — and encoding each element
                    // by the same three rules the decode reads them back with (reference → static map →
                    // literal) is what makes the round trip agree.
                    if (value.Length == 0) return result;
                    var items = new List<int>();
                    foreach (var tok in value.Split(','))
                    {
                        string t = tok.Trim();
                        if (t.Length == 0) continue;
                        // jg non-null: uiType == "multinumber" only when jg.UiType produced it.
                        items.Add(EncodeListElement(jg!, t, apiName, resolveRef));
                    }
                    if (items.Count == 0) return result;
                    result.Add(M2Message.U32ArraySys(key, items.ToArray()));
                    return result;
                }
                case "multitristatearray":
                {
                    // A list whose elements may each be negated — /system/logging 'topics', where the API
                    // writes "info,!debug". webfig types.multitristatearray.put splits the one list into TWO
                    // arrays by that flag and writes both keys, so the '!' is a different KEY here, not a
                    // value prefix (contrast the NotKey flag on a scalar). Both arrays are always sent,
                    // including as empty, because that is the only way to CLEAR the other half.
                    // jg non-null: uiType == "multitristatearray" only when jg.UiType produced it.
                    if (jg!.OffKey == 0) break;   // no second key: not the shape webfig describes — refuse below
                    if (value.Length == 0) return result;
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
                    if (on.Count == 0 && off.Count == 0) return result;
                    result.Add(M2Message.U32ArraySys(key, on.ToArray()));
                    result.Add(M2Message.U32ArraySys(jg.OffKey, off.ToArray()));
                    return result;
                }
            }

            // (opt/not flags for the generic encoders below were emitted before the switch, together with every
            // typed branch's — see the "opt/not container flags" block.)

            // enum static map: encode the API string to its numeric index.
            if (jg?.EnumMap != null)
            {
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        // ...at the field's own wire type. A two-member map over a BOOL key is a real shape
                        // (/system/health spells its two bools 'disabled'/'enabled'), and a u32 written to a
                        // bool key is a request the router accepts, answers, and ignores — G4's shape.
                        result.Add(jg.WireType == "bool"
                            ? M2Message.BoolSys(key, kv.Key != 0)
                            : EncodeU32(key, (uint)kv.Key));
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
                    byte[]? v6 = PackIpV6(value.Split('/')[0]);
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

        /// <summary>
        /// Parses a RouterOS duration into the <paramref name="scale"/>-per-second unit the wire carries —
        /// the inverse of <c>WinboxRecordCodec.FormatDuration</c>, which is why the unit table and the
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

        // True for a list/array field that EncodeField has no specific encoder for (so it would otherwise be
        // silently mis-sent as a scalar string). Array wire types end in "[]"; 'multi…' UI types are the
        // WinBox multi-value controls (multitristatearray, …). multinumberrange, numberrangelist and
        // multinumber are handled before this check, so they never reach here.
        private static bool IsUnsupportedListType(string? wireType, string? uiType)
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
            // uppercase hex ("00155D041F03") rather than a byte[], so every macaddr field — which is a raw
            // on the wire — fell straight through to ToString() and reached the caller as one 12-digit run
            // where RouterOS prints 00:15:5D:04:1F:03. /interface/ethernet, /ip/arp, /ip/neighbor and
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
        // looking like something the router said (P2.25). There is no honest substitute, so this throws — and
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
