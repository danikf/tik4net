using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    /// <summary>
    /// Task-based half of <see cref="TikCommandExtensions"/> — the O/R mapper on top of the
    /// <see cref="ITikCommandAsync"/> surface. Each method is the exact counterpart of the synchronous one
    /// next to it, differing only in awaiting the command instead of blocking on it.
    /// </summary>
    /// <remarks>
    /// Gated the same way as every other <c>*Async</c> entry point: dispatch goes through
    /// <c>ExecuteListAsync</c>, so a transport that does not declare
    /// <see cref="TikConnectionCapability.AsyncCommands"/> throws
    /// <see cref="TikConnectionCapabilityNotSupportedException"/> rather than quietly blocking a thread.
    /// </remarks>
    public static class TikCommandAsyncExtensions
    {
        /// <summary>
        /// Loads an entity list from the given command. Async counterpart of
        /// <see cref="TikCommandExtensions.LoadList{TEntity}(ITikCommand)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        public static async Task<IList<TEntity>> LoadListAsync<TEntity>(this ITikCommand command,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var responseSentences = await command.ExecuteListAsync(cancellationToken).ConfigureAwait(false);
            return responseSentences.Select(sentence => TikCommandExtensions.CreateEntity<TEntity>(sentence)).ToList();
        }

        /// <summary>
        /// Loads exactly one entity from the given command. Async counterpart of
        /// <see cref="TikCommandExtensions.LoadSingle{TEntity}(ITikCommand)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The single loaded entity.</returns>
        /// <exception cref="TikNoSuchItemException">No row returned.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static async Task<TEntity> LoadSingleAsync<TEntity>(this ITikCommand command,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            if (entities.Count == 0)
                throw new TikNoSuchItemException(command);
            if (entities.Count > 1)
                throw new TikCommandAmbiguousResultException(command, entities.Count);
            return entities[0];
        }

        /// <summary>
        /// Loads at most one entity from the given command. Async counterpart of
        /// <see cref="TikCommandExtensions.LoadSingleOrDefault{TEntity}(ITikCommand)"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entity type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The single loaded entity, or the type default when nothing was returned.</returns>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static async Task<TEntity> LoadSingleOrDefaultAsync<TEntity>(this ITikCommand command,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);

            if (entities.Count == 0)
                return default(TEntity);
            if (entities.Count > 1)
                throw new TikCommandAmbiguousResultException(command, entities.Count);
            return entities[0];
        }
    }
}
