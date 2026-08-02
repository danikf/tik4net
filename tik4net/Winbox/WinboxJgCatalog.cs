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
    /// yields a full catalog instead of silently degrading the connection to seeds (P2.23).</para>
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

        // Handlers backed by a singleton window (type:'item') — read via get-singleton, not getall.
        private readonly HashSet<string> _singletonHandlers = new HashSet<string>(StringComparer.Ordinal);

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
        private int[] _genericIfaceHandler;
        private int _ifaceTypeKey;

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
        private static readonly Dictionary<char, string> Prefix = new Dictionary<char, string>
        {
            ['u'] = "u32",    ['U'] = "u32[]",
            ['s'] = "string", ['S'] = "string[]",
            ['b'] = "bool",   ['B'] = "bool[]",
            ['r'] = "raw",    ['R'] = "raw[]",
            ['m'] = "addr",   ['M'] = "addr[]",
            ['a'] = "ip6",    ['A'] = "ip6[]",
            ['x'] = "u64",    ['X'] = "u64[]",
            ['q'] = "u64",    ['i'] = "i32", ['d'] = "dur", ['t'] = "time",
        };

        /// <summary>True once at least one <c>.jg</c> window has been parsed into the catalog.</summary>
        internal bool HasData => _byHandler.Count > 0;

        /// <summary>
        /// Returns the field map for <paramref name="handler"/> as <c>apiName → field</c>, or <c>null</c>
        /// when no window targeting that handler was found in the catalog.
        /// </summary>
        internal IReadOnlyDictionary<string, WinboxJgField> GetHandlerFields(int[] handler)
        {
            return _byHandler.TryGetValue(HandlerKey(handler), out var map) ? map : null;
        }

        /// <summary>
        /// API paths derived from the <c>.jg</c> menu tree: <c>derivedApiPath → handler</c>.
        /// Built from every <c>type:'map'/'query'</c> window node (its <c>path:[…]</c> handler keyed by
        /// the normalized enclosing <c>group</c> + node <c>name</c>). Dropdown references
        /// (<c>type:'enm'</c>) that reuse a handler are not included.
        /// </summary>
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
        internal IReadOnlyDictionary<string, int> GetHandlerActions(int[] handler)
            => handler != null && _actionsByHandler.TryGetValue(HandlerKey(handler), out var m) ? m : null;

        /// <summary>True when <paramref name="handler"/> is backed by a singleton (<c>type:'item'</c>)
        /// window — its sole record is read via get-singleton rather than getall.</summary>
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
        internal bool IsActionOnlyHandler(int[] handler)
        {
            if (handler == null) return false;
            string key = HandlerKey(handler);
            return _actionWindowHandlers.Contains(key) && !_recordWindowHandlers.Contains(key);
        }

        /// <summary>
        /// The single SYS_CMD for an <see cref="IsActionOnlyHandler"/> handler, or <c>-1</c> when the handler
        /// exposes no action or more than one (ambiguous — the caller must name the verb instead).
        /// </summary>
        internal int GetSoleActionCmd(int[] handler)
        {
            var actions = GetHandlerActions(handler);
            if (actions == null || actions.Count != 1) return -1;
            foreach (var kv in actions) return kv.Value;
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
        internal int[] FindSingletonHandlerByLeaf(string leaf)
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
        internal WinboxMonitorSpec GetMonitorByHandler(int[] handler) =>
            handler != null && _monitorsByHandler.TryGetValue(HandlerKey(handler), out var spec) ? spec : null;

        /// <summary>True when <paramref name="handler"/>'s window is marked <c>autorefresh</c> in the
        /// <c>.jg</c> — it carries runtime/dynamic fields (e.g. firewall counters) that getall only returns
        /// when the stats flag bit is set.</summary>
        internal bool HasDynamicFields(int[] handler) =>
            handler != null && _dynamicHandlers.Contains(HandlerKey(handler));

        /// <summary>Number of M2 handlers this catalog knows fields for. <c>0</c> means the <c>.jg</c> load
        /// produced nothing and every lookup will fall back to the seed table — see
        /// <see cref="WinboxNative.WinboxNativeConnection.CatalogHandlerCount"/> for why that matters.</summary>
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
            byte[] raw = MproxyRead(ops, handle);
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
        private static string ListCachePath(string cacheDir, string routerKey)
        {
            if (string.IsNullOrEmpty(cacheDir) || string.IsNullOrEmpty(routerKey)) return null;
            var safe = new StringBuilder(routerKey.Length);
            foreach (char c in routerKey)
                safe.Append((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                            || c == '.' || c == '-' ? c : '_');
            return Path.Combine(Path.Combine(cacheDir, "lists"), safe.ToString() + ".list");
        }

        private static void RememberPluginList(string routerKey, string cacheDir, List<PluginEntry> plugins)
        {
            if (string.IsNullOrEmpty(routerKey)) return;
            lock (LastGoodListsLock) { LastGoodLists[routerKey] = plugins; }

            string path = ListCachePath(cacheDir, routerKey);
            if (path == null) return;
            try
            {
                string text = FormatPluginList(plugins);
                // Rewrite only on change: this runs on every open, and an unchanged router should not
                // produce a disk write per connection.
                if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), text, StringComparison.Ordinal))
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch { /* remembering is best-effort — never fail an open over it */ }
        }

        private static List<PluginEntry> RecallPluginList(string routerKey, string cacheDir)
        {
            if (string.IsNullOrEmpty(routerKey)) return null;
            lock (LastGoodListsLock)
            {
                List<PluginEntry> inProcess;
                if (LastGoodLists.TryGetValue(routerKey, out inProcess)) return inProcess;
            }

            string path = ListCachePath(cacheDir, routerKey);
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
        /// inside a full suite — read as mproxy refusing to serve the plugin list (P2.23, P2.40), which it
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
        internal static WinboxJgCatalog Load(WinboxNativeM2Operations ops, string cacheDir, string routerKey)
        {
            // The list is ~2 KB and one round trip; it is the only authoritative statement of what this
            // particular router serves, so it is always fetched — it is the plugin *bodies* that are cached.
            List<PluginEntry> plugins = null;
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
                WinboxJgCatalog cached;
                if (SharedCatalogs.TryGetValue(key, out cached)) return cached;
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
                string text = null;
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
                catalog.TryParseInto(text);
                TraceNote(plugin.Name + ": " + text.Length + " chars, handlers now " + catalog._byHandler.Count);
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
        private static string ReadCachedOrFetch(WinboxNativeM2Operations ops, PluginEntry plugin,
                                                string cacheDir)
        {
            string path = null;
            if (!string.IsNullOrEmpty(cacheDir))
            {
                try
                {
                    path = Path.Combine(Path.Combine(cacheDir, "plugins"), plugin.Unique);
                    if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
                }
                catch { path = null; /* cache read is best-effort */ }
            }

            string text = FetchJg(ops, plugin.Unique);
            if (string.IsNullOrEmpty(text)) return null;

            if (path != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
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
                return true;
            }
            catch (Exception ex)
            {
                TraceNote("PARSE FAILED (" + text.Length + " chars): " + ex.Message);
                return false;
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
        private static string FetchJg(WinboxNativeM2Operations ops, string uniqueName)
        {
            // The on-disk file in /home/web/webfig/ is "<unique>.gz" (gzip), served by the mproxy static
            // handler via cmd=7 (NOT cmd=3 = /var/pckg, which CHR denies). A refused open is harmless —
            // it is a clean error reply and the channel survives it.
            int handle = MproxyOpen(ops, uniqueName + ".gz", WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) handle = MproxyOpen(ops, uniqueName, WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) return null;

            byte[] raw = MproxyRead(ops, handle);
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

        private static byte[] MproxyRead(WinboxNativeM2Operations ops, int handle)
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
                byte[] resp = ops.SendReceiveCorrelated(msg);
                byte[] chunk = ExtractMproxyChunk(resp);
                if (chunk == null || chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < MproxyChunk) break;
            }
            return all.Count > 0 ? all.ToArray() : null;
        }

        // Pull the raw/string file-content field (user namespace) out of a read response.
        private static byte[] ExtractMproxyChunk(byte[] resp)
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

        private void Walk(object node, string handlerKey, List<string> crumb)
        {
            if (node is Dictionary<string, object> dict)
            {
                string owner = handlerKey;
                int[] handlerInts = null;
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
                string groupSeg = (dict.TryGetValue("group", out var gv) && gv is string gs && gs.Length > 0)
                    ? WinboxFieldResolver.NormalizeLabel(gs) : null;
                string ty = dict.TryGetValue("type", out var tyv) && tyv is string tys ? tys : null;

                // Harvest derived menu-label path → handler for WINDOW nodes (map/query/item/doit/action
                // with a handler path). Dropdown references (type:'enm',values:{type:'dynamic',path:…}) reuse
                // the same handler but are NOT windows — skip them. The key is the full breadcrumb
                // (ancestor menu chain + this node's group + name).
                if (handlerInts != null && ty != null && WindowTypes.Contains(ty))
                {
                    string apiPath = BuildPath(crumb, groupSeg, nodeName);
                    if (apiPath != null && !_derivedPaths.ContainsKey(apiPath))
                        _derivedPaths[apiPath] = handlerInts;
                    if (ty == "item") _singletonHandlers.Add(HandlerKey(handlerInts));
                    // Track record-listing vs action-only windows separately so a handler that has BOTH (e.g.
                    // [20,125] = 'SMS Message' map + 'Send SMS' doit) is never mistaken for action-only.
                    if (ty == "doit" || ty == "action") _actionWindowHandlers.Add(HandlerKey(handlerInts));
                    else _recordWindowHandlers.Add(HandlerKey(handlerInts));
                    if (dict.ContainsKey("autorefresh")) _dynamicHandlers.Add(HandlerKey(handlerInts));
                    HarvestMonitor(handlerInts, ty, dict);

                    // The base interface window (generic:'iface') anchors the subtype filtering: remember its
                    // handler and the key of the `type` discriminator field (named by `typeon`, default 'type').
                    if (dict.TryGetValue("generic", out var genv) && genv is string gen && gen == "iface")
                    {
                        _genericIfaceHandler = handlerInts;
                        string typeon = dict.TryGetValue("typeon", out var tov) && tov is string tos ? tos : "type";
                        int tk = FindChildFieldKey(dict, typeon);
                        if (tk != 0) _ifaceTypeKey = tk;
                    }
                }

                // Interface subtype window: inherit:'iface' (reuse the generic handler, has no own path:) +
                // typevalue:N (the discriminator). Register a derived path → the generic handler, plus a subtype
                // filter (typeKey, N). The leaf is the window TITLE — `name` is the generic 'Interface' shared by
                // every subtype, so it cannot discriminate; the title carries the subtype ('Bridge','VLAN',…).
                if (handlerInts == null && _genericIfaceHandler != null && _ifaceTypeKey != 0
                    && dict.TryGetValue("inherit", out var inhv) && inhv is string inh && inh == "iface"
                    && dict.TryGetValue("typevalue", out var tvv) && tvv is int tval)
                {
                    string title = dict.TryGetValue("title", out var ttv) && ttv is string tts && tts.Length > 0
                        ? tts : nodeName;
                    string apiPath = BuildPath(crumb, groupSeg, title);
                    if (apiPath != null && !_derivedPaths.ContainsKey(apiPath))
                    {
                        _derivedPaths[apiPath] = _genericIfaceHandler;
                        _subtypeFilters[apiPath] = Tuple.Create(_ifaceTypeKey, tval);
                    }
                }

                // A named opt/not wrapper (type:'opt'/'not' with children) holds its real value in an inner leaf;
                // the wrapper's own id is just a flag bool (opt=present, not=negated). Map the API name to the
                // inner value leaf, carrying the flag keys — e.g. firewall 'Connection State' (opt→not→set) maps
                // to the inner bitmask, not the outer bool. (A childless opt is a plain bool — handled below.)
                if (owner != null && (ty == "opt" || ty == "not")
                    && dict.ContainsKey("c") && !string.IsNullOrEmpty(nodeName))
                {
                    AddOptionField(owner, nodeName, dict);
                }
                else if (owner != null && dict.TryGetValue("id", out var idv) && idv is string idStr)
                {
                    var dec = DecodeId(idStr);
                    if (dec != null)
                    {
                        bool ro = dict.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                        int maskKey = (dict.TryGetValue("maskid", out var mkv) && mkv is string mks
                            && DecodeId(mks) is var md && md != null) ? md.Value.key : 0;
                        int[] refHandler = ExtractRefHandler(dict);
                        bool isRange = dict.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                        string allow = dict.TryGetValue("allow", out var alv) ? alv as string : null;
                        AddField(owner, nodeName, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(dict),
                            ty, maskKey, refHandler, isRange: isRange, allow: allow);
                    }
                }

                // Per-record action verb: a named doit/action carrying a cmd (no own field id) invokes that
                // SYS_CMD on the owning handler against a record .id — e.g. the scripts window's
                // {name:'Run Script',type:'doit',cmd:1} on [48,101]. Register it for /system/script/run dispatch.
                if (owner != null && (ty == "doit" || ty == "action") && !dict.ContainsKey("id")
                    && dict.TryGetValue("cmd", out var cmdv) && cmdv is int cmdN && !string.IsNullOrEmpty(nodeName))
                {
                    AddAction(owner, nodeName, cmdN);
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

                foreach (var kv in dict)
                {
                    if (kv.Key == "id") continue;
                    Walk(kv.Value, owner, childCrumb);
                }
            }
            else if (node is List<object> list)
            {
                foreach (var it in list)
                    Walk(it, handlerKey, crumb);
            }
        }

        // Build a derived menu-label path "/ip/firewall/connection" from the breadcrumb + this window's
        // group segment + node name, each normalized (lower, spaces→'-'). Returns null for an empty leaf.
        private static string BuildPath(List<string> crumb, string groupSeg, string nodeName)
        {
            string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
            if (string.IsNullOrEmpty(leaf)) return null;
            var parts = new List<string>(crumb);
            if (groupSeg != null) parts.Add(groupSeg);
            parts.Add(leaf);
            return "/" + string.Join("/", parts);
        }

        private void AddField(string handlerKey, string label, int key, string wireType, bool ro,
            IReadOnlyDictionary<int, string> enumMap, string uiType, int maskKey, int[] refHandler,
            int optKey = 0, int notKey = 0, bool isRange = false, string allow = null)
        {
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            if (!_byHandler.TryGetValue(handlerKey, out var map))
            {
                map = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                _byHandler[handlerKey] = map;
            }
            // first label wins for a given apiName; do not let later, less-specific windows clobber it.
            if (!map.ContainsKey(apiName))
                map[apiName] = new WinboxJgField(apiName, key, wireType, ro, enumMap, uiType, maskKey,
                    refHandler, optKey, notKey, isRange, allow);
        }

        // Resolves a named opt/not wrapper (e.g. firewall 'Connection State': opt→not→set) to its inner value
        // leaf and registers the API name against that leaf, carrying the opt/not flag-bool keys. Drills first
        // dict child through nested opt/not layers; the first non-wrapper node is the value leaf (its own id is
        // the value key, uiType the control type — 'set' for a bitmask). If no inner leaf is found, the wrapper
        // itself is the value (a bare bool option) — register its own key as a bool.
        private void AddOptionField(string handlerKey, string label, Dictionary<string, object> wrapper)
        {
            int optKey = 0, notKey = 0;
            var cur = wrapper;
            for (int guard = 0; guard < 8 && cur != null; guard++)
            {
                string ty = cur.TryGetValue("type", out var tv) && tv is string ts ? ts : null;
                var dec = (cur.TryGetValue("id", out var iv) && iv is string ids) ? DecodeId(ids) : null;

                if (ty == "opt" || ty == "not")
                {
                    if (dec != null) { if (ty == "opt") optKey = dec.Value.key; else notKey = dec.Value.key; }
                    var child = FirstChildDict(cur);
                    if (child == null)
                    {
                        // wrapper with no inner control: it is a plain bool option (e.g. opt around a checkbox).
                        if (dec != null)
                            AddField(handlerKey, label, dec.Value.key, "bool", false, null, "bool", 0, null);
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
                    int[] refHandler = ExtractRefHandler(cur);
                    // 'range' must be read here as well as on the unwrapped path: EVERY firewall address field
                    // is an opt→not→network with range:1, so dropping it here made the range-END sibling decode
                    // as a netmask (see P2.33 / Docs/winbox-native-m2-protocol.md §24).
                    bool isRange = cur.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                    string allow = cur.TryGetValue("allow", out var alv) ? alv as string : null;
                    AddField(handlerKey, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(cur),
                        ty, maskKey, refHandler, optKey, notKey, isRange, allow);
                }
                return;
            }
        }

        // Registers a per-record action verb (doit/action label → SYS_CMD) under its owning handler.
        private void AddAction(string handlerKey, string label, int cmd)
        {
            string norm = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(norm)) return;
            if (!_actionsByHandler.TryGetValue(handlerKey, out var m))
            {
                m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _actionsByHandler[handlerKey] = m;
            }
            if (!m.ContainsKey(norm)) m[norm] = cmd;
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
        private static Dictionary<string, object> FirstChildDict(Dictionary<string, object> node)
        {
            if (node.TryGetValue("c", out var cv) && cv is List<object> list)
                foreach (var it in list)
                    if (it is Dictionary<string, object> d) return d;
            return null;
        }

        // Pulls the referenced table handler from an enm dropdown (values:{type:'dynamic',path:[…]}),
        // searching one level of nesting (defenum/pair wrappers carry the dynamic node inside their own
        // values/c). Returns the first dynamic path found, or null for a static/non-reference enum.
        private static int[] ExtractRefHandler(Dictionary<string, object> node)
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

        private static int[] FindDynamicPath(object node, int depth)
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

        // Pulls a static enum value list into {numeric → label}. Two .jg forms:
        //  • array  (values:{map:['off','on',…]})            → index = the numeric value (plain enum)
        //  • object (values:{map:{0:'invalid',1:'established'}}) → the explicit key; for type:'set' this key is
        //    the BIT INDEX of a bitmask (webfig types.set.tostr: `if(val&(1<<i))`), for a sparse enum it is the value.
        private static IReadOnlyDictionary<int, string> ExtractEnumMap(Dictionary<string, object> node)
        {
            if (!node.TryGetValue("values", out var vv) || !(vv is Dictionary<string, object> vals)) return null;
            if (!vals.TryGetValue("map", out var mv)) return null;
            var map = new Dictionary<int, string>();
            if (mv is List<object> arr)
            {
                for (int i = 0; i < arr.Count; i++)
                    if (arr[i] is string s) map[i] = WinboxFieldResolver.NormalizeLabel(s);
            }
            else if (mv is Dictionary<string, object> obj)
            {
                foreach (var kv in obj)
                    if (kv.Value is string s && int.TryParse(kv.Key, out int k))
                        map[k] = WinboxFieldResolver.NormalizeLabel(s);
            }
            return map.Count > 0 ? map : null;
        }

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
                if (_i >= _n) return null;
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
