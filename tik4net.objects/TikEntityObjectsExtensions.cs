using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Extension methods related to mikrotik entities.
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikEntityObjectsExtensions
    {
        /// <summary>
        /// Creates clone of all entities in list by their fields. Usefull for storing state before list modification - <see cref="TikConnectionExtensions.SaveListDifferences"/>. 
        /// </summary>
        /// <typeparam name="TEntity">Type of entity in list</typeparam>
        /// <param name="originalList">Original list to be cloned</param>
        /// <returns>Instance of new list with cloned items.</returns>
        /// <remarks>Clones only fields marked with <see cref="TikPropertyAttribute"/>.</remarks>
        /// <seealso cref="CloneEntity"/>
        public static IEnumerable<TEntity> CloneEntityList<TEntity>(this IEnumerable<TEntity> originalList)
            where TEntity: new()
        {
            List<TEntity> result = originalList.Select(entity => CloneEntity(entity)).ToList();            

            return result;
        }

        /// <summary>
        /// Crates clone of given entity by its fields.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity.</typeparam>
        /// <param name="entity">Entity to be cloned.</param>
        /// <returns>Cloned instance of entity.</returns>
        /// <remarks>Clones only fields marked with <see cref="TikPropertyAttribute"/>.</remarks>
        public static TEntity CloneEntity<TEntity>(this TEntity entity) 
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            TEntity result = new TEntity();

            //copy all "field" properties
            foreach(var property in metadata.Properties)
            {
                // entity: TEntity is unconstrained (only `new()`), so its nullability is oblivious to the
                // compiler; the loop never receives a null instance in practice.
                property.SetEntityValue(result, property.GetEntityValue(entity!));
                // The name the original was read under, so the clone saves to the same router the same way.
                if (property.AlternateNames.Count > 0
                    && TikEntityNotes.NamesRead.Get(entity!, property.FieldName) is string nameRead)
                    TikEntityNotes.NamesRead.Record(result!, property.FieldName, nameRead);
            }

            return result;
        }

        /// <summary>
        /// The word the router printed for an enum property that read as its <see cref="TikEnumUnknownAttribute"/>
        /// member because the enum does not know it; <c>null</c> when the property read a known member.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity.</typeparam>
        /// <param name="entity">An entity read from the router.</param>
        /// <param name="propertyName">The CLR property's name — <c>nameof(FirewallFilter.Action)</c>.</param>
        /// <returns>The router's word (for a <c>[Flags]</c> property, the unknown parts, comma-separated), or <c>null</c>.</returns>
        /// <remarks>
        /// RouterOS adds words to a field's vocabulary between versions, and an old router uses words a newer one
        /// dropped. A word the enum does not know used to fail the read of the whole menu; the property now reads
        /// as <c>Unknown</c> and the word is kept here, and a save writes it back unchanged.
        /// </remarks>
        /// <exception cref="ArgumentException">The entity has no mapped property of that name.</exception>
        public static string? GetUnknownWord<TEntity>(this TEntity entity, string propertyName)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var property = metadata.Properties.FirstOrDefault(p => p.PropertyName == propertyName)
                ?? throw new ArgumentException(string.Format("{0} has no mapped property '{1}'.",
                    typeof(TEntity).Name, propertyName), nameof(propertyName));
            return TikEntityNotes.UnknownWords.Get(entity!, property.FieldName);
        }

        /// <summary>
        /// Compares two instances of entity by their fields.
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <param name="entity1">First entity.</param>
        /// <param name="entity2">Seconf entity.</param>
        /// <param name="skipIdCompare">If is true, than coparation of .id property is skipped.</param>
        /// <returns>True if all entity fields are equals.</returns>
        /// <remarks>Compares only fields marked with <see cref="TikPropertyAttribute"/>.</remarks>
        public static bool EntityEquals<TEntity>(this TEntity entity1, TEntity entity2, bool skipIdCompare = false)
        {
            return !GetDifferentFields(entity1, entity2, skipIdCompare).Any();
        }

        /// <summary>
        /// Compares two instances of entity by their fields and returns different fields (field names).
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <param name="entity1">First entity.</param>
        /// <param name="entity2">Seconf entity.</param>
        /// <param name="skipIdCompare">If is true, than coparation of .id property is skipped.</param>
        /// <returns>List of different fields.</returns>
        /// <remarks>Compares only fields marked with <see cref="TikPropertyAttribute"/>.</remarks>
        public static IEnumerable<string> GetDifferentFields<TEntity>(this TEntity entity1, TEntity entity2, bool skipIdCompare = false)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            foreach (var property in metadata.Properties)
            {
                if (!skipIdCompare || property.FieldName != TikSpecialProperties.Id)
                {
                    // entity1/entity2: TEntity is unconstrained, so the compiler treats it as possibly null;
                    // the caller passes an actual instance.
                    string? prop1 = property.GetEntityValue(entity1!);
                    string? prop2 = property.GetEntityValue(entity2!);

                    if (!string.Equals(prop1, prop2))
                        yield return property.FieldName;
                }
            }
        }

        /// <summary>
        /// Compares IDs (.id) of two instances of entity. 
        /// </summary>
        /// <param name="entity1">First entity.</param>
        /// <param name="entity2">Seconf entity.</param>
        /// <returns>True if ids are equal.</returns>
        public static bool IdEquals<TEntity>(this TEntity entity1, TEntity entity2)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            if (!metadata.HasIdProperty)
                throw new InvalidOperationException(string.Format("Can not compare ids of entity which doesn't contains property for '{0}' field.", TikSpecialProperties.Id));

            // IdProperty is non-null here: HasIdProperty was just checked above.
            string? id1 = metadata.IdProperty!.GetEntityValue(entity1!);
            string? id2 = metadata.IdProperty!.GetEntityValue(entity2!);

            return string.Equals(id1, id2);
        }

        /// <summary>
        /// Creates entity string description (for logging, etc.) by its fields.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity.</typeparam>
        /// <param name="entity">Entity instance.</param>
        /// <returns>Readable description of entity and its fields.</returns>
        public static string EntityToString<TEntity>(this TEntity entity)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            StringBuilder sb = new StringBuilder(typeof(TEntity).FullName + ":");

            foreach (var property in metadata.Properties)
            {
                sb.AppendLine(string.Format("  {0}={1}", property.FieldName, property.GetEntityValue(entity!)));
            }

            return sb.ToString();
        }
    }
}
