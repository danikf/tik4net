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
    internal sealed partial class WinboxJgCatalog
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

        // A singleton window that names its own read or write command: derived path → (getcmd, setcmd), each
        // null when the window leaves it to the default. webfig's ObjectHolder sends `uff0007 = getcmd ||
        // 0xfe000d` to read and `setcmd || 0xfe000e` to write, the message otherwise unchanged; the default
        // on a window with its own command is refused (0xFE0003). Per window, since [46] is RouterOS 6's
        // 'Traffic Accounting' settings (getcmd:2, setcmd:1) AND its snapshot list.
        private readonly Dictionary<string, Tuple<int?, int?>> _singletonCommands =
            new Dictionary<string, Tuple<int?, int?>>(StringComparer.OrdinalIgnoreCase);

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

        // The flag bits a handler's windows ask a read for: `refreshfilter` and `refetchonopen`, OR'd across every
        // window on the handler. webfig sends `base | refetchonopen | refreshfilter` on getall and get-singleton
        // alike; without the window's bits a read can be a sketch of the list columns — RouterOS 6.49's route
        // window (refreshfilter:131072) leaves out scope, target-scope, routing-mark and vrf-interface.
        private readonly Dictionary<string, int> _readFlagsByHandler = new Dictionary<string, int>(StringComparer.Ordinal);

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

        /// <summary>
        /// The <c>numflag</c> fields of a window or handler: <c>key → (value → the flag's own name)</c>.
        /// A <c>numflag</c> is one numeric key carrying a SET of named row flags, one of which the row is —
        /// <c>{type:'numflag',id:'u7',c:{2:['connected','C'],3:['static','S'],…}}</c> — and the names are the
        /// router's own, which is why the flags a version has need no version test: each catalog declares the
        /// members that version knows (RouterOS 6 has no <c>DHCP</c> member, and RouterOS 7 does).
        /// </summary>
        /// <remarks>
        /// The node carries no <c>name</c> of its own, so it is not a field and does not go into the field
        /// maps. Window members overlay the handler's, as <see cref="GetWindowFields"/> does.
        /// </remarks>
        internal IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? GetNumFlags(
            int[] handler, string? derivedPath)
        {
            _numFlags.TryGetValue(HandlerKey(handler), out var byHandler);
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>? byWindow = null;
            if (derivedPath != null && _numFlags.TryGetValue(WindowKey(derivedPath), out var w)) byWindow = w;
            if (byHandler == null) return byWindow;
            if (byWindow == null) return byHandler;
            var merged = new Dictionary<int, IReadOnlyDictionary<int, string>>(byHandler);
            foreach (var kv in byWindow) merged[kv.Key] = kv.Value;
            return merged;
        }

        // owner (handler or window key) → key → value → flag name.
        private readonly Dictionary<string, Dictionary<int, IReadOnlyDictionary<int, string>>> _numFlags =
            new Dictionary<string, Dictionary<int, IReadOnlyDictionary<int, string>>>(StringComparer.Ordinal);

        // {type:'numflag',id:'u7',c:{2:[ 'connected','C' ],…}} — the member list is a map from the key's value
        // to [name, one-letter flag]; only the name is kept.
        private void AddNumFlag(string owner, string idStr, Dictionary<string, object> dict)
        {
            var dec = DecodeId(idStr);
            if (dec == null || !dict.TryGetValue("c", out var cv) || !(cv is Dictionary<string, object> members))
                return;
            var byValue = new Dictionary<int, string>();
            foreach (var m in members)
            {
                if (!int.TryParse(m.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) continue;
                string? name = m.Value is List<object> pair && pair.Count > 0 ? pair[0] as string : m.Value as string;
                if (!string.IsNullOrEmpty(name)) byValue[value] = name!;
            }
            if (byValue.Count == 0) return;
            Register(owner);
            // Also under the owning HANDLER, the way AddField registers a handler-backed window's fields: a
            // caller addressing the handler rather than this window still reads the flags. First wins, so a
            // second window on the same handler does not rename another's flags at handler level.
            string? handlerOfWindow = IsWindowKey(owner) ? HandlerOfOwner(owner) : null;
            if (handlerOfWindow != null) Register(handlerOfWindow);

            void Register(string key)
            {
                if (!_numFlags.TryGetValue(key, out var forOwner))
                    _numFlags[key] = forOwner = new Dictionary<int, IReadOnlyDictionary<int, string>>();
                if (!forOwner.ContainsKey(dec.Value.key)) forOwner[dec.Value.key] = byValue;
            }
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

        /// <summary>
        /// The read and write commands of the singleton window behind <paramref name="derivedKey"/>, when the
        /// window names its own (<c>getcmd</c>/<c>setcmd</c>); each is <c>null</c> where the default
        /// get-singleton / set-singleton applies, and both are when the path is unknown or names neither.
        /// </summary>
        internal void GetSingletonCommands(string? derivedKey, out int? getCommand, out int? setCommand)
        {
            getCommand = null; setCommand = null;
            if (derivedKey != null && _singletonCommands.TryGetValue(derivedKey, out var c))
            {
                getCommand = c.Item1; setCommand = c.Item2;
            }
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
        /// The SYS_CMD an action window's path names: the handler's sole action, or — where several windows share
        /// one handler — the action whose label is the path's last segment. The system handler [24] carries Reboot (5),
        /// Shutdown (6), Reset Configuration (7) and a package downgrade (9), one window each (6.49.13, 7.24.4), so
        /// <c>/system/reboot</c> is cmd 5. <c>-1</c> when neither rule names exactly one action.
        /// </summary>
        internal int GetActionCmd(int[] handler, string lastSegment, out string? label)
        {
            int sole = GetSoleActionCmd(handler, out label);
            if (sole >= 0) return sole;

            label = null;
            var actions = GetHandlerActions(handler);
            if (actions == null) return -1;
            foreach (var kv in actions)
                if (string.Equals(kv.Key, lastSegment, StringComparison.OrdinalIgnoreCase)) { label = kv.Key; return kv.Value; }
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

        /// <summary>The flag word (<c>ufe000c</c>) a getall or get-singleton on <paramref name="handler"/> sends:
        /// <see cref="WinboxM2Protocol.GetAllFlags"/>, OR the bits its windows declare as <c>refreshfilter</c> and
        /// <c>refetchonopen</c> (what webfig sends), OR <see cref="WinboxM2Protocol.GetAllStatsFlag"/> when a window
        /// is <c>autorefresh</c> (<see cref="HasDynamicFields"/>) — a bit webfig sends only where the window's
        /// <c>refreshfilter</c> carries it, kept because the counters this transport returns depend on it.</summary>
        internal int GetReadFlags(int[] handler)
            => WinboxM2Protocol.GetAllFlags | GetWindowReadFlags(handler)
               | (HasDynamicFields(handler) ? WinboxM2Protocol.GetAllStatsFlag : 0);

        /// <summary>The <c>refreshfilter | refetchonopen</c> bits <paramref name="handler"/>'s windows declare, or
        /// <c>0</c>. A get-singleton sends <see cref="WinboxM2Protocol.GetAllFlags"/> OR these, as webfig's
        /// <c>ObjectHolder.fetch</c> does.</summary>
        internal int GetWindowReadFlags(int[] handler)
            => handler != null && _readFlagsByHandler.TryGetValue(HandlerKey(handler), out int flags) ? flags : 0;

        /// <summary>Number of field maps this catalog holds. <c>0</c> means the <c>.jg</c> load produced
        /// nothing and every lookup will fall back to the seed table — see
        /// <see cref="WinboxNative.WinboxNativeConnection.CatalogHandlerCount"/> for why that matters.
        /// <para>It counts every map, not only the per-handler ones: a window and an action each keep a map
        /// of their own alongside their handler's, so the number is larger than the handler count and is a
        /// LOAD indicator, not an inventory. Compare it against zero, never against a remembered figure.</para>
        /// </summary>
        internal int HandlerCount => _byHandler.Count;
    }
}
