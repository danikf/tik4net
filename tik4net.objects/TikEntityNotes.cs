using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace tik4net.Objects
{
    /// <summary>
    /// Something the mapper learned about one entity's field while reading it, which a later save or a caller
    /// needs and the property value itself cannot carry — kept per entity instance, keyed by field name.
    /// </summary>
    /// <remarks>
    /// Per entity, not per connection: a row comes from one router, and the notes go away with the entity
    /// (<see cref="ConditionalWeakTable{TKey, TValue}"/>).
    /// </remarks>
    internal sealed class TikEntityNotes
    {
        /// <summary>
        /// The name a field was read under when it was an ALTERNATE one (<see cref="TikPropertyAttribute.AlternateNames"/>),
        /// so a save goes out under the name the entity's own router printed.
        /// </summary>
        public static readonly TikEntityNotes NamesRead = new TikEntityNotes();

        /// <summary>
        /// The word a field was printed with when its enum did not know it and it read as the
        /// <see cref="TikEnumUnknownAttribute"/> member — returned to the caller, and written back unchanged.
        /// </summary>
        public static readonly TikEntityNotes UnknownWords = new TikEntityNotes();

        private readonly ConditionalWeakTable<object, Dictionary<string, string>> _byEntity
            = new ConditionalWeakTable<object, Dictionary<string, string>>();

        private TikEntityNotes() { }

        public void Record(object entity, string fieldName, string note)
        {
            var notes = _byEntity.GetOrCreateValue(entity);
            lock (notes)
                notes[fieldName] = note;
        }

        public void Forget(object entity, string fieldName)
        {
            if (!_byEntity.TryGetValue(entity, out var notes))
                return;
            lock (notes)
                notes.Remove(fieldName);
        }

        public string? Get(object entity, string fieldName)
        {
            if (!_byEntity.TryGetValue(entity, out var notes))
                return null;
            lock (notes)
                return notes.TryGetValue(fieldName, out string? note) ? note : null;
        }
    }
}
