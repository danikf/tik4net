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
    /// For reads the important direction is <c>key → apiName</c> (decoding records back
    /// to API field names). The forward direction (<c>apiName → key</c>, for writes) is also exposed.
    /// Ambiguity (an API name that maps to two different keys) throws a clear exception telling the caller
    /// to add a session field override or use a <c>WinboxCli</c> connection instead.
    /// </remarks>
    internal sealed partial class WinboxFieldResolver
    {
        private readonly WinboxHandler _handler;
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
        // The entity's WinBox labels for this command (TikPropertyAttribute.WinboxLabel, carried by the
        // .winbox-labels marker): apiName → catalog name. Second in priority, after the session overrides and
        // before every heuristic; a label this version's catalog does not have is ignored.
        private readonly IReadOnlyDictionary<string, string>? _labelHints;

        internal WinboxFieldResolver(string? apiPath, WinboxHandler handler, WinboxJgCatalog catalog,
            IReadOnlyDictionary<string, int> overrides, bool useGuiNames = false, string? windowKey = null,
            IReadOnlyDictionary<string, WinboxJgField>? actionFields = null,
            IReadOnlyDictionary<string, string>? labelHints = null)
        {
            _labelHints = labelHints;
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
                var layers = LayersMostSpecificFirst().ToList();
                var handlerFields = layers.Where(l => l.Layer == FieldLayer.Handler).Select(l => l.Fields).FirstOrDefault();
                if (layers.All(l => l.Layer == FieldLayer.Handler || l.Fields.Count == 0)) return _fields = handlerFields;

                // By name, the most specific layer has the last word: folded from the least specific up.
                var merged = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                for (int i = layers.Count - 1; i >= 0; i--)
                    foreach (var kv in layers[i].Fields) merged[kv.Key] = kv.Value;
                return _fields = merged;
            }
        }

        // Where a field map in force for a path comes from — see LayersMostSpecificFirst for the order.
        private enum FieldLayer { OwnSynthetic, Action, Window, InheritedSynthetic, Handler, Fallback }

        /// <summary>
        /// The field maps in force for this path, MOST SPECIFIC FIRST — the one statement of their order, which
        /// <see cref="JgFields"/> folds from the other end (by name, last wins) and
        /// <see cref="JgFieldsSpecificFirst"/> walks as it stands (by key, first wins), so a layer cannot be added to
        /// one direction only.
        /// </summary>
        /// <remarks>
        /// <list type="number">
        /// <item>A synthetic field shipped for THIS path: it exists precisely because the catalog does not describe
        /// the key (or describes it wrongly for this path).</item>
        /// <item>The invoked action's arguments: 'Key Size' is a read-only column of the IPsec 'Keys' list and a
        /// writable enum argument of its 'Generate Key' doit.</item>
        /// <item>The window's own fields — what makes an interface subtype addressable: every subtype reads handler
        /// <c>[20,0]</c>, but 'Remote Address' is a different key on EoIP, GRE and IPIP.</item>
        /// <item>A synthetic field shipped for an ANCESTOR path (the /interface set, for an interface subtype): below
        /// the path's own window, which may declare the key itself — /interface/ipip's 'Local Address' is u3e9, where
        /// the base set's synthetic calls the same key mac-address.</item>
        /// <item>The handler's map.</item>
        /// <item>A fallback field, the lowest of all: any catalog field of the same name replaces it.</item>
        /// </list>
        /// </remarks>
        private IEnumerable<(FieldLayer Layer, IReadOnlyDictionary<string, WinboxJgField> Fields)> LayersMostSpecificFirst()
        {
            var synthetic = OwnSyntheticFields;
            if (synthetic != null) yield return (FieldLayer.OwnSynthetic, synthetic);
            if (_actionFields != null) yield return (FieldLayer.Action, _actionFields);
            var windowFields = _catalog?.GetWindowFields(_windowKey);
            if (windowFields != null) yield return (FieldLayer.Window, windowFields);
            var inherited = InheritedSyntheticFields;
            if (inherited != null) yield return (FieldLayer.InheritedSynthetic, inherited);
            var handlerFields = _catalog?.GetHandlerFields(_handler);
            if (handlerFields != null) yield return (FieldLayer.Handler, handlerFields);
            var fallback = FallbackFields;
            if (fallback != null) yield return (FieldLayer.Fallback, fallback);
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
            bool everyPane = PrintsEveryPane.Contains(WinboxHandlerMap.Normalize(_apiPath ?? ""));
            IEnumerable<KeyValuePair<string, WinboxJgField>> Scoped(IEnumerable<KeyValuePair<string, WinboxJgField>> source)
                => everyPane
                    ? source.Select(kv => new KeyValuePair<string, WinboxJgField>(kv.Key, kv.Value.WithoutPaneSelector()))
                    : source;

            var layers = LayersMostSpecificFirst().ToList();
            var synthetic = layers.Where(l => l.Layer == FieldLayer.OwnSynthetic).Select(l => l.Fields).FirstOrDefault();
            // A synthetic field REPLACES the catalog's field of the same name, as it does in JgFields. By key the
            // catalog's would otherwise keep its own key under that name, and a row carrying both keys would read
            // whichever the frame happened to list first: /ip/neighbor's 'Interface' u4 holds the first element
            // of the list the synthetic 0x13 holds whole.
            bool Replaced(KeyValuePair<string, WinboxJgField> kv)
                => synthetic != null && synthetic.TryGetValue(kv.Key, out var own) && own.Key != kv.Value.Key;
            // A fallback names its key only where the catalog leaves the name to it.
            bool CatalogNames(string apiName)
                => layers.Any(l => l.Layer != FieldLayer.OwnSynthetic && l.Layer != FieldLayer.Fallback
                                   && l.Fields.ContainsKey(apiName));

            // The keys another window of this handler declares and the path's own window does not (WinBox review
            // D6). The handler's map is every window's, and its record carries every window's keys: /ip/settings
            // decoded connection tracking's and neighbour discovery's fields into its row. With a window to go by,
            // the handler layer keeps only what no other window claims. A path reached by PathOverride (no window)
            // keeps the whole handler.
            var otherWindows = layers.Any(l => l.Layer == FieldLayer.Window)
                ? _catalog.GetOtherWindowKeys(_windowKey) : null;

            foreach (var (layer, fields) in layers)
                switch (layer)
                {
                    case FieldLayer.OwnSynthetic:
                        foreach (var kv in fields) yield return kv;
                        break;
                    case FieldLayer.Action:
                    case FieldLayer.Window:
                        foreach (var kv in Scoped(fields)) if (!Replaced(kv)) yield return kv;
                        break;
                    case FieldLayer.Handler:
                        foreach (var kv in Scoped(fields))
                            if (!Replaced(kv) && (otherWindows == null || !otherWindows.Contains(kv.Value.Key))) yield return kv;
                        break;
                    case FieldLayer.InheritedSynthetic:
                        foreach (var kv in fields) if (!Replaced(kv)) yield return kv;
                        break;
                    case FieldLayer.Fallback:
                        foreach (var kv in fields) if (!Replaced(kv) && !CatalogNames(kv.Key)) yield return kv;
                        break;
                }
        }

        /// <summary>
        /// Paths whose API prints the fields of EVERY deck pane on every row, not just the pane of the row's
        /// own kind.
        /// </summary>
        /// <remarks>
        /// The usual rule is the opposite, and it is the codec's default (see WinboxRecordCodec
        /// IsAnotherKindsField): a memory logging action prints memory-lines and nothing of the disk pane. A
        /// bond does not: its 'Link Monitoring' deck puts ARP Interval / ARP IP Targets on the arp pane and MII
        /// Interval on the mii pane, and the API prints arp-interval=100ms and arp-ip-targets= on a mii bond
        /// (7.24.2), so native dropped two fields the API reports.
        /// </remarks>
        private static readonly HashSet<string> PrintsEveryPane =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/interface/bonding" };

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

        // Universal system keys the router sends on table after table and no .jg window ever declares: row
        // STATE the router computes, not configuration. Two things distinguish them from SystemSeed above.
        // They are filled in LAST, so a catalog field owning the same key keeps its own name; and they are
        // deliberately absent from TryResolveKey, so a write refuses by name rather than sending an
        // untyped seed value at a bool key — which is what the API does with them too.
        private static readonly Dictionary<string, int> ReadOnlySystemSeed =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["dynamic"] = WinboxM2Protocol.RecordKey.Dynamic, // 0xFE0007
            ["invalid"] = WinboxM2Protocol.RecordKey.Invalid, // 0xFE0008 ('inactive' on /interface)
            // One flag, two API spellings: 'default' on the tables that ship rows you may edit (queue types,
            // logging rules and actions, hotspot and IPsec profiles) and 'builtin' on the few that ship rows
            // you may not (/interface/list). The majority spelling is the seed; the other is a per-path
            // alias, the same way every other name difference is handled.
            ["default"] = WinboxM2Protocol.RecordKey.Builtin, // 0xFE000D
        };

        // Wire type for seed fields without a .jg entry (so EncodeField types them correctly).
        private static M2Type SeedWireType(string apiName)
            => string.Equals(apiName, "disabled", StringComparison.OrdinalIgnoreCase) ? M2Type.Bool : M2Type.String;

        // ── Deck panes: the kind prefix (stable text) ──────────────────────────

        /// <summary>
        /// The name a pane field is filed under: the kind and the field's own label joined with '-', unless
        /// the label already begins with the kind (WinBox writes 'Remote Port' inside the remote pane and
        /// 'PFIFO Queue Size' inside the pfifo one, where the API says <c>remote-port</c> and
        /// <c>pfifo-limit</c> — not <c>remote-remote-port</c>).
        /// </summary>
        /// <summary>
        /// Parses the <c>.winbox-labels</c> marker the O/R mapper puts on its commands —
        /// <c>api-name=WinBox Label|other=memory: Stop on Full</c> — into <c>apiName → catalog name</c>. A label
        /// written <c>qualifier: Label</c> names a field whose label repeats in its window, by the deck pane's kind or
        /// the tab it sits on (the same qualified name the catalog files it under). <c>null</c> for no marker.
        /// </summary>
        internal static IReadOnlyDictionary<string, string>? ParseLabelHints(string? marker)
        {
            if (string.IsNullOrEmpty(marker)) return null;
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in marker!.Split('|'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0 || eq == pair.Length - 1) continue;
                string apiName = pair.Substring(0, eq).Trim();
                string label = pair.Substring(eq + 1).Trim();
                int colon = label.IndexOf(':');
                string name = colon > 0
                    ? PrefixWithKind(NormalizeLabel(label.Substring(0, colon)), NormalizeLabel(label.Substring(colon + 1)))
                    : NormalizeLabel(label);
                if (name.Length > 0) hints[apiName] = name;
            }
            return hints.Count > 0 ? hints : null;
        }

        // The catalog field an entity's WinBox label names for apiName, when this version's catalog has it.
        private bool TryLabelHint(string apiName, out WinboxJgField field)
        {
            field = null!;
            var jg = JgFields;
            return _labelHints != null && jg != null
                && _labelHints.TryGetValue(apiName, out string? name) && jg.TryGetValue(name, out field!);
        }

        internal static string PrefixWithKind(string? kind, string apiName)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(apiName)) return apiName;
            if (apiName.Equals(kind, StringComparison.OrdinalIgnoreCase)
                || apiName.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase))
                return apiName;
            return kind + "-" + apiName;
        }

        /// <summary>
        /// Which <c>tab</c>s RouterOS's API spells with the tab name as a prefix.
        /// </summary>
        /// <remarks>
        /// <para>Shipped rather than derived, for the same reason as <see cref="PanePrefixed"/>: the
        /// catalog carries nothing that separates the two cases. On <c>/interface/bridge</c> the
        /// <c>MLAG</c> tab and the <c>STP</c> tab are both a bare <c>{name:'…',type:'tab'}</c> with no
        /// attributes, and the router prefixes one and not the other — <c>mlag-heartbeat</c>,
        /// <c>mlag-peer-port</c>, <c>mlag-priority</c>, but <c>protocol-mode</c> and <c>priority</c>. A
        /// blanket tab prefix would rename correctly named fields, so the default stays "leave the name
        /// alone".</para>
        /// <para>Read off the live router with tab completion (<c>/interface/bridge set ?</c>,
        /// <c>/interface/vlan set ?</c>), not inferred. The transport audit is what would find a missing
        /// entry: the field shows up as an <c>api-only</c> name with the unprefixed label reported beside
        /// it.</para>
        /// <para>It also settles a collision rather than making one. The bridge window labels two different
        /// fields <c>Priority</c> — one under <c>STP</c>, one under <c>MLAG</c> — and first-wins gave the
        /// name to whichever was read first. With the prefix they are <c>priority</c> and
        /// <c>mlag-priority</c>, which is what the router calls them.</para>
        /// </remarks>
        private static readonly HashSet<string> TabPrefixedTabs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "loop-protect", "mlag" };

        /// <summary>Whether fields on <paramref name="tab"/> carry the tab name as a prefix.</summary>
        internal static bool TabPrefixed(string? tab)
            => !string.IsNullOrEmpty(tab) && TabPrefixedTabs.Contains(tab!);

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
            get { ResolveAliases(); return _aliases; }
        }

        /// <summary>Synthetic fields shipped for THIS path — they outrank the catalog (see JgFields).</summary>
        private IReadOnlyDictionary<string, WinboxJgField>? OwnSyntheticFields
        {
            get { ResolveAliases(); return _ownSynthetic; }
        }

        /// <summary>
        /// Synthetic fields shipped for an ANCESTOR path — the base <c>/interface</c> set, for an interface
        /// subtype — which rank below the path's own window.
        /// </summary>
        private IReadOnlyDictionary<string, WinboxJgField>? InheritedSyntheticFields
        {
            get { ResolveAliases(); return _inheritedSynthetic; }
        }

        /// <summary>The path's <see cref="FieldAliasSet.FallbackFields"/> — below every catalog field.</summary>
        private IReadOnlyDictionary<string, WinboxJgField>? FallbackFields
        {
            get { ResolveAliases(); return _fallback; }
        }

        private static readonly WinboxHandler GenericInterfaceHandler = new WinboxHandler(20, 0);
        private bool _aliasesResolved;
        private FieldAliasSet? _aliases;
        private IReadOnlyDictionary<string, WinboxJgField>? _ownSynthetic;
        private IReadOnlyDictionary<string, WinboxJgField>? _inheritedSynthetic;
        private IReadOnlyDictionary<string, WinboxJgField>? _fallback;

        // The nearest shipped set (the path's own, or its nearest ancestor's), and — for an interface subtype
        // read on the generic [20,0] handler that has a set of its OWN — the base /interface set merged beneath
        // it. Every subtype row is an /interface row: without the merge a subtype that needed one alias of its
        // own lost /interface's counter names and its type/mac-address synthetics, which is what happened to
        // /interface/ethernet.
        private void ResolveAliases()
        {
            if (_aliasesResolved) return;
            _aliasesResolved = true;

            string norm = WinboxHandlerMap.Normalize(_apiPath ?? "");
            string path = norm;
            FieldAliasSet? found = null;
            string? foundPath = null;
            while (!string.IsNullOrEmpty(path))
            {
                if (ShippedFieldAliases.TryGetValue(path, out var s)) { found = s; foundPath = path; break; }
                int cut = path.LastIndexOf('/');
                if (cut <= 0) break;
                path = path.Substring(0, cut);
            }

            if (found == null) return;

            bool exact = string.Equals(foundPath, norm, StringComparison.OrdinalIgnoreCase);
            FieldAliasSet? baseSet = null;
            if (exact && !string.Equals(norm, "/interface", StringComparison.OrdinalIgnoreCase)
                && norm.StartsWith("/interface/", StringComparison.OrdinalIgnoreCase)
                && _handler == GenericInterfaceHandler)
                ShippedFieldAliases.TryGetValue("/interface", out baseSet);

            _aliases = baseSet == null ? found : MergeAliases(found, baseSet);
            _ownSynthetic = exact ? found.SyntheticFields : null;
            var inherited = exact ? baseSet?.SyntheticFields : found.SyntheticFields;
            if (inherited != null && _ownSynthetic != null)
            {
                // A synthetic of the path's own replaces the base's on the same name or the same key.
                var own = _ownSynthetic;
                inherited = inherited.Where(kv => !own.ContainsKey(kv.Key) && !own.Values.Any(o => o.Key == kv.Value.Key))
                                     .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }
            _inheritedSynthetic = inherited;
            _fallback = exact ? found.FallbackFields : null;
        }

        // Child entries win; synthetic fields are kept apart (see ResolveAliases), so the merged set carries none.
        private static FieldAliasSet MergeAliases(FieldAliasSet child, FieldAliasSet parent)
        {
            Dictionary<TKey, TValue>? Merge<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? c,
                IReadOnlyDictionary<TKey, TValue>? p, IEqualityComparer<TKey>? comparer) where TKey : notnull
            {
                if (c == null && p == null) return null;
                var m = comparer == null ? new Dictionary<TKey, TValue>() : new Dictionary<TKey, TValue>(comparer);
                if (p != null) foreach (var kv in p) m[kv.Key] = kv.Value;
                if (c != null) foreach (var kv in c) m[kv.Key] = kv.Value;
                return m;
            }
            var ci = StringComparer.OrdinalIgnoreCase;
            return new FieldAliasSet(
                apiToJgOnly: Merge(child.ApiToJg, parent.ApiToJg, ci), jgToApiOnly: Merge(child.JgToApi, parent.JgToApi, ci),
                keyToApi: Merge(child.KeyToApi, parent.KeyToApi, null),
                alsoKnownAs: Merge(child.AlsoKnownAs, parent.AlsoKnownAs, ci),
                keyUiType: Merge(child.KeyUiType, parent.KeyUiType, null),
                addrPortPairs: Merge(child.AddrPortPairs, parent.AddrPortPairs, ci),
                derivedBools: Merge(child.DerivedBools, parent.DerivedBools, ci),
                syntheticFields: null,
                pairedFields: Merge(child.PairedFields, parent.PairedFields, ci),
                numFlagMembers: child.NumFlagMembers == null ? parent.NumFlagMembers
                    : parent.NumFlagMembers == null ? child.NumFlagMembers
                    : child.NumFlagMembers.Union(parent.NumFlagMembers));
        }

        /// <summary>
        /// The bool fields this path derives from another decoded field — see
        /// <see cref="FieldAliasSet.DerivedBools"/>. Empty for all but a handful of paths.
        /// </summary>
        internal IReadOnlyDictionary<string, Tuple<string, string>>? DerivedBoolFields => Aliases?.DerivedBools;

        /// <summary>The other spellings this path's fields are also reported under (see
        /// <see cref="FieldAliasSet.AlsoKnownAs"/>).</summary>
        internal IReadOnlyDictionary<string, string[]>? ExtraSpellings => Aliases?.AlsoKnownAs;

        /// <summary>
        /// Everything a decode of this path's records needs, built once per command (see <see cref="WinboxDecodePlan"/>).
        /// </summary>
        internal WinboxDecodePlan DecodePlan() => new WinboxDecodePlan(this);

        // Rewrite an API field name to its .jg label (encode/resolve direction); identity when no alias.
        // An alias whose label this catalog lacks yields to the API name when the catalog has THAT: a label
        // can differ by RouterOS version (address-list 'Name' on 6.x, 'List' on 7.x), and falling through to
        // a seed key instead would address a different field.
        private string AliasToJg(string apiName)
        {
            var a = Aliases;
            if (a == null || !a.ApiToJg.TryGetValue(apiName, out var jg)) return apiName;
            var fields = JgFields;
            if (fields != null && !fields.ContainsKey(jg) && fields.ContainsKey(apiName)) return apiName;
            return jg;
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
            var kinds = new Dictionary<int, int>();
            var contested = new HashSet<int>();
            foreach (var kv in JgFieldsSpecificFirst())
            {
                int kind = WinboxM2Protocol.TypedKey.KindOf(kv.Value.WireType);
                if (kinds.TryGetValue(kv.Value.Key, out int seen))
                {
                    if (seen != kind) contested.Add(kv.Value.Key);
                }
                else kinds[kv.Value.Key] = kind;
            }
            return contested;
        }

        // A field's key qualified by the kind of its .jg wire type ('U12' → array, 'u12' → scalar, 'b1f' → bool).
        private static int TypedKeyOf(WinboxJgField f)
            => WinboxM2Protocol.TypedKey.Qualify(f.Key, f.WireType);

        /// <summary>
        /// A <c>numflag</c> member whose label is not the word RouterOS prints for that flag. RouterOS 6's
        /// route window says 'connected' where the API — and RouterOS 7's own catalog — say <c>connect</c>.
        /// </summary>
        private static readonly Dictionary<string, string> NumFlagMemberAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["connected"] = "connect",
            };

        /// <summary>
        /// The row flags this path's <c>numflag</c> fields carry, as <c>key → (value → API flag name)</c>.
        /// </summary>
        /// <remarks>
        /// <para>One numeric key, a set of named flags, one of which the row is: the route's origin is
        /// <c>{numflag,id:'u7',c:{2:['connected','C'],3:['static','S'],…}}</c> on RouterOS 6 and
        /// <c>{numflag,id:'u112',c:{2:['connect','C'],…,10:['DHCP','d'],…}}</c> on 7. The API prints the one
        /// the row is (<c>connect=true</c>), which is what the decode emits.</para>
        /// <para>Because each catalog declares the members ITS version knows, no version test is involved:
        /// a RouterOS 6 route can never read <c>dhcp</c>, because that catalog has no such member and the
        /// router does not print it either.</para>
        /// <para>Only the members the path names in <see cref="FieldAliasSet.NumFlagMembers"/> are kept: not
        /// every <c>numflag</c> is a set of API flags, and a path that names none reports none.</para>
        /// </remarks>
        internal IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? BuildNumFlags()
        {
            var printed = Aliases?.NumFlagMembers;
            if (printed == null || printed.Count == 0) return null;
            var raw = _catalog?.GetNumFlags(_handler, _windowKey);
            if (raw == null || raw.Count == 0) return null;
            var result = new Dictionary<int, IReadOnlyDictionary<int, string>>();
            foreach (var byKey in raw)
            {
                var named = new Dictionary<int, string>();
                foreach (var m in byKey.Value)
                {
                    // A member's label is the router's own flag name; normalized like any other label, and
                    // through the same member-alias table an enum member uses ('connected' → `connect`).
                    string name = NormalizeLabel(m.Value, applyOverrides: false);
                    if (NumFlagMemberAliases.TryGetValue(name, out var api)) name = api;
                    if (printed.Contains(name)) named[m.Key] = name;
                }
                if (named.Count > 0) result[byKey.Key] = named;
            }
            return result.Count > 0 ? result : null;
        }

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
            void Put(int key, string apiName, bool sameField = false)
            {
                if (map.ContainsKey(key)) return;
                // A qualified registration is the SAME field as its plain one (see ContestedKeys), so it must
                // not be the thing that claims the name away from it. Neither must a union's other family or
                // a tuple's other part (sameField) — those keys ARE the field that just claimed the name, and
                // letting the claim block them is what left a v6 address unreported.
                if (!sameField && !WinboxM2Protocol.TypedKey.IsQualified(key) && !claimed.Add(apiName)) return;
                map[key] = apiName;
            }

            var contested = ContestedKeys();
            // Extra registrations of a union/tuple, applied after the loop below — see there.
            var deferred = new List<KeyValuePair<int, string>>();
            foreach (var kv in _overrides) Put(kv.Value, kv.Key);
            if (_labelHints != null)
                foreach (var hint in _labelHints)
                    if (TryLabelHint(hint.Key, out var hinted)) Put(hinted.Key, hint.Key);
            // A paired field's composite name has to win over the upload half's own, and Put is first-wins,
            // so it goes in before the catalog. Without this the joined value came out under the HALF's
            // name — 'upload-max-limit = 1000000/2000000' — which is the right value answering to a name
            // no other transport uses, and so still not the field the caller asked for.
            foreach (var kv in PairedCompositeNames()) Put(kv.Key, kv.Value);
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
                // A `nonpublic:1` declaration is a slot WinBox reads and never paints, and the API does not print
                // it either: the identity window's 'Version' (sd) made /system/identity/print answer
                // {version, name}, and ExecuteScalar() the RouterOS version (WinBox review D6). A shipped
                // synthetic field over such a key (/system/package's bundle) is declared public and still names it,
                // and so does a key another window of the handler paints (see WinboxJgCatalog.IsDeclaredPublic).
                if (kv.Value.NonPublic && !_catalog.IsDeclaredPublic(_handler, kv.Value.Key)) continue;
                Put(kv.Value.Key, AliasToApi(kv.Key));
                // One declaration, several keys: a union's other address families and a tuple's other parts
                // (see WinboxJgField.ExtraRegistrations). They answer to the same name, so the row decodes
                // whichever key the router actually sent — but they are a FALLBACK, not an owner, and are
                // applied only after every field that has a key of its own has had it.
                if (kv.Value.ExtraRegistrations != null)
                    foreach (var extra in kv.Value.ExtraRegistrations)
                        deferred.Add(new KeyValuePair<int, string>(extra.Key, AliasToApi(kv.Key)));
            }
            // …here. The Ping window is why: its reply's 'Seq #' is `uf` and its request's 'Src. Address'
            // union has `af` for the IPv6 family — ONE key, 0xF, told apart by nothing but the ftype letter.
            // Registering the alternative inside the loop let it win 0xF from a field that owns it outright,
            // and every ping reply lost its sequence number.
            foreach (var kv in deferred) Put(kv.Key, kv.Value, sameField: true);
            foreach (var kv in FallbackSeed) Put(kv.Value, kv.Key);
            foreach (var kv in ReadOnlySystemSeed) Put(kv.Value, kv.Key);

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
            var deferredFields = new List<WinboxJgField>();
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
                if (kv.Value.ExtraRegistrations != null) deferredFields.AddRange(kv.Value.ExtraRegistrations);
            }
            // Each extra registration is typed as ITSELF — a union's network6 family renders through its own
            // prefix-length sibling, not through the first family's netmask — and, as in BuildKeyToApiName,
            // only where no field owns the key outright.
            foreach (var extra in deferredFields)
                if (!map.ContainsKey(extra.Key)) map[extra.Key] = extra;
            // Synthesize typed fields for shipped key aliases the .jg leaves unnamed (collide on empty apiName),
            // so decode formats them correctly (e.g. ping reply 'host' @0x1 as an ipaddr u32).
            var aliasSet = Aliases;
            if (aliasSet != null)
                foreach (var kv in aliasSet.KeyUiType)
                    if (!map.ContainsKey(kv.Key))
                    {
                        aliasSet.KeyToApi.TryGetValue(kv.Key, out var nm);
                        map[kv.Key] = new WinboxJgField(nm ?? "", kv.Key, M2Type.U32, true, null, kv.Value);
                    }
            ApplyAddrPortPairs(map, aliasSet);
            ApplyPairedFields(map, aliasSet);
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

        /// <summary>
        /// The synthetic UI type of a number RouterOS prints joined to an address-block NETMASK that WinBox keeps
        /// in a box of its own — firewall <c>connection-limit=10,24</c>. The netmask key rides in
        /// <see cref="WinboxJgField.MaskKey"/>; derived from the <c>.jg</c> (a group of exactly the number and a
        /// <c>netmask</c>), see <c>WinboxJgCatalog.NetmaskSibling</c>.
        /// </summary>
        internal const string NumberNetmaskUiType = "numbernetmask";

        /// <summary>
        /// The synthetic UI type of a field RouterOS prints as one <c>upload/download</c> pair while the M2
        /// model keeps two scalars — the <c>/queue/simple</c> rate fields. The download half's key rides in
        /// <see cref="WinboxJgField.MaskKey"/>, both halves' typed fields in
        /// <see cref="WinboxJgField.PairHalves"/>, and the codec consumes the key so the half does not also
        /// surface as a field the API never reports.
        /// </summary>
        /// <remarks>
        /// Shipped per path rather than derived: nothing in the <c>.jg</c> says 'Target Upload / Max Limit'
        /// and 'Target Download / Max Limit' are one API field, any more than it says the queue-type
        /// dropdowns beside them are.
        /// <para>
        /// The composite REPLACES the upload half's name rather than being added next to it. The halves are
        /// WinBox labels, not RouterOS API names — no other transport reports an <c>upload-max-limit</c> —
        /// and a row carrying all three would invite a write to the one the router does not take.
        /// </para>
        /// </remarks>
        internal const string PairUiType = "pair";

        private void ApplyPairedFields(Dictionary<int, WinboxJgField> map, FieldAliasSet? aliasSet)
        {
            if (aliasSet?.PairedFields == null) return;
            var jg = JgFields;
            if (jg == null) return;

            foreach (var pair in aliasSet.PairedFields)
            {
                if (!jg.TryGetValue(pair.Value.Item1, out var upload)) continue;
                if (!jg.TryGetValue(pair.Value.Item2, out var download)) continue;

                map[upload.Key] = new WinboxJgField(pair.Key, upload.Key, upload.WireType, upload.ReadOnly,
                    enumMap: upload.EnumMap, uiType: PairUiType, maskKey: download.Key,
                    scale: upload.Scale, pairHalves: Tuple.Create(upload, download));
            }
        }

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

        /// <summary>
        /// The upload half's key → the composite API name, for every paired field of this path.
        /// </summary>
        private IEnumerable<KeyValuePair<int, string>> PairedCompositeNames()
        {
            var paired = Aliases?.PairedFields;
            var jg = JgFields;
            if (paired == null || jg == null) yield break;

            foreach (var pair in paired)
                if (jg.TryGetValue(pair.Value.Item1, out var upload))
                    yield return new KeyValuePair<int, string>(upload.Key, pair.Key);
        }

        /// <summary>
        /// The two <c>.jg</c> labels a paired API field is made of, or <c>null</c> when the name is not one.
        /// </summary>
        private Tuple<string, string>? PairedHalfNames(string apiName)
        {
            var paired = Aliases?.PairedFields;
            if (paired == null) return null;
            return paired.TryGetValue(apiName, out var halves) ? halves : null;
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

            // A paired field reaches this only from a FILTER or a .proplist, never from a write (EncodeField
            // splits it first). Left failing on purpose: the composite spans two keys, so filtering on it
            // would have to filter on one of them, and a filter that silently matches half a value is worse
            // than one that says it cannot.
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
            if (TryLabelHint(apiName, out var hinted)) { key = hinted.Key; return true; }
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
        /// The API field name a WinBox caption stands for (<c>Chain</c> → <c>chain</c>, <c>Src. Address</c> →
        /// <c>src-address</c>), as a read reports it; <c>null</c> when GUI names are off, the name is already
        /// API-styled (lower case, no space, underscore or dot), or it resolves to no field. For a filter: the
        /// filter check and the read compare names as the router spells them, where a write resolves the
        /// caption itself (<see cref="ResolveKey"/>).
        /// </summary>
        internal string? GuiCaptionToApiName(string name)
        {
            if (!_useGuiNames || !name.Any(c => char.IsUpper(c) || c == ' ' || c == '_' || c == '.'))
                return null;
            int key;
            try { key = ResolveKey(name); }
            catch (WinboxFieldResolutionException) { return null; }
            return BuildKeyToApiName().TryGetValue(key, out string? apiName) ? apiName : null;
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
        /// <param name="label">The WinBox label.</param>
        /// <param name="applyOverrides">
        /// <c>false</c> for an enum MEMBER: the override map renames FIELD labels ('Tx' is the interface's
        /// tx-byte counter), and applied to a member it turned /interface/vxlan rem-csum's 'tx' into 'tx-byte'.
        /// </param>
        /// <param name="keepDecimalPoints">
        /// <c>true</c> for an enum MEMBER: a point between two digits is a version or a decimal
        /// (<c>802.3ad</c>, <c>wireless-802.11</c>), which RouterOS keeps. A field label never carries one.
        /// </param>
        internal static string NormalizeLabel(string label, bool applyOverrides = true, bool keepDecimalPoints = false)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            string trimmed = label.Trim();
            if (applyOverrides && LabelOverride.TryGetValue(trimmed, out var ovr)) return ovr;

            var sb = new StringBuilder(trimmed.Length);
            bool lastDash = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (keepDecimalPoints && c == '.' && i > 0 && i + 1 < trimmed.Length
                    && char.IsDigit(trimmed[i - 1]) && char.IsDigit(trimmed[i + 1]))
                {
                    sb.Append('.');
                    lastDash = false;
                    continue;
                }
                if (c == '.' || c == '\'')
                {
                    // Abbreviation dot in a UI label ("Dst. Address" → "dst-address"), and the apostrophe of a
                    // contraction ("Don't Fragment" → "dont-fragment", 7.24.2): API names carry neither.
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
