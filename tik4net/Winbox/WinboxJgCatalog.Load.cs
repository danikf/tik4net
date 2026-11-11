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
    internal sealed partial class WinboxJgCatalog
    {
        // ── Loading ────────────────────────────────────────────────────────────

        // One entry of the mproxy "list" catalog: the plugin's stable name and the version-stamped file
        // that actually exists on disk.
        internal struct PluginEntry
        {
            internal string Name;    // e.g. "roteros.jg" — stable, used for diagnostics
            internal string Unique;  // e.g. "roteros-33a7039ff432.jg" — on-disk name; ".gz" appended to open
        }

        // Asks the router which plugins it serves. The set is version- and package-dependent (7.23.2 CHR
        // serves 18 .jg files, including container/iot/userman5/dude that no fixed list would have named,
        // and none of the mpls/roting4 an older one did), so it must never be hardcoded.
        private static List<PluginEntry> FetchPluginList(WinboxNativeM2Operations ops)
        {
            var result = new List<PluginEntry>();

            int handle = WinboxMproxyClient.Open(ops, "list", WinboxM2Protocol.Mproxy.OpenStatic);
            if (handle < 0) return result;
            byte[]? raw = WinboxMproxyClient.Read(ops, handle);
            if (raw == null || raw.Length == 0) return result;

            return WinboxPluginList.Parse(Encoding.UTF8.GetString(raw));
        }

        // ── Remembering the resolved plugin set (per router) ───────────────────

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
                string text = WinboxPluginList.Format(plugins);
                // Rewrite only on change: this runs on every open, and an unchanged router should not
                // produce a disk write per connection.
                if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), text, StringComparison.Ordinal))
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); // path is a file path from Path.Combine, always has a directory component
                WriteAtomically(path, text);
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
                var plugins = WinboxPluginList.ParseRemembered(File.ReadAllText(path, Encoding.UTF8));
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
        internal static void ClearSharedCatalogsForTests()
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
                // instead of an error (which once made a read failure look like a timing problem).
                // A `list` can fail because the channel it rode on has stopped answering, which is
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

            // roteros.jg first: it is served by every router, holds the core windows (interface, ip,
            // routing, system) that the resolver actually needs, and is by far the largest — so it is the
            // one that must be fetched while the channel is freshest.
            //
            // "Freshest" is a hedge, not a budget: measured 2026-07-28 on 7.23.2, one channel took 74 opens
            // / 6.8 MB with no trouble, and the failures that do occur land at no consistent offset (0 B,
            // 0.5 MB, 1.5 MB, 2.7 MB). What a failure does mean is that THIS channel is finished — it never
            // answers again, not even a 26-byte `list`, while a new connection to the same router works
            // immediately. Hence the stop in Assemble.
            var catalog = Assemble(
                plugins.OrderByDescending(p => string.Equals(p.Name, "roteros.jg", StringComparison.OrdinalIgnoreCase)),
                plugin => ReadCached(plugin, cacheDir),
                plugin => WinboxMproxyClient.FetchJg(ops, plugin.Unique),
                (plugin, text) => StoreCached(plugin, cacheDir, text),
                plugin => EvictCached(plugin, cacheDir),
                out bool complete);

            // Only publish a complete catalog for reuse; a partial one must be retried next connection.
            if (complete)
                lock (SharedCatalogsLock) { SharedCatalogs[key] = catalog; }
            return catalog;
        }

        /// <summary>
        /// Builds a catalog from <paramref name="plugins"/> in order, each read from the cache or else fetched.
        /// <paramref name="complete"/> is false when a plugin could not be had or did not parse — the catalog is then
        /// usable for this connection but must not be shared or reused.
        /// </summary>
        /// <remarks>
        /// A plugin is stored in the cache only after it parsed. The cache is content-addressed and never
        /// invalidated, so a truncated download stored there — a read that ends early returns what it got — would
        /// be served to every later connection, on every process sharing the directory, as that plugin. A cached
        /// file that does not parse is evicted for the same reason, so the next connection fetches it again.
        /// </remarks>
        internal static WinboxJgCatalog Assemble(IEnumerable<PluginEntry> plugins,
                                                 Func<PluginEntry, string?> readCached,
                                                 Func<PluginEntry, string?> fetch,
                                                 Action<PluginEntry, string> store,
                                                 Action<PluginEntry> evict,
                                                 out bool complete)
        {
            var catalog = new WinboxJgCatalog();
            complete = true;
            foreach (var plugin in plugins.ToList())
            {
                string? text = null;
                bool fromCache = false;
                try
                {
                    text = readCached(plugin);
                    fromCache = !string.IsNullOrEmpty(text);
                    if (!fromCache)
                        text = fetch(plugin);
                }
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

                if (!catalog.TryParseInto(text!)) // IsNullOrEmpty above already excluded null/empty
                {
                    // The content arrived, so the channel is fine: go on with the rest, but this catalog is
                    // missing a plugin and must not pass for the whole one.
                    complete = false;
                    if (fromCache)
                        evict(plugin);
                    TraceNote(plugin.Name + " did not parse (" + (fromCache ? "cached copy evicted" : "not cached") + ")");
                    continue;
                }
                if (!fromCache)
                    store(plugin, text!);
                TraceNote(plugin.Name + ": " + text!.Length + " chars, handlers now " + catalog._byHandler.Count);
            }
            return catalog;
        }

        // Plugin bodies are cached content-addressed, at <cacheDir>/plugins/<unique>. The `unique` name
        // carries a version+content stamp, which makes the cache correct across routers by construction:
        // any router advertising that name wants that exact file, an upgrade resolves a new name (so there
        // is nothing to invalidate), and routers sharing a plugin share one copy.
        private static string? CachePathOf(PluginEntry plugin, string? cacheDir)
            => string.IsNullOrEmpty(cacheDir) ? null : Path.Combine(Path.Combine(cacheDir, "plugins"), plugin.Unique);

        private static string? ReadCached(PluginEntry plugin, string? cacheDir)
        {
            try
            {
                string? path = CachePathOf(plugin, cacheDir);
                return path != null && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch { return null; /* cache read is best-effort */ }
        }

        private static void StoreCached(PluginEntry plugin, string? cacheDir, string text)
        {
            string? path = CachePathOf(plugin, cacheDir);
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); // path is a file path from Path.Combine, always has a directory component
                WriteAtomically(path, text);
            }
            catch { /* cache write is best-effort */ }
        }

        private static void EvictCached(PluginEntry plugin, string? cacheDir)
        {
            string? path = CachePathOf(plugin, cacheDir);
            if (path == null) return;
            try { File.Delete(path); }
            catch { /* another process may hold it; the next connection tries again */ }
        }

        // The cache directory is shared by every process of the user (the default is LocalApplicationData/tik4net), and
        // File.WriteAllText truncates the file before it writes. A process opening it in between would read part of
        // a plugin, and TryParseInto is tolerant by design, so a catalog missing half its windows would pass as
        // complete. So the file is written under a name of its own and moved into place: a reader sees the old file
        // or the new one, never half of one.
        private static void WriteAtomically(string path, string text)
            => tik4net.Connection.TikCacheFileSystem.WriteAtomicallyCore(path, text);

        // Tolerant by design — one plugin we cannot parse must not cost the whole catalog. But a silently
        // dropped plugin is a catalog that is quietly missing handlers, which surfaces later as wrong values
        // rather than as an error, so the drop is always traced. Expect this to fire first on a RouterOS
        // version whose .jg syntax has moved.
        internal bool TryParseInto(string text)
        {
            try
            {
                object tree = new WinboxJgParser(text).Parse();
                Walk(tree, null, new List<string>());
                SettleTitleNames();
                ResolveConditions();
                lock (_otherWindowKeys) _otherWindowKeys.Clear();
                return true;
            }
            catch (Exception ex)
            {
                TraceNote("PARSE FAILED (" + text.Length + " chars): " + ex.Message);
                return false;
            }
        }

        // .jg nodes that are another field's flag and not a field themselves (AddOptionField's InvertSibling).
        private readonly HashSet<Dictionary<string, object>> _flagOnlyNodes = new HashSet<Dictionary<string, object>>();

        // The type:'cond' nodes of each window (owner -> name -> node), every field of it by its raw .jg label
        // (a condition's `on` names one, nonpublic ones included), and the fields waiting for their condition.
        private readonly Dictionary<JgOwner, Dictionary<string, Dictionary<string, object>>> _condNodes =
            new Dictionary<JgOwner, Dictionary<string, Dictionary<string, object>>>();
        private readonly Dictionary<JgOwner, Dictionary<string, WinboxJgField>> _fieldsByLabel =
            new Dictionary<JgOwner, Dictionary<string, WinboxJgField>>();
        private readonly List<Tuple<WinboxJgField, JgOwner, string>> _pendingConditions =
            new List<Tuple<WinboxJgField, JgOwner, string>>();
        private string? _currentOn;

        /// <summary>
        /// Gives every field declared with <c>on:'name'</c> the condition of that name from its own window
        /// (see <see cref="WinboxJgCondition"/>). Run once the plugin is parsed, because a condition may name a
        /// field declared after the field it governs.
        /// </summary>
        private void ResolveConditions()
        {
            var built = new Dictionary<(JgOwner, string), WinboxJgCondition?>();
            foreach (var pending in _pendingConditions)
                pending.Item1.Condition = ConditionOf(pending.Item2, pending.Item3, built, 0);
            _pendingConditions.Clear();
        }

        private WinboxJgCondition? ConditionOf(JgOwner owner, string name,
            Dictionary<(JgOwner, string), WinboxJgCondition?> built, int depth)
        {
            var id = (owner, name);
            if (built.TryGetValue(id, out var known)) return known;
            built[id] = null;   // an oron cycle resolves to "no fallback" rather than recursing
            if (!(_condNodes.TryGetValue(owner, out var conds) && conds.TryGetValue(name, out var node))) return null;
            _fieldsByLabel.TryGetValue(owner, out var labels);
            var cond = WinboxJgCondition.From(name, node,
                label => labels != null && labels.TryGetValue(label, out var f) ? f : null);
            if (cond != null && depth < 8 && WinboxJgCondition.OrOnName(node) is string orName)
                cond.OrOn = ConditionOf(owner, orName, built, depth + 1);
            built[id] = cond;
            return cond;
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
    }
}
