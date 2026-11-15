using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    public static partial class TikConnectionAsyncExtensions
    {
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
    }
}
