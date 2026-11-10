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
                                          IEnumerable<string>? trackedFields = null)
        {
            // entity: TEntity is unconstrained here, so the compiler treats it as possibly null; callers
            // always pass an actual loaded instance.
            // Taken for every loaded entity, so a plain loop into an array by property ordinal.
            var properties = metadata.PropertyList;
            var values = new string?[properties.Count];
            bool[]? present = trackedFields == null ? null : new bool[properties.Count];
            for (int i = 0; i < properties.Count; i++)
            {
                var p = properties[i];
                if (present == null)
                    values[i] = p.GetEntityValue(entity!);
                else if (trackedFields!.Contains(p.FieldName))
                {
                    values[i] = p.GetEntityValue(entity!);
                    present[i] = true;
                }
            }

            var snapshot = new TikSnapshot(metadata, values, present, trackedFields);

            lock (_lock)
            {
                _snapshots.Remove(entity!);
                _snapshots.Add(entity!, snapshot);
            }
        }

        /// <summary>
        /// Resets the snapshot to the current entity state, preserving the original set of
        /// tracked fields. Call this after a successful save to mark the entity clean.
        /// </summary>
        /// <remarks>
        /// A field the entity holds as <see cref="TikFieldState.Absent"/> keeps its previous snapshot value: a save sends
        /// nothing for it, so the router still holds what was loaded, and a later assigned <c>null</c> must still unset it.
        /// </remarks>
        public void ResetSnapshot<TEntity>(TEntity entity, TikEntityMetadata metadata)
        {
            var existing = GetSnapshot(entity!);
            if (existing == null)
            {
                TakeSnapshot(entity, metadata);
                return;
            }

            // A field outside a partial load that the save sent is known from now on.
            var properties = metadata.PropertyList;
            var values = new string?[properties.Count];
            var present = new bool[properties.Count];
            var tracked = new List<string>(properties.Count);
            for (int i = 0; i < properties.Count; i++)
            {
                var p = properties[i];
                if (!(existing.IsTracked(p.FieldName) || (p.IsWrapped && !p.IsAbsent(entity!))))
                    continue;
                values[i] = p.IsAbsent(entity!) && existing.TryGetValue(p.FieldName, out string? kept) ? kept : p.GetEntityValue(entity!);
                present[i] = true;
                tracked.Add(p.FieldName);
            }
            var snapshot = new TikSnapshot(metadata, values, tracked.Count == properties.Count ? null : present,
                existing.TrackedFields == null ? null : tracked);

            lock (_lock)
            {
                _snapshots.Remove(entity!);
                _snapshots.Add(entity!, snapshot);
            }
        }

        /// <summary>Returns the snapshot for <paramref name="entity"/>, or <c>null</c> if not tracked.</summary>
        internal TikSnapshot? GetSnapshot(object entity)
            => _snapshots.TryGetValue(entity, out var s) ? s : null;

        /// <summary>
        /// Gives <paramref name="copy"/> the snapshot <paramref name="original"/> has (none when it has none), so a copy
        /// built to check a save is checked as the original would be saved. The snapshot is immutable and shared.
        /// </summary>
        internal void ShareSnapshot(object original, object copy)
        {
            var snapshot = GetSnapshot(original);
            lock (_lock)
            {
                _snapshots.Remove(copy);
                if (snapshot != null)
                    _snapshots.Add(copy, snapshot);
            }
        }

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
        /// Returns an empty dictionary when no snapshot exists or when nothing changed.
        /// </summary>
        /// <remarks>
        /// A <see cref="TikField{T}"/> that is <see cref="TikFieldState.Absent"/> is never a change: Absent says nothing,
        /// even where the load saw a value. One outside a partial <c>.proplist</c> load that the caller assigned — a value,
        /// or <c>null</c> — is always a change, with <c>Old</c> <c>null</c>: the load did not read it, so there is nothing
        /// to compare with.
        /// </remarks>
        public IReadOnlyDictionary<string, (string? Old, string? New)> GetChanges<TEntity>(
            TEntity entity, TikEntityMetadata metadata)
        {
            var snapshot = GetSnapshot(entity!);
            if (snapshot == null)
                return _emptyChanges;

            var result = new Dictionary<string, (string?, string?)>();
            foreach (var prop in metadata.Properties)
            {
                if (prop.IsReadOnly || prop.IsAbsent(entity!))
                    continue;
                if (!snapshot.IsTracked(prop.FieldName))
                {
                    result[prop.FieldName] = (null, prop.GetEntityValue(entity!));
                    continue;
                }

                string? current = prop.GetEntityValue(entity!);
                if (snapshot.TryGetValue(prop.FieldName, out string? old) && old != current)
                    result[prop.FieldName] = (old, current);
            }
            return result;
        }

        private static readonly IReadOnlyDictionary<string, (string?, string?)> _emptyChanges
            = new Dictionary<string, (string?, string?)>();
    }
}
