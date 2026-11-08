using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

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
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikObjectsCommandAsyncExtensions
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
            return TikCommandExtensions.Materialize<TEntity>(command, responseSentences);
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
            return TikCommandExtensions.ExactlyOneRow(command, entities);
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
        public static async Task<TEntity?> LoadSingleOrDefaultAsync<TEntity>(this ITikCommand command,
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : new()
        {
            var entities = await command.LoadListAsync<TEntity>(cancellationToken).ConfigureAwait(false);
            return TikCommandExtensions.AtMostOneRow(command, entities);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Streams entities for <paramref name="duration"/>, each one as the router sends its row, then cancels the
        /// command on the router. The typed form of
        /// <see cref="TikCommandStreamingExtensions.ExecuteListWithDurationAsync(ITikCommand, TimeSpan, CancellationToken)"/>,
        /// and the streaming counterpart of <see cref="TikCommandExtensions.LoadWithDuration{TEntity}(ITikCommand, TimeSpan)"/>.
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.Streaming"/> (the binary API); without it this throws
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> when called, before anything is sent. Each row is
        /// mapped as <c>LoadWithDuration</c> maps it; the read is reported to the connection's
        /// <see cref="ITikEntityDiagnostics"/> when the enumeration ends (<see cref="TikEntityReadReport.IsComplete"/> is
        /// false when the caller stopped early).
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="command">Tik command executed to load.</param>
        /// <param name="duration">How long to keep the command open. A negative value is zero.</param>
        /// <param name="cancellationToken">Ends the read early; the command is cancelled on the router.</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport does not stream.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error or the connection was lost, after the rows already delivered.</exception>
        public static IAsyncEnumerable<TEntity> LoadWithDurationAsync<TEntity>(this ITikCommand command,
            TimeSpan duration, CancellationToken cancellationToken = default)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var sentences = command.ExecuteListWithDurationAsync(duration, cancellationToken);
            return MaterializeStream<TEntity>(command, sentences, null, cancellationToken);
        }

        /// <summary>
        /// Streams entities until the command ends by itself (<c>!done</c>), or until <paramref name="timeout"/>
        /// elapses. The typed form of
        /// <see cref="TikCommandStreamingExtensions.ExecuteListUntilDoneAsync(ITikCommand, TimeSpan?, CancellationToken)"/>.
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.AsyncCommands"/>: streamed on the binary API, the
        /// awaited list on every other transport that declares it (the same rows, yielded once the read has ended).
        /// Without it this throws <see cref="TikConnectionCapabilityNotSupportedException"/> when called.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="command">Tik command executed to load.</param>
        /// <param name="timeout">Optional upper bound. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no async commands.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error, the connection was lost, or <paramref name="timeout"/> elapsed first.</exception>
        public static IAsyncEnumerable<TEntity> LoadUntilDoneAsync<TEntity>(this ITikCommand command,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var sentences = command.ExecuteListUntilDoneAsync(timeout, cancellationToken);
            return MaterializeStream<TEntity>(command, sentences, null, cancellationToken);
        }

        /// <summary>
        /// Maps a sentence stream row by row through the mapper's one row path, and reports the read to the
        /// connection's diagnostics when it ends. <paramref name="onRow"/> runs before each entity is handed out
        /// (the connection-level loads take the change-tracker snapshot there).
        /// </summary>
        internal static async IAsyncEnumerable<TEntity> MaterializeStream<TEntity>(ITikCommand command,
            IAsyncEnumerable<ITikReSentence> sentences, Action<TEntity>? onRow,
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where TEntity : new()
        {
            // Rows are kept only for a sink: an until-done read can be long, and nothing else needs them.
            bool report = command.Connection?.GetEntityDiagnostics() != null;
            var rows = report ? new List<ITikReSentence>() : null;
            var entities = report ? new List<TEntity>() : null;
            bool complete = false;
            try
            {
                await foreach (var sentence in sentences.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    var entity = TikCommandExtensions.CreateObject<TEntity>(sentence);
                    onRow?.Invoke(entity);
                    rows?.Add(sentence);
                    entities?.Add(entity);
                    yield return entity;
                }
                complete = true;
            }
            finally
            {
                if (rows != null && entities != null && (complete || rows.Count > 0))
                    TikEntityDiagnosticsExtensions.Report(command, rows, entities, isComplete: complete);
            }
        }
#endif
    }
}
