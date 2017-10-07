using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Extension methods related to mikrotik entities.
    /// </summary>
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
                property.SetEntityValue(result, property.GetEntityValue(entity));
            }

            return result;                      
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
                    string prop1 = property.GetEntityValue(entity1);
                    string prop2 = property.GetEntityValue(entity2);

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

            string id1 = metadata.IdProperty.GetEntityValue(entity1);
            string id2 = metadata.IdProperty.GetEntityValue(entity2);

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
                sb.AppendLine(string.Format("  {0}={1}", property.FieldName, property.GetEntityValue(entity)));
            }

            return sb.ToString();
        }
    }
}
