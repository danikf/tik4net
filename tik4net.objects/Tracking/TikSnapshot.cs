using System;
using System.Collections.Generic;

namespace tik4net.Objects.Tracking
{
    /// <summary>
    /// Immutable per-entity snapshot of field values taken at load time.
    /// Used by <see cref="TikChangeTracker"/> to diff against current state before save.
    /// </summary>
    /// <remarks>
    /// One is held for every loaded entity, so the values are an array indexed by the property's ordinal in its
    /// <see cref="TikEntityMetadata"/> rather than a dictionary per entity; the name → ordinal map is the metadata's,
    /// shared by every snapshot of the type.
    /// </remarks>
    internal sealed class TikSnapshot
    {
        private readonly TikEntityMetadata _metadata;

        // by property ordinal: the serialized string value (same format as TikEntityPropertyAccessor.GetEntityValue);
        // the value itself is null for a nullable property the router never reported.
        private readonly string?[] _values;

        // null  = every property has a value in the snapshot
        // array = by ordinal, which properties have one; the others are not in the snapshot at all
        private readonly bool[]? _present;

        // null  = full load — all entity fields are tracked
        // set   = partial .proplist load — only these fields are meaningful for diffing
        internal readonly HashSet<string>? TrackedFields;

        /// <summary>Takes <paramref name="values"/> and <paramref name="present"/> over: the caller built them for this
        /// snapshot, by <paramref name="metadata"/>'s ordinals, and does not keep them.</summary>
        internal TikSnapshot(TikEntityMetadata metadata, string?[] values, bool[]? present,
                             IEnumerable<string>? trackedFields)
        {
            _metadata = metadata;
            _values = values;
            _present = present;

            TrackedFields = trackedFields != null
                ? new HashSet<string>(trackedFields, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        /// <summary>Returns true when the field was part of the original load and can be diffed.</summary>
        internal bool IsTracked(string fieldName) =>
            TrackedFields == null || TrackedFields.Contains(fieldName);

        internal bool TryGetValue(string fieldName, out string? value)
        {
            if (_metadata.TryGetPropertyOrdinal(fieldName, out int ordinal) && (_present == null || _present[ordinal]))
            {
                value = _values[ordinal];
                return true;
            }
            value = null;
            return false;
        }
    }
}
