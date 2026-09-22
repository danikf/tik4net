using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace tik4net.Objects
{
    /// <summary>
    /// Which name an entity's field was read under, for the fields that have
    /// <see cref="TikPropertyAttribute.AlternateNames"/> — so a save goes out under the name the entity's own
    /// router printed. Only a read under an ALTERNATE name is recorded; everything else writes
    /// <see cref="TikPropertyAttribute.FieldName"/>, as before.
    /// </summary>
    /// <remarks>
    /// Kept per entity instance, not per connection: a row comes from one router, and the entry goes away with
    /// the entity (<see cref="ConditionalWeakTable{TKey, TValue}"/>).
    /// </remarks>
    internal static class TikFieldNamesRead
    {
        private static readonly ConditionalWeakTable<object, Dictionary<string, string>> _byEntity
            = new ConditionalWeakTable<object, Dictionary<string, string>>();

        public static void Record(object entity, string fieldName, string nameRead)
        {
            var names = _byEntity.GetOrCreateValue(entity);
            lock (names)
                names[fieldName] = nameRead;
        }

        public static string? NameRead(object entity, string fieldName)
        {
            if (!_byEntity.TryGetValue(entity, out var names))
                return null;
            lock (names)
                return names.TryGetValue(fieldName, out string? name) ? name : null;
        }
    }
}
