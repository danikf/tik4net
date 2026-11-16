using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects.Tracking;
using tik4net.Helpers;

namespace tik4net.Objects
{
    public static partial class TikConnectionAsyncExtensions
    {
        /// <summary>
        /// Creates or updates the entity. Async counterpart of
        /// <see cref="TikConnectionExtensions.Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)"/>,
        /// with the same create/update, unset and change-tracking rules — they are the same code.
        /// </summary>
        /// <typeparam name="TEntity">Saved entity type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="entity">Saved entity.</param>
        /// <param name="usedFieldsFilter">List of field names (on mikrotik) which should be modified. If not null, only listed fields are modified.</param>
        /// <param name="saveMode">Controls which fields are sent — see <see cref="TikSaveMode"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task SaveAsync<TEntity>(this ITikConnection connection, TEntity entity,
            IEnumerable<string>? usedFieldsFilter = null,
            TikSaveMode saveMode = TikSaveMode.Default,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            string? id = TikConnectionExtensions.ResolveSaveId(entity, metadata);

            // Guarded per branch, exactly as the sync Save - see the note there.
            if (TikConnectionExtensions.IsCreate(metadata, id))
            {
                TikConnectionExtensions.EnsureSupported(metadata, TikEntityOperations.Add);
                var createCmd = TikConnectionExtensions.BuildCreateCommand(connection, entity, metadata, usedFieldsFilter);
                string newId = await createCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                TikConnectionExtensions.FinishCreate(connection, entity, metadata, newId);
                return;
            }

            TikConnectionExtensions.EnsureSupported(metadata, TikEntityOperations.Set);

            if (TikConnectionExtensions.NeedsFilterResolution(metadata, usedFieldsFilter))
            {
                var resolution = TikConnectionExtensions.ResolveUpdateFilter(connection, entity, metadata, saveMode);
                if (resolution.Kind == TikConnectionExtensions.UpdateFilterKind.NothingChanged)
                    return; // nothing changed — skip the API call
                if (resolution.Kind == TikConnectionExtensions.UpdateFilterKind.NeedsUnmodifiedEntity)
                {
                    // id: non-null for a non-singleton here, same reasoning as the sync Save (see its note).
                    var unmodifiedEntity = metadata.IsSingleton
                        ? await connection.LoadSingleAsync<TEntity>(cancellationToken).ConfigureAwait(false)
                        : await connection.LoadByIdAsync<TEntity>(id!, cancellationToken).ConfigureAwait(false);
                    usedFieldsFilter = TikConnectionExtensions.FullUpdateFields(connection, entity, metadata, unmodifiedEntity);
                }
                else
                    usedFieldsFilter = resolution.Filter;
            }

            var update = TikConnectionExtensions.BuildUpdateCommands(connection, entity, metadata, usedFieldsFilter, id);
            foreach (var unsetCmd in update.UnsetCommands)
                await unsetCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (update.SetCommand != null)
                await update.SetCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            TikChangeTracker.For(connection).ResetSnapshot(entity, metadata);
        }

        /// <summary>
        /// Deletes the entity. Async counterpart of
        /// <see cref="TikConnectionExtensions.Delete{TEntity}(ITikConnection, TEntity)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete.</param>
        /// <param name="entity">Entity to delete (must carry its <c>.id</c>).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentException">Entity has no <c>.id</c>.</exception>
        public static Task DeleteAsync<TEntity>(this ITikConnection connection, TEntity entity,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            return TikConnectionExtensions.BuildDeleteCommand(connection, entity)
                .ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Deletes the row with the given <c>.id</c>, without loading it first. Async counterpart of
        /// <see cref="TikConnectionExtensions.DeleteById{TEntity}(ITikConnection, string)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Entity type whose menu the row is in.</typeparam>
        /// <param name="connection">Tik connection used to delete.</param>
        /// <param name="id">The row's <c>.id</c>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="id"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The entity's menu has no <c>remove</c>, or the entity has no
        /// <c>.id</c> property (a singleton).</exception>
        public static Task DeleteByIdAsync<TEntity>(this ITikConnection connection, string id,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            return TikConnectionExtensions.BuildDeleteByIdCommand<TEntity>(connection, id)
                .ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
