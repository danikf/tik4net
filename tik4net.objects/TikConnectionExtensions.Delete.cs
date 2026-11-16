using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;
using tik4net.Helpers;

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
            Guard.ArgumentNotNull(connection, nameof(connection));
            BuildDeleteCommand(connection, entity).ExecuteNonQuery();
        }

        /// <summary>
        /// Deletes the row with the given <c>.id</c> on the router, without loading it first. Sends the same
        /// <c>remove</c> as <see cref="Delete{TEntity}(ITikConnection, TEntity)"/> does for a loaded entity.
        /// </summary>
        /// <remarks>
        /// The type argument is required: it names the menu (<c>connection.DeleteById&lt;FirewallFilter&gt;("*1A")</c>).
        /// An <c>.id</c> no row has is the router's refusal, as for <see cref="Delete{TEntity}(ITikConnection, TEntity)"/>.
        /// </remarks>
        /// <typeparam name="TEntity">Entity type whose menu the row is in.</typeparam>
        /// <param name="connection">Tik connection used to delete.</param>
        /// <param name="id">The row's <c>.id</c>, as <see cref="LoadById{TEntity}(ITikConnection, string)"/> takes it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="id"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The entity's menu has no <c>remove</c>, or the entity has no
        /// <c>.id</c> property (a singleton).</exception>
        /// <inheritdoc cref="Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)" path="/exception"/>
        public static void DeleteById<TEntity>(this ITikConnection connection, string id)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            BuildDeleteByIdCommand<TEntity>(connection, id).ExecuteNonQuery();
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
            return RemoveCommand(connection, metadata, id!);
        }

        /// <summary>Shared with <c>DeleteByIdAsync</c>.</summary>
        internal static ITikCommand BuildDeleteByIdCommand<TEntity>(ITikConnection connection, string id)
        {
            Guard.ArgumentNotNullOrEmptyString(id, nameof(id));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureSupported(metadata, TikEntityOperations.Remove);
            EnsureHasIdProperty(metadata);
            return RemoveCommand(connection, metadata, id);
        }

        private static ITikCommand RemoveCommand(ITikConnection connection, TikEntityMetadata metadata, string id)
            => connection.CreateCommandAndParameters(metadata.EntityPath + "/remove", TikCommandParameterFormat.NameValue,
                TikSpecialProperties.Id, id);

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
