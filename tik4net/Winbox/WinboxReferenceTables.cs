using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// The one service that translates between a referenced table's row names and its M2 record ids, in both
    /// directions: id → name for the decoder (<see cref="WinboxRecordCodec"/>), name → id for the encoder and the
    /// command paths (a <c>.id</c>, a move destination, a dynamic-enum reference), and a bit set's member table for
    /// both. One per connection.
    /// </summary>
    /// <remarks>
    /// <para><b>One name-key rule.</b> A row's name sits at the table's <c>name</c> key, else at the key its window
    /// declares as <c>nameval</c> (<see cref="NameKeysOf(WinboxHandler)"/>) — tried in that order per row, by id → name and
    /// name → id alike, so a reference that reads back as a name can be written with that name. A bit set's member is
    /// the other way round: the <c>nameval</c> key when the window declares one (for the policy table [13,3] 'Name' is
    /// the sentence "read router configuration" and 'Alias' the word the API prints, <c>read</c>), else <c>name</c>.</para>
    /// <para><b>Three lifetimes.</b> The id → name maps are cached until the next write (<see cref="Forget"/>): a row
    /// added or renamed through this connection is a name the cache has never seen, and a stale entry does not read as
    /// an error — the reference decodes to the bare id, which looks like a value. The member maps are cached for the
    /// connection: they are the router's fixed vocabulary of permissions, not user records. A name → id lookup is never
    /// cached: a name must resolve against the router's current table, or a record added moments earlier by the same
    /// caller would be invisible to the next command. A table that cannot be read is never memoized, so a transient
    /// failure is retried rather than made permanent.</para>
    /// </remarks>
    internal sealed class WinboxReferenceTables
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog;

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        // Guards the three caches: a multiplexed connection can decode two commands' rows at once, and the awaited
        // prime writes while a synchronous decode may be reading.
        private readonly object _lock = new object();
        // table → id → name, dropped on every write.
        private readonly Dictionary<WinboxHandler, Dictionary<int, string>> _names =
            new Dictionary<WinboxHandler, Dictionary<int, string>>();
        // table → bit index → member name, kept for the connection.
        private readonly Dictionary<WinboxHandler, IReadOnlyDictionary<int, string>> _members =
            new Dictionary<WinboxHandler, IReadOnlyDictionary<int, string>>();
        // table → the table's (name, nameval) keys, from the catalog alone.
        private readonly Dictionary<WinboxHandler, (int NameKey, int NameValKey)> _nameKeys =
            new Dictionary<WinboxHandler, (int NameKey, int NameValKey)>();

        internal WinboxReferenceTables(WinboxNativeM2Operations ops, WinboxJgCatalog catalog)
        {
            _ops = ops;
            _catalog = catalog;
        }

        // ── The name-key rule ───────────────────────────────────────────────────

        /// <summary>
        /// The two keys a row of <paramref name="table"/> can be named at: its <c>name</c> field and the field its window
        /// declares as <c>nameval</c> (<c>-1</c> for either it has none). From the catalog alone, cached per table.
        /// </summary>
        /// <remarks>
        /// The <c>nameval</c> key cannot be decided from the <c>name</c> key alone: the resolver seeds <c>name</c> at
        /// <c>0x10006</c> for a table whose window has no Name box. <c>/queue/type</c> is one: its rows are named by
        /// 'Type Name' at <c>0x16</c>, and <c>/queue/simple</c>'s queue read back as <c>*FFFFFFFA</c> where the API says
        /// pcq-upload-default (7.24.2).
        /// </remarks>
        private (int NameKey, int NameValKey) NameKeysOf(WinboxHandler table)
        {
            lock (_lock)
                if (_nameKeys.TryGetValue(table, out var cached)) return cached;
            var keys = RefNameKeys(_catalog, table);
            lock (_lock) _nameKeys[table] = keys;
            return keys;
        }

        /// <summary><see cref="NameKeysOf(WinboxHandler)"/>, uncached, for a catalog and table.</summary>
        internal static (int nameKey, int nameValKey) RefNameKeys(WinboxJgCatalog catalog, WinboxHandler table)
            => NameKeysOf(catalog, table, new WinboxFieldResolver(null, table, catalog, EmptyOverrides).BuildKeyToApiName());

        /// <summary>
        /// <see cref="RefNameKeys"/> over a key map the caller already holds — a command path's own resolver, whose
        /// session overrides and label hints the lookup of its <c>.id</c> honours.
        /// </summary>
        private static (int nameKey, int nameValKey) NameKeysOf(WinboxJgCatalog catalog, WinboxHandler handler,
            IReadOnlyDictionary<int, string> keyToApiName)
        {
            int nameKey = NameKeyOf(keyToApiName);
            int nameValKey = -1;
            string? nameVal = catalog.GetNameValField(handler);
            if (nameVal != null)
                foreach (var kv in keyToApiName)
                    if (string.Equals(kv.Value, nameVal, StringComparison.OrdinalIgnoreCase)
                        && !WinboxM2Protocol.TypedKey.IsQualified(kv.Key))
                    { nameValKey = kv.Key; break; }
            return (nameKey, nameValKey);
        }

        /// <summary>
        /// The M2 key of a table's <c>name</c> field, or -1 when it has none.
        /// </summary>
        private static int NameKeyOf(IReadOnlyDictionary<int, string> keyToApiName)
        {
            if (keyToApiName != null)
                foreach (var kv in keyToApiName)
                    // Only a WIRE key can be looked up in a record: an arrayness-qualified registration
                    // (WinboxM2Protocol.TypedKey) is the resolver's own bookkeeping and matches nothing.
                    if (kv.Value == "name" && !WinboxM2Protocol.TypedKey.IsQualified(kv.Key)) return kv.Key;
            return -1;
        }

        /// <summary>
        /// Reads a row's numeric record id and the name at <paramref name="nameKey"/>, the two fields every name ↔ id
        /// translation needs. Returns <c>false</c> when the row carries neither, or an id that is not a number.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT a <c>DecodeRecord</c> — a name and an id need no typed decoding, and running the full
        /// decoder to read them drags in the referenced-table lookups of every OTHER field on the row. The id lookup then
        /// paid round trips for reference tables it never used, and — because the caller stops at the first matching row —
        /// fetched a different set of them depending on where in the table the match happened to fall.
        /// <para>A row whose id is not numeric is skipped rather than guessed at, so the value it would have named stays
        /// raw instead of picking up a neighbour's name; traced, because a cached map makes such an omission permanent
        /// and invisible.</para>
        /// </remarks>
        private static bool TryReadIdAndName(
            M2Record row, int nameKey, out int id, out string? name)
        {
            id = 0;
            name = null;
            if (nameKey < 0 || row == null) return false;
            if (!row.TryGetValue(WinboxM2Protocol.RecordKey.Id, out var idt) || idt.Value == null) return false;
            if (!row.TryGetValue(nameKey, out var nt) || nt.Value == null) return false;
            if (!WinboxValueFormat.TryToInt64(idt.Value, out long rowId))
            {
                WinboxRecordCodec.TraceNonNumeric("reference table id", idt.Value);
                return false;
            }
            id = unchecked((int)rowId);
            name = nt.Value.ToString();
            return true;
        }

        // A row's name under the rule: the name key, else the nameval key.
        private static bool TryReadName((int NameKey, int NameValKey) keys, M2Record row,
            out int id, out string? name)
            => TryReadIdAndName(row, keys.NameKey, out id, out name)
               || TryReadIdAndName(row, keys.NameValKey, out id, out name);

        // ── id → name (the decoder) ─────────────────────────────────────────────

        /// <summary>
        /// The name of row <paramref name="id"/> of <paramref name="table"/>, or <c>null</c> when the table has no such
        /// row or cannot be read. A table not cached yet is read here, blocking — reached when the awaited path did not
        /// prime it (a synchronous monitor round, or a value whose table was still uncached when the collecting pass ran).
        /// </summary>
        /// <remarks>
        /// With <paramref name="collect"/> set, nothing is looked up: an uncached table is noted in it and the answer is
        /// <c>null</c>. That is the ONLY place that decides a reference needs its table read, so the awaited prefetch and
        /// the decode cannot disagree about which tables those are.
        /// </remarks>
        internal string? NameOf(WinboxHandler table, int id, HashSet<(WinboxHandler Table, bool Members)>? collect)
        {
            Dictionary<int, string>? map;
            lock (_lock) _names.TryGetValue(table, out map);
            if (collect != null)
            {
                if (map == null) collect.Add((table, false));
                return null;
            }
            if (map == null)
            {
                try { map = BuildNameMap(table, _ops.GetAll(table)); }
                catch (Exception ex)
                {
                    // The lookup falls back to the numeric id, which reads as a plausible value rather than as an
                    // error — so a single transient getall failure must not be memoized.
                    TraceUnreadable(table, false, ex);
                    return null;
                }
                lock (_lock) _names[table] = map;
            }
            return map.TryGetValue(id, out var n) ? n : null;
        }

        /// <summary>
        /// The member map of a bit set whose members come from a TABLE rather than a static <c>.jg</c> map
        /// (<c>/user/group</c> policies, a script's or scheduler entry's policy): <c>bit index → member name</c>. The same
        /// collect / cached / blocking structure as <see cref="NameOf"/>.
        /// </summary>
        internal IReadOnlyDictionary<int, string>? MembersOf(WinboxHandler table,
            HashSet<(WinboxHandler Table, bool Members)>? collect)
        {
            IReadOnlyDictionary<int, string>? map;
            lock (_lock) _members.TryGetValue(table, out map);
            if (collect != null)
            {
                if (map == null) collect.Add((table, true));
                return null;
            }
            if (map != null) return map;
            try { map = BuildMemberMap(table, _ops.GetAll(table)); }
            catch (Exception ex)
            {
                TraceUnreadable(table, true, ex);
                return null;   // not memoized: the encoder refuses loudly rather than guessing
            }
            lock (_lock) _members[table] = map;
            return map;
        }

        /// <summary>
        /// Whether any table a field of <paramref name="fields"/> references is not cached yet — the cheap exit before a
        /// collecting decode, which is the steady state.
        /// </summary>
        internal bool AnyUncached(IEnumerable<WinboxJgField> fields)
        {
            lock (_lock)
                foreach (var jf in fields)
                {
                    if (jf?.RefHandler == null) continue;
                    foreach (WinboxHandler table in jf.RefHandlers ?? new[] { jf.RefHandler.Value })
                        if (!_names.ContainsKey(table)) return true;
                }
            return false;
        }

        /// <summary>
        /// Reads, under await, every table a collecting decode noted (<see cref="NameOf"/> / <see cref="MembersOf"/> with
        /// <c>collect</c>), as the map it asks for — a dropdown's names or a bit set's members.
        /// </summary>
        internal async Task PrimeAsync(IEnumerable<(WinboxHandler Table, bool Members)> wanted,
            CancellationToken cancellationToken)
        {
            foreach (var (table, members) in wanted)
            {
                try
                {
                    var rows = await _ops.GetAllAsync(table, cancellationToken).ConfigureAwait(false);
                    if (members)
                    {
                        var map = BuildMemberMap(table, rows);
                        lock (_lock) _members[table] = map;
                    }
                    else
                    {
                        var map = BuildNameMap(table, rows);
                        lock (_lock) _names[table] = map;
                    }
                }
                catch (Exception ex)
                {
                    TraceUnreadable(table, members, ex);   // not memoized — the decode falls back to the blocking lookup
                }
            }
        }

        /// <summary>
        /// Drops the id → name maps. Called after any write: an add, a rename or a remove changes what the referenced
        /// tables contain. The member maps stay — they are the router's fixed vocabulary.
        /// </summary>
        internal void Forget()
        {
            lock (_lock) _names.Clear();
        }

        // Build the id → name map of a referenced table from its rows.
        private Dictionary<int, string> BuildNameMap(WinboxHandler table, IEnumerable<M2Record> rows)
        {
            var keys = NameKeysOf(table);
            var map = new Dictionary<int, string>();
            foreach (var r in rows)
                // TryReadIdAndName only returns true after successfully assigning a non-null name.
                if (TryReadName(keys, r, out int rowId, out string? rowName))
                    map[rowId] = rowName!;
            return map;
        }

        // Build the member map of a bit set's referenced table: bit index → member name, where the bit index IS the
        // referenced record's id (webfig's SetView keys its checkboxes by obj.ufe0001 and ORs 1<<id).
        private IReadOnlyDictionary<int, string> BuildMemberMap(WinboxHandler table, IEnumerable<M2Record> rows)
            => BuildMemberMap(NameKeysOf(table), rows);

        /// <summary>
        /// The member map of a bit set's referenced table from its rows, for a catalog: <c>bit index → member name</c>.
        /// </summary>
        internal static Dictionary<int, string> BuildMemberMap(WinboxJgCatalog catalog, WinboxHandler table,
            IEnumerable<M2Record> rows)
            => BuildMemberMap(RefNameKeys(catalog, table), rows);

        private static Dictionary<int, string> BuildMemberMap((int NameKey, int NameValKey) keys,
            IEnumerable<M2Record> rows)
        {
            int memberKey = keys.NameValKey >= 0 ? keys.NameValKey : keys.NameKey;
            var map = new Dictionary<int, string>();
            foreach (var r in rows)
                if (TryReadIdAndName(r, memberKey, out int rowId, out string? rowName))
                    map[rowId] = rowName!;
            return map;
        }

        private static void TraceUnreadable(WinboxHandler table, bool members, Exception ex)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                    "reference table " + table + (members ? " members" : "") + " unreadable, values stay numeric: "
                    + ex.Message);
        }

        // ── name → id (the encoder and the command paths) ──────────────────────

        /// <summary>
        /// Resolves a dynamic enum reference (the referenced table + a friendly name) to that record's numeric M2 id, or
        /// <c>null</c> when no row is called that. The shape of the <c>resolveRef</c> delegate
        /// <c>WinboxFieldResolver.EncodeField</c> expects.
        /// </summary>
        /// <remarks>
        /// Blocking: the getall runs synchronously because the encoder that asks for it is synchronous. Awaited callers
        /// do not reach this — they pre-resolve with <see cref="ResolveReferencesAsync"/>.
        /// </remarks>
        internal int? ResolveReference(WinboxHandler table, string name)
            => MatchIdByName(_ops.GetAll(table), NameKeysOf(table), name);

        /// <summary>
        /// Awaited batch form of <see cref="ResolveReference"/>: resolves every (referenced table, name) pair in one pass
        /// and hands back the synchronous <c>resolveRef</c> delegate, already holding the answers.
        /// </summary>
        /// <remarks>
        /// One getall per DISTINCT table rather than per name — several references to the same table are matched against
        /// one snapshot. Every requested name gets an entry, INCLUDING the ones that resolve to nothing: "looked up and
        /// absent" is an answer the encoder must get here rather than by a blocking lookup that would only reach the same
        /// conclusion one round trip later (a typo'd interface name is that case, and it ends in a trap the caller sees).
        /// </remarks>
        internal async Task<Func<WinboxHandler, string, int?>> ResolveReferencesAsync(
            IEnumerable<KeyValuePair<WinboxHandler, string>> requests, CancellationToken cancellationToken)
        {
            var names = new Dictionary<WinboxHandler, HashSet<string>>();
            foreach (var req in requests)
            {
                if (!names.TryGetValue(req.Key, out var set))
                    names[req.Key] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(req.Value);
            }

            var resolved = new Dictionary<(WinboxHandler, string), int?>();
            foreach (var kv in names)
            {
                var keys = NameKeysOf(kv.Key);
                var records = await _ops.GetAllAsync(kv.Key, cancellationToken).ConfigureAwait(false);
                foreach (string name in kv.Value)
                    resolved[(kv.Key, name)] = MatchIdByName(records, keys, name);
            }

            return (h, n) =>
            {
                if (resolved.TryGetValue((h, n), out int? id)) return id;
                // Unreachable while the collecting pass and the encoding pass see the same fields and values — they run
                // the same encoder over the same descriptor. Kept as the blocking lookup rather than a null so that if
                // that ever stops holding, the result is a slow encode and not a reference silently dropped.
                return ResolveReference(h, n);
            };
        }

        /// <summary>
        /// The M2 record id of the row of <paramref name="handler"/>'s table called <paramref name="name"/>, read through
        /// the command path's own <paramref name="resolver"/> (used to map friendly names like <c>ether1</c> to a record
        /// id). Returns <c>null</c> when not found — not -1: a record id is a u32 and the router's built-in rows sit at
        /// the top of its range (<c>/queue/type</c>'s pcq-upload-default is <c>*FFFFFFFA</c>), so every int is an id some
        /// row can have.
        /// </summary>
        internal async Task<int?> FindIdByNameAsync(WinboxHandler handler, WinboxFieldResolver resolver, string name,
            CancellationToken cancellationToken)
            => MatchIdByName(
                await _ops.GetAllAsync(handler, cancellationToken).ConfigureAwait(false),
                NameKeysOf(_catalog, handler, resolver.BuildKeyToApiName()), name);

        /// <summary>
        /// The member map of a bit set's referenced table for the encoder (<see cref="MembersOf"/>, blocking), or
        /// <c>null</c> when the table cannot be read, which the encoder turns into a refusal rather than a write that
        /// silently clears the field.
        /// </summary>
        internal IReadOnlyDictionary<int, string>? ReadMemberTable(WinboxHandler table) => MembersOf(table, null);

        /// <summary>
        /// Awaited form of <see cref="ReadMemberTable"/>, so a command that writes such a field does its table read under
        /// <c>await</c> and the encoder then only reads the cache.
        /// </summary>
        internal async Task PrimeMemberTableAsync(WinboxHandler table, CancellationToken cancellationToken)
        {
            lock (_lock)
                if (_members.ContainsKey(table)) return;
            await PrimeAsync(new[] { (table, true) }, cancellationToken).ConfigureAwait(false);
        }

        // The match itself, over records someone else fetched — the one place that decides what "this row is called
        // <name>" means, so the blocking, batch and command lookups cannot answer it differently.
        private static int? MatchIdByName(IEnumerable<M2Record> records,
            (int NameKey, int NameValKey) keys, string name)
        {
            foreach (var rec in records)
                if (TryReadName(keys, rec, out int id, out string? rowName)
                    && string.Equals(rowName, name, StringComparison.Ordinal))
                    return id;
            return null;
        }
    }
}
