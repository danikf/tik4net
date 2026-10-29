using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    internal sealed partial class WinboxFieldResolver
    {
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
            /// Further names the SAME field is reported under, as <c>reported name → other spellings</c>.
            /// </summary>
            /// <remarks>
            /// For a field RouterOS itself renamed between versions over one key and one WinBox label:
            /// <c>/ip/service</c> prints <c>address</c> on 6.49.13 and <c>available-from</c> on 7.24, and
            /// nothing in the catalog distinguishes them. Reporting both spellings answers whichever name
            /// the router in hand uses, without asking it for its version — the read is a superset of that
            /// router's API by exactly the other version's word, and the O/R mapper takes the one its entity
            /// declares. The WRITE side needs no such thing: <see cref="ApiToJg"/> maps the other spelling
            /// onto the label, which both catalogs have.
            /// </remarks>
            public readonly IReadOnlyDictionary<string, string[]>? AlsoKnownAs;

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
            /// <c>/ip/hotspot/ip-binding</c>'s <c>bypassed</c> is one: the API prints a bool where WinBox
            /// reports the binding's <c>type</c> enum, and <c>bypassed</c> is that enum's member. The derived
            /// field is ADDED, not substituted: the enum keeps its own name, so native still reports the finer
            /// answer to anyone who wants it. Where the catalog itself names the flags as a <c>numflag</c>
            /// (the route's <c>active</c> and origin), <see cref="NumFlagMembers"/> is used instead.
            /// </remarks>
            public readonly IReadOnlyDictionary<string, Tuple<string, string>>? DerivedBools;

            /// <summary>
            /// API field → (upload half's <c>.jg</c> label, download half's label), for the fields RouterOS
            /// prints as one <c>upload/download</c> pair where the M2 model has two separate scalars. See
            /// <see cref="PairUiType"/>.
            /// </summary>
            public readonly IReadOnlyDictionary<string, Tuple<string, string>>? PairedFields;

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

            /// <summary>
            /// Synthetic fields for a key only SOME versions' windows leave unnamed: they rank below the path's
            /// window, as an ancestor's synthetics do, so a version whose window names the field keeps its own key.
            /// </summary>
            /// <remarks>
            /// RouterOS 6.49's route window sends vrf-interface at <c>0x3C</c> and declares no field for it; 7.x
            /// declares 'VRF Interface' at <c>u111</c>. An ordinary synthetic outranks the window by name and would
            /// send a 7.x write to <c>0x3C</c>. The same pairing rule applies as to <see cref="SyntheticFields"/>.
            /// </remarks>
            public readonly IReadOnlyDictionary<string, WinboxJgField>? FallbackFields;

            /// <summary>
            /// The API flags this path's <c>numflag</c> members are reported as — each a name the API has been
            /// seen printing as <c>name=true</c> on a row whose key carried that member.
            /// </summary>
            /// <remarks>
            /// <para>The catalog says WHICH key and WHICH value a flag is, per version (<c>/ip/route</c>'s origin
            /// is <c>u7</c> on RouterOS 6 and <c>u112</c> on 7); this set only says which member names the API
            /// prints as flags at all. That is not every <c>numflag</c>: the route's Contribution flags
            /// (<c>filtered</c>, <c>unreachable</c>) are not printed, and a script job's
            /// (<c>login</c>, <c>command</c>) are printed as <c>type=</c> instead. A member not named here is
            /// not reported, so a flag nobody has seen the router print is not a guess on the wire.</para>
            /// </remarks>
            public readonly ISet<string>? NumFlagMembers;

            /// <param name="both">API name → <c>.jg</c> label pairs that rename one value in both directions — "one name
            /// for one value": each goes into <see cref="ApiToJg"/> as written and into <see cref="JgToApi"/> reversed.</param>
            /// <param name="apiToJgOnly">Encode-only spellings, laid over <paramref name="both"/>: an API name a write may
            /// use whose label decodes to another name (the ping target <c>address</c> → <c>ping-to</c>).</param>
            /// <param name="jgToApiOnly">Decode-only names, laid over <paramref name="both"/> reversed: a label that reads as
            /// an API name the write does not spell that way (a second label for the same field).</param>
            /// <param name="keyToApi">See <see cref="KeyToApi"/>.</param>
            /// <param name="keyUiType">See <see cref="KeyUiType"/>.</param>
            /// <param name="alsoKnownAs">See <see cref="AlsoKnownAs"/>.</param>
            /// <param name="addrPortPairs">See <see cref="AddrPortPairs"/>.</param>
            /// <param name="derivedBools">See <see cref="DerivedBools"/>.</param>
            /// <param name="syntheticFields">See <see cref="SyntheticFields"/>.</param>
            /// <param name="pairedFields">See <see cref="PairedFields"/>.</param>
            /// <param name="numFlagMembers">See <see cref="NumFlagMembers"/>.</param>
            /// <param name="fallbackFields">See <see cref="FallbackFields"/>.</param>
            public FieldAliasSet(IReadOnlyDictionary<string, string>? both = null,
                IReadOnlyDictionary<string, string>? apiToJgOnly = null, IReadOnlyDictionary<string, string>? jgToApiOnly = null,
                IReadOnlyDictionary<int, string>? keyToApi = null, IReadOnlyDictionary<int, string>? keyUiType = null,
                IReadOnlyDictionary<string, string[]>? alsoKnownAs = null,
                IReadOnlyDictionary<string, string>? addrPortPairs = null,
                IReadOnlyDictionary<string, Tuple<string, string>>? derivedBools = null,
                IReadOnlyDictionary<string, WinboxJgField>? syntheticFields = null,
                IReadOnlyDictionary<string, Tuple<string, string>>? pairedFields = null,
                IEnumerable<string>? numFlagMembers = null,
                IReadOnlyDictionary<string, WinboxJgField>? fallbackFields = null)
            {
                var apiToJg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var jgToApi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (both != null)
                    foreach (var kv in both) { apiToJg[kv.Key] = kv.Value; jgToApi[kv.Value] = kv.Key; }
                if (apiToJgOnly != null) foreach (var kv in apiToJgOnly) apiToJg[kv.Key] = kv.Value;
                if (jgToApiOnly != null) foreach (var kv in jgToApiOnly) jgToApi[kv.Key] = kv.Value;
                ApiToJg = apiToJg; JgToApi = jgToApi;
                AlsoKnownAs = alsoKnownAs;
                NumFlagMembers = numFlagMembers == null
                    ? null : new HashSet<string>(numFlagMembers, StringComparer.OrdinalIgnoreCase);
                KeyToApi = keyToApi ?? new Dictionary<int, string>();
                KeyUiType = keyUiType ?? new Dictionary<int, string>();
                AddrPortPairs = addrPortPairs;
                PairedFields = pairedFields;
                DerivedBools = derivedBools;
                SyntheticFields = syntheticFields;
                FallbackFields = fallbackFields;
            }
        }

        /// <summary>
        /// A paired-field table: (API name, upload half's label, download half's label). Reads at the call
        /// site the way the field reads on the wire.
        /// </summary>
        private static Dictionary<string, Tuple<string, string>> Pairs(
            params (string api, string upload, string download)[] entries)
        {
            var result = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                result[e.api] = Tuple.Create(e.upload, e.download);
            return result;
        }

        // The routing tables' state flag, named as the API names it (see the /routing entries).
        private static FieldAliasSet RoutingInactive()
            => new FieldAliasSet(keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" });

        // `default` derived from the record id — the row the router ships is *0 (see the /ip/hotspot entries).
        private static FieldAliasSet HotspotDefault()
            => new FieldAliasSet(derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = Tuple.Create(TikSpecialProperties.Id, "*0"),
                });

        private static Dictionary<string, string> Ci(params (string, string)[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // The WEP/static-key tuples of a wireless security profile. Each is an algorithm enm plus a secret,
        // and the .jg gives the pair one name ('Key 0') and its two children none.
        private static readonly Dictionary<int, string> WepAlgoMap = new Dictionary<int, string>
        {
            [0] = "none", [1] = "40bit-wep", [2] = "104bit-wep", [3] = "aes-ccm", [4] = "tkip",
        };

        private static WinboxJgField WepAlgo(string apiName, int key)
            => new WinboxJgField(apiName, key, "u32", false, enumMap: WepAlgoMap);

        // A 'secret' in the .jg; the API prints and accepts it as a plain string.
        private static WinboxJgField WepKey(string apiName, int key)
            => new WinboxJgField(apiName, key, "string", false);

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
                    both: Ci(("count", "packet-count"), ("size", "packet-size"), ("seq", "seq-#"), ("min-rtt", "min"),
                             ("avg-rtt", "avg"), ("max-rtt", "max")),
                    apiToJgOnly: Ci(("address", "ping-to")),
                    // The reply's responder address rides at key 0x1 (u32 ipaddr), which the .jg leaves unnamed —
                    // so its name and ipaddr formatting are supplied here (the value is a packed-u32 IPv4).
                    keyToApi: new Dictionary<int, string> { [0x1] = "host" },
                    keyUiType: new Dictionary<int, string> { [0x1] = "ipaddr" }),

                // /tool/romon/ping (ToolRomonPing). The 'RoMON Ping' window labels the request size 'Packet Size'
                // and the reply's 'Reply Size' (both key 0x2, both the API's 'size'); the statusbar names
                // min/avg/max bare and leaves the loss percentage (key 0x6b) unnamed. The reply record, dumped
                // whole (RomonAgentProbeTest, 7.24.4), carries four keys this path has to supply itself:
                //  - 0x1 is the request's 'ID' and the row's 'Host', and on [127,2] ALSO the settings' 'Enabled'
                //    (bool) — a contested key whose type-qualified registration named every reply's host 'id'.
                //    A synthetic of this path is the most specific claim, so it names the key 'host'.
                //  - 0x64 'Time' is a fixedpoint of scale 1000 with no postfix, so it read 0.001 where the API
                //    prints 1ms; its wire unit is milliseconds, as the statusbar's min/avg/max (postfix ms) are.
                //  - 0x69 / 0x6A are the running sent / received counts; the .jg does not name them.
                ["/tool/romon/ping"] = new FieldAliasSet(
                    both: Ci(("size", "packet-size"), ("seq", "seq-#"), ("min-rtt", "min"), ("avg-rtt", "avg"),
                             ("max-rtt", "max")),
                    jgToApiOnly: Ci(("reply-size", "size")),
                    keyToApi: new Dictionary<int, string> { [0x6b] = "packet-loss" },
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["host"] = new WinboxJgField("host", 0x1, "raw", true, uiType: "macaddr"),
                        ["time"] = new WinboxJgField("time", 0x64, "u32", true, uiType: "number", postfix: "ms"),
                        ["sent"] = new WinboxJgField("sent", 0x69, "u32", true, uiType: "number"),
                        ["received"] = new WinboxJgField("received", 0x6A, "u32", true, uiType: "number"),
                    }),

                // /tool/traceroute (ToolTraceroute). The .jg window (type:'query', path:[26]) labels the target
                // 'Traceroute To' and the per-hop responder 'Host'; the API calls both 'address'.
                // 'count'/'max-hops' need no alias — the window labels them exactly that.
                ["/tool/traceroute"] = new FieldAliasSet(
                    both: Ci(("size", "packet-size")),
                    apiToJgOnly: Ci(("address", "traceroute-to")),
                    jgToApiOnly: Ci(("host", "address"))),

                // /tool/wol (ToolWol, standalone 'Wake on LAN' doit window): the API sends 'mac', WinBox labels
                // the same field 'MAC Address'. 'interface' matches verbatim, so only the one alias is needed.
                ["/tool/wol"] = new FieldAliasSet(
                    both: Ci(("mac", "mac-address"))),

                // /system/identity: the singleton's one field is labelled 'Identity' in WinBox (.jg
                // {title:'Identity',type:'item',path:[24,1],c:[{name:'Identity',id:'sc'},…]}), while the API
                // calls it 'name'. Without the alias a read returned {"identity":…} — so LoadSingle
                // <SystemIdentity> threw "Missing field 'name'" — and a write resolved 'name' through the
                // FallbackSeed to key 0x10006, a key this handler does not have.
                // /tool/bandwidth-server: the window calls the two address lists Ipv4/Ipv6 Allowed Networks
                // where the API says allowed-addresses4/6. One name for one value, both directions.
                ["/tool/bandwidth-server"] = new FieldAliasSet(
                    both: Ci(("allowed-addresses4", "ipv4-allowed-networks"),
                             ("allowed-addresses6", "ipv6-allowed-networks"))),

                // /user/group: the window's field is 'Policies', the API's is 'policy' — one name for one
                // value, so the alias is the whole difference. (Its members come from the policy table
                // [13,3], not from a static map; see EncodeField's bit-set branch.)
                ["/user/group"] = new FieldAliasSet(
                    both: Ci(("policy", "policies"))),

                ["/system/identity"] = new FieldAliasSet(
                    both: Ci(("name", "identity"))),

                // /file: the Files window labels the record's name 'File Name' (it is the window's
                // nameval), while the API calls it 'name'. Without the alias every file row came back
                // without a 'name' and the entity read threw "Missing field 'name'".
                ["/file"] = new FieldAliasSet(
                    both: Ci(("name", "file-name"))),

                // /system/logging/action: the window's field is {name:'Type',title:'Target'} — the API name
                // is 'target', and 'type' is not an API field of this table at all.
                //
                // The rest are deck-pane leaves whose label the API spells differently. The memory/disk/email
                // panes are kind-prefixed (see PanePrefixedPaths) and their labels then match the API exactly
                // — memory-lines, disk-file-count, email-cc — so only three remain, plus the echo pane's one
                // field. Checked against `/system/logging/action add ?` on 7.23.2.
                ["/system/logging/action"] = new FieldAliasSet(
                    both: Ci(("target", "type"),
                             // remote pane, 'Remote Address'
                             ("remote", "remote-address"), ("remote-protocol", "remote-log-protocol"),
                             // email pane, 'Email' (already kind-named)
                             ("email-to", "email"),
                             // echo pane, 'Save'
                             ("remember", "save"), ("syslog-time-format", "timestamp-format"),
                             ("add-topics-string", "add-topics"))),
                // The remote pane's 'Timestamp Format' (u13) and 'Add Topics' (b23) are the API's
                // syslog-time-format and add-topics-string. Both ride on a format condition (on:'timestamp',
                // on:'bsd') that WinboxJgCondition honours, so they are reported exactly where the API reports
                // them; the members the window captions differently ('BSD' is bsd-syslog, the format's 'BSD
                // syslog' is syslog) are renamed in EnumMemberAliases. Name and value measured on 7.24.4 against
                // a t4n action with remote-log-format=syslog: syslog-time-format=bsd-syslog, add-topics-string=false.

                // /queue/type: every pane IS kind-prefixed, so the prefix is derived and only the leaf text
                // differs — WinBox calls the queue depth 'Queue Size' where the API says 'limit', and the RED
                // pane's 'Avg. Packet Size' is 'avg-packet'. The kind-carrying labels ('BFIFO Queue Size')
                // keep their own prefix, so the alias is on the whole name. Checked against
                // `/queue/type add ?` on 7.23.2.
                // ('Type Name' is the window's nameval, the same shape as /file's 'File Name' — without it
                // every row came back without a 'name' and the entity read threw "Missing field 'name'".)
                ["/queue/type"] = new FieldAliasSet(
                    both: Ci(("name", "type-name"), ("bfifo-limit", "bfifo-queue-size"),
                             ("pfifo-limit", "pfifo-queue-size"), ("mq-pfifo-limit", "mq-pfifo-mq-queue-size"),
                             ("red-limit", "red-queue-size"), ("red-avg-packet", "red-avg-packet-size"),
                             ("pcq-limit", "pcq-queue-size"), ("pcq-total-limit", "pcq-total-queue-size"))),

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
                //
                // The pairings added below it, and everything down to /ip/route, were each established by
                // MOVING the value: the audit reads every path
                // over both transports and, for a name only the API reports, names the field only native
                // reports that carries the SAME value on every row (TransportPathMapAuditTest's
                // "value matches"). A proposal it makes on a bool or a zero is a coincidence and is not
                // taken; the ones here either move a distinctive value or were confirmed by writing one.

                // /interface/ethernet, Loop Protect tab: the .jg declares {name:'Loop Protect',type:'tab'}
                // and then Loop Protect / Send Interval / Disable Time / Status under it. RouterOS prefixes
                // the tab's name onto all but the field that IS the tab's name. Confirmed by setting
                // send-interval=7s and disable-time=9m on one interface and not its neighbour.
                //
                // disable-running-check has no box in any window: no .jg names key 0x1000D, and webfig's
                // master.js has no running-check field either. The router still sends it on every ether row.
                // Two unnamed bools were candidates (0x1000D=False, 0x3F4=True — polarity alone could not
                // choose); setting disable-running-check=yes on ether2 over the API moved 0x1000D False → True
                // and left 0x3F4 and ether1 alone (7.24.2). Same polarity as the API's field, writable.
                ["/interface/ethernet"] = new FieldAliasSet(
                    both: Ci(("auto-negotiation", "autoneg"), ("loop-protect-status", "status"),
                             ("loop-protect-send-interval", "send-interval"),
                             ("loop-protect-disable-time", "disable-time")),
                    jgToApiOnly: Ci(("auto-negotiation", "auto-negotiation-status")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["disable-running-check"] = new WinboxJgField("disable-running-check", 0x1000D, "bool", false),
                        // The factory MAC: unnamed 0x404 carries the API's orig-mac-address (AA:BB:CC:DD:EE:FF on
                        // ether1, 7.24.2) beside 0x3E9's mac-address. Read-only, as the API has it.
                        ["orig-mac-address"] = new WinboxJgField("orig-mac-address", 0x404, "raw", true,
                                                                 uiType: "macaddr"),
                        // cable-settings: unnamed 0x3F5. default/standard/short moved it to 2/1/0 on ether2 (7.24.2).
                        ["cable-settings"] = new WinboxJgField("cable-settings", 0x3F5, "u32", false,
                            enumMap: new Dictionary<int, string> { [0] = "short", [1] = "standard", [2] = "default" }),
                    }),

                // /ip/arp and /ip/neighbor: WinBox's 'IP Address' is the API's `address`. On /ip/neighbor
                // the router ALSO prints `address4` for the same value and `address6` for the v6 one, so
                // one native field answers to two API names there; `address` is the one every menu uses.
                ["/ip/arp"] = new FieldAliasSet(
                    both: Ci(("address", "ip-address"))),

                // `interface` is a LIST on the API — a neighbour heard on a bridge port is heard on its bridge too,
                // `interface=ether2,t4n-prio-br` — and the window's 'Interface' (u4) holds only the first of them.
                // The whole list rides the undeclared u32[] 0x13: [3,283] beside 0x4=3 on that row, [2] beside
                // 0x4=2 on a neighbour heard on one interface (7.21.5, 2026-09-28).
                ["/ip/neighbor"] = new FieldAliasSet(
                    both: Ci(("address", "ip-address"), ("address6", "ipv6-address"), ("board", "board-name"),
                             ("unpack", "unpacking")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["interface"] = new WinboxJgField("interface", 0x13, "u32[]", true,
                                                          uiType: "multinumber", refHandler: new[] { 20, 0 }),
                    },
                    // A neighbour heard only over IPv6 has no IPv4 key, and the API's `address` is then its IPv6
                    // address: 6.49.13 prints address=address6=fe80::… for CHR3 on ether2. Reported under both names
                    // only where `address` is absent, so a row with an IPv4 address keeps it.
                    alsoKnownAs: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["address6"] = new[] { "address" },
                    }),

                // /ip/dhcp-client: three, all moving a distinctive value in one read — the lease address
                // (192.168.4.236/24), the reconfigure flag, and the routing-table list.
                ["/ip/dhcp-client"] = new FieldAliasSet(
                    both: Ci(("address", "ip-address"), ("allow-reconfigure", "allow-reconfigure-messages"),
                             ("default-route-tables", "routing-tables"))),

                // /user: WinBox's 'Allowed Address'. Both rows of a stock router leave it EMPTY, which is
                // why the audit could not propose it — two blanks agree vacuously. Confirmed by creating a
                // user with address=192.168.251.0/24 and watching that one row change and the others not.
                ["/user"] = new FieldAliasSet(
                    both: Ci(("address", "allowed-address"))),

                // The rest: one WinBox label, one API name, no structure to derive it from.
                // accounting and interim-update are 7.x's 'Accounting' (b3) and 'Interim Update' (u2); the 6.49.13
                // window declares neither, but the router sends both keys. Confirmed by value there:
                // accounting=no moved 0x3 True -> False, interim-update=1m30s moved 0x2 0 -> 90.
                ["/ip/dhcp-server/config"] = new FieldAliasSet(
                    both: Ci(("store-leases-disk", "store-leases-on-disk")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["accounting"] = new WinboxJgField("accounting", 0x3, "bool", false),
                        ["interim-update"] = new WinboxJgField("interim-update", 0x2, "u32", false, uiType: "interval"),
                    }),

                // /ip/dns: 'mDNS Repeater Interfaces' is the API's mdns-repeat-ifaces. Both read empty on a
                // stock router, so the audit could not propose it; settled by setting it to ether2 and
                // watching that name carry the value on native.
                ["/ip/dns"] = new FieldAliasSet(
                    both: Ci(("verify-doh-cert", "verify-doh-certificate"),
                             ("mdns-repeat-ifaces", "mdns-repeater-interfaces"))),

                // /snmp: WinBox paints 'Contact Info' beside the box the API calls contact. Empty on a stock
                // router; settled by writing a distinctive string and reading it back under that name.
                ["/snmp"] = new FieldAliasSet(
                    both: Ci(("contact", "contact-info"))),

                // 'Available From' is the API's available-from on 7.24 and its address on 6.49.13 — one key
                // (0x6), one label, two API words (ftp address=10.99.0.0/24,10.98.0.1/32 arrived there on 6.x).
                ["/ip/service"] = new FieldAliasSet(
                    both: Ci(("proto", "protocol")),
                    apiToJgOnly: Ci(("address", "available-from")),
                    alsoKnownAs: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["available-from"] = new[] { "address" },
                    }),

                // The same shape: 'Server' (0x1) is the API's server on 7.24 and its address on 6.49.13.
                ["/tool/e-mail"] = new FieldAliasSet(
                    apiToJgOnly: Ci(("address", "server")),
                    alsoKnownAs: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["server"] = new[] { "address" },
                    }),

                ["/ip/socks"] = new FieldAliasSet(
                    both: Ci(("auth-method", "authentication-method"))),

                ["/ip/proxy"] = new FieldAliasSet(
                    both: Ci(("cache-hit-dscp", "cache-hit-dscp-(tos)"))),

                // 7.x: 'NTP Servers'. 6.49.13 has two boxes instead, 'Primary/Secondary NTP Server' (u2, u3), and
                // calls the API's last-update-before 'Last Update' (u6a, an age); no 7.x NTP label collides.
                ["/system/ntp/client"] = new FieldAliasSet(
                    both: Ci(("servers", "ntp-servers"), ("primary-ntp", "primary-ntp-server"),
                             ("secondary-ntp", "secondary-ntp-server"), ("last-update-before", "last-update"))),

                // /ip/accounting (RouterOS 6 only): the window says 'Enable Accounting' (b15).
                ["/ip/accounting"] = new FieldAliasSet(
                    both: Ci(("enabled", "enable-accounting"))),

                // /system/resource: the window declares BOTH 'freq' and 'CPU Frequency' on u5 and first-wins
                // took 'freq'. One key, one value, and the API's name for it is cpu-frequency.
                // /system/resource: the window declares BOTH 'freq' and 'CPU Frequency' on u5 and first-wins
                // took 'freq'. One key, one value — and a value that MOVES between two reads (an AMD host
                // boosting), which is why the audit's own proposal for it could as easily have been a
                // coincidence; the .jg settles it, not the reading. The disk and sector counters pair by
                // name alone: both sector counters read the same number on a router that has been up once.
                ["/system/resource"] = new FieldAliasSet(
                    both: Ci(("cpu-frequency", "freq"), ("total-hdd-space", "total-hdd-size"),
                             ("write-sect-total", "total-sector-writes"),
                             ("write-sect-since-reboot", "sector-writes-since-reboot")),
                    // No window declares the platform; the record carries it unnamed at 0x1B, the one key holding
                    // the API's platform=MikroTik (7.24.2). Read-only, as the API has it.
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platform"] = new WinboxJgField("platform", 0x1B, "string", true),
                    }),

                // /ip/traffic-flow: both fields read 0 on a stock router, so the audit's value match was
                // vacuous. Confirmed by writing 13 and 27 and watching the two arrive in that order.
                ["/ip/traffic-flow"] = new FieldAliasSet(
                    both: Ci(("sampling-interval", "packet-sampling-interval"),
                             ("sampling-space", "packet-sampling-space"))),

                // Each pairing below was confirmed by MOVING the value on a seeded row over the API and naming
                // the one native field that changed (7.24.2) — never by the two names looking alike.
                //
                // /ip/dhcp-relay: 'Local Address As Source IP' is the API's local-address-as-src-ip (yes → true).
                ["/ip/dhcp-relay"] = new FieldAliasSet(
                    both: Ci(("local-address-as-src-ip", "local-address-as-source-ip"))),

                // /ip/traffic-flow/target: webfig's 'v9/IPFIX Template …' boxes. Refresh is a packet count (33
                // moved 33). Timeout is declared a plain {number,def:1800} and the API prints it as a duration —
                // 44m moved the key to 2640 — so it is typed here as an interval rather than aliased.
                ["/ip/traffic-flow/target"] = new FieldAliasSet(
                    both: Ci(("v9-template-refresh", "v9/ipfix-template-refresh")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["v9-template-timeout"] = new WinboxJgField("v9-template-timeout", 0x5, "u32", false,
                                                                    uiType: "interval"),
                    }),

                // /queue/tree: the window says 'Packet Marks' where the API says packet-mark (t4n-probe-mark moved).
                // …and 'Queue Type' is the API's queue: queue=pcq-upload-default moved u0xA to that type's id.
                // …and the window's statistics 'Avg. Rate' (ucc) and 'Avg. Packet Rate' (ucd) are the API's rate and
                // packet-rate: the only rate fields the Queue Tree window declares.
                ["/queue/tree"] = new FieldAliasSet(
                    both: Ci(("packet-mark", "packet-marks"), ("queue", "queue-type"), ("rate", "avg-rate"),
                             ("packet-rate", "avg-packet-rate"))),

                // /interface/vxlan: 'Remote Checksum Offload' is rem-csum (tx moved it).
                ["/interface/vxlan"] = new FieldAliasSet(
                    both: Ci(("rem-csum", "remote-checksum-offload"))),

                // /ip/dhcp-server: 'DNS Entry Suffix' and 'Support The Broadband Forum TR-101'.
                // …and address-lists is a key the DHCP window does not declare: address-lists=t4n-dsal moved 0x1E
                // to the one-element string list [t4n-dsal].
                ["/ip/dhcp-server"] = new FieldAliasSet(
                    both: Ci(("add-dns-entries-suffix", "dns-entry-suffix"),
                             ("support-broadband-tr101", "support-the-broadband-forum-tr-101")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["address-lists"] = new WinboxJgField("address-lists", 0x1E, "string[]", false,
                                                              uiType: "multistring", elementUiType: "string"),
                    }),

                // /ip/dhcp-server/lease: WinBox's 'Address List' is the API's plural address-lists. Its own set,
                // so it does not inherit /ip/dhcp-server's above. `blocked` is the lease's 'Block Access' box as
                // a flag: the catalog declares both on b7e, one key can carry one name, and block-access=yes set
                // the key and the API's blocked=true together.
                // …and the window's plural 'DHCP Options' (U11) is the API's singular dhcp-option: clearing it moved
                // the list [1] → [].
                ["/ip/dhcp-server/lease"] = new FieldAliasSet(
                    both: Ci(("address-lists", "address-list"), ("dhcp-option", "dhcp-options")),
                    derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["blocked"] = Tuple.Create("block-access", "true"),
                    }),

                // /interface/vrrp: 'on-fail' is a key no window declares — on-fail=none moved 0x13 to "none" on a
                // seeded row (7.24.2). And the router's state flag 0xFE0008, which the generic interface window
                // calls 'inactive', is what the API prints for a VRRP row as `invalid` (true on an interface with
                // no IPv4 address, both transports). Merged over the base /interface set (see ResolveAliases).
                // …and the Conn. Tracking tab, whose labels abbreviate what the API spells out (its tab completion
                // lists connection-tracking-mode and connection-tracking-port). The mode rides u17 beside a bool on
                // the same key: connection-tracking-mode=active-active moved the number to 1 and WinBox showed
                // active/active, while the catalog's members are spelled with a slash where the API uses a dash.
                ["/interface/vrrp"] = new FieldAliasSet(
                    both: Ci(("connection-tracking-port", "conn-tracking-port")),
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "invalid" },
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["on-fail"] = new WinboxJgField("on-fail", 0x13, "string", false),
                        ["connection-tracking-mode"] = new WinboxJgField("connection-tracking-mode", 0x17, "u32", false,
                            enumMap: new Dictionary<int, string> { [0] = "passive-active", [1] = "active-active" }),
                    }),

                // The routing tables' state flag 0xFE0008 is `inactive` to the API (seeded rows, 7.24.2: true on a
                // BGP connection with no local address, on a disabled routing rule and filter rule; false on the
                // OSPF instance, area and template — native's `invalid` agreed row for row). The seed's name is
                // `invalid`; the key is named here per path. The two BGP tables and the OSPF template carry the
                // same entry in their own sets.
                ["/routing/filter/rule"] = RoutingInactive(),
                // RouterOS 6 labels the area's name 'Area Name' (0x2 = backbone); 7 labels it 'Name', and the
                // alias yields to that (AliasToJg).
                // The state flag is the API's `inactive` on 7.24 and its `invalid` on 6.49.13 — the same
                // 0xFE0008 the other routing tables carry, so both words are reported.
                ["/routing/ospf/area"] = new FieldAliasSet(
                    both: Ci(("name", "area-name")),
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" },
                    alsoKnownAs: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["inactive"] = new[] { "invalid" },
                    }),
                // /routing/ospf/instance's state flag is with its RouterOS 6 labels, further down: one entry per path.
                ["/routing/rule"] = RoutingInactive(),

                // /ip/ipsec/peer: the PPK secret is a key no Peers window declares — moved to 0x36.
                // …and `responder`: passive=yes moved the unnamed bool 0xE False → True together with the API's
                // responder (0x23, the Passive box, moved beside it). Read-only, as the API has it.
                ["/ip/ipsec/peer"] = new FieldAliasSet(
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ppk-secret"] = new WinboxJgField("ppk-secret", 0x36, "string", false),
                        ["responder"] = new WinboxJgField("responder", 0xE, "bool", true),
                    }),

                // /interface/bridge: auto-mac is a key no Bridge window declares. auto-mac=no with
                // admin-mac=02:00:00:00:00:01 moved the unnamed bool 0x65 True → False (0x66 took the admin MAC)
                // on a probe bridge (7.24.2). Merged over the base /interface set.
                // …and the VLAN tab's 'EtherType' is one word where the API says ether-type (6.49.13, 7.21.5, 7.24.4).
                ["/interface/bridge"] = new FieldAliasSet(
                    both: Ci(("ether-type", "ethertype")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["auto-mac"] = new WinboxJgField("auto-mac", 0x65, "bool", false),
                    }),

                // /interface/l2tp-server/server: the two accept filters are keys no window declares. Each value the
                // router completes was set and read back: accept-proto-version all/l2tpv2/l2tpv3 moved 0xDF to
                // 0/2/3, accept-pseudowire-type all/ether/ppp moved 0xDE to 0/5/7 (7.24.2).
                ["/interface/l2tp-server/server"] = new FieldAliasSet(
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["accept-proto-version"] = new WinboxJgField("accept-proto-version", 0xDF, "u32", false,
                            enumMap: new Dictionary<int, string> { [0] = "all", [2] = "l2tpv2", [3] = "l2tpv3" }),
                        ["accept-pseudowire-type"] = new WinboxJgField("accept-pseudowire-type", 0xDE, "u32", false,
                            enumMap: new Dictionary<int, string> { [0] = "all", [5] = "ether", [7] = "ppp" }),
                    }),

                // /interface/wireguard/peers: the Peers window's 'Endpoint' is the API's endpoint-address (the
                // probe peer's 10.99.0.20 rides m3ee). Its 'Rx'/'Tx' (q3f3/q3f4) lose their names to the interface
                // counters' label overrides, so they are supplied by key; the API prints the byte counts bare.
                ["/interface/wireguard/peers"] = new FieldAliasSet(
                    both: Ci(("endpoint-address", "endpoint")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["rx"] = new WinboxJgField("rx", 0x3F3, "u64", true),
                        ["tx"] = new WinboxJgField("tx", 0x3F4, "u64", true),
                    }),

                // /ip/address: actual-interface is a key no window declares. The unnamed 0x5 carries the interface
                // id on both stock rows (3 on the ether2 address, 2 on ether1's), matching the API's
                // actual-interface=ether2/ether1. Read-only; a reference into the interface table.
                ["/ip/address"] = new FieldAliasSet(
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["actual-interface"] = new WinboxJgField("actual-interface", 0x5, "u32", true,
                                                                 uiType: "enm", refHandler: new[] { 20, 0 }),
                    }),

                // /caps-man/provisioning: the window's singular 'Slave Configuration' (U500c) is the API's plural
                // slave-configurations — clearing it moved the list [77] → [].
                ["/caps-man/provisioning"] = new FieldAliasSet(
                    both: Ci(("slave-configurations", "slave-configuration"))),

                // /system/package: webfig declares `available` as an inverted flag on the Installed key
                // ({name:'available',type:'flag',id:'bb',hint:'A',inv:1}); the key already carries `installed`, so
                // the flag is derived from it. Every row a router lists is installed=true, available=false.
                ["/system/package"] = new FieldAliasSet(
                    derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["available"] = Tuple.Create("installed", "false"),
                    },
                    // `bundle` is the window's nonpublic {number, u6}: the record id of the package this one ships
                    // in, which the API prints by NAME — routeros-x86 where u6=1 (6.49.13; the 7.x windows declare
                    // the same u6). A reference into this very table.
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["bundle"] = new WinboxJgField("bundle", 0x6, "u32", true, uiType: "enm", refHandler: new[] { 24, 23 }),
                    }),

                // /ip/firewall/mangle: action=route's destination is u3f4, labelled 'Route Dst.' on 7.x but
                // 'Dst. Address' on 6.49.13 — the rule matcher's own label, so there it loses the name to
                // dst-address and the value (on the wire, 10.99.3.1 at 0x3F4) was reported under no name and
                // could not be written. Same key and type on both versions.
                // The rest measured on 7.24.5 by moving each value over the API and reading the raw record:
                //  - 'New DSCP (TOS)' u3f3 and 'New TCP MSS' u3eb are new-dscp and new-mss.
                //  - realm is in no field of the Mangle window, yet rides every rule that sets it: the number at
                //    0x7E, its present flag at 0x1C2 and its '!' at 0xE0 (realm=!7 → 0x7E=7, 0x1C2, 0xE0=True;
                //    realm=9 → 0xE0=False).
                //  - new-ttl is one API field over two boxes: 'TTL Action' u3ee (0 = set, 1 = increment,
                //    2 = decrement; the window calls the first 'change') and 'New TTL' u3ef — increment:3 is
                //    {0x3EE=1, 0x3EF=3}. Alone, 'New TTL' read as new-ttl=3.
                ["/ip/firewall/mangle"] = new FieldAliasSet(
                    both: Ci(("new-dscp", "new-dscp-(tos)"), ("new-mss", "new-tcp-mss")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["route-dst"] = new WinboxJgField("route-dst", 0x3F4, "u32", false, uiType: "ipaddr"),
                        ["realm"] = new WinboxJgField("realm", 0x7E, "u32", false, uiType: "number",
                            optKey: 0x1C2, notKey: 0xE0),
                        ["new-ttl"] = new WinboxJgField("new-ttl", 0x3EE, "u32", false, uiType: WinboxJgCatalog.TupleUiType,
                            elementSeparator: ":",
                            elementParts: new[]
                            {
                                new WinboxJgElementPart(0x3EE, "enm", 0,
                                    enumMap: new Dictionary<int, string> { [0] = "set", [1] = "increment", [2] = "decrement" }),
                                new WinboxJgElementPart(0x3EF, "number", 0),
                            }),
                        ["ipsec-policy"] = FirewallIpsecPolicy(),
                        ["limit"] = FirewallLimit(),
                        ["dst-limit"] = FirewallDstLimit(),
                        ["tos"] = FirewallTos(),
                    }),

                // /ip/firewall/filter: see FirewallIpsecPolicy and FirewallLimit; realm is mangle's (in no field of the
                // Firewall Rule window either, the same three keys: realm=!7 is 0x7E=7, 0x1C2, 0xE0=True on 7.24.5).
                ["/ip/firewall/filter"] = new FieldAliasSet(
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ipsec-policy"] = FirewallIpsecPolicy(),
                        ["limit"] = FirewallLimit(),
                        ["dst-limit"] = FirewallDstLimit(),
                        ["tos"] = FirewallTos(),
                        ["realm"] = new WinboxJgField("realm", 0x7E, "u32", false, uiType: "number", optKey: 0x1C2, notKey: 0xE0),
                    }),

                // /ip/firewall/raw: the Raw window draws no bridge-port or packet-mark boxes, yet a raw rule takes them
                // and carries them in the filter window's keys (packet-mark=!x is {0x19A, 0x1D=True, 0x1E=x}) — in-bridge-port=!ether1 is {0x1B0, 0xD1=True, 0x54=2},
                // in-bridge-port-list=!all {0xBB8, 0x7D0=True, 0x44C=0x2000000}, the out- pair one key further on
                // (7.24.5, every value moved over the API and the raw record read back).
                ["/ip/firewall/raw"] = new FieldAliasSet(
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ipsec-policy"] = FirewallIpsecPolicy(),
                        ["limit"] = FirewallLimit(),
                        ["dst-limit"] = FirewallDstLimit(),
                        ["tos"] = FirewallTos(),
                        ["packet-mark"] = new WinboxJgField("packet-mark", 0x1E, "string", false, optKey: 0x19A, notKey: 0x1D),
                        ["in-bridge-port"] = FirewallBridgePort("in-bridge-port", 0x54, 0x1B0, 0xD1),
                        ["out-bridge-port"] = FirewallBridgePort("out-bridge-port", 0x55, 0x1B1, 0xD2),
                        ["in-bridge-port-list"] = new WinboxJgField("in-bridge-port-list", 0x44C, "u32", false,
                            uiType: "enm", refHandler: new[] { 20, 90 }, optKey: 0xBB8, notKey: 0x7D0),
                        ["out-bridge-port-list"] = new WinboxJgField("out-bridge-port-list", 0x44D, "u32", false,
                            uiType: "enm", refHandler: new[] { 20, 90 }, optKey: 0xBB9, notKey: 0x7D1),
                    }),

                // /ip/firewall/connection: 'helper used' is the API's uses-helper, and the GRE key is a key the
                // Connections window does not declare — the one GRE row (to 10.99.0.8) carried the API's
                // gre-key=26642 at 0x15, which no other row sent (7.24.2). Read-only, as the table is.
                ["/ip/firewall/connection"] = new FieldAliasSet(
                    both: Ci(("uses-helper", "helper-used")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gre-key"] = new WinboxJgField("gre-key", 0x15, "u32", true),
                    }),

                // /interface/bridge/port: the status tab's 'External FDB' (bce) is external-fdb-status. Its 'Hw.
                // Offload' (bd7) is NOT the API's hw: on the seeded ether2 port the API printed hw=true where bd7 read
                // false, and a port whose record carries no bd7 has no hw either (7.24.2).
                ["/interface/bridge/port"] = new FieldAliasSet(
                    both: Ci(("external-fdb-status", "external-fdb"))),

                // /ip/ipsec/active-peers: the window declares 'Side' {enm b5: initiator, responder} and a 'Responder'
                // flag on the same b5, so the key carries `side` and the flag is derived from it. The two SPIs are
                // keys no window declares: an initiating peer printed spii=ef035cfdda65233b and
                // spir=0000000000000000, the strings the record carried at 0x15 and 0x16 (7.24.2).
                ["/ip/ipsec/active-peers"] = new FieldAliasSet(
                    derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["responder"] = Tuple.Create("side", "responder"),
                    },
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["spii"] = new WinboxJgField("spii", 0x15, "string", true),
                        ["spir"] = new WinboxJgField("spir", 0x16, "string", true),
                    }),

                // /ip/hotspot/user and /ip/hotspot/user/profile: `default` marks the row the router ships, and
                // webfig declares it as a flag ON THE RECORD ID (ufe0001) — no key of its own. The API prints
                // default=true on exactly the *0 row of each table (7.24.2), so it is derived from the id.
                ["/ip/hotspot/user"] = HotspotDefault(),
                // The user profile's 'Rate Limit (rx/tx)' carries its units in the label, as on the server
                // profile: the API says rate-limit, with the same value (1M/2M on a t4n profile, 7.24.4).
                ["/ip/hotspot/user/profile"] = new FieldAliasSet(
                    both: Ci(("rate-limit", "rate-limit-(rx/tx)")),
                    derivedBools: HotspotDefault().DerivedBools),

                // /ip/hotspot/ip-binding: `bypassed` is the binding's type, as a flag (type=bypassed, bypassed=true).
                ["/ip/hotspot/ip-binding"] = new FieldAliasSet(
                    derivedBools: new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["bypassed"] = Tuple.Create("type", "bypassed"),
                    }),

                // /ip/settings: confirmed by writing 393216 and 27 and reading both back. `icmp-rate-mask` is a key
                // no window declares: icmp-rate-mask=0x1819 moved the unnamed 0x1C from 6168 to 6169, and the API
                // prints it in hex with the prefix.
                ["/ip/settings"] = new FieldAliasSet(
                    both: Ci(("ipv4-high-fragment-thresh", "ipv4-fragment-threshold-bytes"),
                             ("ipv4-fragment-time", "ipv4-fragment-timeout")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["icmp-rate-mask"] = new WinboxJgField("icmp-rate-mask", 0x1C, "u32", false,
                                                               postfix: "hex", radix: 16),
                    }),

                // The two IPsec algorithm sets: singular where the window says plural, and vice versa.
                ["/ip/ipsec/profile"] = new FieldAliasSet(
                    both: Ci(("hash-algorithm", "hash-algorithms"), ("enc-algorithm", "encryption-algorithm"))),

                ["/ip/ipsec/proposal"] = new FieldAliasSet(
                    both: Ci(("enc-algorithms", "encr-algorithms"))),

                // The two timeouts were confirmed by writing 17s and 23s and reading both back: every other
                // TCP timeout on this window is named '<state>-timeout' on both sides, and these two are the
                // pair where the window drops the suffix.
                ["/ip/firewall/connection/tracking"] = new FieldAliasSet(
                    both: Ci(("tcp-max-retrans-timeout", "tcp-max-retransmit-timeout"),
                             ("tcp-close-timeout", "tcp-close"), ("tcp-time-wait-timeout", "tcp-time-wait"),
                             ("total-ip4-entries", "total-ipv4-entries"), ("total-ip6-entries", "total-ipv6-entries")),
                    // active-ipv6: enabled=yes moved the unnamed bool 0x2F False → True with the API's
                    // active-ipv6, and enabled=auto put both back. Read-only.
                    // total-entries: 7.x declares it ('Total Entries', uf); the 6.49.13 window does not, but
                    // the router sends the key — enabled=yes moved 0xF from 0 to 9 with total-entries=9.
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["active-ipv6"] = new WinboxJgField("active-ipv6", 0x2F, "bool", true),
                        ["total-entries"] = new WinboxJgField("total-entries", 0xF, "u32", true, uiType: "number"),
                    }),

                // The address list's own key (0xFE0010) is labelled 'Name' by RouterOS 6 and 'List' by 7; the
                // API says `list` on both. On 7.x the alias yields to the catalog's own label (AliasToJg). The
                // IPv6 window is labelled the same way.
                ["/ip/firewall/address-list"] = new FieldAliasSet(
                    both: Ci(("list", "name"))),
                ["/ipv6/firewall/address-list"] = new FieldAliasSet(
                    both: Ci(("list", "name"))),

                // /ip/route: the API's `active` bool is the route window's 'Contribution' enum — one wire
                // field (u22), two vocabularies. The base 'All Routes' window's numflag on that key
                // (4:['active','A']) is what says which member the API's true stands for. Derived rather
                // than renamed, so `contribution` survives alongside it. That numflag's other members are
                // NOT flags the API prints: an unreachable route (u22=1) printed neither `unreachable` nor
                // `filtered` on 7.24, only `inactive=true`.
                //
                // The route's ORIGIN is a numflag of its own — u7 on RouterOS 6, u112 on 7 — and the API
                // prints the member the row is (`connect=true`, `dhcp=true`, `static=true`) and nothing for
                // the rest; the catalog supplies the key and the values per version (BuildNumFlags). Measured
                // beside the API: 7.24's connected route carried 0x112=2, its DHCP route 0x112=10, a static
                // route to a scratch prefix 0x112=3; 6.49.13's connected route 0x7=2 and its DHCP client's
                // route 0x7=3 (RouterOS 6 calls that route static). RouterOS names more origins (bgp, ospf,
                // rip, …); they are deliberately absent, since a flag nobody has seen the API print under a
                // name is a guess at that name.
                //
                // 'Pref. Source' is the API's pref-src, on both versions: a 6.49.13 connected route printed
                // pref-src=<its own address> and carried that address at 0x3 under the opt flag 0x3EB.
                ["/ip/route"] = new FieldAliasSet(
                    both: Ci(("pref-src", "pref-source")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["immediate-gw"] = new WinboxJgField("immediate-gw", 0x108, "addr[]", true,
                                                             uiType: "multi", elementUiType: "addr"),
                    },
                    numFlagMembers: new[] { "active", "connect", "static", "dhcp" },
                    // vrf-interface: 6.49.13 sends it at 0x3C (with the window's refreshfilter bit) and its window
                    // declares nothing there; setting it to ether1 then ether2 over the API moved 0x3C from 1 to 2,
                    // the interfaces' ids. 7.x names it itself ('VRF Interface', u111), so this ranks below the
                    // window. Read-only: a native write of it has not been measured.
                    fallbackFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["vrf-interface"] = new WinboxJgField("vrf-interface", 0x3C, "u32", true,
                                                              uiType: "enm", refHandler: new[] { 20, 0 }),
                    }),

                // /system/history: 'undoable' is a numflag member (u4=0), and the API prints undoable=true on
                // every such row (7.24). Its redoable and floating-undo members have not been seen printed.
                ["/system/history"] = new FieldAliasSet(numFlagMembers: new[] { "undoable" }),

                // /routing/ospf/interface-template: the template window types Transmit Delay as a plain
                // `number` (u2c700b, max:65535 min:1) while the live 'OSPF Interface' window on [44,111]
                // types the SAME key as an `interval` — and RouterOS prints `1s`, siding with the second.
                // WinBox is not wrong about its own box; the API is simply a different contract, and ours.
                // Typed here per path rather than by preferring `interval` wherever two windows disagree:
                // that rule would reach every such pair in the catalog on the strength of one example.
                // Its 'Network Type' is the API's type — confirmed by setting type=ptp and watching it arrive.
                ["/routing/ospf/interface-template"] = new FieldAliasSet(
                    both: Ci(("type", "network-type")),
                    // The state flag, as the routing tables name it (see RoutingInactive).
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" },
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["transmit-delay"] = new WinboxJgField("transmit-delay", 0x2C700B, "u32", false,
                                                               uiType: "interval"),
                    }),

                // /ip/dhcp-server/network: WinBox labels five of these boxes in the PLURAL — 'DNS Servers',
                // 'NTP Servers', 'WINS Servers', 'CAPS Managers', 'DHCP Options' — while RouterOS names every
                // one of them in the singular however many values it holds. Two more are not plurals at all:
                // the window's 'No DNS'/'No NTP' are the router's dns-none/ntp-none.
                //
                // All seven read off `/ip/dhcp-server/network add ?` on 7.24, which is the whole settable
                // field set — so this is the complete list for the path, not the five the audit could see.
                // The other two it could not: the API prints a false flag as no word at all, so no
                // comparison would ever have named them.
                ["/ip/dhcp-server/network"] = new FieldAliasSet(
                    both: Ci(("dns-server", "dns-servers"), ("ntp-server", "ntp-servers"),
                             ("wins-server", "wins-servers"), ("caps-manager", "caps-managers"),
                             ("dhcp-option", "dhcp-options"), ("dns-none", "no-dns"), ("ntp-none", "no-ntp"))),

                // Three fields the WRITE audit found and no read could: each is empty on a stock row, so
                // the two transports agreed on an empty vocabulary and the path passed. They surfaced the
                // moment something tried to WRITE them — the resolver could not name the field at all.
                //
                // Each pairing was then confirmed by value, not by resemblance: with the API asked to set
                // it, native reports the same value under its own name.
                ["/caps-man/security"] = new FieldAliasSet(
                    both: Ci(("authentication-types", "authentication-type"))),

                // /routing/bgp/connection: RouterOS 7 groups a connection's sub-settings with a DOT --
                // remote.address, local.role -- and WinBox labels them 'Remote Address' and 'Local Role',
                // which normalize with a hyphen. Two spellings of one field, and the write side could not
                // resolve the API's at all: `remote.address` named nothing, so an add carrying it threw.
                //
                // Aliased per path rather than by turning every dot into a hyphen everywhere. A dot is not
                // reserved in a RouterOS field name, and a global rewrite would quietly re-point any future
                // field that has one.
                ["/routing/bgp/connection"] = new FieldAliasSet(
                    both: Ci(("remote.address", "remote-address"), ("local.role", "local-role")),
                    // The state flag, as the routing tables name it (see RoutingInactive): a connection with no
                    // local address reads inactive=true over the API and 0xFE0008=true natively (7.24.2).
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" }),

                // The BGP instance window calls the router id 'IP'. Confirmed by value: with the API's
                // router-id set to 10.99.0.13, native reports ip=10.99.0.13. The state flag 0xFE0008 is the API's
                // `inactive` here too — told apart on the BGP connection, where a row reads true on both sides.
                // On 6.49.13 the window also spells out 'Ignore AS Path Length' (b12), the API's ignore-as-path-len.
                ["/routing/bgp/instance"] = new FieldAliasSet(
                    both: Ci(("router-id", "ip"), ("ignore-as-path-len", "ignore-as-path-length")),
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" }),

                // /routing/ospf/instance on RouterOS 6: the 6.49.13 window (and its OSPFv3 twin) spells each field
                // out — 'Redistribute Static Routes', 'Static Routes Metric' — where the API says
                // redistribute-static and metric-static. 7.x's window has none of these labels. The state flag
                // 0xFE0008 is the API's `inactive`, as on the other routing tables (7.24.5: a disabled instance).
                // ONE entry: a second one for this path in this initializer silently replaced the first.
                ["/routing/ospf/instance"] = new FieldAliasSet(
                    both: Ci(("distribute-default", "redistribute-default-route"),
                             ("redistribute-connected", "redistribute-connected-routes"),
                             ("redistribute-static", "redistribute-static-routes"),
                             ("redistribute-rip", "redistribute-rip-routes"),
                             ("redistribute-bgp", "redistribute-bgp-routes"),
                             ("redistribute-other-ospf", "redistribute-other-ospf-routes"),
                             ("metric-default", "default-route-metric"),
                             ("metric-connected", "connected-routes-metric"),
                             ("metric-static", "static-routes-metric"), ("metric-rip", "rip-routes-metric"),
                             ("metric-bgp", "bgp-routes-metric"), ("metric-other-ospf", "other-ospf-routes-metric")),
                    keyToApi: new Dictionary<int, string> { [WinboxM2Protocol.RecordKey.Invalid] = "inactive" },
                    // state is the 6.x window's 'Running' {bool, u65, ro}: the API's down at 0 and running at 1
                    // (6.49.13: an OSPF network on an addressed interface moved it 0 -> 1, state down -> running).
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["state"] = new WinboxJgField("state", 0x65, "u32", true,
                            enumMap: new Dictionary<int, string> { [0] = "down", [1] = "running" }),
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
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["state"] = new WinboxJgField("state", 0x8, "bool", true, enumMap: HealthStateMap),
                        ["state-after-reboot"] = new WinboxJgField("state-after-reboot", 0x9, "bool", false,
                                                                   enumMap: HealthStateMap),
                    }),

                // /interface/wireless/security-profiles ([88,14], wlan6.jg) — the audit's one standing
                // MISMATCH, in two halves.
                //
                // The RADIUS tab drops the 'radius-' prefix the API spells out and renames three fields
                // outright, exactly as /ip/hotspot/profile's RADIUS tab does. All seven were measured in
                // both directions on 7.24 before shipping; the values already agreed.
                //
                // The Static Keys tab is the other half, and the fields are NOT missing from the wire —
                // that was the assumption, and a trace disproves it. With mode=static-keys-required and
                // static-algo-0=40bit-wep the record carries `0x7=1` and `0xB=1234567890`. They are dropped
                // because the .jg wraps each pair in a type:'tuple' ('Key 0'…'Key 3', 'St. Private Key')
                // whose two children — an enm for the algorithm and a secret for the key — carry ids but no
                // names, while RouterOS splits every tuple into two fields. Named here per key, with the
                // algorithm's enum map, so they read, resolve and write like catalogued fields.
                ["/interface/wireless/security-profiles"] = new FieldAliasSet(
                    both: Ci(("radius-mac-authentication", "mac-authentication"),
                             ("radius-mac-accounting", "mac-accounting"), ("radius-eap-accounting", "eap-accounting"),
                             ("radius-mac-format", "mac-format"), ("radius-mac-mode", "mac-mode"),
                             ("radius-called-format", "called-id-format"), ("radius-mac-caching", "mac-caching-time"),
                             ("static-transmit-key", "transmit-key")),
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["static-algo-0"] = WepAlgo("static-algo-0", 0x7),
                        ["static-key-0"]  = WepKey("static-key-0", 0xB),
                        ["static-algo-1"] = WepAlgo("static-algo-1", 0x8),
                        ["static-key-1"]  = WepKey("static-key-1", 0xC),
                        ["static-algo-2"] = WepAlgo("static-algo-2", 0x9),
                        ["static-key-2"]  = WepKey("static-key-2", 0xD),
                        ["static-algo-3"] = WepAlgo("static-algo-3", 0xA),
                        ["static-key-3"]  = WepKey("static-key-3", 0xE),
                        ["static-sta-private-algo"] = WepAlgo("static-sta-private-algo", 0x10),
                        ["static-sta-private-key"]  = WepKey("static-sta-private-key", 0x11),
                    }),

                // /ip/upnp: the settings singleton's second field is labelled 'Allow To Disable External
                // Interface' in WinBox and 'allow-disable-external-interface' in the API — one stray "to".
                // Without the alias the entity read back a field name RouterOS never uses.
                ["/ip/upnp"] = new FieldAliasSet(
                    both: Ci(("allow-disable-external-interface", "allow-to-disable-external-interface"))),

                // /interface/wifi/security: WinBox writes the label with the ID spelled out — 'FT Preserve
                // VLAN ID' normalizes to ft-preserve-vlan-id, where the API says ft-preserve-vlanid. Same
                // field (wave2 b2023), one hyphen apart, so an add carrying it could not resolve a key at all.
                // …and the window labels the authentication set plain 'Types' where the API spells out
                // authentication-types. Found by the WRITE audit: empty on a stock row, so no read could
                // pair it, and the resolver could not name the field at all when something tried to set it.
                //
                // Merged into THIS entry rather than added as a second one for the path. The table is
                // initialised through the indexer, so a duplicate key does not throw — it silently
                // overwrites, and a second entry lower down took this one's place with no sign at all.
                ["/interface/wifi/security"] = new FieldAliasSet(
                    both: Ci(("ft-preserve-vlanid", "ft-preserve-vlan-id"), ("authentication-types", "types"))),

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
                    both: Ci(("radius-accounting", "accounting"), ("radius-default-domain", "default-domain"),
                             ("radius-interim-update", "interim-update"), ("radius-location-id", "location-id"),
                             ("radius-location-name", "location-name"), ("radius-mac-format", "mac-format"),
                             ("rate-limit", "rate-limit-(rx/tx)")),
                    // `default` is the shipped *0 profile, as on the user tables (see HotspotDefault).
                    derivedBools: HotspotDefault().DerivedBools),

                // /tool/sniffer: every field of the window's FILTER tab is spelled with a 'filter-' prefix
                // by the API and without one by WinBox, and the streaming half is spelled the other way
                // round ('Server'/'Port' are the API's one streaming-server="0.0.0.0:37008"). The filter
                // 'Port' needs no alias: it shares its label with the streaming port, and the catalog
                // registers the loser of a label collision under its tab - which spells it filter-port.
                //
                // The filter fields are the live shape of a `not`-wrapped list element: each is a message
                // array whose element carries a negation flag beside the value, which is the '!' in
                // filter-ip-address="!192.168.251.0/24,10.0.0.1/32".
                ["/tool/sniffer"] = new FieldAliasSet(
                    addrPortPairs: Ci(("server", "port")),
                    both: Ci(("filter-interface", "interfaces"), ("filter-mac-address", "mac-address"),
                             ("filter-src-mac-address", "src-mac-address"),
                             ("filter-dst-mac-address", "dst-mac-address"), ("filter-mac-protocol", "mac-protocol"),
                             ("filter-ip-address", "ip-address"), ("filter-src-ip-address", "src-ip-address"),
                             ("filter-dst-ip-address", "dst-ip-address"), ("filter-ipv6-address", "ipv6-address"),
                             ("filter-src-ipv6-address", "src-ipv6-address"),
                             ("filter-dst-ipv6-address", "dst-ipv6-address"), ("filter-ip-protocol", "ip-protocol"),
                             ("filter-src-port", "src-port"), ("filter-dst-port", "dst-port"),
                             ("filter-vlan", "vlan"), ("filter-cpu", "cpu"), ("filter-direction", "direction"),
                             ("filter-operator-between-entries", "filter-operation"), ("quick-rows", "rows"),
                             ("quick-show-frame", "show-frame"), ("streaming-server", "server"))),

                // /interface/list: the shipped-row flag (0xFE000D) is spelled 'builtin' here, where most
                // tables that have it say 'default' — which is the name the universal seed gives it. A key
                // alias wins over the seed, so this one path reports the word RouterOS uses for it.
                ["/interface/list"] = new FieldAliasSet(
                    keyToApi: new Dictionary<int, string>
                    {
                        [WinboxM2Protocol.RecordKey.Builtin] = "builtin",
                    }),

                // /ip/pool: the window labels the ranges list 'Addresses'; the API calls it 'ranges'.
                ["/ip/pool"] = new FieldAliasSet(
                    both: Ci(("ranges", "addresses"))),

                // /queue/simple: RouterOS prints one field per rate where the M2 model keeps two — 'max-limit'
                // is 'Target Upload / Max Limit' (0xD8) beside 'Target Download / Max Limit' (0x13C) in the
                // window, and the API joins them as "1000000/2000000". Without the pairing, native reported
                // upload-max-limit and download-max-limit and no max-limit at all, so the field simply did
                // not exist on this transport.
                //
                // All eight paired CONFIGURATION fields are here. Each half is formatted by its own typed field
                // (PairHalves), which is what lets the last two pair at all: burst-time's halves are intervals,
                // and queue's are queue-type references — each read as its own field, not as the other's type.
                //
                // The READ-ONLY statistics pair the same way, and for the same reason. The window's
                // Statistics tab is a column of {tuple,separate:1,c:[{name:'Upload …'},{name:'Download …'}]}
                // and the TUPLE's own name is the API's field name — 'Bytes' over 'Total Uploaded Bytes'
                // (0xC8) and 'Total Downloaded Bytes' (0x12C) is the API's 'bytes'. Every statistic the API
                // prints on this menu is one such tuple and every half is consumed by exactly one of them,
                // which is what makes the correspondence a reading of the catalog rather than a guess.
                //
                // Magnitudes are NOT verified against the API: a simple queue counts forwarded traffic only
                // and the lab CHR forwards none, so every half reads 0 on both transports. What is verified
                // is the shape — the API's own "0/0" on each of them — and the direction, which the
                // configuration pairs above pin down on this same path with 1M/2M.
                //
                // 'PCQ Queues' (0xD5/0x139) is deliberately NOT paired: the API prints no such field on
                // /queue/simple, so pairing it would invent one.
                ["/queue/simple"] = new FieldAliasSet(
                    pairedFields: Pairs(
                        ("max-limit", "upload-max-limit", "download-max-limit"),
                        ("limit-at", "upload-limit-at", "download-limit-at"),
                        ("burst-limit", "upload-burst-limit", "download-burst-limit"),
                        ("burst-threshold", "upload-burst-threshold", "download-burst-threshold"),
                        ("priority", "upload-priority", "download-priority"),
                        ("bucket-size", "upload-bucket-size", "download-bucket-size"),
                        // Both moved on a seeded row (7.24.2): queue=pcq-upload-default/pcq-download-default
                        // arrived as the two queue-type references, burst-time=7s/9s as the two burst times.
                        ("queue", "upload-queue-type", "download-queue-type"),
                        ("burst-time", "upload-burst-time", "download-burst-time"),
                        // Statistics tab — read-only, one tuple per API field.
                        ("bytes", "total-uploaded-bytes", "total-downloaded-bytes"),
                        ("packets", "total-uploaded-packets", "total-downloaded-packets"),
                        ("dropped", "upload-dropped", "download-dropped"),
                        ("rate", "upload-avg-rate", "download-avg-rate"),
                        ("packet-rate", "upload-avg-packet-rate", "download-avg-packet-rate"),
                        ("queued-bytes", "upload-queued-bytes", "download-queued-bytes"),
                        ("queued-packets", "upload-queued-packets", "download-queued-packets")),
                    // The Total Statistics tab needs no pairing — those are single values — but WinBox says
                    // 'Avg.' where the API does not: 'Total Avg. Rate' (0x194) is the API's 'total-rate' and
                    // 'Total Avg. Packet Rate' (0x195) its 'total-packet-rate'. The other five totals
                    // (total-bytes, total-packets, total-dropped, total-queued-bytes, total-queued-packets)
                    // already carry the API's own name.
                    both: Ci(("total-rate", "total-avg-rate"), ("total-packet-rate", "total-avg-packet-rate"))),

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
                    keyToApi: new Dictionary<int, string>
                    {
                        [0x10001] = "type-id",
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
                    },
                    // 'type' is a synthetic rather than a key alias so it can say what KIND of value it is:
                    // 0x1001E carries the type NAME on almost every row, and on an ENSLAVED interface it
                    // carries its master's numeric id instead — WinBox nests a slave under its master in the
                    // list. Declared `string`, the number is refused; the row's own type id (0x10001) then names
                    // it, from the id → name pairs the other rows carry (KindIdKey, WinboxRecordCodec.LearnKindWords).
                    // A type no row has named yet stays absent, which beats reporting the master's id as one.
                    //
                    // The generic Interface window declares no MAC Address at all — WinBox paints the box in
                    // the SUBTYPE dialog beside it (the Ethernet tab of Interface > ether1), so /interface
                    // had no name for the key and dropped a field the router sends on every single row.
                    // Not a wire gap: a /interface getall carries 0x3E9=AABBCCDDEEFF next to the name.
                    //
                    // The pairing was CONFIRMED by moving the value, not by the three rows agreeing (ether1,
                    // ether2 and lo all matched the API exactly, which is suggestive and proves nothing).
                    // ether2 could not be moved — this is a CHR on Hyper-V, whose vSwitch refuses a spoofed
                    // MAC, so RouterOS logs the set and goes on reporting the hardware address. A bridge has
                    // its MAC entirely in software: admin-mac 02:00:00:AA:BB:01 -> …02 moved 0x3E9 with it,
                    // and no key was left holding the old value. Of the four keys a bridge row carries that
                    // value under, 0x3E9 is the only one present on an ether or on lo.
                    //
                    // Writable, as the subtype window declares it: a subtype path without a set of its own
                    // inherits this one (see Aliases), and a read-only synthetic would shadow its own MAC
                    // Address field and take the write away. /interface/ethernet has its own set, so it does
                    // NOT see these — a synthetic an ethernet row needs goes in that set.
                    syntheticFields: new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = new WinboxJgField("type", 0x1001E, "string", true) { KindIdKey = 0x10001 },
                        ["mac-address"] = new WinboxJgField("mac-address", 0x3E9, "raw", false,
                                                            uiType: "macaddr"),
                        // 7.x declares it ('Default Name', s10031); the 6.49.13 window does not, but the router
                        // sends the key — ether1 and ether2 carried their own default-name at 0x10031.
                        ["default-name"] = new WinboxJgField("default-name", 0x10031, "string", true),
                    }),
            };

        /// <summary>
        /// Enum MEMBERS whose <c>.jg</c> label is not the word RouterOS uses, keyed by the field's API name
        /// and then by the member's normalized label.
        /// </summary>
        /// <remarks>
        /// <para>The field-name tables above pair a BOX with a property; this pairs the words inside one.
        /// A member is only reachable through the label WinBox paints in the dropdown, and where that label
        /// is a caption rather than the router's own token the value read back is a word no other transport
        /// answers with — and a write of the router's word is refused.</para>
        /// <para>Keyed by field name rather than by path for the same reason as
        /// <c>WinboxRecordCodec.ZeroSpelledAsWord</c>: these are RouterOS-wide vocabularies, and
        /// <c>protocol</c> means the same 33 numbers on every firewall menu.</para>
        /// <para>Both entries come from the router's own tab completion, and each was checked against the
        /// WHOLE member list rather than against the one member the audit happened to catch: of the 33
        /// protocol names the catalog declares, <c>ip-encap</c> is the only one RouterOS spells differently
        /// (<c>ipencap</c>); of the three lease identifiers, TWO are — the audit saw
        /// <c>mac-address</c>→<c>client-mac</c> because bits 0 and 1 were set, and
        /// <c>option-82</c>→<c>opt-82</c> would have waited for a row nobody had made.</para>
        /// </remarks>
        private static readonly Dictionary<string, Dictionary<string, string>> EnumMemberAliases =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["protocol"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ip-encap"] = "ipencap",
                },
                // /ip/firewall/mangle: the window captions the action 'change DSCP (TOS)'; RouterOS writes change-dscp.
                ["action"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["change-dscp-(tos)"] = "change-dscp",
                },
                ["dynamic-lease-identifiers"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mac-address"] = "client-mac",
                    ["option-82"]   = "opt-82",
                },
                // The window spells these with a space ('30 s', '1 s'), which normalizes to a hyphen;
                // RouterOS writes them as one word. `/interface/bonding add lacp-rate=?` on 7.24.
                ["lacp-rate"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["30-s"] = "30secs",
                    ["1-s"]  = "1sec",
                },
                // /system/logging/action: the window captions the formats; `add remote-log-format=?` completes
                // default cef syslog, `syslog-time-format=?` bsd-syslog iso8601 (7.24.4).
                ["remote-log-format"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bsd-syslog"] = "syslog",
                },
                ["timestamp-format"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["bsd"] = "bsd-syslog",
                },
                // Only the zero differs here: the router's members are 0, 4-bytes and 8-bytes, and the
                // window calls the first '0 bytes'.
                ["l2tpv3-cookie-length"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["0-bytes"] = "0",
                },
                // OpenVPN: the window captions 'blowfish 128', 'aes 128' (6.49.13) and 'aes 128 cbc' (7.x);
                // RouterOS writes the key size onto the algorithm — the 6.x API printed cipher=blowfish128,aes128.
                // The 7.x words (aes128-cbc, …) are the RouterOS 7 manual's; the 7.x lab has no OpenVPN row to
                // measure them on.
                ["cipher"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blowfish-128"] = "blowfish128",
                    ["aes-128"] = "aes128", ["aes-192"] = "aes192", ["aes-256"] = "aes256",
                    ["aes-128-cbc"] = "aes128-cbc", ["aes-192-cbc"] = "aes192-cbc", ["aes-256-cbc"] = "aes256-cbc",
                    ["aes-128-gcm"] = "aes128-gcm", ["aes-192-gcm"] = "aes192-gcm", ["aes-256-gcm"] = "aes256-gcm",
                },
                // The 6.x OSPF instance's 'Redistribute Default Route' (the API's distribute-default): the window
                // parenthesises the type, RouterOS does not.
                ["redistribute-default-route"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["if-installed-(as-type-1)"] = "if-installed-as-type-1",
                    ["if-installed-(as-type-2)"] = "if-installed-as-type-2",
                    ["always-(as-type-1)"] = "always-as-type-1",
                    ["always-(as-type-2)"] = "always-as-type-2",
                },
                // The 6.x ethernet window says '2.5G full', '5G full', '10G full'; RouterOS 6 counts those in
                // megabits like the rest of its list. 7.x's members ('2.5G baseT', '10G baseT') are its own tokens.
                ["advertise"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["2.5G-full"] = "2500M-full",
                    ["5G-full"] = "5000M-full",
                    ["10G-full"] = "10000M-full",
                },
                // The 6.x simple queue's rate halves caption 0 as 'unlimited'; the API prints max-limit=0/0
                // (6.49.13). 7.x declares no such member on them.
                ["upload-max-limit"] = UnlimitedIsZero(), ["download-max-limit"] = UnlimitedIsZero(),
                ["upload-limit-at"] = UnlimitedIsZero(), ["download-limit-at"] = UnlimitedIsZero(),
                ["upload-burst-limit"] = UnlimitedIsZero(), ["download-burst-limit"] = UnlimitedIsZero(),
                ["upload-burst-threshold"] = UnlimitedIsZero(), ["download-burst-threshold"] = UnlimitedIsZero(),
            };

        private static Dictionary<string, string> UnlimitedIsZero()
            => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["unlimited"] = "0" };

        /// <summary>
        /// Fields the <c>.jg</c> declares as an enum but RouterOS prints as the plain NUMBER.
        /// </summary>
        /// <remarks>
        /// The window and the API disagree about whether the value has a name at all, and here the API
        /// wins — a transport is supposed to answer what the binary API answers. <c>icmp-type</c> on
        /// <c>/ip/firewall/connection</c> is the measured case: the wire carries <c>8</c> (M2 key
        /// <c>0x10</c>), the catalog names it <c>echo-request</c> because that is what WinBox shows, and
        /// <c>/ip/firewall/connection/print</c> over the API says <c>8</c>.
        /// <para>Dropping the map rather than aliasing every member: the members are not spelled
        /// differently, they should not be words. The codec then falls through to the raw number, which is
        /// what the field carried all along.</para>
        /// <para>Keyed by field name, which is safe while <c>icmp-type</c> exists on one path. A sibling
        /// would be found the same way this one was — as a VALUE-DIFF in the transport audit, which is the
        /// only thing that compares a decoded name against what the API prints.</para>
        /// </remarks>
        private static readonly HashSet<string> NumericEnumFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "icmp-type" };

        /// <summary>
        /// <paramref name="members"/> with any member RouterOS spells differently renamed, or the map
        /// itself when this field has none. Applied once, where the field is built, so every reader and the
        /// write side all see the router's vocabulary and not WinBox's.
        /// </summary>
        internal static IReadOnlyDictionary<int, string>? AliasEnumMembers(
            string apiName, IReadOnlyDictionary<int, string>? members)
        {
            if (NumericEnumFields.Contains(apiName)) return null;
            if (members == null || !EnumMemberAliases.TryGetValue(apiName, out var table)) return members;
            Dictionary<int, string>? renamed = null;
            foreach (var kv in members)
            {
                if (!table.TryGetValue(kv.Value, out string? word) || word == null) continue;
                renamed ??= new Dictionary<int, string>(members.ToDictionary(x => x.Key, x => x.Value));
                renamed[kv.Key] = word;
            }
            return renamed ?? members;
        }
    }
}
