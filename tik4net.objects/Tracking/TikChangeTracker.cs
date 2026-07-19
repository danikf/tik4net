using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace tik4net.Objects.Tracking
{
    /// <summary>
    /// Connection-scoped store of entity snapshots enabling diff-based saves.
    /// Instances are created and retrieved via <see cref="For"/>.
    /// Snapshots are taken automatically by <c>Load*</c> extension methods and consumed by
    /// <see cref="TikConnectionExtensions.Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)"/>.
    /// </summary>
    /// <remarks>
    /// Lifetime follows the <see cref="ITikConnection"/> instance — the tracker is released when
    /// the connection is GC'd (<see cref="ConditionalWeakTable{TKey,TValue}"/> semantics).
    /// Individual entity snapshots are also released when the entity itself is GC'd.
    /// </remarks>
    public sealed class TikChangeTracker
    {
        private static readonly ConditionalWeakTable<ITikConnection, TikChangeTracker> _perConnection
            = new ConditionalWeakTable<ITikConnection, TikChangeTracker>();

        private readonly ConditionalWeakTable<object, TikSnapshot> _snapshots
            = new ConditionalWeakTable<object, TikSnapshot>();

        // ConditionalWeakTable.AddOrUpdate is not available in netstandard2.0; guard Remove+Add.
        private readonly object _lock = new object();

        // --- Factory ---

        /// <summary>Returns the tracker bound to <paramref name="connection"/>, creating it on first call.</summary>
        public static TikChangeTracker For(ITikConnection connection)
            => _perConnection.GetOrCreateValue(connection);

        // --- Snapshot management ---

        /// <summary>
        /// Records the current serialized field values of <paramref name="entity"/> as a snapshot.
        /// Called automatically by <c>Load*</c> methods; can also be called manually.
        /// </summary>
        /// <typeparam name="TEntity">Tracked entity type.</typeparam>
        /// <param name="entity">Entity whose current field values are snapshotted.</param>
        /// <param name="metadata">Entity metadata describing the fields to serialize.</param>
        /// <param name="trackedFields">
        /// When <c>null</c> all entity fields are tracked (full load).
        /// Pass a set of field names when loading with a partial <c>.proplist</c>.
        /// </param>
        public void TakeSnapshot<TEntity>(TEntity entity, TikEntityMetadata metadata,
                                          IEnumerable<string> trackedFields = null)
        {
            var values = metadata.Properties
                .Where(p => trackedFields == null || trackedFields.Contains(p.FieldName))
                .Select(p => new KeyValuePair<string, string>(p.FieldName, p.GetEntityValue(entity)));

            var snapshot = new TikSnapshot(values, trackedFields);

            lock (_lock)
            {
                _snapshots.Remove(entity);
                _snapshots.Add(entity, snapshot);
            }
        }

        /// <summary>
        /// Resets the snapshot to the current entity state, preserving the original set of
        /// tracked fields. Call this after a successful save to mark the entity clean.
        /// </summary>
        public void ResetSnapshot<TEntity>(TEntity entity, TikEntityMetadata metadata)
        {
            var existing = GetSnapshot(entity);
            TakeSnapshot(entity, metadata, existing?.TrackedFields);
        }

        /// <summary>Returns the snapshot for <paramref name="entity"/>, or <c>null</c> if not tracked.</summary>
        internal TikSnapshot GetSnapshot(object entity)
            => _snapshots.TryGetValue(entity, out var s) ? s : null;

        /// <summary>Removes the entity from tracking. The next Save will use FullUpdate behavior.</summary>
        public void Forget(object entity)
        {
            lock (_lock) { _snapshots.Remove(entity); }
        }

        // --- Diff API ---

        /// <summary>Returns true when any tracked field has changed since the snapshot was taken.</summary>
        public bool HasChanges<TEntity>(TEntity entity, TikEntityMetadata metadata)
            => GetChanges(entity, metadata).Count > 0;

        /// <summary>
        /// Returns the changed writable fields as a dictionary of field-name → (oldValue, newValue).
        /// Only fields that are tracked (present in the snapshot) are included.
        /// Returns an empty dictionary when no snapshot exists or when nothing changed.
        /// </summary>
        public IReadOnlyDictionary<string, (string Old, string New)> GetChanges<TEntity>(
            TEntity entity, TikEntityMetadata metadata)
        {
            var snapshot = GetSnapshot(entity);
            if (snapshot == null)
                return _emptyChanges;

            var result = new Dictionary<string, (string, string)>();
            foreach (var prop in metadata.Properties)
            {
                if (prop.IsReadOnly || !snapshot.IsTracked(prop.FieldName))
                    continue;

                string current = prop.GetEntityValue(entity);
                if (snapshot.TryGetValue(prop.FieldName, out string old) && old != current)
                    result[prop.FieldName] = (old, current);
            }
            return result;
        }

        private static readonly IReadOnlyDictionary<string, (string, string)> _emptyChanges
            = new Dictionary<string, (string, string)>();
    }
}
