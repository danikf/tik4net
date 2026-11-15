using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    public static partial class TikConnectionExtensions
    {
        /// <summary>
        /// Deletes entity (.id is the key) on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <param name="entity">Entity to be deleted (.id property is the key)</param>
        /// <inheritdoc cref="Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)" path="/exception"/>
        public static void Delete<TEntity>(this ITikConnection connection, TEntity entity)
        {
            Guard.ArgumentNotNull(connection, "connection");
            BuildDeleteCommand(connection, entity).ExecuteNonQuery();
        }

        /// <summary>Shared with <c>DeleteAsync</c> — see the note on <see cref="ResolveSaveId"/>.</summary>
        internal static ITikCommand BuildDeleteCommand<TEntity>(ITikConnection connection, TEntity entity)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureSupported(metadata, TikEntityOperations.Remove);
            EnsureHasIdProperty(metadata);
            // IdProperty is non-null: EnsureHasIdProperty just verified it. The .id value itself can
            // legitimately be null/empty for a never-loaded entity — that is exactly what is checked next.
            string? id = metadata.IdProperty!.GetEntityValue(entity!);
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Entity has no .id (entity is not loaded from mikrotik router)", "entity");

            // `!`: string.IsNullOrEmpty isn't NotNullWhen-annotated on netstandard2.0, so the compiler can't
            // narrow through the throw above even though it guarantees id is non-empty here.
            return connection.CreateCommandAndParameters(metadata.EntityPath + "/remove", TikCommandParameterFormat.NameValue,
                TikSpecialProperties.Id, id!);
        }

        /// <summary>
        /// Deletes all entities of given type on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <returns>Number of deleted entities. </returns>
        /// <inheritdoc cref="LoadAll{TEntity}(ITikConnection)" path="/exception"/>
        public static int DeleteAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            var list = connection.LoadAll<TEntity>();
            int result = list.Count();

            connection.SaveListDifferences(new List<TEntity>() /*empty list as expected => delete all*/, list);

            return result;
        }
    }
}
