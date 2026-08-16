using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    /// <summary>
    /// Task-based CRUD on the O/R mapper — the counterpart of <see cref="TikConnectionExtensions"/>, one
    /// method per synchronous original.
    /// <code>
    /// var addresses = await connection.LoadAllAsync&lt;IpAddress&gt;(cancellationToken);
    /// addresses[0].Comment = "changed";
    /// await connection.SaveAsync(addresses[0], cancellationToken: cancellationToken);
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are not re-implementations. Every rule that is not "wait for the router" — how many rows a
    /// load may return and which exception says otherwise, the change-tracker snapshot that lets a later
    /// <c>Save</c> send only what changed, what counts as a create, which fields are unset rather than set,
    /// and that the unsets go out before the set — lives once in <see cref="TikConnectionExtensions"/> and is
    /// called from both halves. The difference between a method here and its twin there is the awaits.
    /// </para>
    /// <para>
    /// Every method dispatches through the <see cref="ITikCommandAsync"/> surface, so a transport that does
    /// not declare <see cref="TikConnectionCapability.AsyncCommands"/> throws
    /// <see cref="TikConnectionCapabilityNotSupportedException"/> rather than block a thread and call the
    /// result asynchronous. What a <see cref="CancellationToken"/> can actually stop differs by transport —
    /// see <see cref="TikConnectionCapability.CancelInFlight"/>.
    /// </para>
    /// <para>
    /// <b>Not here, and deliberately.</b> <c>LoadWithDuration</c> needs
    /// <see cref="TikConnectionCapability.Streaming"/>, and <c>LoadAsync</c>/<c>LoadListenAsync</c> are the
    /// callback monitors — a different thing that happens to share the word (see
    /// <see cref="TikConnectionExtensions.LoadAsync"/>). The compound operations — <c>SaveListDifferences</c>,
    /// <c>DeleteAll</c>, <c>Move</c> — stay synchronous for now: each is a sequence of the primitives below,
    /// and an async form of them is a design question about partial failure, not a mechanical translation.
    /// </para>
    /// </remarks>
    public static class TikConnectionAsyncExtensions
    {
        #region -- LOAD --

        /// <summary>
        /// Loads all entities of the given type. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadAll{TEntity}(ITikConnection)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        public static Task<IList<TEntity>> LoadAllAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
            => LoadListAsync<TEntity>(connection, cancellationToken);

        /// <summary>
        /// Loads an entity list, optionally filtered. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND).</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        public static async Task<IList<TEntity>> LoadListAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommand<TEntity>(connection, filterParameters);
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            TikConnectionExtensions.RegisterLoadedSnapshots(connection, entities, metadata, command);
            return entities;
        }

        /// <summary>
        /// Loads exactly one entity. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadSingle{TEntity}(ITikConnection, ITikCommandParameter[])"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND).</param>
        /// <returns>The single loaded entity.</returns>
        /// <exception cref="TikNoSuchItemException">No row returned.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static async Task<TEntity> LoadSingleAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommand<TEntity>(connection, filterParameters);
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            return TikConnectionExtensions.ExactlyOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Loads at most one entity. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadSingleOrDefault{TEntity}(ITikConnection, ITikCommandParameter[])"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND).</param>
        /// <returns>The single loaded entity, or the type default when nothing matched.</returns>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static async Task<TEntity> LoadSingleOrDefaultAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommand<TEntity>(connection, filterParameters);
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            return TikConnectionExtensions.AtMostOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Loads the entity with the given <c>.id</c>. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadById{TEntity}(ITikConnection, string)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="id">Entity id.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The loaded entity.</returns>
        /// <exception cref="TikNoSuchItemException">No entity with that id.</exception>
        public static async Task<TEntity> LoadByIdAsync<TEntity>(this ITikConnection connection, string id,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommand<TEntity>(connection,
                connection.CreateParameter(TikSpecialProperties.Id, id));
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            return TikConnectionExtensions.ExactlyOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Loads the entity with the given name. Async counterpart of
        /// <see cref="TikConnectionExtensions.LoadByName{TEntity}(ITikConnection, string)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="name">Entity name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The loaded entity.</returns>
        /// <exception cref="TikNoSuchItemException">No entity with that name.</exception>
        public static async Task<TEntity> LoadByNameAsync<TEntity>(this ITikConnection connection, string name,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommand<TEntity>(connection,
                connection.CreateParameter("name", name));
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            return TikConnectionExtensions.ExactlyOne(connection, command, entities, metadata);
        }

        #endregion

        #region -- SAVE / DELETE --

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
            IEnumerable<string> usedFieldsFilter = null,
            TikSaveMode saveMode = TikSaveMode.Default,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            TikConnectionExtensions.EnsureNotReadonly(metadata);
            string id = TikConnectionExtensions.ResolveSaveId(entity, metadata);

            if (TikConnectionExtensions.IsCreate(metadata, id))
            {
                var createCmd = TikConnectionExtensions.BuildCreateCommand(connection, entity, metadata, usedFieldsFilter);
                string newId = await createCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                TikConnectionExtensions.FinishCreate(connection, entity, metadata, newId);
                return;
            }

            if (TikConnectionExtensions.NeedsFilterResolution(metadata, usedFieldsFilter))
            {
                var resolution = TikConnectionExtensions.ResolveUpdateFilter(connection, entity, metadata, saveMode);
                if (resolution.Kind == TikConnectionExtensions.UpdateFilterKind.NothingChanged)
                    return; // nothing changed — skip the API call
                if (resolution.Kind == TikConnectionExtensions.UpdateFilterKind.NeedsUnmodifiedEntity)
                {
                    var unmodifiedEntity = await connection.LoadByIdAsync<TEntity>(id, cancellationToken).ConfigureAwait(false);
                    usedFieldsFilter = entity.GetDifferentFields(unmodifiedEntity);
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
            Guard.ArgumentNotNull(connection, "connection");

            return TikConnectionExtensions.BuildDeleteCommand(connection, entity)
                .ExecuteNonQueryAsync(cancellationToken);
        }

        #endregion
    }
}
