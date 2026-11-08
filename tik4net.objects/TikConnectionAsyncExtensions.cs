using System;
using System.Diagnostics.CodeAnalysis;
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
    /// <b>The streaming loads</b> (net8.0) — <c>LoadWithDurationAsync</c> and <c>LoadUntilDoneAsync</c> — return an
    /// <c>IAsyncEnumerable&lt;T&gt;</c> rather than a <see cref="Task"/>, one entity per row as it arrives;
    /// <c>LoadWithDurationAsync</c> needs <see cref="TikConnectionCapability.Streaming"/>.
    /// </para>
    /// <para>
    /// <b>Not here, and deliberately.</b> <c>LoadWithCallback</c>/<c>LoadListenWithCallback</c> are the
    /// callback monitors — a different shape, handing back a running <see cref="ITikCommand"/>
    /// rather than a <see cref="Task"/> (see <see cref="TikConnectionExtensions.LoadWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, ITikCommandParameter[])"/>).
    /// </para>
    /// <para>
    /// <b>The list writers</b> — <see cref="SaveListDifferencesAsync"/>, <see cref="DeleteAllAsync"/> and
    /// <see cref="TikListMerge{TEntity}.SaveAsync"/> — are sequences of commands with no transaction behind them.
    /// They send what their sync originals send, in the same order, and check the token before each command; a
    /// failure or a cancellation part-way leaves the commands already sent applied. Recovery is to reload and run
    /// again: both writers compute from the state they are handed, so a re-run continues from wherever the
    /// router is.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
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
        /// <remarks>
        /// The token comes before the filters, so a filtered call passes it first —
        /// <c>LoadListAsync&lt;T&gt;(cancellationToken, filter1, filter2)</c> — or names the filters without a token:
        /// <c>LoadListAsync&lt;T&gt;(filterParameters: new[] { filter1, filter2 })</c>. <c>LoadListAsync&lt;T&gt;(filter)</c> does not
        /// compile, since the first argument after the connection is the token.
        /// </remarks>
        public static async Task<IList<TEntity>> LoadListAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            TikConnectionExtensions.RegisterSnapshots(connection, entities, metadata, command);
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
        /// <remarks>
        /// The token comes before the filters, so a filtered call passes it first —
        /// <c>LoadSingleAsync&lt;T&gt;(cancellationToken, filter1, filter2)</c> — or names the filters without a token:
        /// <c>LoadSingleAsync&lt;T&gt;(filterParameters: new[] { filter1, filter2 })</c>. <c>LoadSingleAsync&lt;T&gt;(filter)</c> does not
        /// compile, since the first argument after the connection is the token.
        /// </remarks>
        public static async Task<TEntity> LoadSingleAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
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
        /// <remarks>
        /// The token comes before the filters, so a filtered call passes it first —
        /// <c>LoadSingleOrDefaultAsync&lt;T&gt;(cancellationToken, filter1, filter2)</c> — or names the filters without a token:
        /// <c>LoadSingleOrDefaultAsync&lt;T&gt;(filterParameters: new[] { filter1, filter2 })</c>. <c>LoadSingleOrDefaultAsync&lt;T&gt;(filter)</c> does not
        /// compile, since the first argument after the connection is the token.
        /// </remarks>
        public static async Task<TEntity?> LoadSingleOrDefaultAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken),
            params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
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
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection,
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
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection,
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
            IEnumerable<string>? usedFieldsFilter = null,
            TikSaveMode saveMode = TikSaveMode.Default,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

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
            Guard.ArgumentNotNull(connection, "connection");

            return TikConnectionExtensions.BuildDeleteCommand(connection, entity)
                .ExecuteNonQueryAsync(cancellationToken);
        }

        #endregion

        #region -- LIST / MOVE --

        /// <summary>
        /// Saves the differences between two lists. Async counterpart of
        /// <see cref="TikConnectionExtensions.SaveListDifferences{TEntity}(ITikConnection, IEnumerable{TEntity}, IEnumerable{TEntity})"/>,
        /// with the same rules — including the order of <paramref name="modifiedList"/> on an ordered entity, the
        /// minimal moves, creates in place and dynamic rows left alone. See the remarks on the class for what a
        /// failure part-way leaves behind.
        /// </summary>
        /// <typeparam name="TEntity">Saved entity type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="modifiedList">List with modifications. Its order is applied when the entity is ordered.</param>
        /// <param name="unmodifiedList">Original (cloned) unmodified list.</param>
        /// <param name="cancellationToken">Cancellation token — checked before each command.</param>
        public static async Task SaveListDifferencesAsync<TEntity>(this ITikConnection connection,
            IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var plan = TikConnectionExtensions.PlanListDifferences(modifiedList, unmodifiedList);
            TikConnectionExtensions.ValidateListDifferences(connection, plan);

            foreach (var entity in plan.Deletes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteAsync(connection, entity, cancellationToken).ConfigureAwait(false);
            }
            foreach (var entity in plan.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SaveAsync(connection, entity, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            if (plan.OrderSteps != null)
                await TikListSync.ApplyOrderAsync(connection, plan.Metadata, plan.Desired, plan.OrderSteps, null, null,
                    cancellationToken).ConfigureAwait(false);
            else
                foreach (var entity in plan.Creates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await SaveAsync(connection, entity, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
        }

        /// <summary>
        /// Deletes every entity of the given type. Async counterpart of
        /// <see cref="TikConnectionExtensions.DeleteAll{TEntity}(ITikConnection)"/>; dynamic rows are left in place,
        /// as the router refuses to remove them.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete.</param>
        /// <param name="cancellationToken">Cancellation token — checked before each command.</param>
        /// <returns>Number of loaded entities (dynamic ones included).</returns>
        public static async Task<int> DeleteAllAsync<TEntity>(this ITikConnection connection,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            var list = await LoadAllAsync<TEntity>(connection, cancellationToken).ConfigureAwait(false);
            await SaveListDifferencesAsync(connection, new List<TEntity>(), list, cancellationToken).ConfigureAwait(false);
            return list.Count;
        }

        /// <summary>
        /// Moves <paramref name="entityToMove"/> in front of <paramref name="entityToMoveBefore"/>. Async counterpart of
        /// <see cref="TikConnectionExtensions.Move{TEntity}(ITikConnection, TEntity, TEntity)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <param name="entityToMoveBefore">Entity in front of which it is moved.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task MoveAsync<TEntity>(this ITikConnection connection, TEntity entityToMove, TEntity entityToMoveBefore,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(connection, "connection");

            return TikConnectionExtensions.BuildMoveCommand(connection, entityToMove, entityToMoveBefore)
                .ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Moves <paramref name="entityToMove"/> to the end of the list. Async counterpart of
        /// <see cref="TikConnectionExtensions.MoveToEnd{TEntity}(ITikConnection, TEntity)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task MoveToEndAsync<TEntity>(this ITikConnection connection, TEntity entityToMove,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(connection, "connection");

            return TikConnectionExtensions.BuildMoveCommand(connection, entityToMove, default(TEntity))
                .ExecuteNonQueryAsync(cancellationToken);
        }

        #endregion

#if NET8_0_OR_GREATER
        #region -- STREAMING LOAD --

        /// <summary>
        /// Streams entities for <paramref name="duration"/>, each one as the router sends its row, then cancels the
        /// command on the router. The streaming counterpart of
        /// <see cref="TikConnectionExtensions.LoadWithDuration{TEntity}(ITikConnection, TimeSpan, ITikCommandParameter[])"/>:
        /// each row is tracked as that method tracks it, so a later <c>Save</c> sends only what changed.
        /// <code>
        /// await foreach (var t in connection.LoadWithDurationAsync&lt;InterfaceMonitorTraffic&gt;(TimeSpan.FromSeconds(5),
        ///     cancellationToken, connection.CreateParameter("interface", "ether1")))
        ///     Console.WriteLine(t.RxBitsPerSecond);
        /// </code>
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.Streaming"/> (the binary API); without it this throws
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> when called, before anything is sent. The token comes
        /// before the parameters, as in <see cref="LoadListAsync{TEntity}(ITikConnection, CancellationToken, ITikCommandParameter[])"/>.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="duration">How long to keep the command open. A negative value is zero.</param>
        /// <param name="cancellationToken">Ends the read early; the command is cancelled on the router.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND).</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport does not stream.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error or the connection was lost, after the rows already delivered.</exception>
        public static IAsyncEnumerable<TEntity> LoadWithDurationAsync<TEntity>(this ITikConnection connection,
            TimeSpan duration, CancellationToken cancellationToken = default,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var sentences = command.ExecuteListWithDurationAsync(duration, cancellationToken);
            return TikObjectsCommandAsyncExtensions.MaterializeStream<TEntity>(command, sentences,
                entity => TikConnectionExtensions.RegisterSnapshots(connection, new[] { entity }, metadata, command),
                cancellationToken);
        }

        /// <summary>
        /// Streams entities until the command ends by itself (<c>!done</c>), or until <paramref name="timeout"/>
        /// elapses. Each row is tracked like any loaded entity.
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.AsyncCommands"/>: streamed on the binary API, the
        /// awaited list on every other transport that declares it. Without it this throws
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> when called.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="timeout">Optional upper bound. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND).</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no async commands.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error, the connection was lost, or <paramref name="timeout"/> elapsed first.</exception>
        public static IAsyncEnumerable<TEntity> LoadUntilDoneAsync<TEntity>(this ITikConnection connection,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var sentences = command.ExecuteListUntilDoneAsync(timeout, cancellationToken);
            return TikObjectsCommandAsyncExtensions.MaterializeStream<TEntity>(command, sentences,
                entity => TikConnectionExtensions.RegisterSnapshots(connection, new[] { entity }, metadata, command),
                cancellationToken);
        }

        #endregion
#endif
    }
}
