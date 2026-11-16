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
        /// Moves given <paramref name="entityToMove"/> before given <paramref name="entityToMoveBefore"/>.
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <param name="entityToMoveBefore">Entity before which is given <paramref name="entityToMove"/> moved.</param>
        /// <inheritdoc cref="Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)" path="/exception"/>
        public static void Move<TEntity>(this ITikConnection connection, TEntity entityToMove, TEntity entityToMoveBefore)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            BuildMoveCommand(connection, entityToMove, entityToMoveBefore).ExecuteNonQuery();
        }

        /// <summary>The checks and the command of <see cref="Move"/>, shared with <c>MoveAsync</c>.</summary>
        internal static ITikCommand BuildMoveCommand<TEntity>(ITikConnection connection, TEntity entityToMove, TEntity? entityToMoveBefore)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureSupportsOrdering(metadata);   // first: "this menu has no order" is the more informative answer
            EnsureSupported(metadata, TikEntityOperations.Move);
            EnsureHasIdProperty(metadata);

            // IdProperty is non-null: EnsureHasIdProperty just verified it. idToMove/idToMoveBefore are not
            // guarded against an unloaded entity here (unlike BuildDeleteCommand) — see the caller contract;
            // `!` types the existing behaviour rather than adding a new check.
            string idToMove = metadata.IdProperty!.GetEntityValue(entityToMove!)!;
            string? idToMoveBefore = entityToMoveBefore != null ? metadata.IdProperty!.GetEntityValue(entityToMoveBefore) : null;
            return TikListSync.BuildMoveCommand(connection, metadata, idToMove, idToMoveBefore);
        }

        /// <summary>
        /// Moves given <paramref name="entityToMove"/> to the end (make it last entity in the list).
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <inheritdoc cref="Save{TEntity}(ITikConnection, TEntity, IEnumerable{string}, TikSaveMode)" path="/exception"/>
        public static void MoveToEnd<TEntity>(this ITikConnection connection, TEntity entityToMove)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Move(connection, entityToMove, default(TEntity));
        }
    }
}
