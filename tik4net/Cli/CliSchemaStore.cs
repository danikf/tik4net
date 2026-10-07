using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The CLI menu grammar a router build answers — per menu its commands, sub-menus, the arguments of each verb, the
    /// readable and the unsettable fields — kept on disk under <c>CatalogCachePath</c> and shared by every CLI connection
    /// in the process (and, through the file, by every process) that talks to the same build.
    /// </summary>
    /// <remarks>
    /// <para><b>Keyed by the build, not the router</b>: RouterOS version plus the enabled packages
    /// (<see cref="KeyFromAnswer"/>). The lists are the router's grammar; nothing learnt from a row, no value
    /// completion (live names), and no refusal (absence depends on board and packages) is ever stored.</para>
    /// <para><b>Filled as it is used.</b> A list is stored when a connection has asked the router for it; one the store
    /// lacks is asked live and added. A save merges with what is on disk under a lock file, so processes that learn
    /// different menus at once lose nothing.</para>
    /// <para><b>Never throws.</b> A cache is an optimisation: an unreadable, corrupt or foreign file reads as empty, a
    /// failed save is skipped and retried, and every problem goes to the wire trace (channel <c>cli.schemacache</c>).
    /// The worst a cache problem can do is cost the probes the cache was meant to save.</para>
    /// <para><b>Besides the menus, the build's features and per-command facts</b> (<see cref="TryGetFeature"/>,
    /// <see cref="Facts"/>): what <c>RouterFeatureSet</c> and <c>RouterMenuFacts</c> learn, stored only where the router's
    /// answer was recognised — never a fact learnt from a timeout or from the rows a menu happened to hold.</para>
    /// <para>The format is <see cref="FormatVersion"/>, part of the path. Bump it when the file's shape changes AND
    /// when a reader that produces the stored lists changes how it parses — a file written by the old reader must not
    /// outlive its bug.</para>
    /// </remarks>
    internal sealed class CliSchemaStore
    {
        /// <summary>The store format; part of the directory (<c>cli-schema/v2/</c>), so another format is never read.</summary>
        internal const int FormatVersion = 2;

        internal const string TraceChannel = "cli.schemacache";

        /// <summary>How long a new list may wait before it is written (one write for a burst, not one per list).</summary>
        internal static TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

        /// <summary>How long a save waits for another process's save.</summary>
        internal static TimeSpan LockTimeout = TimeSpan.FromSeconds(2);

        private static readonly ConcurrentDictionary<string, CliSchemaStore> Registry =
            new ConcurrentDictionary<string, CliSchemaStore>(StringComparer.OrdinalIgnoreCase);

        static CliSchemaStore()
        {
            // A backstop for the debounce: the timer of a process that ends normally would otherwise never fire.
            // A killed process loses at most the lists of its last SaveDelay — they are asked again next time.
            try { AppDomain.CurrentDomain.ProcessExit += (s, e) => FlushAll(); }
            catch { /* not all hosts allow it; the timer and Close still save */ }
        }

        private readonly ITikCacheFileSystem _fs;
        private readonly object _sync = new object();
        // menu path → (list name → list; a null list means "asked, and the router has none")
        private readonly Dictionary<string, MenuEntry> _menus = new Dictionary<string, MenuEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _features = new Dictionary<string, bool>(StringComparer.Ordinal);
        // fact set → command texts (or "command|name" keys); a fact is only ever added
        private readonly Dictionary<string, HashSet<string>> _facts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private volatile bool _loaded;
        private long _changes;     // bumped on every new list
        private long _savedChanges; // _changes as of the last successful save
        private Timer? _timer;

        internal CliSchemaStore(string filePath, string key, ITikCacheFileSystem fileSystem)
        {
            FilePath = filePath;
            Key = key;
            _fs = fileSystem;
        }

        /// <summary>The file this store reads and writes.</summary>
        internal string FilePath { get; }

        /// <summary>The build the lists belong to (<see cref="KeyFromAnswer"/>).</summary>
        internal string Key { get; }

        /// <summary>
        /// The process-wide store for <paramref name="key"/> under <paramref name="cacheDirectory"/>, or <c>null</c> when
        /// there is no directory (the persistent cache is off) or the key is empty.
        /// </summary>
        internal static CliSchemaStore? For(string? cacheDirectory, string? key, ITikCacheFileSystem? fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory) || string.IsNullOrWhiteSpace(key))
                return null;
            try
            {
                string path = PathFor(cacheDirectory!, key!);
                return Registry.GetOrAdd(path, p => new CliSchemaStore(p, key!, fileSystem ?? TikCacheFileSystem.Default));
            }
            catch (Exception ex)
            {
                Trace("cache off: no usable path under '" + cacheDirectory + "' (" + ex.Message + ")");
                return null;
            }
        }

        /// <summary><c>&lt;dir&gt;/cli-schema/v&lt;format&gt;/&lt;version&gt;/&lt;8 hex of the key's SHA-256&gt;.json</c>.</summary>
        internal static string PathFor(string cacheDirectory, string key)
        {
            string version = key.Split('|')[0];
            var safe = new StringBuilder(version.Length);
            foreach (char c in version)
                safe.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 8).ToLowerInvariant();
            return Path.Combine(cacheDirectory, "cli-schema", "v" + FormatVersion, safe.ToString(), hash + ".json");
        }

        /// <summary>
        /// The command that answers the key: the version, the architecture and the enabled packages, on one
        /// <see cref="KeyMarker"/> line, followed by the router's own count of that text's characters. The same on
        /// RouterOS 6 and 7 (measured on 6.49.13 and 7.24.4).
        /// </summary>
        /// <remarks>The count makes the answer check itself, like a counted read: a line that lost characters on
        /// the way is refused rather than filed under a wrong key. That is what lets the MAC-layer datagram-loss
        /// heuristic stand down for this command — it condemned complete answers to it over a RoMON relay to
        /// RouterOS 6, whose per-character echo of a line this long is many datagrams.</remarks>
        internal const string KeyCommand =
            ":local p \"\"; :foreach i in=[/system package find where disabled=no] do={:set p ($p . [/system package get $i name] . \",\")}; "
            + ":local k ([/system resource get version] . \"|\" . [/system resource get architecture-name] . \"|\" . $p); "
            + ":put (\"" + KeyMarker + "\" . $k . \"|\" . [:len $k])";

        internal const string KeyMarker = "#t4n-build=";

        /// <summary>The line <see cref="KeyCommand"/> prints for <paramref name="build"/> (<c>version|arch|packages</c>).</summary>
        internal static string KeyLine(string build) => KeyMarker + build + "|" + build.Length;

        /// <summary>
        /// The key from <see cref="KeyCommand"/>'s answer: <c>7.24.4|x86_64|container,dude,…</c> — the channel word
        /// (<c>(stable)</c>) dropped, the packages sorted. <c>null</c> when the answer carries no key line, or one
        /// whose length is not the one the router counted.
        /// </summary>
        internal static string? KeyFromAnswer(string? answer)
        {
            if (answer == null)
                return null;
            foreach (string raw in answer.Split('\n'))
            {
                string line = raw.Trim();
                int at = line.IndexOf(KeyMarker, StringComparison.Ordinal);
                if (at != 0)
                    continue;
                string body = line.Substring(KeyMarker.Length);
                int counted = body.LastIndexOf('|');
                if (counted < 0 || !int.TryParse(body.Substring(counted + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int length)
                    || length != counted)
                    return null;
                string[] parts = body.Substring(0, counted).Split('|');
                if (parts.Length != 3)
                    return null;
                string version = parts[0].Trim();
                int paren = version.IndexOf(' ');
                if (paren > 0)
                    version = version.Substring(0, paren);
                if (version.Length == 0 || !char.IsDigit(version[0]))
                    return null;
                var packages = parts[2].Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)
                    .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal);
                return version + "|" + parts[1].Trim() + "|" + string.Join(",", packages);
            }
            return null;
        }

        // ── Lists ───────────────────────────────────────────────────────────────

        /// <summary>The source the menu was described from, when the store holds the menu.</summary>
        internal TikMenuSchemaSource? SourceOf(string menuPath)
        {
            EnsureLoaded();
            lock (_sync)
                return _menus.TryGetValue(menuPath, out var entry) ? entry.Source : (TikMenuSchemaSource?)null;
        }

        /// <summary>
        /// <c>true</c> when the store holds <paramref name="list"/> of <paramref name="menuPath"/>; <paramref name="value"/>
        /// is then the list, or <c>null</c> when the router has none (a menu without <c>get</c>).
        /// </summary>
        internal bool TryGet(string menuPath, string list, out IReadOnlyList<string>? value)
        {
            EnsureLoaded();
            lock (_sync)
            {
                if (_menus.TryGetValue(menuPath, out var entry) && entry.Lists.TryGetValue(list, out var stored))
                {
                    value = stored;
                    return true;
                }
            }
            value = null;
            return false;
        }

        /// <summary>Stores what the router answered for a list; replaces a stored one (a re-asked list wins).</summary>
        internal void Put(string menuPath, TikMenuSchemaSource source, string list, IEnumerable<string>? value)
        {
            try
            {
                EnsureLoaded();
                var copy = value?.ToArray();
                lock (_sync)
                {
                    if (!_menus.TryGetValue(menuPath, out var entry))
                        _menus[menuPath] = entry = new MenuEntry(source);
                    if (entry.Lists.TryGetValue(list, out var old) && SameList(old, copy) && entry.Source == source)
                        return;
                    entry.Source = source;
                    entry.Lists[list] = copy;
                    _changes++;
                    ArmTimer();
                }
            }
            catch (Exception ex)
            {
                Trace("could not record " + menuPath + " " + list + ": " + ex.Message);
            }
        }

        // ── Features and facts ──────────────────────────────────────────────────

        /// <summary><c>true</c> when the store holds <paramref name="name"/>; <paramref name="value"/> is then its answer.</summary>
        internal bool TryGetFeature(string name, out bool value)
        {
            EnsureLoaded();
            lock (_sync)
                return _features.TryGetValue(name, out value);
        }

        /// <summary>Stores what the router answered for a feature; a later answer replaces it.</summary>
        internal void PutFeature(string name, bool value)
        {
            try
            {
                EnsureLoaded();
                lock (_sync)
                {
                    if (_features.TryGetValue(name, out bool old) && old == value)
                        return;
                    _features[name] = value;
                    _changes++;
                    ArmTimer();
                }
            }
            catch (Exception ex)
            {
                Trace("could not record feature " + name + ": " + ex.Message);
            }
        }

        /// <summary>The members of a fact set (a copy; empty when the store has none).</summary>
        internal IReadOnlyCollection<string> Facts(string set)
        {
            EnsureLoaded();
            lock (_sync)
                return _facts.TryGetValue(set, out var members) ? members.ToArray() : new string[0];
        }

        /// <summary>Adds a member to a fact set.</summary>
        internal void PutFact(string set, string member)
        {
            try
            {
                EnsureLoaded();
                lock (_sync)
                {
                    if (!_facts.TryGetValue(set, out var members))
                        _facts[set] = members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!members.Add(member))
                        return;
                    _changes++;
                    ArmTimer();
                }
            }
            catch (Exception ex)
            {
                Trace("could not record fact " + set + " " + member + ": " + ex.Message);
            }
        }

        private static bool SameList(string[]? a, string[]? b)
            => a == null ? b == null : b != null && a.SequenceEqual(b, StringComparer.Ordinal);

        // ── Load and save ───────────────────────────────────────────────────────

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            lock (_sync)
            {
                if (_loaded)
                    return;
                _loaded = true;
                MergeFromDisk();
            }
        }

        // Under _sync. Adds what the file holds and memory does not; memory wins for a list in both.
        private void MergeFromDisk()
        {
            string? text;
            try
            {
                text = _fs.ReadAllText(FilePath);
            }
            catch (Exception ex)
            {
                Trace("cannot read " + FilePath + ": " + ex.Message);
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                using (var doc = JsonDocument.Parse(text!))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number
                        || format.GetInt32() != FormatVersion
                        || !root.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String
                        || key.GetString() != Key)
                    {
                        Trace("ignoring " + FilePath + ": another format or build");
                        return;
                    }
                    if (root.TryGetProperty("menus", out var menus) && menus.ValueKind == JsonValueKind.Object)
                        foreach (var menu in menus.EnumerateObject())
                            MergeMenu(menu);
                    if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object)
                        foreach (var feature in features.EnumerateObject())
                            if (!_features.ContainsKey(feature.Name)   // memory wins
                                && (feature.Value.ValueKind == JsonValueKind.True || feature.Value.ValueKind == JsonValueKind.False))
                                _features[feature.Name] = feature.Value.GetBoolean();
                    if (root.TryGetProperty("facts", out var facts) && facts.ValueKind == JsonValueKind.Object)
                        foreach (var set in facts.EnumerateObject())
                        {
                            if (set.Value.ValueKind != JsonValueKind.Array)
                                continue;
                            if (!_facts.TryGetValue(set.Name, out var members))
                                _facts[set.Name] = members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var member in set.Value.EnumerateArray())
                                if (member.ValueKind == JsonValueKind.String)
                                    members.Add(member.GetString()!);   // a union: a fact is only ever added
                        }
                }
            }
            catch (Exception ex)
            {
                Trace("ignoring " + FilePath + ": not a readable cache (" + ex.Message + ")");
            }
        }

        // One malformed menu or list is dropped; the rest is used.
        private void MergeMenu(JsonProperty menu)
        {
            try
            {
                if (menu.Value.ValueKind != JsonValueKind.Object
                    || !menu.Value.TryGetProperty("source", out var sourceEl) || sourceEl.ValueKind != JsonValueKind.String
                    || !Enum.TryParse(sourceEl.GetString(), out TikMenuSchemaSource source)
                    || !menu.Value.TryGetProperty("lists", out var lists) || lists.ValueKind != JsonValueKind.Object)
                    return;
                if (!_menus.TryGetValue(menu.Name, out var entry))
                    _menus[menu.Name] = entry = new MenuEntry(source);
                foreach (var list in lists.EnumerateObject())
                {
                    if (entry.Lists.ContainsKey(list.Name))
                        continue;   // memory wins
                    if (list.Value.ValueKind == JsonValueKind.Null)
                        entry.Lists[list.Name] = null;
                    else if (list.Value.ValueKind == JsonValueKind.Array
                             && list.Value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String))
                        entry.Lists[list.Name] = list.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
                }
            }
            catch (Exception ex)
            {
                Trace("dropping cached " + menu.Name + ": " + ex.Message);
            }
        }

        /// <summary>Writes what has changed since the last save, merged with the file; never throws.</summary>
        internal void Flush()
        {
            try
            {
                lock (_sync)
                {
                    if (_changes == _savedChanges)
                        return;
                }
                using (var held = _fs.TryLock(FilePath, LockTimeout))
                {
                    if (held == null)
                    {
                        Trace("save deferred: " + FilePath + " is locked by another save");
                        lock (_sync) ArmTimer();
                        return;
                    }
                    string text;
                    long saving;
                    lock (_sync)
                    {
                        MergeFromDisk();
                        saving = _changes;
                        text = Serialize();
                    }
                    _fs.WriteAtomically(FilePath, text);
                    lock (_sync)
                        _savedChanges = Math.Max(_savedChanges, saving);
                }
            }
            catch (Exception ex)
            {
                Trace("save failed, retried later: " + FilePath + " (" + ex.Message + ")");
                try { lock (_sync) ArmTimer(); } catch { }
            }
        }

        // Under _sync.
        private string Serialize()
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteNumber("format", FormatVersion);
                    w.WriteString("key", Key);
                    w.WriteString("writtenBy", "tik4net " + typeof(CliSchemaStore).Assembly.GetName().Version);
                    w.WriteStartObject("menus");
                    foreach (var menu in _menus.OrderBy(m => m.Key, StringComparer.Ordinal))
                    {
                        w.WriteStartObject(menu.Key);
                        w.WriteString("source", menu.Value.Source.ToString());
                        w.WriteStartObject("lists");
                        foreach (var list in menu.Value.Lists.OrderBy(l => l.Key, StringComparer.Ordinal))
                        {
                            if (list.Value == null)
                            {
                                w.WriteNull(list.Key);
                                continue;
                            }
                            w.WriteStartArray(list.Key);
                            foreach (string item in list.Value)
                                w.WriteStringValue(item);
                            w.WriteEndArray();
                        }
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                    w.WriteStartObject("features");
                    foreach (var feature in _features.OrderBy(f => f.Key, StringComparer.Ordinal))
                        w.WriteBoolean(feature.Key, feature.Value);
                    w.WriteEndObject();
                    w.WriteStartObject("facts");
                    foreach (var set in _facts.OrderBy(f => f.Key, StringComparer.Ordinal))
                    {
                        w.WriteStartArray(set.Key);
                        foreach (string member in set.Value.OrderBy(m => m, StringComparer.Ordinal))
                            w.WriteStringValue(member);
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        // Under _sync. (Re)arms the debounce. The callback catches everything: an exception on a timer thread would
        // end the process.
        private void ArmTimer()
        {
            if (_timer == null)
                _timer = new Timer(_ => { try { Flush(); } catch { } }, null, SaveDelay, Timeout.InfiniteTimeSpan);
            else
                _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Saves every store of the process; never throws.</summary>
        internal static void FlushAll()
        {
            foreach (var store in Registry.Values)
                store.Flush();
        }

        /// <summary>Forgets the process's stores (tests: the next <see cref="For"/> reads the file again).</summary>
        internal static void ResetRegistryForTests() => Registry.Clear();

        private static void Trace(string note)
        {
            try { TikWireTrace.Emit(TraceChannel, TikWireDir.Note, note); }
            catch { }
        }

        private sealed class MenuEntry
        {
            internal MenuEntry(TikMenuSchemaSource source) { Source = source; }
            internal TikMenuSchemaSource Source;
            internal readonly Dictionary<string, string[]?> Lists = new Dictionary<string, string[]?>(StringComparer.Ordinal);
        }
    }
}
