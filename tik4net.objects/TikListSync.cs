using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    /// <summary>
    /// Sends the ordering pass <see cref="TikListSyncPlanner"/> planned — the part the two list writers
    /// (<see cref="TikListMerge{TEntity}"/> and <c>SaveListDifferences</c>) and their sync and async halves share.
    /// </summary>
    /// <remarks>
    /// The sync and async executors are the same steps with awaits in place of the calls that reach the router;
    /// everything that decides lives in the planner and in the helpers here, once.
    /// </remarks>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    internal static class TikListSync
    {
        /// <summary>The <c>add</c> argument that creates a row in front of another on an ordered menu.</summary>
        internal const string PlaceBefore = "place-before";

        /// <summary>
        /// A called-back action before each step is sent: the step, and the entity it places.
        /// </summary>
        internal delegate void StepCallback<TEntity>(TikListSyncStep step, TEntity entity);

        /// <summary>
        /// Whether the row is one the router made itself (<c>dynamic=true</c> — the fasttrack counter rule,
        /// hotspot- or PPP-generated rules). RouterOS refuses to move, remove or change such a row
        /// ("cannot move builtin"), and refuses a move in front of one too, so the list writers keep it out of the
        /// list they order: it is never deleted, never moved, and never an anchor. An entity without a <c>dynamic</c> field has none.
        /// </summary>
        internal static bool IsDynamic(TikEntityMetadata metadata, object? entity)
        {
            if (entity == null)
                return false;
            var property = metadata.DynamicProperty;
            return property != null && TikBoolText.IsTrue(property.GetEntityValue(entity));
        }

        /// <summary><c>/move</c> of one row, in front of <paramref name="idToMoveBefore"/> or to the end when null.</summary>
        internal static ITikCommand BuildMoveCommand(ITikConnection connection, TikEntityMetadata metadata,
            string idToMove, string? idToMoveBefore)
        {
            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/move", TikCommandParameterFormat.NameValue,
                "numbers", idToMove);
            if (idToMoveBefore != null)
                cmd.AddParameter("destination", idToMoveBefore);
            return cmd;
        }

        /// <summary>The create command of <see cref="TikConnectionExtensions.Save{TEntity}"/>, placed in front of <paramref name="placeBeforeId"/>.</summary>
        private static ITikCommand BuildPlacedCreateCommand<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata, IEnumerable<string>? createFields, string? placeBeforeId)
        {
            var cmd = TikConnectionExtensions.BuildCreateCommand(connection, entity, metadata, createFields);
            if (placeBeforeId != null)
                cmd.AddParameter(PlaceBefore, placeBeforeId, TikCommandParameterFormat.NameValue);
            return cmd;
        }

        private static string IdOf<TEntity>(TikEntityMetadata metadata, TEntity entity)
            => metadata.IdProperty!.GetEntityValue(entity!)!;   // desired rows are loaded, or created by an earlier step

        /// <summary>
        /// The steps a pass sends, each with the anchor it goes in front of. The anchor is read when the step is reached,
        /// so a row the previous step created already has its new <c>.id</c>: walk this one step at a time, sending each.
        /// </summary>
        /// <remarks>
        /// The menu is guarded for creates before the first step. <c>move</c> is not guarded up front. On a menu that does
        /// not offer it the creates are still sent and the moves skipped, and the refusal is thrown at the end — every row
        /// exists with the right fields and only the order is wrong, which is what a refusal of the last pass has always
        /// meant here.
        /// </remarks>
        private static IEnumerable<(TikListSyncStep Step, TEntity Entity, string? AnchorId)> Walk<TEntity>(TikEntityMetadata metadata,
            IList<TEntity> desired, IReadOnlyList<TikListSyncStep> steps)
        {
            if (steps.Any(s => s.Kind == TikListSyncStepKind.Create))
                TikConnectionExtensions.EnsureSupported(metadata, TikEntityOperations.Add);

            bool canMove = metadata.Supports(TikEntityOperations.Move);
            foreach (var step in steps)
            {
                if (step.Kind == TikListSyncStepKind.Move && !canMove)
                    continue;
                yield return (step, desired[step.Row], step.Anchor == TikListSyncPlanner.Tail ? null : IdOf(metadata, desired[step.Anchor]));
            }

            if (!canMove && steps.Any(s => s.Kind == TikListSyncStepKind.Move))
                TikConnectionExtensions.EnsureSupported(metadata, TikEntityOperations.Move);   // throws
        }

        /// <summary>Sends the ordering pass.</summary>
        /// <param name="connection">Connection to send on.</param>
        /// <param name="metadata">Entity metadata (ordered, with an <c>.id</c> property).</param>
        /// <param name="desired">The rows in desired order; the steps index into it. A created row gets its new
        /// <c>.id</c> written back, so a later step can anchor on it.</param>
        /// <param name="steps">The plan.</param>
        /// <param name="createFields">Field filter for a create (as <see cref="TikConnectionExtensions.Save{TEntity}"/>'s <c>usedFieldsFilter</c>).</param>
        /// <param name="beforeStep">Called before each step is sent (logging).</param>
        internal static void ApplyOrder<TEntity>(ITikConnection connection, TikEntityMetadata metadata,
            IList<TEntity> desired, IReadOnlyList<TikListSyncStep> steps, IEnumerable<string>? createFields,
            StepCallback<TEntity>? beforeStep)
        {
            foreach (var (step, entity, anchorId) in Walk(metadata, desired, steps))
            {
                beforeStep?.Invoke(step, entity);
                if (step.Kind == TikListSyncStepKind.Create)
                {
                    var cmd = BuildPlacedCreateCommand(connection, entity, metadata, createFields, anchorId);
                    TikConnectionExtensions.FinishCreate(connection, entity, metadata, cmd.ExecuteScalar());
                }
                else
                    BuildMoveCommand(connection, metadata, IdOf(metadata, entity), anchorId).ExecuteNonQuery();
            }
        }

        /// <summary>Async <see cref="ApplyOrder"/>: the same steps; cancellation is checked before each command.</summary>
        internal static async Task ApplyOrderAsync<TEntity>(ITikConnection connection, TikEntityMetadata metadata,
            IList<TEntity> desired, IReadOnlyList<TikListSyncStep> steps, IEnumerable<string>? createFields,
            StepCallback<TEntity>? beforeStep, CancellationToken cancellationToken)
        {
            foreach (var (step, entity, anchorId) in Walk(metadata, desired, steps))
            {
                cancellationToken.ThrowIfCancellationRequested();
                beforeStep?.Invoke(step, entity);
                if (step.Kind == TikListSyncStepKind.Create)
                {
                    var cmd = BuildPlacedCreateCommand(connection, entity, metadata, createFields, anchorId);
                    string newId = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    TikConnectionExtensions.FinishCreate(connection, entity, metadata, newId);
                }
                else
                    await BuildMoveCommand(connection, metadata, IdOf(metadata, entity), anchorId)
                        .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
