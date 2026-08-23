using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// Loads and parses the WinBox <c>.jg</c> plugin catalogs (tolerant JS object literals) and exposes,
    /// per M2 handler path, the field map <c>apiName → (key, wireType, ro)</c>. The <c>.jg</c> files are the
    /// version-volatile source of truth for field keys/types; <see cref="WinboxFieldResolver"/> layers a
    /// stable label↔apiName normalizer and overrides on top.
    /// </summary>
    /// <remarks>
    /// <para>Source: live fetch over the WinBox mproxy file handler (<c>[2,2]</c> cmd=7). The plugin set is
    /// resolved from the router's own <c>list</c> catalog — never hardcoded, since which plugins exist
    /// depends on version and installed packages — and each is downloaded by its version-stamped
    /// <c>unique</c> name (<c>&lt;unique&gt;.gz</c>, gzip), then cached content-addressed to
    /// <c>&lt;CatalogCachePath&gt;/plugins/&lt;unique&gt;</c>. The resolved set itself is remembered per router
    /// under <c>&lt;CatalogCachePath&gt;/lists/</c>, so a router that momentarily refuses <c>list</c> still
    /// yields a full catalog instead of silently degrading the connection to seeds.</para>
    /// <para>The parser is a C# port of <c>Tools/probes/jg_analyze.py</c> — it walks the object tree
    /// and, for every node carrying a <c>path:[…]</c>, attributes the enclosing field <c>id:'&lt;prefix&gt;&lt;hex&gt;'</c>
    /// entries to that handler. Multiple windows may target the same handler; their fields are merged.</para>
    /// </remarks>
    internal sealed class WinboxJgCatalog
    {
        // handler-path-string ("20,0") → (apiName → field)
        private readonly Dictionary<string, Dictionary<string, WinboxJgField>> _byHandler =
            new Dictionary<string, Dictionary<string, WinboxJgField>>(StringComparer.Ordinal);

        // derived menu-label path (e.g. "/ip/firewall/connection", "/ip/dns/dns-static-entry") → handler
        // array, harvested from the menu tree: every WINDOW node (type map/query/item/doit/action with a
        // path:[…]) contributes its handler, keyed by the normalized full breadcrumb (enclosing menu
        // group+name chain + node name). First window wins for a given derived path. These keys reflect the
        // WinBox *menu labels*, which often differ from the RouterOS API leaf — WinboxHandlerMap bridges
        // apiPath → menu-label path via a curated alias tail for the irregular cases.
        private readonly Dictionary<string, int[]> _derivedPaths =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        // handler-path-string → the label of the field a WINDOW declares as the row's display name
        // (`nameval:'Alias'`). That is what WinBox shows for a row in a dropdown, and it is not always the
        // field called 'Name': the policy table [13,3] names its rows by 'Alias' ('read', 'write'), while
        // its 'Name' holds the sentence 'read router configuration'.
        private readonly Dictionary<string, string> _nameValByHandler =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Handlers backed by a singleton window (type:'item') — read via get-singleton, not getall.
        private readonly HashSet<string> _singletonHandlers = new HashSet<string>(StringComparer.Ordinal);

        // The same, but per WINDOW rather than per handler: derived menu-label path → is that window a
        // singleton. One handler routinely hosts both kinds — [28,0] is 'UPnP Settings' (item) AND the 'UPnP
        // Interfaces' list (map), [96,1] is the web-proxy settings item and its connections list — so asking
        // the handler alone answers "singleton" for the list too and returns one record where there are many.
        // Consulted first, with the handler-level set as the fallback for paths reached by override.
        private readonly Dictionary<string, bool> _singletonPaths =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Window field maps are keyed by WindowKey(derivedPath); these two remember what a window key MEANS.
        //
        // _windowHandlerKey: window key → the handler key that window reads. An action declared inside a window
        // is still keyed by its HANDLER (GetActionFields is asked by handler), so the walk needs the way back.
        //
        // _handlerBackedWindows: the windows whose fields also belong in their handler's own map. That is every
        // ordinary window — the handler map stays exactly what it was, and the window map becomes an overlay on
        // top. It is NOT the interface subtype windows: those disagree about keys under identical labels
        // ('Remote Address' is 0x7E1 on EoIP and 0x7E5 on GRE), so merging them into [20,0] would hand every
        // subtype but the first-parsed one a key belonging to another interface kind.
        private readonly Dictionary<string, string> _windowHandlerKey =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _handlerBackedWindows = new HashSet<string>(StringComparer.Ordinal);

        // Handlers that appear behind an action window (type:'doit'/'action') and, separately, behind a
        // record-listing window (map/query/item). A handler in the first set but NOT the second is a
        // standalone action — invoking it is the only thing it can do, there are no records to read.
        private readonly HashSet<string> _actionWindowHandlers = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _recordWindowHandlers = new HashSet<string>(StringComparer.Ordinal);

        // Handlers whose window carries live/dynamic data (the .jg marks it with `autorefresh:<ms>`), e.g. the
        // firewall rule list with its bytes/packets counters. getall on these must OR the stats flag bit so the
        // router includes the runtime counter fields (matching what RouterOS `print` returns).
        private readonly HashSet<string> _dynamicHandlers = new HashSet<string>(StringComparer.Ordinal);

        // Interface subtype filters. RouterOS interface subtypes (bridge, vlan, eoip, …) are NOT separate M2
        // handlers — they all live in the generic interface table (the `generic:'iface'` window, [20,0]) and are
        // distinguished by a numeric `type` field. The .jg declares each subtype window with `inherit:'iface'`
        // (reuse the generic handler) + `typevalue:N` (the discriminator value). A getall on the shared handler
        // returns every interface; the subtype list keeps only rows whose type field == N. Keyed by derived
        // menu-label path → (typeKey, typeValue); _genericIfaceHandler/_ifaceTypeKey capture the base window.
        private readonly Dictionary<string, Tuple<int, int>> _subtypeFilters =
            new Dictionary<string, Tuple<int, int>>(StringComparer.OrdinalIgnoreCase);
        private int[]? _genericIfaceHandler;
        private int _ifaceTypeKey;

        // The same shape outside the interface family: a `generic:'X'` base window that HAS a handler of its
        // own and declares a `typeon` discriminator, so its `inherit:'X'`+`typevalue:N` children are rows of
        // the base's table rather than tables of their own. `generic:'route'` ([44,21], typeon:'rtype') is the
        // live case — IPv4 routes are rtype 2 and IPv6 routes rtype 3 in ONE table, so a read of /ip/route
        // that does not filter answers with the IPv6 routes too. Keyed generic name → (handler, typeKey).
        private readonly Dictionary<string, Tuple<int[], int>> _genericBases =
            new Dictionary<string, Tuple<int[], int>>(StringComparer.Ordinal);

        // Windows that declare `generic:'<name>'` — the bases other windows extend with `inherit:'<name>'`.
        // The interface subtype tree is NOT one level deep: the PPP tunnel windows inherit a `generic:'ppp'`
        // base which itself inherits 'iface', and the wireless ones go 'iface' → 'wlan' → 'ath' → 'athhw'.
        // Recognising only `inherit:'iface'` therefore missed every PPP client/server binding and every
        // wireless interface window — 6 API paths that the API and CLI transports carry without trouble.
        // Value: the discriminator this base declares for ITS children (`typeon`, default 'type') and
        // whether the base belongs to the interface family (transitively rooted at generic:'iface').
        private readonly Dictionary<string, Tuple<string, bool>> _generics =
            new Dictionary<string, Tuple<string, bool>>(StringComparer.Ordinal);

        // Per-record action verbs (.jg type:'doit'/'action' with a cmd, e.g. the scripts window's
        // "Run Script" doit cmd:1). These are NOT CRUD — they invoke a command on the owning handler targeting
        // a record .id. Keyed handler-path → (normalized action label → SYS_CMD). The RouterOS API verb
        // (/system/script/run) is matched to the label by its first token ("run" ↔ "run-script").
        private readonly Dictionary<string, Dictionary<string, int>> _actionsByHandler =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        // Streaming-monitor windows (.jg type:'query', or type:'action' with a pollcmd): start → poll → cancel
        // over the normal request/reply channel (NOT a server push — the client re-polls every autorefresh ms;
        // see Docs/winbox-native-m2-protocol.md §20). Keyed handler-path → spec (start/poll/cancel cmd + interval).
        private readonly Dictionary<string, WinboxMonitorSpec> _monitorsByHandler =
            new Dictionary<string, WinboxMonitorSpec>(StringComparer.Ordinal);

        // id-prefix letter → wire-type name (lower=scalar, upper=array). Mirrors jg_analyze.py PREFIX.
        //
        // The letters are not a convention to be inferred from examples: webfig declares the whole mapping in
        // one literal, `id2int={b:0<<27,u:1<<27,q:2<<27,a:3<<27,s:4<<27,m:5<<27,r:6<<27,B:16<<27,U:17<<27,
        // Q:18<<27,A:19<<27,S:20<<27,M:21<<27,R:22<<27}` — an ftype per letter, the uppercase one being the
        // array ftype of the same thing. A letter MISSING from this table is worse than a mistyped one:
        // DecodeId yields the type "?" and the field is dropped at harvest, so it does not read wrong, it
        // ceases to exist. That is what `Q` (FT_U64_ARRAY) did to every `multibignumber` in the catalog.
        //
        // `id2int`'s fourteen letters, and no more: this table used to carry x/X/i/d/t as well, which are in
        // neither that literal nor any catalog of either version (the 7.17 and 7.24 histograms find the same
        // thirteen letters — every one of them here, `B` being the one this table has and no window uses).
        // An invented mapping is worse than a missing one: a missing letter makes the field unreadable and
        // obvious, an invented one makes it read WRONG.
        private static readonly Dictionary<char, string> Prefix = new Dictionary<char, string>
        {
            ['u'] = "u32",    ['U'] = "u32[]",
            ['s'] = "string", ['S'] = "string[]",
            ['b'] = "bool",   ['B'] = "bool[]",
            ['r'] = "raw",    ['R'] = "raw[]",
            ['m'] = "addr",   ['M'] = "addr[]",
            ['a'] = "ip6",    ['A'] = "ip6[]",
            ['q'] = "u64",    ['Q'] = "u64[]",
        };

        /// <summary>True once at least one <c>.jg</c> window has been parsed into the catalog.</summary>
        internal bool HasData => _byHandler.Count > 0;

        /// <summary>
        /// Returns the field map for <paramref name="handler"/> as <c>apiName → field</c>, or <c>null</c>
        /// when no window targeting that handler was found in the catalog.
        /// </summary>
        internal IReadOnlyDictionary<string, WinboxJgField>? GetHandlerFields(int[] handler)
        {
            return _byHandler.TryGetValue(HandlerKey(handler), out var map) ? map : null;
        }

        // Field maps of a WINDOW rather than of a handler live in the same dictionary under a key that
        // cannot collide with a handler key ("20,0").
        private static string WindowKey(string derivedPath) => "win:" + derivedPath;

        // The same trick for the ARGUMENTS of one action window (a .jg doit/action with a cmd): its fields are
        // kept under their own key as well as merged into the owning handler's map, so a caller invoking the
        // action can be given the action's own vocabulary. See GetActionFields.
        private static string ActionKey(string handlerKey, string normalizedLabel)
            => "act:" + handlerKey + "|" + normalizedLabel;

        private static bool IsActionKey(string? key) => key != null && key.StartsWith("act:", StringComparison.Ordinal);

        private static bool IsWindowKey(string? key) => key != null && key.StartsWith("win:", StringComparison.Ordinal);

        // The handler a walk owner ultimately reads: identity for a handler key, the owning handler for a
        // window key. Actions are keyed by handler even when declared inside a window, because that is how
        // GetActionFields asks for them.
        private string? HandlerOfOwner(string? owner)
            => IsWindowKey(owner) && owner != null && _windowHandlerKey.TryGetValue(owner, out var h) ? h : owner;

        // "act:85,5|generate-key" → "85,5"
        private static string HandlerOfActionKey(string actionKey)
        {
            int bar = actionKey.IndexOf('|');
            return actionKey.Substring(4, bar - 4);
        }

        /// <summary>
        /// The argument fields declared by ONE action window (<c>type:'doit'/'action'</c> with a <c>cmd</c>),
        /// as <c>apiName → field</c>, or <c>null</c> when that action declares none.
        /// </summary>
        /// <remarks>
        /// These OVERLAY <see cref="GetHandlerFields"/> the same way a subtype window's fields do, and for the
        /// same reason: an action's arguments and the record list's columns share one handler and routinely
        /// share LABELS while meaning different things. <c>/ip/ipsec/key/rsa</c> is the worked example — the
        /// 'Keys' window lists 'Key Size' as a read-only column (<c>u1</c>, <c>ro:1</c>) and the 'Generate Key'
        /// doit takes 'Key Size' as an argument (<c>u1</c>, an enum, writable). Merged into one per-handler map
        /// the read-only column wins (first label wins), so the argument encoded to nothing and the router
        /// generated a default 1024-bit key while the caller was told it had asked for 2048.
        /// </remarks>
        internal IReadOnlyDictionary<string, WinboxJgField>? GetActionFields(int[] handler, string? normalizedLabel)
            => handler != null && normalizedLabel != null
               && _byHandler.TryGetValue(ActionKey(HandlerKey(handler), normalizedLabel), out var map)
                ? map : null;

        /// <summary>
        /// The field map declared by one specific window, or <c>null</c> when the window declares none. These
        /// fields OVERLAY <see cref="GetHandlerFields"/>: where a label — or a KEY — appears in both, the
        /// window's answer is the right one for a caller addressing that window.
        /// </summary>
        /// <remarks>
        /// Two shapes need it. An interface subtype (EoIP Tunnel, L2TP Client, …) adds its own fields to the
        /// generic <c>[20,0]</c> window's and disagrees with its siblings about their keys. And two unrelated
        /// windows routinely share one handler while numbering their fields from scratch: <c>[28,0]</c> is both
        /// the UPnP settings singleton (<c>b1</c> 'Enabled') and the UPnP interface list (<c>u1</c>
        /// 'Interface'), so the merged per-handler map answers "enabled" for key 1 and an interface row read
        /// back without the <c>interface</c> field at all.
        /// </remarks>
        internal IReadOnlyDictionary<string, WinboxJgField>? GetWindowFields(string? derivedPath)
        {
            return derivedPath != null && _byHandler.TryGetValue(WindowKey(derivedPath), out var map) ? map : null;
        }

        /// <summary>
        /// API paths derived from the <c>.jg</c> menu tree: <c>derivedApiPath → handler</c>.
        /// Built from every <c>type:'map'/'query'</c> window node (its <c>path:[…]</c> handler keyed by
        /// the normalized enclosing <c>group</c> + node <c>name</c>). Dropdown references
        /// (<c>type:'enm'</c>) that reuse a handler are not included.
        /// </summary>
        /// <summary>
        /// The API name of the field a handler's window declares as the row's display name
        /// (<c>nameval</c>), or <c>null</c> when it declares none.
        /// </summary>
        internal string? GetNameValField(int[] handler)
            => _nameValByHandler.TryGetValue(HandlerKey(handler), out var label) ? label : null;

        internal IReadOnlyDictionary<string, int[]> GetDerivedPaths() => _derivedPaths;

        /// <summary>
        /// Interface subtype filters: <c>derivedApiPath → (typeKey, typeValue)</c>. For a subtype path
        /// (e.g. <c>/bridge/bridge</c>) a getall runs on the shared generic interface handler and the result
        /// is filtered to rows whose <c>typeKey</c> field equals <c>typeValue</c>. Empty when the catalog has
        /// no <c>inherit:'iface'</c>/<c>typevalue</c> windows.
        /// </summary>
        internal IReadOnlyDictionary<string, Tuple<int, int>> GetSubtypeFilters() => _subtypeFilters;

        /// <summary>
        /// Per-record action verbs for <paramref name="handler"/> as <c>normalized action label → SYS_CMD</c>
        /// (from <c>type:'doit'/'action'</c> nodes with a <c>cmd</c>), or <c>null</c> when the handler exposes
        /// no actions. Used to dispatch non-CRUD verbs such as <c>/system/script/run</c>.
        /// </summary>
        internal IReadOnlyDictionary<string, int>? GetHandlerActions(int[] handler)
            => handler != null && _actionsByHandler.TryGetValue(HandlerKey(handler), out var m) ? m : null;

        /// <summary>
        /// Whether the window behind a specific derived menu-label path is a singleton (<c>type:'item'</c>,
        /// read with get-singleton) or a record list (<c>map</c>/<c>query</c>, read with getall). Returns
        /// <c>false</c> with <paramref name="isSingleton"/> unset when the path is unknown — the caller then
        /// falls back to <see cref="IsSingletonHandler"/>.
        /// </summary>
        /// <remarks>
        /// Prefer this over the handler-level answer whenever the path is known: a handler that hosts both a
        /// settings item and a list (UPnP, web proxy) reports "singleton" for both, which turns the list into
        /// a single record.
        /// </remarks>
        internal bool TryIsSingletonPath(string? derivedKey, out bool isSingleton)
        {
            isSingleton = false;
            return derivedKey != null && _singletonPaths.TryGetValue(derivedKey, out isSingleton);
        }

        /// <summary>True when <paramref name="handler"/> is backed by a singleton (<c>type:'item'</c>)
        /// window — its sole record is read via get-singleton rather than getall. Handler-level, so it
        /// answers <c>true</c> for a handler that ALSO hosts a list window; prefer
        /// <see cref="TryIsSingletonPath"/> when the path is known.</summary>
        internal bool IsSingletonHandler(int[] handler) =>
            handler != null && _singletonHandlers.Contains(HandlerKey(handler));

        /// <summary>
        /// True when <paramref name="handler"/> is backed <b>only</b> by action windows
        /// (<c>type:'doit'/'action'</c>) and by no record-listing window — e.g. Wake on LAN
        /// (<c>advtool.jg</c>, handler <c>[82]</c>). Such a path is an operation to invoke, not a table to
        /// read: a <c>getall</c> on it returns nothing useful.
        /// </summary>
        /// <remarks>
        /// The exclusion matters. Handler <c>[20,125]</c> hosts both the 'SMS Message' <c>map</c> and the
        /// 'Send SMS' <c>doit</c>; treating it as action-only would break reading SMS messages. So a handler
        /// qualifies only when no record window anywhere in the catalog claims it.
        /// </remarks>
        internal bool IsActionOnlyHandler(int[]? handler)
        {
            if (handler == null) return false;
            string key = HandlerKey(handler);
            return _actionWindowHandlers.Contains(key) && !_recordWindowHandlers.Contains(key);
        }

        /// <summary>
        /// The single SYS_CMD for an <see cref="IsActionOnlyHandler"/> handler, or <c>-1</c> when the handler
        /// exposes no action or more than one (ambiguous — the caller must name the verb instead).
        /// <paramref name="label"/> receives the action's normalized label, which
        /// <see cref="GetActionFields"/> keys its arguments by.
        /// </summary>
        internal int GetSoleActionCmd(int[] handler, out string? label)
        {
            label = null;
            var actions = GetHandlerActions(handler);
            if (actions == null || actions.Count != 1) return -1;
            foreach (var kv in actions) { label = kv.Key; return kv.Value; }
            return -1;
        }

        /// <summary>
        /// Finds a SINGLETON (<c>type:'item'</c>) window whose derived menu-label leaf contains
        /// <paramref name="leaf"/> (case-insensitive), returning its handler — or <c>null</c> when none match.
        /// </summary>
        /// <remarks>
        /// Used to recover the board-gated singleton variant of a menu that ALSO exposes a non-singleton
        /// <c>type:'map'</c> window under the same label. <c>/system/health</c> is the live example: an x86
        /// hardware-sensor <c>item</c> window (<c>[24,14]</c>, read via get-singleton) sits alongside the
        /// RouterBOARD name/value <c>map</c> window (<c>[24,29]</c>), and the shipped path alias resolves to
        /// the map handler — which answers getall with <c>NotImplemented</c> on x86/CHR (verified live). The
        /// handler number is taken live from the <c>.jg</c>, so this stays version-portable (no hardcoded path).
        /// </remarks>
        internal int[]? FindSingletonHandlerByLeaf(string leaf)
        {
            if (string.IsNullOrEmpty(leaf)) return null;
            foreach (var kv in _derivedPaths)
            {
                if (!_singletonHandlers.Contains(HandlerKey(kv.Value))) continue;
                int slash = kv.Key.LastIndexOf('/');
                string l = slash >= 0 ? kv.Key.Substring(slash + 1) : kv.Key;
                if (l.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            }
            return null;
        }

        /// <summary>Returns the streaming-monitor spec for <paramref name="handler"/> (a <c>type:'query'</c>
        /// or poll-action window), or <c>null</c> when the handler is not a monitor window.</summary>
        internal WinboxMonitorSpec? GetMonitorByHandler(int[]? handler) =>
            handler != null && _monitorsByHandler.TryGetValue(HandlerKey(handler), out var spec) ? spec : null;

        /// <summary>True when <paramref name="handler"/>'s window is marked <c>autorefresh</c> in the
        /// <c>.jg</c> — it carries runtime/dynamic fields (e.g. firewall counters) that getall only returns
        /// when the stats flag bit is set.</summary>
        internal bool HasDynamicFields(int[] handler) =>
            handler != null && _dynamicHandlers.Contains(HandlerKey(handler));

        /// <summary>Number of field maps this catalog holds. <c>0</c> means the <c>.jg</c> load produced
        /// nothing and every lookup will fall back to the seed table — see
        /// <see cref="WinboxNative.WinboxNativeConnection.CatalogHandlerCount"/> for why that matters.
        /// <para>It counts every map, not only the per-handler ones: a window and an action each keep a map
        /// of their own alongside their handler's, so the number is larger than the handler count and is a
        /// LOAD indicator, not an inventory. Compare it against zero, never against a remembered figure.</para>
        /// </summary>
        internal int HandlerCount => _byHandler.Count;

        // ── Loading ────────────────────────────────────────────────────────────

        // One entry of the mproxy "list" catalog: the plugin's stable name and the version-stamped file
        // that actually exists on disk.
        internal struct PluginEntry
        {
            internal string Name;    // e.g. "roteros.jg" — stable, used for diagnostics
            internal string Unique;  // e.g. "roteros-33a7039ff432.jg" — on-disk name; ".gz" appended to open
        }

        // The list catalog is a JS object literal, one { … } per served file. Only entries carrying a
        // `unique` are fetchable plugins; the icon PNGs have none.
        private static readonly Regex ListEntryRegex = new Regex(@"\{([^}]*)\}", RegexOptions.CultureInvariant);
        private static readonly Regex ListNameRegex = new Regex(@"\bname\s*:\s*""([^""]*)""", RegexOptions.CultureInvariant);
        private static readonly Regex ListUniqueRegex = new Regex(@"\bunique\s*:\s*""([^""]*)""", RegexOptions.CultureInvariant);

        // The `unique` value is router-supplied and is used both as an mproxy filename and as a local cache
        // filename, so it is constrained to a plain name here — no separators, no "..", nothing that could
        // walk out of the cache directory.
        private static readonly Regex SafePluginNameRegex =
            new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.jg$", RegexOptions.CultureInvariant);

        private static bool IsSafePluginName(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= 128 &&
            !name.Contains("..") && SafePluginNameRegex.IsMatch(name);

        // Asks the router which plugins it serves. The set is version- and package-dependent (7.23.2 CHR
        // serves 18 .jg files, including container/iot/userman5/dude that no fixed list would have named,
        // and none of the mpls/roting4 an older one did), so it must never be hardcoded.
        private static List<PluginEntry> FetchPluginList(WinboxNativeM2Operations ops)
        {
            var result = new List<PluginEntry>();

            int handle = MproxyOpen(ops, "list", WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) return result;
            byte[]? raw = MproxyRead(ops, handle);
            if (raw == null || raw.Length == 0) return result;

            return ParsePluginList(Encoding.UTF8.GetString(raw));
        }

        /// <summary>
        /// Parses the mproxy <c>list</c> catalog into the fetchable <c>.jg</c> plugins. Entries without a
        /// <c>unique</c> (the icon PNGs) and entries whose names are not plain filenames are skipped — the
        /// names come from the router and are used as local cache filenames.
        /// </summary>
        internal static List<PluginEntry> ParsePluginList(string text)
        {
            var result = new List<PluginEntry>();
            foreach (Match m in ListEntryRegex.Matches(text ?? ""))
            {
                string body = m.Groups[1].Value;
                Match name = ListNameRegex.Match(body), unique = ListUniqueRegex.Match(body);
                if (!name.Success || !unique.Success) continue;
                if (!IsSafePluginName(name.Groups[1].Value) || !IsSafePluginName(unique.Groups[1].Value)) continue;
                result.Add(new PluginEntry { Name = name.Groups[1].Value, Unique = unique.Groups[1].Value });
            }
            return result;
        }

        // ── Remembering the resolved plugin set (per router) ───────────────────

        /// <summary>Serializes a resolved plugin set as <c>name TAB unique</c> lines.</summary>
        internal static string FormatPluginList(IEnumerable<PluginEntry> plugins) =>
            string.Join("\n", plugins.Select(p => p.Name + "\t" + p.Unique).ToArray());

        /// <summary>
        /// Reads back a set written by <see cref="FormatPluginList"/>. Names are re-validated on the way in:
        /// the file survives between runs and its contents become mproxy filenames and cache filenames again,
        /// so it is treated as untrusted exactly like the router's own <c>list</c>.
        /// </summary>
        internal static List<PluginEntry> ParseRememberedList(string text)
        {
            var result = new List<PluginEntry>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                string trimmed = line.Trim();
                int tab = trimmed.IndexOf('\t');
                if (tab <= 0) continue;
                string name = trimmed.Substring(0, tab).Trim(), unique = trimmed.Substring(tab + 1).Trim();
                if (!IsSafePluginName(name) || !IsSafePluginName(unique)) continue;
                result.Add(new PluginEntry { Name = name, Unique = unique });
            }
            return result;
        }

        // <cacheDir>/lists/<router>.list — one file per router, since two routers on the same version may
        // serve different plugin sets. The key is host or MAC, so it is reduced to filename-safe characters.
        private static string? ListCachePath(string? cacheDir, string? routerKey)
        {
            if (string.IsNullOrEmpty(cacheDir) || string.IsNullOrEmpty(routerKey)) return null;
            var safe = new StringBuilder(routerKey!.Length); // IsNullOrEmpty above already excluded null
            foreach (char c in routerKey)
                safe.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                            || c == '.' || c == '-' ? c : '_');
            return Path.Combine(Path.Combine(cacheDir, "lists"), safe.ToString() + ".list");
        }

        private static void RememberPluginList(string routerKey, string? cacheDir, List<PluginEntry> plugins)
        {
            if (string.IsNullOrEmpty(routerKey)) return;
            lock (LastGoodListsLock) { LastGoodLists[routerKey] = plugins; }

            string? path = ListCachePath(cacheDir, routerKey);
            if (path == null) return;
            try
            {
                string text = FormatPluginList(plugins);
                // Rewrite only on change: this runs on every open, and an unchanged router should not
                // produce a disk write per connection.
                if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), text, StringComparison.Ordinal))
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); // path is a file path from Path.Combine, always has a directory component
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { /* remembering is best-effort — never fail an open over it */ }
        }

        private static List<PluginEntry>? RecallPluginList(string routerKey, string? cacheDir)
        {
            if (string.IsNullOrEmpty(routerKey)) return null;
            lock (LastGoodListsLock)
            {
                if (LastGoodLists.TryGetValue(routerKey, out var inProcess)) return inProcess;
            }

            string? path = ListCachePath(cacheDir, routerKey);
            if (path == null) return null;
            try
            {
                if (!File.Exists(path)) return null;
                var plugins = ParseRememberedList(File.ReadAllText(path, Encoding.UTF8));
                if (plugins.Count == 0) return null;
                lock (LastGoodListsLock) { LastGoodLists[routerKey] = plugins; }
                return plugins;
            }
            catch { return null; }
        }

        // Catalog loading is silent by design (every failure is tolerated), which is exactly how a broken
        // live fetch stayed hidden behind a stale cache. Route the steps to the wire-trace channel so the
        // load can be observed without changing its behaviour.
        private const string TraceChannel = "wbx.catalog";

        private static void TraceNote(string note)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(TraceChannel, TikWireDir.Note, note);
        }

        // Parsed catalogs, shared process-wide and keyed by the exact set of plugin files a router
        // advertises. The plugin set — not the RouterOS version — is a catalog's identity: two routers on
        // the same version serve different plugins when their installed packages differ, while two routers
        // that serve the same files have byte-identical catalogs whatever else differs about them. So a
        // second connection to a like-configured router reuses the parse instead of redoing ~1 MB of it.
        private static readonly Dictionary<string, WinboxJgCatalog> SharedCatalogs =
            new Dictionary<string, WinboxJgCatalog>(StringComparer.Ordinal);
        private static readonly object SharedCatalogsLock = new object();

        /// <summary>
        /// Drops the process-wide parsed-catalog cache, so the next load genuinely re-fetches. Exists for
        /// tests that mean to exercise the COLD path: pointing a connection at an empty
        /// <c>CatalogCachePath</c> is not enough, because this cache is keyed by the plugin set rather than
        /// by the cache directory, so inside a run where any earlier connection already loaded the catalog
        /// the "cold" connection is served from memory and never writes a byte to its own cache dir. That
        /// is what made <c>WinboxNative_CatalogCache_ColdThenWarm</c> pass standalone and go Inconclusive
        /// inside a full suite — read as mproxy refusing to serve the plugin list, which it
        /// was not.
        /// </summary>
        internal static void ClearSharedCatalogs()
        {
            lock (SharedCatalogsLock)
                SharedCatalogs.Clear();
        }

        // The plugin set each router was last seen serving, remembered so that a *failed* `list` never
        // downgrades a connection. Keyed by router (host or MAC), unlike SharedCatalogs which is keyed by
        // the set itself — the whole point is to have an answer when the set could not be resolved.
        private static readonly Dictionary<string, List<PluginEntry>> LastGoodLists =
            new Dictionary<string, List<PluginEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LastGoodListsLock = new object();

        /// <summary>
        /// Returns the catalog for the plugin set this router advertises, reusing an already-parsed one when
        /// another connection has seen the same set and reading each plugin from <paramref name="cacheDir"/>
        /// when it was downloaded before. <paramref name="routerKey"/> (host or MAC) identifies the router
        /// for the remembered plugin set. Failures are tolerated — an empty catalog just leaves the resolver
        /// on its seed table and the normalizer.
        /// </summary>
        internal static WinboxJgCatalog Load(WinboxNativeM2Operations ops, string? cacheDir, string routerKey)
        {
            // The list is ~2 KB and one round trip; it is the only authoritative statement of what this
            // particular router serves, so it is always fetched — it is the plugin *bodies* that are cached.
            List<PluginEntry>? plugins = null;
            try { plugins = FetchPluginList(ops); }
            catch (Exception ex) { TraceNote("list FAILED: " + ex.Message); }

            if (plugins == null || plugins.Count == 0)
            {
                // A failed list must NOT mean an empty catalog. On seeds alone the connection still opens
                // and most paths still resolve, so nothing looks wrong — but IsSingletonHandler,
                // HasDynamicFields (the getall stats bit behind firewall bytes/packets), monitor specs and
                // reference resolution all quietly answer "no", and the caller gets plausible wrong data
                // instead of an error (P2.23; this is what made P2.21 look like a timing problem).
                // A `list` can fail because the channel it rode on has stopped answering (P2.20), which is
                // rare per connection but certain to happen across a long run, so this path must work.
                // The bodies are already cached content-addressed, so reusing the last known set costs no
                // mproxy traffic at all — and cross-version safety is intact: `unique` names are
                // version-stamped, so a real upgrade resolves a new set; only a *failed* list reuses this one.
                plugins = RecallPluginList(routerKey, cacheDir);
                if (plugins == null || plugins.Count == 0)
                {
                    TraceNote("list unavailable, nothing remembered for '" + routerKey + "' — SEEDS ONLY");
                    return new WinboxJgCatalog();
                }
                TraceNote("list unavailable — reusing " + plugins.Count + " remembered plugins for '" + routerKey + "'");
            }
            else
            {
                TraceNote("list: " + plugins.Count + " plugins");
                RememberPluginList(routerKey, cacheDir, plugins);
            }

            string key = string.Join("|", plugins.Select(p => p.Unique)
                                                 .OrderBy(u => u, StringComparer.Ordinal).ToArray());
            lock (SharedCatalogsLock)
            {
                if (SharedCatalogs.TryGetValue(key, out var cached)) return cached;
            }

            var catalog = new WinboxJgCatalog();
            bool complete = true;
            // roteros.jg first: it is served by every router, holds the core windows (interface, ip,
            // routing, system) that the resolver actually needs, and is by far the largest — so it is the
            // one that must be fetched while the channel is freshest.
            //
            // "Freshest" is a hedge, not a budget: measured 2026-07-28 on 7.23.2, one channel took 74 opens
            // / 6.8 MB with no trouble, and the failures that do occur land at no consistent offset (0 B,
            // 0.5 MB, 1.5 MB, 2.7 MB). What a failure does mean is that THIS channel is finished — it never
            // answers again, not even a 26-byte `list`, while a new connection to the same router works
            // immediately. Hence the break below (P2.20).
            foreach (var plugin in plugins.OrderByDescending(p =>
                         string.Equals(p.Name, "roteros.jg", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                string? text = null;
                try { text = ReadCachedOrFetch(ops, plugin, cacheDir); }
                catch (Exception ex) { TraceNote(plugin.Name + " FAILED: " + ex.Message); text = null; }

                if (string.IsNullOrEmpty(text))
                {
                    TraceNote("stopping at " + plugin.Name + " (no content)");
                    // Stop at the first plugin we could not get rather than keep hammering a channel that
                    // may already be gone — the connection itself still has to work over it. What has been
                    // fetched is cached, so the next connection starts from disk and gets further; the
                    // catalog fills in across connections instead of being lost.
                    complete = false;
                    break;
                }
                catalog.TryParseInto(text!); // IsNullOrEmpty above already excluded null/empty
                TraceNote(plugin.Name + ": " + text!.Length + " chars, handlers now " + catalog._byHandler.Count);
            }

            // Only publish a complete catalog for reuse; a partial one must be retried next connection.
            if (complete)
                lock (SharedCatalogsLock) { SharedCatalogs[key] = catalog; }
            return catalog;
        }

        // Plugin bodies are cached content-addressed, at <cacheDir>/plugins/<unique>. The `unique` name
        // carries a version+content stamp, which makes the cache correct across routers by construction:
        // any router advertising that name wants that exact file, an upgrade resolves a new name (so there
        // is nothing to invalidate), and routers sharing a plugin share one copy.
        private static string? ReadCachedOrFetch(WinboxNativeM2Operations ops, PluginEntry plugin,
                                                string? cacheDir)
        {
            string? path = null;
            if (!string.IsNullOrEmpty(cacheDir))
            {
                try
                {
                    path = Path.Combine(Path.Combine(cacheDir, "plugins"), plugin.Unique);
                    if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
                }
                catch { path = null; /* cache read is best-effort */ }
            }

            string? text = FetchJg(ops, plugin.Unique);
            if (string.IsNullOrEmpty(text)) return null;

            if (path != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); // path is a file path from Path.Combine, always has a directory component
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                }
                catch { /* cache write is best-effort */ }
            }
            return text;
        }

        // Tolerant by design — one plugin we cannot parse must not cost the whole catalog. But a silently
        // dropped plugin is a catalog that is quietly missing handlers, which surfaces later as wrong values
        // rather than as an error, so the drop is always traced. Expect this to fire first on a RouterOS
        // version whose .jg syntax has moved (P2.24).
        internal bool TryParseInto(string text)
        {
            try
            {
                object tree = new JgParser(text).Parse();
                Walk(tree, null, new List<string>());
                SettleTitleNames();
                return true;
            }
            catch (Exception ex)
            {
                TraceNote("PARSE FAILED (" + text.Length + " chars): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Resolves the <c>.jg</c>'s "Old X" / "X" field pairs: a declaration named by its own
        /// <c>title</c> elsewhere on the SAME key is the other spelling of that field, not a field of its
        /// own, so it stops claiming the key's reported name.
        /// </summary>
        /// <remarks>
        /// <para>The catalog declares a file-path field twice on one key, guarded by opposite conditions —
        /// <c>{name:'Old Cache Path',title:'Cache Path',on:'oldfileman'}</c> and
        /// <c>{name:'Cache Path',on:'newfileman'}</c> — and first-wins picked the 'Old' one, so
        /// <c>/ip/proxy</c> reported <c>old-cache-path</c> where RouterOS says <c>cache-path</c>.
        /// Twenty-five such pairs in the 7.24 catalog, on paths from <c>/ip/proxy</c> and
        /// <c>/ip/hotspot/profile</c> to <c>/certificate</c> and <c>/system/scheduler</c>.</para>
        /// <para>Both names stay REGISTERED — a caller that resolved <c>old-cache-path</c> before still
        /// reaches the same key — but the loser's <c>ApiName</c> becomes the winner's, which is what
        /// <c>WinboxFieldResolver.BuildKeyToApiName</c> reads to decide whether a registration may claim a
        /// key ("only the spelling this path reports").</para>
        /// <para>The twin has to EXIST for any of this to happen. A title with no same-key namesake — the
        /// <c>addr</c>/'Address Acquisition' and 'NV2 Security'/'Security' declarations, where the title is
        /// a heading rather than the field's other name — is left exactly as it was; acting on those moved
        /// eighteen keys onto names nothing had confirmed.</para>
        /// </remarks>
        private void SettleTitleNames()
        {
            foreach (var handler in _byHandler)
            {
                List<string>? demote = null;
                foreach (var entry in handler.Value)
                {
                    string? title = entry.Value.TitleApiName;
                    if (title == null || string.Equals(title, entry.Key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (handler.Value.TryGetValue(title, out var twin) && twin.Key == entry.Value.Key)
                        (demote ??= new List<string>()).Add(entry.Key);
                }
                if (demote == null) continue;
                foreach (string name in demote)
                    handler.Value[name] = handler.Value[name].WithApiName(
                        handler.Value[name].TitleApiName!);   // non-null: only names collected above
            }
        }

        // Window node types whose path:[…] is a real handler target (vs dropdown references type:'enm').
        private static readonly HashSet<string> WindowTypes =
            new HashSet<string>(StringComparer.Ordinal) { "map", "query", "item", "doit", "action" };

        // ── mproxy .jg fetch (gzip <name>.jg.gz over [2,2] cmd=3) ──────────────

        // `uniqueName` must be the version-stamped name from the mproxy "list" catalog
        // ("roteros-33a7039ff432.jg"), never the stable plugin name. The stable name is a trap: mproxy
        // *opens* "roteros.jg.gz" and even reports the right size, but the subsequent read never answers
        // and takes the whole M2 channel down with it, so every later plugin fails too (P2.18).
        private static string? FetchJg(WinboxNativeM2Operations ops, string uniqueName)
        {
            // The on-disk file in /home/web/webfig/ is "<unique>.gz" (gzip), served by the mproxy static
            // handler via cmd=7 (NOT cmd=3 = /var/pckg, which CHR denies). A refused open is harmless —
            // it is a clean error reply and the channel survives it.
            int handle = MproxyOpen(ops, uniqueName + ".gz", WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) handle = MproxyOpen(ops, uniqueName, WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) return null;

            byte[]? raw = MproxyRead(ops, handle);
            if (raw == null || raw.Length == 0) return null;

            // .jg.gz is gzip; a plain .jg is already text. Detect the gzip magic.
            if (raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B)
                raw = GzipDecompress(raw);
            return Encoding.UTF8.GetString(raw);
        }

        private static int MproxyOpen(WinboxNativeM2Operations ops, string filename, int openCmd)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ops.NextCorrelatedReqIdField(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.Command, (byte)openCmd),  // OpenStatic(7) / OpenVarPkg(3)
                M2Message.StringUser(WinboxM2Protocol.Mproxy.Key.FileName, filename));
            byte[] resp = ops.SendReceiveCorrelated(msg);
            try { return M2Message.ParseSessionId(resp); }
            catch { return -1; }
        }

        private const int MproxyChunk = WinboxM2Protocol.Mproxy.ChunkSize;

        private static byte[]? MproxyRead(WinboxNativeM2Operations ops, int handle)
        {
            var all = new List<byte>();
            for (int guard = 0; guard < 256; guard++)
            {
                byte[] msg = M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                    M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ops.NextCorrelatedReqIdField(),
                    M2Message.SessionIdField(handle),
                    M2Message.U32User(WinboxM2Protocol.Mproxy.Key.MaxChunk, MproxyChunk),
                    M2Message.U8Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mproxy.Read));  // read chunk
                byte[]? resp = ops.SendReceiveCorrelated(msg);
                byte[]? chunk = ExtractMproxyChunk(resp);
                if (chunk == null || chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < MproxyChunk) break;
            }
            return all.Count > 0 ? all.ToArray() : null;
        }

        // Pull the raw/string file-content field (user namespace) out of a read response.
        private static byte[]? ExtractMproxyChunk(byte[]? resp)
        {
            if (resp == null || resp.Length < 2 || resp[0] != 'M' || resp[1] != '2') return resp;
            int pos = 2;
            while (pos + 4 <= resp.Length)
            {
                int ns = resp[pos + 2], type = resp[pos + 3];
                pos += 4;
                if (ns == 0x00 && (type == 0x31 || type == 0x30 || type == 0x21 || type == 0x20))
                {
                    int len = (type == 0x31 || type == 0x21)
                        ? resp[pos++]
                        : (int)BitConverter.ToUInt16(resp, (pos += 2) - 2);
                    if (len > 0 && pos + len <= resp.Length)
                        return resp.Skip(pos).Take(len).ToArray();
                }
                pos += M2Message.SkipTypeBytes(type, resp, pos);
            }
            return null;
        }

        private static byte[] GzipDecompress(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
        }

        // ── Tree walk + field extraction (port of jg_analyze.py walk) ──────────

        // What a `type:'deck'` pane tells us about the fields inside it: which KIND of record they belong to,
        // and how a record says it is of that kind. Carried down the walk so a field several levels inside a
        // pane still knows (a pane's children are wrapped in tabs, groups and opt containers).
        private sealed class PaneContext
        {
            internal readonly string? Kind;     // normalized selector label ("memory", "fq-codel"); null when
                                                // the pane covers several selector values and so has no one kind
            internal readonly int SelectorKey;  // M2 key of the deck's `selon` field
            internal readonly int[] Values;     // the pane's `vals`
            internal PaneContext(string? kind, int selectorKey, int[] values)
            { Kind = kind; SelectorKey = selectorKey; Values = values; }
        }

        private void Walk(object? node, string? handlerKey, List<string> crumb, PaneContext? pane = null,
            string? tab = null)
        {
            if (node is Dictionary<string, object> dict)
            {
                string? owner = handlerKey;
                int[]? handlerInts = null;
                if (dict.TryGetValue("path", out var pv) && pv is List<object> pathList && pathList.Count > 0)
                {
                    var ints = new List<int>();
                    bool ok = true;
                    foreach (var p in pathList)
                    {
                        if (p is int pi) ints.Add(pi);
                        else { ok = false; break; }
                    }
                    if (ok) { handlerInts = ints.ToArray(); owner = HandlerKey(handlerInts); }
                }

                string nodeName = (dict.TryGetValue("name", out var nnv) && nnv is string nns && nns.Length > 0)
                    ? nns
                    : (dict.TryGetValue("title", out var ntv) && ntv is string nts ? nts : "");
                string? groupSeg = (dict.TryGetValue("group", out var gv) && gv is string gs && gs.Length > 0)
                    ? WinboxFieldResolver.NormalizeLabel(gs) : null;
                string? ty = dict.TryGetValue("type", out var tyv) && tyv is string tys ? tys : null;

                // Harvest derived menu-label path → handler for WINDOW nodes (map/query/item/doit/action
                // with a handler path). Dropdown references (type:'enm',values:{type:'dynamic',path:…}) reuse
                // the same handler but are NOT windows — skip them. The key is the full breadcrumb
                // (ancestor menu chain + this node's group + name).
                if (handlerInts != null && ty != null && WindowTypes.Contains(ty))
                {
                    string? apiPath = BuildPath(crumb, groupSeg, nodeName);
                    if (apiPath != null && !_derivedPaths.ContainsKey(apiPath))
                    {
                        _derivedPaths[apiPath] = handlerInts;
                        // Record what THIS window is, not what its handler is — see _singletonPaths.
                        if (ty == "item" || ty == "map" || ty == "query")
                            _singletonPaths[apiPath] = ty == "item";
                    }
                    // Attribute this window's fields to the window as well as to its handler, so a caller
                    // addressing the window is answered by the window's own vocabulary. Windows sharing a
                    // handler number their fields from scratch and collide — see GetWindowFields.
                    if (apiPath != null)
                    {
                        string wk = WindowKey(apiPath);
                        // owner is non-null here: this block requires handlerInts != null, and owner is set
                        // (unconditionally, non-null) to HandlerKey(handlerInts) whenever handlerInts != null.
                        _windowHandlerKey[wk] = owner!;
                        _handlerBackedWindows.Add(wk);
                        owner = wk;
                    }
                    // The row's display name, as the window declares it (see _nameValByHandler).
                    if (dict.TryGetValue("nameval", out var nvv) && nvv is string nvs && nvs.Length > 0
                        && !_nameValByHandler.ContainsKey(HandlerKey(handlerInts)))
                        _nameValByHandler[HandlerKey(handlerInts)] = WinboxFieldResolver.NormalizeLabel(nvs);
                    if (ty == "item") _singletonHandlers.Add(HandlerKey(handlerInts));
                    // Track record-listing vs action-only windows separately so a handler that has BOTH (e.g.
                    // [20,125] = 'SMS Message' map + 'Send SMS' doit) is never mistaken for action-only.
                    if (ty == "doit" || ty == "action") _actionWindowHandlers.Add(HandlerKey(handlerInts));
                    else _recordWindowHandlers.Add(HandlerKey(handlerInts));
                    if (dict.ContainsKey("autorefresh")) _dynamicHandlers.Add(HandlerKey(handlerInts));
                    HarvestMonitor(handlerInts, ty, dict);

                    // The base interface window (generic:'iface') anchors the subtype filtering: remember its
                    // handler and the key of the `type` discriminator field (named by `typeon`, default 'type').
                    if (dict.TryGetValue("generic", out var genv) && genv is string gen && gen.Length > 0)
                    {
                        string typeon = dict.TryGetValue("typeon", out var tov) && tov is string tos ? tos : "type";
                        int tk = FindChildFieldKey(dict, typeon);
                        if (gen == "iface")
                        {
                            _genericIfaceHandler = handlerInts;
                            if (tk != 0) _ifaceTypeKey = tk;
                        }
                        // A base that carries BOTH a handler and a resolvable discriminator can filter its own
                        // children; one that carries neither (the ppp/wlan mid-tree bases) is handled by the
                        // interface-family chain below and must not overwrite an entry with a real key.
                        if (tk != 0 && !_genericBases.ContainsKey(gen))
                            _genericBases[gen] = Tuple.Create(handlerInts, tk);
                    }
                }

                // A window declaring `generic:'X'` is a BASE others extend with `inherit:'X'`. Remember what
                // discriminator it hands its children (`typeon`, default 'type') and whether it belongs to the
                // interface family, so a chain (iface → ppp → 'L2TP Client') resolves. Recorded outside the
                // window block above because these bases often have no `path:` of their own.
                if (dict.TryGetValue("generic", out var genv2) && genv2 is string genName && genName.Length > 0
                    && !_generics.ContainsKey(genName))
                {
                    string typeonChild = dict.TryGetValue("typeon", out var tov2) && tov2 is string tos2 ? tos2 : "type";
                    bool ifaceFamily = genName == "iface"
                        || (dict.TryGetValue("inherit", out var ginh) && ginh is string ginhs
                            && _generics.TryGetValue(ginhs, out var gbase) && gbase.Item2);
                    _generics[genName] = Tuple.Create(typeonChild, ifaceFamily);
                }

                // Interface subtype window: inherit:'<base>' (reuse the generic interface handler, has no path:
                // of its own) + typevalue:N (the discriminator value). Register a derived path → the generic
                // handler, plus a subtype filter (typeKey, N). The leaf is the window TITLE — `name` is the
                // generic 'Interface' shared by every subtype, so it cannot discriminate; the title carries the
                // subtype ('Bridge', 'VLAN', 'L2TP Client', …).
                //
                // Two constraints keep this honest. The base must be in the interface family, so an unrelated
                // inherit tree cannot be filtered against the interface `type` field. And the base must hand its
                // children the 'type' discriminator: the wireless tree switches to `typeon:'hwtype'` below the
                // 'Wireless' window, and those hardware variants (Atheros AR5212, …) are NOT addressable by an
                // interface type filter — registering them would answer with a filter on the wrong field.
                // A wildcard `typevalue:4294967295` (a base standing for "any of my subtypes") is not an int
                // after parsing and is skipped here for the same reason: it is a set, not a value.
                if (handlerInts == null && _genericIfaceHandler != null && _ifaceTypeKey != 0
                    && dict.TryGetValue("inherit", out var inhv) && inhv is string inh
                    && dict.TryGetValue("typevalue", out var tvv) && tvv is int tval
                    && (inh == "iface"
                        || (_generics.TryGetValue(inh, out var baseGen) && baseGen.Item2 && baseGen.Item1 == "type")))
                {
                    string title = dict.TryGetValue("title", out var ttv) && ttv is string tts && tts.Length > 0
                        ? tts : nodeName;
                    string? apiPath = BuildPath(crumb, groupSeg, title);
                    if (apiPath != null)
                    {
                        if (!_derivedPaths.ContainsKey(apiPath))
                        {
                            _derivedPaths[apiPath] = _genericIfaceHandler;
                            _subtypeFilters[apiPath] = Tuple.Create(_ifaceTypeKey, tval);
                            _singletonPaths[apiPath] = false;   // a subtype window is always a record list
                        }
                        // Attribute this window's fields to the WINDOW, not to the shared [20,0] handler.
                        // The subtypes disagree about keys under identical labels — 'Remote Address' is
                        // 0x7E1 on EoIP, 0x7E5 on GRE and 0x7E3 on IPIP, 'Local Address' is a u32 on EoIP
                        // and an addr on VXLAN — so merging them into one per-handler map would hand every
                        // subtype but the first-parsed one a wrong key: a write that lands on another field.
                        // (Before this, these fields were attributed to the enclosing menu node, i.e. to no
                        // handler at all, and were simply lost — every subtype `add` failed with "cannot
                        // resolve API field 'remote-address'".)
                        owner = WindowKey(apiPath);
                        // Known to read the generic interface handler, but deliberately NOT handler-backed:
                        // see _handlerBackedWindows.
                        _windowHandlerKey[owner] = HandlerKey(_genericIfaceHandler);
                    }
                }

                // The same registration for a NON-interface family whose base owns its handler and its
                // discriminator (_genericBases). The interface branch above is left exactly as it is: its
                // guards were written against a tree that is several levels deep and switches discriminator
                // half-way down, and nothing here needs to disturb them.
                //
                // /ip/route is the case. WinBox has one routes table ([44,21], 'All Routes', typeon:'rtype')
                // and two subtype windows over it — 'Routes' (typevalue 2, IPv4) and ipv6.jg's 'Routes6'
                // (typevalue 3). Addressing the BASE returns both families: on the lab CHR a /ip/route read
                // came back with six rows where the API returns two, the extras being ::1/128 and fe80::/64.
                // roteros.jg is parsed first by design, so the base is registered before ipv6.jg inherits it.
                if (handlerInts == null
                    && dict.TryGetValue("inherit", out var ginhv) && ginhv is string ginh2
                    && dict.TryGetValue("typevalue", out var gtvv) && gtvv is int gtval
                    && !(ginh2 == "iface" || (_generics.TryGetValue(ginh2, out var gfam) && gfam.Item2))
                    && _genericBases.TryGetValue(ginh2, out var gbase2))
                {
                    string gtitle = dict.TryGetValue("title", out var gttv) && gttv is string gtts && gtts.Length > 0
                        ? gtts : nodeName;
                    string? gApiPath = BuildPath(crumb, groupSeg, gtitle);
                    if (gApiPath != null)
                    {
                        if (!_derivedPaths.ContainsKey(gApiPath))
                        {
                            _derivedPaths[gApiPath] = gbase2.Item1;
                            _subtypeFilters[gApiPath] = Tuple.Create(gbase2.Item2, gtval);
                            _singletonPaths[gApiPath] = false;   // a subtype window is always a record list
                        }
                        // Its fields belong to the WINDOW: the subtype declares the whole General/Status tab
                        // the base only sketches (Distance, Scope, Target Scope, VRF Interface, Routing
                        // Table, Immediate Gateway, Pref. Source), and the base's bare numeric 'active' is
                        // the subtype's 'Contribution' enum.
                        owner = WindowKey(gApiPath);
                        _windowHandlerKey[owner] = HandlerKey(gbase2.Item1);
                    }
                }

                // A named opt/not wrapper (type:'opt'/'not' with children) holds its real value in an inner leaf;
                // the wrapper's own id is just a flag bool (opt=present, not=negated). Map the API name to the
                // inner value leaf, carrying the flag keys — e.g. firewall 'Connection State' (opt→not→set) maps
                // to the inner bitmask, not the outer bool. (A childless opt is a plain bool — handled below.)
                if (owner != null && (ty == "opt" || ty == "not")
                    && dict.ContainsKey("c") && !string.IsNullOrEmpty(nodeName))
                {
                    AddOptionField(owner, nodeName, dict, pane);
                }
                // A named `union` holds one value that can be of several families, each child carrying its
                // OWN key — IPsec 'Address' is {union, c:[network u1, network6 a17, string s2f]}. The union
                // node itself has no id, and its children have no name, so without this the field is not in
                // the catalog at all: every dual-stack address field (IPsec peer/policy/identity, graphing
                // 'Allow Address', traffic-flow 'Src./Dst. Address', …) answered "cannot resolve API field".
                // The first child is registered, i.e. the IPv4 family — an IPv6 value then fails to encode
                // with the codec's own error rather than being sent on the wrong key.
                else if (owner != null && ty == "union" && !dict.ContainsKey("id")
                         && !string.IsNullOrEmpty(nodeName))
                {
                    AddUnionField(owner, nodeName, dict, pane);
                }
                // A named `tuple` is the same problem with a different answer: the label is on the node, the
                // ids are on the children, and the API prints the children joined by the node's `sep`.
                else if (owner != null && ty == "tuple" && !dict.ContainsKey("id")
                         && !string.IsNullOrEmpty(nodeName))
                {
                    AddTupleField(owner, nodeName, dict, pane);
                }
                else if (owner != null && dict.TryGetValue("id", out var idv) && idv is string idStr)
                {
                    var dec = DecodeId(idStr);
                    if (dec != null)
                    {
                        bool ro = dict.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                        int maskKey = (dict.TryGetValue("maskid", out var mkv) && mkv is string mks
                            && DecodeId(mks) is var md && md != null) ? md.Value.key : 0;
                        int[]? refHandler = ExtractRefHandler(dict);
                        bool isRange = dict.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                        string? allow = dict.TryGetValue("allow", out var alv) ? alv as string : null;
                        // A list field carries its own present-flag on the node (`optid`) rather than in an
                        // enclosing opt wrapper, and webfig writes it from the list's LENGTH
                        // (types.multi.put: obj[optid] = val.length>0). Same meaning as OptKey, same
                        // encoder — without it the 21 firewall/bridge match lists (dst-port, protocol,
                        // vlan-id, …) reach the router with the option down and are ignored.
                        int optKey = DecodedKeyOf(dict, "optid");
                        // The negated half of a multitristatearray (see WinboxJgField.OffKey).
                        int offKey = DecodedKeyOf(dict, "oid");
                        AddField(owner, nodeName, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(dict),
                            ty, maskKey, refHandler, optKey: optKey, isRange: isRange, allow: allow,
                            def: ExtractDef(dict), pane: pane, offKey: offKey,
                            isOptional: IsOptionalAttr(dict), elementUiType: ElementUiTypeOf(dict),
                            scale: ScaleOf(dict), elementParts: ElementPartsOf(dict),
                            postfix: PostfixOf(dict), elementSeparator: ElementSeparatorOf(dict),
                            elementNotKey: ElementNotKeyOf(dict), elementIsRange: ElementIsRangeOf(dict),
                            tab: tab, title: TitleOf(dict),
                            nonPublic: dict.TryGetValue("nonpublic", out var npv) && npv is int npi && npi != 0,
                            min: dict.TryGetValue("min", out var mnv) && mnv is int mni ? mni : 0,
                            radix: RadixOf(dict));
                    }
                }

                // Per-record action verb: a named doit/action carrying a cmd (no own field id) invokes that
                // SYS_CMD on the owning handler against a record .id — e.g. the scripts window's
                // {name:'Run Script',type:'doit',cmd:1} on [48,101]. Register it for /system/script/run dispatch.
                if (owner != null && (ty == "doit" || ty == "action") && !dict.ContainsKey("id")
                    && dict.TryGetValue("cmd", out var cmdv) && cmdv is int cmdN && !string.IsNullOrEmpty(nodeName))
                {
                    // Actions are looked up BY HANDLER (GetActionFields/GetHandlerActions), so an action
                    // declared inside a window is still keyed by the handler that window reads.
                    // owner is non-null here (checked above), so HandlerOfOwner always returns non-null too.
                    string actionOwner = HandlerOfOwner(owner)!;
                    string? actionLabel = AddAction(actionOwner, nodeName, cmdN);
                    // Descend into the action's ARGUMENTS under a key of their own. They keep going into the
                    // handler's map as well (AddField writes both, so nothing that resolved before stops
                    // resolving), but the action's own map is what the caller invoking it resolves against —
                    // otherwise an argument sharing a label with a record column loses to it. See GetActionFields.
                    if (actionLabel != null) owner = ActionKey(actionOwner, actionLabel);
                }

                // Descend. A structural menu node (has children, no field id, has a name) extends the
                // breadcrumb so its descendant windows derive a nested path.
                List<string> childCrumb = crumb;
                if (dict.ContainsKey("c") && !dict.ContainsKey("id") && !string.IsNullOrEmpty(nodeName))
                {
                    childCrumb = new List<string>(crumb);
                    if (groupSeg != null) childCrumb.Add(groupSeg);
                    string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
                    if (leaf.Length > 0) childCrumb.Add(leaf);
                }

                // A deck's panes are walked here, each with its own pane context, so their fields carry the
                // kind they belong to. A window of its own inside a pane starts fresh (handlerInts != null).
                if (ty == "deck" && handlerInts == null)
                {
                    WalkDeck(dict, owner, childCrumb);
                    return;
                }

                foreach (var kv in dict)
                {
                    if (kv.Key == "id") continue;
                    Walk(kv.Value, owner, childCrumb, handlerInts != null ? null : pane,
                        handlerInts != null ? null : tab);
                }
            }
            else if (node is List<object> list)
            {
                // A tab is a SIBLING of the fields it heads, not their parent, so the current tab is carried
                // along the list rather than down a subtree. It is what tells apart two fields a window gives
                // the same label - and what the API prefixes the second one with (see AddField).
                string? currentTab = tab;
                foreach (var it in list)
                {
                    if (it is Dictionary<string, object> td
                        && td.TryGetValue("type", out var ttv) && (ttv as string) == "tab"
                        && td.TryGetValue("name", out var tnv) && tnv is string tn)
                        currentTab = WinboxFieldResolver.NormalizeLabel(tn);
                    Walk(it, handlerKey, crumb, pane, currentTab);
                }
            }
        }

        /// <summary>
        /// Walks a <c>type:'deck'</c> node: resolves the selector field named by <c>selon</c> (its M2 key and
        /// its value labels) among the fields already harvested for this handler, then walks each pane with a
        /// <see cref="PaneContext"/> naming the kind it belongs to.
        /// </summary>
        /// <remarks>
        /// The selector is looked up in the handler's own field map rather than by re-reading the .jg, so it
        /// works wherever the deck sits relative to its selector — in every window seen on 7.23.2 the selector
        /// ('Type', 'Kind') is declared before the deck. A deck whose selector or labels cannot be resolved is
        /// walked as an ordinary node: its fields keep the plain names they have always had.
        /// </remarks>
        private void WalkDeck(Dictionary<string, object> deck, string? owner, List<string> crumb)
        {
            int selectorKey = 0;
            IReadOnlyDictionary<int, string>? kinds = null;
            if (deck.TryGetValue("selon", out var sv) && sv is string selon
                && owner != null && _byHandler.TryGetValue(owner, out var known)
                && known.TryGetValue(WinboxFieldResolver.NormalizeLabel(selon), out var selField))
            {
                selectorKey = selField.Key;
                kinds = selField.EnumMap;
            }

            if (deck.TryGetValue("panes", out var pv2) && pv2 is List<object> panes)
                foreach (var p in panes)
                {
                    if (!(p is Dictionary<string, object> pd)) continue;
                    var vals = new List<int>();
                    if (pd.TryGetValue("vals", out var vv) && vv is List<object> vl)
                        foreach (var v in vl) if (v is int vi) vals.Add(vi);

                    // One kind per pane — but only a pane shown for a SINGLE selector value has one to be
                    // named after. A pane covering several (IPsec identity's 'My ID' is shown for fqdn, user
                    // fqdn AND key id) is walked without a kind: its fields keep their plain names, and only
                    // the "which kind is this record" filter applies to them. That filter needs the selector
                    // and the values, not the label, so it is deliberately not conditioned on the kind.
                    string? kind = null;
                    if (kinds != null && vals.Count == 1 && kinds.TryGetValue(vals[0], out var label))
                        kind = label;

                    var ctx = (selectorKey != 0 && vals.Count > 0)
                        ? new PaneContext(kind, selectorKey, vals.ToArray())
                        : null;
                    if (pd.TryGetValue("c", out var pc)) Walk(pc, owner, crumb, ctx);
                }
        }

        // Build a derived menu-label path "/ip/firewall/connection" from the breadcrumb + this window's
        // group segment + node name, each normalized (lower, spaces→'-'). Returns null for an empty leaf.
        private static string? BuildPath(List<string> crumb, string? groupSeg, string nodeName)
        {
            string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
            if (string.IsNullOrEmpty(leaf)) return null;
            var parts = new List<string>(crumb);
            if (groupSeg != null) parts.Add(groupSeg);
            parts.Add(leaf);
            return "/" + string.Join("/", parts);
        }

        private void AddField(string handlerKey, string label, int key, string wireType, bool ro,
            IReadOnlyDictionary<int, string>? enumMap, string? uiType, int maskKey, int[]? refHandler,
            int optKey = 0, int notKey = 0, bool isRange = false, string? allow = null, long? def = null,
            PaneContext? pane = null, int offKey = 0, bool isOptional = false, string? elementUiType = null,
            int scale = 1, IReadOnlyList<WinboxJgElementPart>? elementParts = null, string? postfix = null,
            string? elementSeparator = null, int elementNotKey = 0, bool elementIsRange = false,
            string? tab = null, string? title = null,
            IReadOnlyList<WinboxJgField>? extraRegistrations = null, bool nonPublic = false,
            long min = 0, int radix = 0, string? prefix = null)
        {
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            // A declaration's own `title` is carried on the field (see WinboxJgField.TitleApiName) and acted
            // on in SettleTitleNames, once the whole handler is known: it only decides anything when a
            // SECOND declaration on the same key is named by that title, and at this point that one may not
            // have been read yet. Never for a deck-pane leaf — there the API name comes from the pane KIND
            // (bfifo-limit, pcq-rate) and four panes whose boxes all read 'Queue Size' would collapse.
            string? titleName = pane == null && title != null
                ? WinboxFieldResolver.NormalizeLabel(title) : null;
            if (string.IsNullOrEmpty(titleName) || titleName == apiName) titleName = null;
            // A member whose .jg label is a caption rather than the router's own token is renamed here, once,
            // so every reader and the write side see RouterOS's vocabulary (see AliasEnumMembers).
            enumMap = WinboxFieldResolver.AliasEnumMembers(apiName, enumMap);
            var field = new WinboxJgField(apiName, key, wireType, ro, enumMap, uiType, maskKey,
                refHandler, optKey, notKey, isRange, allow, def,
                pane?.Kind, pane?.SelectorKey ?? 0, pane?.Values, offKey, isOptional, elementUiType, scale,
                elementParts, postfix, elementSeparator, elementNotKey: elementNotKey,
                elementIsRange: elementIsRange, titleApiName: titleName,
                extraRegistrations: extraRegistrations, nonPublic: nonPublic, min: min, radix: radix,
                prefix: prefix);
            // Two fields of one window may carry the same label - the packet sniffer's streaming 'Port' (a
            // number) and its filter 'Port' (a list of port matches) - and first-wins kept only the first,
            // leaving the second reachable under no name at all. The TAB it sits under is what tells them
            // apart, and is what RouterOS prefixes the second with: 'filter-port'. Registered only for the
            // loser of a collision, so every name that resolved before keeps resolving to the same key.
            // An incumbent WinBox never paints is not a collision — the visible field takes the plain name
            // off it in Put below, so qualifying it here would rename the wrong one of the two.
            if (tab != null && _byHandler.TryGetValue(handlerKey, out var taken)
                && taken.TryGetValue(apiName, out var incumbent)
                && !(incumbent.NonPublic && !nonPublic))
            {
                string qualified = WinboxFieldResolver.PrefixWithKind(tab, apiName);
                if (qualified != apiName) field = field.WithApiName(qualified);
                apiName = qualified;
            }
            Put(handlerKey, apiName, field);
            // A pane field is ALSO filed under the kind-prefixed name the API uses for it (memory-lines,
            // pcq-rate). Both registrations are needed: the plain one keeps every name that resolved before
            // resolving, and the prefixed one is the only way to reach a field whose label another pane
            // already claimed — 'Stop on Full' is memory's b4 AND disk's b6, and first-wins kept only b4, so
            // disk-stop-on-full could not be written at all.
            if (pane != null)
            {
                string prefixed = WinboxFieldResolver.PrefixWithKind(pane.Kind, apiName);
                if (prefixed != apiName) Put(handlerKey, prefixed, field);
            }
            // An action's arguments belong to the action AND, as before, to the handler that owns it — a
            // handler whose only window is an action (Wake on LAN) has no other map to be found in. Written
            // second, so the handler map keeps exactly the first-wins content it had before actions were
            // scoped: the record window's column still wins there.
            if (IsActionKey(handlerKey)) Put(HandlerOfActionKey(handlerKey), apiName, field);
            // The same rule one level out: an ordinary window's field is also its handler's, written second so
            // the per-handler map keeps byte-for-byte the first-wins content it had before windows were scoped.
            // (Interface subtype windows are excluded — they are not in _handlerBackedWindows.)
            else if (IsWindowKey(handlerKey) && _handlerBackedWindows.Contains(handlerKey)
                     && _windowHandlerKey.TryGetValue(handlerKey, out var ownerHandler))
            {
                Put(ownerHandler, apiName, field);
                if (pane != null)
                {
                    string prefixedH = WinboxFieldResolver.PrefixWithKind(pane.Kind, apiName);
                    if (prefixedH != apiName) Put(ownerHandler, prefixedH, field);
                }
            }
        }

        // The .jg 'opt:1' ATTRIBUTE — the field may legitimately have no value. Not to be confused with a
        // type:'opt' WRAPPER, which is a node of its own carrying a present-flag bool (see OptKey).
        private static bool IsOptionalAttr(Dictionary<string, object> dict)
            => dict.TryGetValue("opt", out var ov) && ov is int oi && oi != 0;

        // The .jg `scale` of an interval field: how many wire units make one second.
        private static int ScaleOf(Dictionary<string, object> dict)
            => dict.TryGetValue("scale", out var sv) && sv is int si && si > 0 ? si : 1;

        // The .jg `postfix` — the unit a numeric field's value is expressed in (see WinboxJgField.Postfix).
        private static string? PostfixOf(Dictionary<string, object> dict)
            => dict.TryGetValue("postfix", out var pv) ? pv as string : null;

        // The `type` of a list field's unnamed element child — what webfig renders each element through.
        private static string? ElementUiTypeOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("type", out var tv) ? tv as string : null;
        }

        /// <summary>
        /// The element child of a list field, with a <c>not</c> WRAPPER unwrapped: an element declared
        /// <c>{type:'not',id:'b1',c:[…]}</c> carries a negation flag at its own id and the real value inside,
        /// exactly as <c>types.not</c> does for a scalar. <paramref name="notKey"/> receives the flag's M2
        /// key, or 0 when the element is not wrapped.
        /// </summary>
        /// <remarks>
        /// The wrapper is NOT the value, which is why it is unwrapped here rather than treated as a leaf:
        /// its own id is a bool, and writing a caller's number into it would set the negation flag and drop
        /// the value (see <see cref="SingleLeafElementTypes"/>).
        /// </remarks>
        private static Dictionary<string, object>? ElementChild(Dictionary<string, object> dict, out int notKey)
        {
            notKey = 0;
            var child = FirstChildDict(dict);
            for (int guard = 0; guard < 4 && child != null; guard++)
            {
                if (!(child.TryGetValue("type", out var tv) && (tv as string) == "not")) return child;
                int k = DecodedKeyOf(child, "id");
                var inner = FirstChildDict(child);
                if (inner == null) return child;   // a childless 'not' is a plain flag, not a wrapper
                notKey = k;
                child = inner;
            }
            return child;
        }

        // The negation-flag key of a `not`-wrapped list element (see ElementChild), or 0.
        private static int ElementNotKeyOf(Dictionary<string, object> dict)
        {
            ElementChild(dict, out int notKey);
            return notKey;
        }

        // The element's own `range:1` — for a network element it says the SIBLING is the range END rather
        // than a netmask, exactly as it does on a scalar (types.network.tostr reads attrs.range).
        private static bool ElementIsRangeOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("range", out var rv) && rv is int ri && ri != 0;
        }

        /// <summary>
        /// The parts of a list element that is a <c>type:'union'</c> or a <c>type:'tuple'</c>, in <c>.jg</c>
        /// order. <c>null</c> when the element is neither (a list of plain scalars is described by
        /// <see cref="WinboxJgField.ElementUiType"/> alone).
        /// </summary>
        /// <remarks>
        /// A NAMED union field takes its FIRST usable member (<c>AddUnionField</c>): tik4net has one key per
        /// API field name, and the API answers with the IPv4 family. Inside a LIST that shortcut is not
        /// available — every element chooses its own member, and <c>/snmp/community</c>'s single row is
        /// IPv6 — so every alternative is carried and the element picks among them at decode time.
        /// </remarks>
        private static IReadOnlyList<WinboxJgElementPart>? ElementPartsOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            string? childType = child != null && child.TryGetValue("type", out var tv) ? tv as string : null;
            if (childType == "tuple")
            {
                // child is non-null here: childType can only be "tuple" via child's own TryGetValue above.
                if (!(child!.TryGetValue("c", out var cv) && cv is List<object> parts)) return null;
                var result = new List<WinboxJgElementPart>();
                foreach (var p in parts)
                    if (p is Dictionary<string, object> pd && PartOf(pd) is WinboxJgElementPart part)
                        result.Add(part);
                return result.Count > 0 ? result : null;
            }
            if (childType == "union")
            {
                // child is non-null here, for the same reason as the "tuple" branch above.
                var part = PartOf(child!);
                return part != null ? new List<WinboxJgElementPart> { part } : null;
            }
            // A list whose element is ONE addressable leaf — {type:'multi',id:'M18',c:[{type:'enm',id:'u19',
            // values:{…}}]}. The element is still a submessage, with the value at the leaf's own key, so it
            // is described by exactly the same one-part list a single-member tuple would be. Without this it
            // had no parts at all and the whole element fell back to a generic dump of the nested message:
            // /snmp's trap-interfaces read as "2" where the API prints "ether1".
            //
            // 'addr' is deliberately excluded: it is a compound of its own with its own formatter and its
            // own allow-mask rules (see FormatAddr / EncodeAddr).
            if (child != null && childType != null && SingleLeafElementTypes.Contains(childType))
            {
                var leaf = PartOf(child);
                return leaf != null ? new List<WinboxJgElementPart> { leaf } : null;
            }
            return null;
        }

        // The element types that ARE one value at one key, so a list of them is described by a single part.
        // A whitelist, not an exclusion list: a wrapper such as {type:'not',id:'b6',c:[…]} also carries an
        // id, and treating it as the value would write the caller's number into the negation FLAG and drop
        // the value entirely. 'addr' is excluded for the opposite reason — it is a compound with its own
        // encoder and its own allow-mask rules.
        private static readonly HashSet<string> SingleLeafElementTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ipaddr", "ip6addr", "macaddr", "macnetwork", "network", "network6",
            "number", "enm", "interval", "string", "secret", "bool",
        };

        // One part of a tuple/union element: a union node becomes a part carrying its alternatives, anything
        // with an id becomes a value leaf, anything else is not addressable and is dropped.
        private static WinboxJgElementPart? PartOf(Dictionary<string, object> node)
        {
            string? ty = node.TryGetValue("type", out var tv) ? tv as string : null;
            if (ty == "union")
            {
                if (!(node.TryGetValue("c", out var cv) && cv is List<object> members)) return null;
                var alts = new List<WinboxJgElementPart>();
                foreach (var m in members)
                    if (m is Dictionary<string, object> md && PartOf(md) is WinboxJgElementPart leaf)
                        alts.Add(leaf);
                return alts.Count > 0 ? new WinboxJgElementPart(0, "union", 0, alts) : null;
            }
            if (!(node.TryGetValue("id", out var idv) && idv is string ids)) return null;
            var dec = DecodeId(ids);
            if (dec == null) return null;
            // ty! : WinboxJgElementPart.UiType is declared non-nullable; a genuinely missing 'type' here
            // was already passed through as null pre-nullable (unchanged runtime behaviour).
            return new WinboxJgElementPart(dec.Value.key, ty!, DecodedKeyOf(node, "maskid"),
                enumMap: ExtractEnumMap(node), refHandler: ExtractRefHandler(node),
                radix: RadixOf(node));
        }

        // webfig's types.number.tostr is `val.toString(attrs.radix||10)`: a declaration saying 16 is saying
        // the number is written in hexadecimal. 0 here means "declares none", i.e. decimal.
        private static int RadixOf(Dictionary<string, object> dict)
            => dict.TryGetValue("radix", out var rv) && rv is int ri ? ri : 0;

        // The `sep` of a tuple element (webfig's types.tuple.tostr default is '/'), or null when the element
        // is not a tuple.
        private static string? ElementSeparatorOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            if (child == null || !(child.TryGetValue("type", out var tv) && (tv as string) == "tuple"))
                return null;
            return child.TryGetValue("sep", out var sv) && sv is string s ? s : "/";
        }

        // The M2 key held by a sibling-key attribute of a field node ('maskid', 'optid', 'oid'), or 0.
        private static int DecodedKeyOf(Dictionary<string, object> node, string attribute)
            => node.TryGetValue(attribute, out var v) && v is string s && DecodeId(s) is var d && d != null
                ? d.Value.key : 0;

        private void Put(string mapKey, string apiName, WinboxJgField field)
        {
            if (!_byHandler.TryGetValue(mapKey, out var map))
            {
                map = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                _byHandler[mapKey] = map;
            }
            // first label wins for a given apiName; do not let later, less-specific windows clobber it.
            if (!map.ContainsKey(apiName)) { map[apiName] = field; return; }
            // …with one exception: a field WinBox actually paints displaces one it never does. A
            // `nonpublic:1` declaration is an internal slot (see WinboxJgField.NonPublic), and where it is
            // spelled like a visible field, the visible one is what the API is naming. /file declares
            // {name:'type',id:'u3',nonpublic:1} BEFORE {name:'Type',id:'s7'}, so first-wins alone kept the
            // numeric file kind and dropped the text: the path reported type=5 for type=directory, and the
            // string the router sends on every row reached no name at all.
            if (map[apiName].NonPublic && !field.NonPublic) map[apiName] = field;
        }

        // Resolves a named opt/not wrapper (e.g. firewall 'Connection State': opt→not→set) to its inner value
        // leaf and registers the API name against that leaf, carrying the opt/not flag-bool keys. Drills first
        // dict child through nested opt/not layers; the first non-wrapper node is the value leaf (its own id is
        // the value key, uiType the control type — 'set' for a bitmask). If no inner leaf is found, the wrapper
        // itself is the value (a bare bool option) — register its own key as a bool.
        private void AddOptionField(string handlerKey, string label, Dictionary<string, object> wrapper,
            PaneContext? pane = null)
        {
            int optKey = 0, notKey = 0;
            var cur = wrapper;
            for (int guard = 0; guard < 8 && cur != null; guard++)
            {
                string? ty = cur.TryGetValue("type", out var tv) && tv is string ts ? ts : null;
                var dec = (cur.TryGetValue("id", out var iv) && iv is string ids) ? DecodeId(ids) : null;

                if (ty == "opt" || ty == "not")
                {
                    if (dec != null) { if (ty == "opt") optKey = dec.Value.key; else notKey = dec.Value.key; }
                    var child = FirstChildDict(cur);
                    if (child == null)
                    {
                        // wrapper with no inner control: it is a plain bool option (e.g. opt around a checkbox).
                        if (dec != null)
                            AddField(handlerKey, label, dec.Value.key, "bool", false, null, "bool", 0, null,
                                pane: pane);
                        return;
                    }
                    cur = child;
                    continue;
                }

                // value leaf
                if (dec != null)
                {
                    bool ro = cur.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                    int maskKey = (cur.TryGetValue("maskid", out var mkv) && mkv is string mks
                        && DecodeId(mks) is var md && md != null) ? md.Value.key : 0;
                    int[]? refHandler = ExtractRefHandler(cur);
                    // 'range' must be read here as well as on the unwrapped path: EVERY firewall address field
                    // is an opt→not→network with range:1, so dropping it here made the range-END sibling decode
                    // as a netmask (see P2.33 / Docs/winbox-native-m2-protocol.md §24).
                    bool isRange = cur.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                    string? allow = cur.TryGetValue("allow", out var alv) ? alv as string : null;
                    // The wrapper's own opt flag wins; a leaf 'optid' fills in when there is no wrapper flag.
                    if (optKey == 0) optKey = DecodedKeyOf(cur, "optid");
                    AddField(handlerKey, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(cur),
                        ty, maskKey, refHandler, optKey, notKey, isRange, allow, ExtractDef(cur), pane,
                        DecodedKeyOf(cur, "oid"), IsOptionalAttr(cur), ElementUiTypeOf(cur), ScaleOf(cur),
                        ElementPartsOf(cur), PostfixOf(cur), ElementSeparatorOf(cur),
                        ElementNotKeyOf(cur), ElementIsRangeOf(cur),
                        // A wrapped leaf carries its own base and its own minimum, and both decide how the
                        // API spells the value: the bridge's priority is a wrapped {radix:16,postfix:'hex'}
                        // and reads 0x1000 where the raw number is 4096.
                        radix: RadixOf(cur),
                        min: cur.TryGetValue("min", out var omnv) && omnv is int omni ? omni : 0);
                }
                return;
            }
        }

        /// <summary>
        /// Registers a named <c>type:'union'</c> field from its FIRST child that carries a usable id. A union
        /// is one logical value with a per-family key (IPv4 <c>network</c>, IPv6 <c>network6</c>, a free-form
        /// <c>string</c>); WinBox picks the member by what the user typed. tik4net has one key per API field
        /// name, so it takes the first member — the IPv4 family in every union seen on 7.23.2 — which is the
        /// one RouterOS's own API answers with for an IPv4 value.
        /// </summary>
        /// <remarks>
        /// The alternative was to keep dropping the field, which is what happened before: the union node has
        /// no <c>id</c> and its children have no <c>name</c>, so nothing was registered and the field did not
        /// exist as far as the resolver was concerned. An IPv6 value now fails in the codec (a reported
        /// encode error naming the value) instead of being unnameable — never silently written to the IPv4
        /// key, because the codec parses the value against the registered wire type first.
        /// </remarks>
        private void AddUnionField(string handlerKey, string label, Dictionary<string, object> union,
            PaneContext? pane = null)
        {
            var child = FirstChildDict(union);
            if (child == null) return;
            if (!(child.TryGetValue("id", out var idv) && idv is string ids)) return;
            var dec = DecodeId(ids);
            if (dec == null) return;

            // ro / opt live on the union node, the typed attributes on the member.
            bool ro = (union.TryGetValue("ro", out var urov) && urov is int uri && uri != 0)
                   || (child.TryGetValue("ro", out var rov) && rov is int rin && rin != 0);
            int maskKey = (child.TryGetValue("maskid", out var mkv) && mkv is string mks
                && DecodeId(mks) is var md && md != null) ? md.Value.key : 0;
            bool isRange = child.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
            string? allow = child.TryGetValue("allow", out var alv) ? alv as string : null;
            string? uiType = child.TryGetValue("type", out var tv) && tv is string ts ? ts : null;

            // opt/scale/element type are read from the union node as well as the member: the union node is
            // where 'Watch Address' carries its opt:1, and dropping it left the field looking mandatory, so
            // the "not set" rules could not fire on it.
            AddField(handlerKey, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(child),
                uiType, maskKey, ExtractRefHandler(child), isRange: isRange, allow: allow,
                def: ExtractDef(child), pane: pane,
                isOptional: IsOptionalAttr(union) || IsOptionalAttr(child),
                elementUiType: ElementUiTypeOf(child), scale: ScaleOf(child),
                elementParts: ElementPartsOf(child), postfix: PostfixOf(child),
                elementSeparator: ElementSeparatorOf(child),
                // The base and the minimum are the member's too. /interface/bridge's 'Priority' is a union
                // of a {radix:16,postfix:'hex'} number and an enm whose members are spelled '0x1000' — two
                // renderings of one key, and the API uses the first one's.
                radix: RadixOf(child),
                min: child.TryGetValue("min", out var cmnv) && cmnv is int cmni ? cmni : 0,
                // …and the OTHER families answer to the same name at their own keys. The router sends one of
                // them, not the first one: /snmp's 'Src. Address' is {ipaddr u1b, ip6addr a1c} and the row
                // carries 0x1C, so registering only the IPv4 member left the field unreported on every row
                // that had a v6 value. The write side is unchanged — the name still resolves to the first
                // member, so an IPv6 value still fails to encode with the codec's own error.
                extraRegistrations: UnionAlternatives(label, union, ro, pane));
        }

        /// <summary>
        /// Every member of a <c>union</c> after the first, as a field of its own — same API name, its own
        /// key, wire type, <c>maskid</c> and enum/reference metadata.
        /// </summary>
        private static IReadOnlyList<WinboxJgField>? UnionAlternatives(string label,
            Dictionary<string, object> union, bool unionReadOnly, PaneContext? pane)
        {
            if (!(union.TryGetValue("c", out var cv) && cv is List<object> members)) return null;
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return null;

            var result = new List<WinboxJgField>();
            bool first = true;
            foreach (var m in members)
            {
                if (!(m is Dictionary<string, object> md)) continue;
                if (!(md.TryGetValue("id", out var idv) && idv is string ids)) continue;
                var dec = DecodeId(ids);
                if (dec == null) continue;
                if (first) { first = false; continue; }     // the primary, registered by AddUnionField

                bool ro = unionReadOnly
                       || (md.TryGetValue("ro", out var rov) && rov is int rin && rin != 0);
                result.Add(new WinboxJgField(apiName, dec.Value.key, dec.Value.type, ro,
                    ExtractEnumMap(md), md.TryGetValue("type", out var tv) ? tv as string : null,
                    DecodedKeyOf(md, "maskid"), ExtractRefHandler(md),
                    def: ExtractDef(md),
                    paneKind: pane?.Kind, paneSelectorKey: pane?.SelectorKey ?? 0, paneValues: pane?.Values,
                    isOptional: IsOptionalAttr(union) || IsOptionalAttr(md),
                    scale: ScaleOf(md)));
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// A named scalar <c>tuple</c> — one field the API prints as its children joined by <c>sep</c>.
        /// </summary>
        /// <remarks>
        /// Like a union the node has no <c>id</c> of its own and the children have no names, so without this
        /// the field is not in the catalog at all. <c>/ip/service</c>'s
        /// <c>{name:'Remote',type:'tuple',sep:':',c:[{ip6addr ad},{number ue}]}</c> is one, and the API
        /// prints <c>192.168.4.31:65504</c> where the record carries <c>0xD</c> and <c>0xE</c>.
        /// <para>Every part key registers the same compound field, so the row renders the whole value
        /// whichever part the decode reaches first — and the parts, which have no label of their own, do
        /// not surface as fields the API never reports.</para>
        /// </remarks>
        private void AddTupleField(string handlerKey, string label, Dictionary<string, object> tuple,
            PaneContext? pane = null)
        {
            if (!(tuple.TryGetValue("c", out var cv) && cv is List<object> children)) return;
            // `separate:1` is webfig saying the parts are shown as boxes of their OWN, and such a tuple's
            // children carry their own names — /queue/simple's 'Max Limit' is
            // {tuple,separate:1,c:[{name:'Upload Max Limit',…},{name:'Download Max Limit',…}]}. Registering
            // the parent there would claim the children's keys first and take two named fields away to put
            // one joined value under a label RouterOS does not use. A child with a name of its own is a
            // field in its own right whatever the tuple says, so both tests are applied.
            if (tuple.TryGetValue("separate", out var sepv) && sepv is int sepi && sepi != 0) return;
            foreach (var c0 in children)
                if (c0 is Dictionary<string, object> cd0 && cd0.TryGetValue("name", out var cn)
                    && cn is string cns && cns.Length > 0)
                    return;

            var parts = new List<WinboxJgElementPart>();
            var keys = new List<Tuple<int, string>>();
            foreach (var c in children)
            {
                if (!(c is Dictionary<string, object> cd)) continue;
                if (PartOf(cd) is WinboxJgElementPart part) parts.Add(part);
                if (cd.TryGetValue("id", out var idv) && idv is string ids && DecodeId(ids) is var d && d != null)
                    keys.Add(Tuple.Create(d.Value.key, d.Value.type));
            }
            if (parts.Count == 0 || keys.Count == 0) return;

            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            bool ro = tuple.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
            string sep = TupleSeparatorOf(tuple);
            // Text RouterOS prints in front of the whole joined value — every occurrence in the 7.24
            // catalog is the STP identifiers' '0x', which belongs to the compound and not to its hex part
            // (port-id is 0x80.1, its second part decimal).
            string? prefix = tuple.TryGetValue("prefix", out var pfv) ? pfv as string : null;

            // The compound answers at the first part's key; the rest are registered as the same field.
            var extra = new List<WinboxJgField>();
            for (int i = 1; i < keys.Count; i++)
                extra.Add(new WinboxJgField(apiName, keys[i].Item1, keys[i].Item2, ro,
                    uiType: TupleUiType, elementParts: parts, elementSeparator: sep,
                    isOptional: IsOptionalAttr(tuple),
                    paneKind: pane?.Kind, paneSelectorKey: pane?.SelectorKey ?? 0, paneValues: pane?.Values,
                    prefix: prefix));

            AddField(handlerKey, label, keys[0].Item1, keys[0].Item2, ro, null,
                TupleUiType, 0, null, pane: pane, isOptional: IsOptionalAttr(tuple),
                elementParts: parts, elementSeparator: sep,
                extraRegistrations: extra.Count > 0 ? extra : null, prefix: prefix);
        }

        /// <summary>The UI type of a scalar <c>tuple</c> field. webfig's <c>types.tuple.tostr</c> default
        /// separator is <c>'/'</c>.</summary>
        internal const string TupleUiType = "tuple";

        private static string TupleSeparatorOf(Dictionary<string, object> tuple)
            => tuple.TryGetValue("sep", out var sv) && sv is string ss ? ss : "/";

        // Registers a per-record action verb (doit/action label → SYS_CMD) under its owning handler and
        // returns the normalized label its arguments are keyed by (null when the label is unusable).
        private string? AddAction(string handlerKey, string label, int cmd)
        {
            string norm = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(norm)) return null;
            if (!_actionsByHandler.TryGetValue(handlerKey, out var m))
            {
                m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _actionsByHandler[handlerKey] = m;
            }
            if (!m.ContainsKey(norm)) m[norm] = cmd;
            return norm;
        }

        // Harvest a streaming-monitor window. A monitor is a type:'query' window, or a type:'action' window
        // carrying a pollcmd; both run start → poll → cancel (webfig ObjectQuery/ObjectAction). The cmd trio
        // comes straight from the .jg: startcmd, cancelcmd, and the poll command — getallcmd||0xFE0004 for a
        // query (rows under Mfe0002), or pollcmd for an action (a single status record per poll). Windows that
        // lack a startcmd (e.g. a plain doit action verb) are not monitors and are skipped.
        private void HarvestMonitor(int[] handlerInts, string ty, Dictionary<string, object> dict)
        {
            bool isQuery = ty == "query";
            bool isPollAction = ty == "action" && dict.ContainsKey("pollcmd");
            if (!isQuery && !isPollAction) return;
            if (!dict.TryGetValue("startcmd", out var scv) || !(scv is int startCmd)) return;
            if (!dict.TryGetValue("cancelcmd", out var ccv) || !(ccv is int cancelCmd)) return;

            int pollCmd = isQuery
                ? (dict.TryGetValue("getallcmd", out var gav) && gav is int ga ? ga : WinboxM2Protocol.Command.GetAll)
                : (dict.TryGetValue("pollcmd", out var pcv) && pcv is int pc ? pc : WinboxM2Protocol.Command.GetAll);
            int autorefresh = dict.TryGetValue("autorefresh", out var arv) && arv is int ar ? ar : 1000;

            string key = HandlerKey(handlerInts);
            if (!_monitorsByHandler.ContainsKey(key))
                _monitorsByHandler[key] = new WinboxMonitorSpec(handlerInts, startCmd, pollCmd, cancelCmd, autorefresh, isQuery);
        }

        // Key of the direct child field named <paramref name="childName"/> (e.g. the 'type' discriminator the
        // generic interface window references via typeon), or 0 when absent/undecodable.
        private static int FindChildFieldKey(Dictionary<string, object> window, string childName)
        {
            if (window.TryGetValue("c", out var cv) && cv is List<object> list)
                foreach (var it in list)
                    if (it is Dictionary<string, object> d
                        && d.TryGetValue("name", out var nm) && nm is string ns && ns == childName
                        && d.TryGetValue("id", out var idv) && idv is string ids
                        && DecodeId(ids) is var dec && dec != null)
                        return dec.Value.key;
            return 0;
        }

        // First child dict inside a node's 'c' list (skips non-dict entries), or null.
        private static Dictionary<string, object>? FirstChildDict(Dictionary<string, object> node)
        {
            if (node.TryGetValue("c", out var cv) && cv is List<object> list)
                foreach (var it in list)
                    if (it is Dictionary<string, object> d) return d;
            return null;
        }

        // Pulls the referenced table handler from an enm dropdown (values:{type:'dynamic',path:[…]}),
        // searching one level of nesting (defenum/pair wrappers carry the dynamic node inside their own
        // values/c). Returns the first dynamic path found, or null for a static/non-reference enum.
        private static int[]? ExtractRefHandler(Dictionary<string, object> node)
        {
            if (node.TryGetValue("values", out var vv))
                return FindDynamicPath(vv, 0);

            // A LIST field declares its element type as an unnamed child instead of carrying 'values' itself
            // — the log's Topics is {name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:
            // 'dynamic',path:[3,3]}}]}. Reading only 'values' left its RefHandler null, so a log row's topics
            // decoded as the raw handle "[9,3]" instead of "script,error". Only reached for a field leaf (the
            // caller requires an 'id'), so this cannot pick up a path from an unrelated menu child.
            if (node.TryGetValue("c", out var cv))
                return FindDynamicPath(cv, 0);
            return null;
        }

        private static int[]? FindDynamicPath(object node, int depth)
        {
            if (depth > 4) return null;
            if (node is Dictionary<string, object> d)
            {
                if (d.TryGetValue("type", out var t) && t is string ts && ts == "dynamic"
                    && d.TryGetValue("path", out var pv) && pv is List<object> pl)
                {
                    var ints = new List<int>();
                    foreach (var p in pl) { if (p is int pi) ints.Add(pi); else return null; }
                    if (ints.Count > 0) return ints.ToArray();
                }
                foreach (var kv in d)
                {
                    var r = FindDynamicPath(kv.Value, depth + 1);
                    if (r != null) return r;
                }
            }
            else if (node is List<object> list)
            {
                foreach (var it in list)
                {
                    var r = FindDynamicPath(it, depth + 1);
                    if (r != null) return r;
                }
            }
            return null;
        }

        /// <summary>
        /// Pulls a static enum value list into <c>{numeric → label}</c>, following the <c>values:</c> chain
        /// wherever the map actually sits.
        /// </summary>
        /// <remarks>
        /// <para>Two map forms: an <b>array</b> (<c>values:{map:['off','on',…]}</c>) whose index is the numeric
        /// value, and an <b>object</b> (<c>values:{map:{0:'invalid',1:'established'}}</c>) with explicit keys —
        /// for <c>type:'set'</c> that key is the BIT INDEX of a bitmask (webfig <c>types.set.tostr</c>:
        /// <c>if(val&amp;(1&lt;&lt;i))</c>), for a sparse enum it is the value itself.</para>
        /// <para>The map is not always the first thing under <c>values</c>. RouterOS wraps it in
        /// <c>enumfilter</c> (which members this board offers), <c>defenum</c> (a sentinel id/name in front of
        /// the real list) and <c>pair</c> (a static sentinel list beside a dynamic table), and nests those —
        /// an IPsec proposal's PFS group is <c>enumfilter → defenum → static</c>. Reading only the top level
        /// left those fields with NO map at all, so <c>pfs-group</c> reached the caller as the bare number
        /// <c>2</c> where the API says <c>modp1024</c>. A list field carries its element's values on its
        /// unnamed child instead (<c>multinumber c:[{type:'enm',values:…}]</c>), same as
        /// <see cref="ExtractRefHandler"/> already handles for references.</para>
        /// <para>The runtime-computed wrappers (<c>queryenum</c>, <c>offsetenum</c>, <c>slotenum</c>,
        /// <c>remapenum</c>) are deliberately NOT followed: their members come from a live query or from
        /// another field's value, so there is no static map to read and inventing one would name values the
        /// router never meant.</para>
        /// </remarks>
        private static IReadOnlyDictionary<int, string>? ExtractEnumMap(Dictionary<string, object> node)
        {
            var map = new Dictionary<int, string>();
            if (node.TryGetValue("values", out var vv)) CollectEnumMap(vv, map, 0);
            // A list field's element type is an unnamed child ({type:'multinumber',c:[{type:'enm',values:…}]}).
            if (map.Count == 0 && node.TryGetValue("c", out var cv)) CollectEnumMap(cv, map, 0);
            return map.Count > 0 ? Normalized(map) : null;
        }

        /// <summary>
        /// Normalizes a map's labels the way every other .jg label is normalized — <b>unless doing so would
        /// merge two members into one</b>, in which case the raw labels are kept.
        /// </summary>
        /// <remarks>
        /// <para>Normalizing is what makes a label match the API's spelling at all: WinBox's
        /// <c>'as username'</c> is the API's <c>as-username</c> and <c>'key 0'</c> is <c>key-0</c>. But it
        /// lowercases and folds separators, and a handful of maps distinguish members by exactly those.
        /// A whole-catalog sweep of 7.24 finds three:</para>
        /// <list type="bullet">
        /// <item><c>/interface/wireless/security-profiles</c> 'MAC Format' — the same seven formats twice,
        /// uppercase then lowercase, and the case SELECTS how the MAC is sent to RADIUS. 14 labels collapse
        /// to 6. The router agrees they are 14 distinct values (tab-completion offers all of them).</item>
        /// <item><c>/ip/hotspot/profile</c> 'MAC Format' — 7 labels, where <c>'XX XX XX XX XX XX'</c> and
        /// <c>'XX-XX-XX-XX-XX-XX'</c> both fold to the same thing.</item>
        /// <item>'Rate' — <c>'2.5Gbps'</c> and <c>'25Gbps'</c> both fold to <c>25gbps</c>, because the
        /// abbreviation-dot rule drops the point. The API prints <c>rate=1Gbps</c>, i.e. the raw label.</item>
        /// </list>
        /// <para>In all three the raw label is exactly what RouterOS prints and accepts, so keeping it is
        /// not a compromise — normalizing was simply wrong for them. Detected from the map itself rather
        /// than listed per field, so a new such map on a later RouterOS is handled without a code change.</para>
        /// </remarks>
        private static Dictionary<int, string> Normalized(Dictionary<int, string> raw)
        {
            var byNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in raw)
            {
                string n = WinboxFieldResolver.NormalizeLabel(kv.Value);
                // Two DIFFERENT raw labels landing on one normalized form is the collision. The same raw
                // label appearing at two keys is not — a defenum names an id the wrapped list also names.
                if (byNormalized.TryGetValue(n, out string? other) && !string.Equals(other, kv.Value, StringComparison.Ordinal))
                    return raw;
                byNormalized[n] = kv.Value;
            }
            var normalized = new Dictionary<int, string>(raw.Count);
            foreach (var kv in raw) normalized[kv.Key] = WinboxFieldResolver.NormalizeLabel(kv.Value);
            return normalized;
        }

        // The wrappers whose inner `values` still lead to a static map. Anything else (queryenum, slotenum, …)
        // is computed at runtime and has none — see the remarks on ExtractEnumMap.
        //
        // 'tristate' is in the list because it is where a multitristate field keeps its members: the .jg
        // declares TCP Flags as {type:'multitristate',id:'u56',maskid:'u57',c:[{type:'tristate',values:
        // {map:['fin','syn',…]}}]}, and stopping at the child left the field with no map at all — so the
        // bitmask reached the caller as the bare number 2 where the API prints "syn".
        private static readonly HashSet<string> EnumMapWrappers =
            new HashSet<string>(StringComparer.Ordinal) { "enumfilter", "defenum", "pair", "static", "enm", "tristate" };

        private static void CollectEnumMap(object node, Dictionary<int, string> map, int depth)
        {
            // 10, not 5. Each wrapper costs TWO levels (the node, then the list under its `c`), and
            // /ip/traffic-flow 'Interfaces' is enm → pair → pair → static — nine levels down from the field,
            // so its map was cut off entirely and `interfaces=all` read as 4294967295. The guard is only
            // there to stop a cyclic or pathological catalog, so it can afford to be generous.
            if (depth > 10) return;
            if (node is List<object> list)
            {
                foreach (var it in list) CollectEnumMap(it, map, depth + 1);
                return;
            }
            if (!(node is Dictionary<string, object> d)) return;

            string? ty = d.TryGetValue("type", out var tv) && tv is string ts ? ts : null;
            // A defenum names ONE id in front of the list it wraps (defid:0,defname:'none'); the wrapped list
            // then fills in the rest. Put it in first — first-wins below keeps the inner map from renaming it.
            if (ty == "defenum" && d.TryGetValue("defid", out var dv) && TryToLong(dv, out long defId)
                && d.TryGetValue("defname", out var dnv) && dnv is string defName)
                Put(unchecked((int)defId), defName);

            if (d.TryGetValue("map", out var mv))
            {
                if (mv is List<object> arr)
                {
                    for (int i = 0; i < arr.Count; i++)
                        if (arr[i] is string s) Put(i, s);
                }
                else if (mv is Dictionary<string, object> obj)
                {
                    foreach (var kv in obj)
                        if (kv.Value is string s && long.TryParse(kv.Key, out long k))
                            Put(unchecked((int)k), s);
                }
            }

            if (ty != null && !EnumMapWrappers.Contains(ty)) return;
            if (d.TryGetValue("values", out var inner)) CollectEnumMap(inner, map, depth + 1);
            if (d.TryGetValue("c", out var children)) CollectEnumMap(children, map, depth + 1);

            // Stores the RAW label. Normalization happens once, in ExtractEnumMap, because it needs to see
            // the WHOLE map before it can tell whether normalizing would merge two members.
            void Put(int key, string label)
            {
                if (map.ContainsKey(key)) return;
                map[key] = StripValueSuffix(label);
            }
        }

        // WinBox spells the DH GROUP NUMBER out in the label — 'modp1024 (2)' — while the API calls it
        // 'modp1024'. The suffix is display decoration and is always dropped.
        //
        // It used to be dropped only when the number equalled the field's bit KEY, on the theory that a label
        // genuinely ending in a parenthesised number should survive. Two things settled that: the number is the
        // DH group, which merely COINCIDES with the bit key for the classic MODP groups, so 'x25519 (31)' at
        // bit 22 kept its suffix and read as 'x25519-(31)' where the API says 'x25519'; and a sweep of the whole
        // 7.23.2 catalog finds exactly twelve labels of this shape, all of them DH groups. The case the old
        // rule protected does not exist.
        private static string StripValueSuffix(string label)
        {
            if (string.IsNullOrEmpty(label) || label[label.Length - 1] != ')') return label;
            int open = label.LastIndexOf('(');
            if (open <= 0 || label[open - 1] != ' ') return label;
            string inside = label.Substring(open + 1, label.Length - open - 2);
            return long.TryParse(inside, out _) ? label.Substring(0, open - 1) : label;
        }

        // A .jg number too large for int stays a string token after parsing (JgParser.Scalar), so a `def` or
        // `defid` of 4294967295 arrives as text. Read both forms.
        private static bool TryToLong(object v, out long result)
        {
            if (v is int i) { result = i; return true; }
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return true;
            result = 0;
            return false;
        }

        /// <summary>
        /// A field declaration's own <c>title</c>, when it has one that differs from its <c>name</c>.
        /// </summary>
        /// <remarks>
        /// On a FIELD (not a window) the two are not two spellings of one label: the <c>name</c> is what
        /// WinBox paints beside the box and the <c>title</c> is what RouterOS calls the field.
        /// <c>{name:'Type',title:'Target'}</c> on <c>/system/logging/action</c> is <c>target</c> to the API
        /// and <c>type</c> is not a field of that table at all. The commonest shape is a pair of
        /// declarations on ONE key, one of them conditional and prefixed 'Old' —
        /// <c>{name:'Old Cache Path',title:'Cache Path',on:'oldfileman'}</c> beside
        /// <c>{name:'Cache Path',on:'newfileman'}</c> — and there the title says which of the two the API
        /// uses. Twenty-five such pairs in the 7.24 catalog.
        /// </remarks>
        private static string? TitleOf(Dictionary<string, object> node)
            => node.TryGetValue("title", out var tv) && tv is string ts && ts.Length > 0
               && !(node.TryGetValue("name", out var nv) && nv is string ns
                    && string.Equals(ns, ts, StringComparison.Ordinal))
                ? ts : null;

        // The .jg `def` (the field's default), kept only so the u32 unset marker can be recognised — see
        // WinboxJgField.Def.
        private static long? ExtractDef(Dictionary<string, object> node)
            => node.TryGetValue("def", out var dv) && TryToLong(dv, out long d) ? (long?)d : null;

        // 's10006' → (key=0x10006, type="string"); prefix is exactly one leading letter, rest is hex.
        private static (int key, string type)? DecodeId(string idStr)
        {
            var m = Regex.Match(idStr, "^([a-zA-Z])([0-9a-fA-F]+)$");
            if (!m.Success) return null;
            char pfx = m.Groups[1].Value[0];
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int key))
                return null;
            string type = Prefix.TryGetValue(pfx, out var t) ? t : "?";
            return (key, type);
        }

        internal static string HandlerKey(int[] handler) => string.Join(",", handler);


        // ── Tolerant JS-object-literal parser (port of jg_analyze.py JgParser) ─
        // Produces nested Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / int.
        private sealed class JgParser
        {
            private readonly string _s;
            private int _i;
            private readonly int _n;

            internal JgParser(string text) { _s = text ?? ""; _n = _s.Length; }

            internal object Parse() { Ws(); return Value(); }

            private void Ws() { while (_i < _n && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++; }

            private object Value()
            {
                Ws();
                if (_i >= _n) return null!; // only on malformed/truncated input; callers pattern-match the result and tolerate it
                char c = _s[_i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '\'' || c == '"') return Str();
                return Scalar();
            }

            private Dictionary<string, object> Obj()
            {
                _i++; // {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                Ws();
                if (_i < _n && _s[_i] == '}') { _i++; return d; }
                while (_i < _n)
                {
                    Ws();
                    string key;
                    if (_i < _n && (_s[_i] == '\'' || _s[_i] == '"'))
                        key = Str();
                    else
                    {
                        int start = _i;
                        while (_i < _n && ":,}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
                        key = _s.Substring(start, _i - start);
                    }
                    Ws();
                    if (_i >= _n || _s[_i] != ':') throw new FormatException("expected ':'");
                    _i++;
                    object val = Value();
                    d[key] = val; // last duplicate wins (we only read scalar leaves we care about)
                    Ws();
                    if (_i < _n && _s[_i] == ',')
                    {
                        _i++; Ws();
                        if (_i < _n && _s[_i] == '}') { _i++; return d; } // trailing comma
                        continue;
                    }
                    if (_i < _n && _s[_i] == '}') { _i++; return d; }
                    throw new FormatException("obj parse error");
                }
                return d;
            }

            private List<object> Arr()
            {
                _i++; // [
                var a = new List<object>();
                Ws();
                if (_i < _n && _s[_i] == ']') { _i++; return a; }
                while (_i < _n)
                {
                    a.Add(Value());
                    Ws();
                    if (_i < _n && _s[_i] == ',')
                    {
                        _i++; Ws();
                        if (_i < _n && _s[_i] == ']') { _i++; return a; }
                        continue;
                    }
                    if (_i < _n && _s[_i] == ']') { _i++; return a; }
                    throw new FormatException("arr parse error");
                }
                return a;
            }

            private string Str()
            {
                char q = _s[_i]; _i++;
                var sb = new StringBuilder();
                while (_i < _n)
                {
                    char c = _s[_i];
                    if (c == '\\') { if (_i + 1 < _n) sb.Append(_s[_i + 1]); _i += 2; continue; }
                    if (c == q) { _i++; return sb.ToString(); }
                    sb.Append(c); _i++;
                }
                throw new FormatException("unterminated string");
            }

            private object Scalar()
            {
                int start = _i;
                while (_i < _n && ",}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
                string tok = _s.Substring(start, _i - start);
                if (Regex.IsMatch(tok, "^-?\\d+$") && int.TryParse(tok, out int iv))
                    return iv;
                return tok;
            }
        }
    }
}
