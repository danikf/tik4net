using System;
using System.Collections.Generic;

namespace tik4net.Objects.Tracking
{
    /// <summary>
    /// Immutable per-entity snapshot of field values taken at load time.
    /// Used by <see cref="TikChangeTracker"/> to diff against current state before save.
    /// </summary>
    internal sealed class TikSnapshot
    {
        // field-name → serialized string value (same format as TikEntityPropertyAccessor.GetEntityValue)
        private readonly Dictionary<string, string> _values;

        // null  = full load — all entity fields are tracked
        // set   = partial .proplist load — only these fields are meaningful for diffing
        internal readonly HashSet<string> TrackedFields;

        internal TikSnapshot(IEnumerable<KeyValuePair<string, string>> values,
                              IEnumerable<string> trackedFields = null)
        {
            _values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in values)
                _values[kv.Key] = kv.Value;

            TrackedFields = trackedFields != null
                ? new HashSet<string>(trackedFields, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        /// <summary>Returns true when the field was part of the original load and can be diffed.</summary>
        internal bool IsTracked(string fieldName) =>
            TrackedFields == null || TrackedFields.Contains(fieldName);

        internal bool TryGetValue(string fieldName, out string value) =>
            _values.TryGetValue(fieldName, out value);
    }
}
