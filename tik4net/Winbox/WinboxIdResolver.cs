using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Winbox
{
    /// <summary>
    /// Resolves friendly RouterOS names to WinBox M2 numeric record ids by listing a table and matching its
    /// <c>name</c> field. Split out of <c>WinboxNativeConnection</c> as the reusable "name → id" lookup used both
    /// when resolving a command's <c>.id</c>/move target and when encoding a dynamic enum reference field.
    /// Pure lookup logic — depends only on the M2 operations channel, the record decoder and the <c>.jg</c> catalog.
    /// </summary>
    internal sealed class WinboxIdResolver
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog;

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        internal WinboxIdResolver(WinboxNativeM2Operations ops, WinboxJgCatalog catalog)
        {
            _ops = ops;
            _catalog = catalog;
        }

        /// <summary>
        /// Resolves a dynamic enum reference (the referenced table handler + a friendly name) to that record's
        /// numeric M2 id, by listing the referenced table and matching its <c>name</c> field. Returns
        /// <c>null</c> when not found. Shape matches the <c>resolveRef</c> delegate <c>WinboxFieldResolver.EncodeField</c> expects.
        /// </summary>
        /// <remarks>
        /// Blocking: the getall runs synchronously because the encoder that asks for it is synchronous. Awaited
        /// callers do not reach this — they pre-resolve with <see cref="ResolveReferencesAsync"/> and hand the
        /// encoder a delegate that only reads the answers.
        /// </remarks>
        internal int? ResolveReference(int[] refHandler, string name)
        {
            var refResolver = new WinboxFieldResolver(null, refHandler, _catalog, EmptyOverrides);
            int id = FindIdByName(refHandler, refResolver, name);
            return id >= 0 ? (int?)id : null;
        }

        /// <summary>
        /// Awaited batch form of <see cref="ResolveReference"/>: resolves every (referenced table, name) pair in
        /// one pass and hands back the synchronous <c>resolveRef</c> delegate
        /// <c>WinboxFieldResolver.EncodeField</c> expects, already holding the answers.
        /// </summary>
        /// <remarks>
        /// This is what lets the encoder stay synchronous without blocking the awaiting thread: every round trip
        /// happens here, under <c>await</c>, and the encoder then only reads a filled map. One getall per
        /// DISTINCT handler rather than per name — several references to the same table are matched against one
        /// snapshot, which is fewer round trips than the blocking path made.
        /// </remarks>
        internal async Task<Func<int[], string, int?>> ResolveReferencesAsync(
            IEnumerable<KeyValuePair<int[], string>> requests, CancellationToken cancellationToken)
        {
            var handlers = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var req in requests)
            {
                string hk = HandlerKey(req.Key);
                handlers[hk] = req.Key;
                if (!names.TryGetValue(hk, out var set))
                    names[hk] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(req.Value);
            }

            // Every requested name gets an entry, INCLUDING the ones that resolve to nothing: "looked up and
            // absent" is an answer the encoder must get here rather than by falling back to a blocking lookup
            // that would only reach the same conclusion one round trip later (a typo'd interface name is
            // exactly that case, and it is the one that ends in a trap the caller sees).
            var resolved = new Dictionary<string, int?>(StringComparer.Ordinal);
            foreach (var kv in handlers)
            {
                int[] handler = kv.Value;
                var refResolver = new WinboxFieldResolver(null, handler, _catalog, EmptyOverrides);
                var keyToName = refResolver.BuildKeyToApiName();
                var records = await _ops.GetAllAsync(handler, cancellationToken).ConfigureAwait(false);
                foreach (string name in names[kv.Key])
                {
                    int id = MatchIdByName(records, keyToName, name);
                    resolved[kv.Key + "\0" + name] = id >= 0 ? (int?)id : null;
                }
            }

            return (h, n) =>
            {
                if (resolved.TryGetValue(HandlerKey(h) + "\0" + n, out int? id)) return id;
                // Unreachable while the collecting pass and the encoding pass see the same fields and values —
                // they run the same encoder over the same descriptor. Kept as the blocking lookup rather than
                // a null so that if that ever stops holding, the result is a slow encode and not a reference
                // silently dropped (which the router accepts as "field not sent", see EncodeField).
                return ResolveReference(h, n);
            };
        }

        /// <summary>
        /// Looks up the M2 numeric record id whose <c>name</c> field equals <paramref name="name"/> on the given
        /// handler's table (used to map friendly names like <c>ether1</c> to a record id). Returns -1 when not found.
        /// </summary>
        internal int FindIdByName(int[] handler, WinboxFieldResolver resolver, string name)
            => MatchIdByName(_ops.GetAll(handler), resolver.BuildKeyToApiName(), name);

        /// <inheritdoc cref="FindIdByName"/>
        /// <remarks>
        /// The awaited form, used by every command path that resolves a <c>.id</c> or a move target. The lookup
        /// is deliberately NOT cached: a name must resolve against the router's current table, or a record added
        /// moments earlier by the same caller would be invisible to the next command.
        /// </remarks>
        internal async Task<int> FindIdByNameAsync(int[] handler, WinboxFieldResolver resolver, string name,
            CancellationToken cancellationToken)
            => MatchIdByName(
                await _ops.GetAllAsync(handler, cancellationToken).ConfigureAwait(false),
                resolver.BuildKeyToApiName(), name);

        // The match itself, over records someone else fetched — the one place that decides what "this row is
        // called <name>" means, so the sync, async and batch lookups cannot answer it differently. Reads the
        // name and id keys straight off the row (see WinboxRecordCodec.TryReadIdAndName): decoding the whole
        // record would pull in the referenced-table lookups of every other field on it, for two values that
        // need no decoding at all.
        private static int MatchIdByName(
            IEnumerable<Dictionary<int, Tuple<string, object>>> records,
            IReadOnlyDictionary<int, string> keyToName, string name)
        {
            int nameKey = WinboxRecordCodec.NameKeyOf(keyToName);
            foreach (var rec in records)
                if (WinboxRecordCodec.TryReadIdAndName(rec, nameKey, out int id, out string rowName)
                    && string.Equals(rowName, name, StringComparison.Ordinal))
                    return id;
            return -1;
        }

        private static string HandlerKey(int[] handler) => string.Join(",", handler);
    }
}
