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
    public static partial class TikConnectionAsyncExtensions
    {

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
            Guard.ArgumentNotNull(connection, nameof(connection));

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
            Guard.ArgumentNotNull(connection, nameof(connection));

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
            Guard.ArgumentNotNull(connection, nameof(connection));

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
            Guard.ArgumentNotNull(connection, nameof(connection));

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
            Guard.ArgumentNotNull(connection, nameof(connection));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection,
                connection.CreateParameter("name", name));
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            return TikConnectionExtensions.ExactlyOne(connection, command, entities, metadata);
        }
    }
}
