#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Threading;

namespace tik4net
{
    /// <summary>
    /// The streaming reads as an <see cref="IAsyncEnumerable{T}"/>: rows reach the caller <b>as the router
    /// sends them</b>, rather than being collected into a list that is handed over once the read ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>net8.0 only.</b> <see cref="IAsyncEnumerable{T}"/> is not part of <c>netstandard2.0</c> — reaching
    /// it there means taking a dependency on <c>Microsoft.Bcl.AsyncInterfaces</c> for every consumer,
    /// including the ones on .NET Framework who will never call this. Multi-targeting is what lets the
    /// modern surface exist without that cost, so this interface simply is not there on the
    /// <c>netstandard2.0</c> build.
    /// </para>
    /// <para>
    /// Implemented by the binary API, the one transport that reports
    /// <see cref="TikConnectionCapability.Streaming"/>: a streaming read needs several rows inside one
    /// command exchange, which a terminal transport has no way to deliver. Call it through
    /// <see cref="TikCommandStreamingExtensions"/>, which checks the capability first:
    /// <c>ExecuteListWithDurationAsync</c> needs <see cref="TikConnectionCapability.Streaming"/>, while
    /// <c>ExecuteListUntilDoneAsync</c> runs on every transport that declares
    /// <see cref="TikConnectionCapability.AsyncCommands"/> — streamed on the binary API, the awaited list elsewhere.
    /// </para>
    /// <para>
    /// The rows are the same ones the synchronous <c>ExecuteListWithDuration</c>/<c>ExecuteListUntilDone</c>
    /// produce, and the read ends on the same events — the requested duration, the command's own
    /// <c>!done</c>, a router error or a lost connection. The last two surface as
    /// <see cref="TikCommandAbortException"/> thrown out of the enumeration after the rows already delivered, so a
    /// truncated stream cannot be read as a complete one; its <see cref="Exception.InnerException"/> is the cause —
    /// the classified trap (<see cref="TikNoSuchItemException"/>, … or <see cref="TikCommandTrapException"/>) or a
    /// <see cref="TikCommandFatalException"/>.
    /// </para>
    /// </remarks>
    public interface ITikStreamingCommand
    {
        /// <summary>
        /// Streams rows for <paramref name="duration"/>, then cancels the command on the router
        /// (<c>/cancel</c>) and ends the enumeration. The async form of
        /// <see cref="ITikCommand.ExecuteListWithDuration(TimeSpan)"/>.
        /// </summary>
        /// <param name="duration">How long to keep the command open. A negative value is zero.</param>
        /// <param name="cancellationToken">
        /// Ends the read early. The command is cancelled on the router exactly as the duration would have
        /// cancelled it, so the connection is left usable (<see cref="TikConnectionCapability.CancelInFlight"/>).
        /// </param>
        /// <exception cref="TikCommandAbortException">The router ended the command with an error, or the connection was
        /// lost; the cause is the <see cref="Exception.InnerException"/>.</exception>
        IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(TimeSpan duration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams rows until the command ends by itself (<c>!done</c>), or until
        /// <paramref name="timeout"/> elapses. The async form of
        /// <see cref="ITikCommand.ExecuteListUntilDone(TimeSpan?)"/>; it does <b>not</b> send <c>/cancel</c>.
        /// </summary>
        /// <param name="timeout">Optional upper bound. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <exception cref="TikCommandAbortException">The router ended the command with an error, or the connection was
        /// lost (the cause is the <see cref="Exception.InnerException"/>), or <paramref name="timeout"/> elapsed first
        /// (no inner exception).</exception>
        IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reaches <see cref="ITikStreamingCommand"/> from a plain <see cref="ITikCommand"/>, checking the
    /// transport's capability first — the same fail-closed shape as the <c>Execute*Async</c> extensions:
    /// <see cref="TikConnectionCapability.Streaming"/> for <c>ExecuteListWithDurationAsync</c>,
    /// <see cref="TikConnectionCapability.AsyncCommands"/> for <c>ExecuteListUntilDoneAsync</c>.
    /// </summary>
    public static class TikCommandStreamingExtensions
    {
        /// <inheritdoc cref="ITikStreamingCommand.ExecuteListWithDurationAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="duration">How long to keep the command open. A negative value is zero.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        public static IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(this ITikCommand command,
            TimeSpan duration, CancellationToken cancellationToken = default)
            => AsStreaming(command).ExecuteListWithDurationAsync(duration, cancellationToken);

        /// <inheritdoc cref="ITikStreamingCommand.ExecuteListUntilDoneAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="timeout">Optional upper bound. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <remarks>
        /// Needs <see cref="TikConnectionCapability.AsyncCommands"/>, not <see cref="TikConnectionCapability.Streaming"/>.
        /// On a transport that streams (the binary API) the rows arrive as the router sends them. On every other
        /// transport that declares <see cref="TikConnectionCapability.AsyncCommands"/> the read is the awaited
        /// <c>ExecuteListAsync</c>, and its rows are yielded once it has ended: not streamed, but the same rows
        /// <see cref="ITikCommand.ExecuteListUntilDone(TimeSpan?)"/> returns. There <paramref name="timeout"/> ends a read
        /// still running with <see cref="TikCommandAbortException"/> (on a CLI connection under
        /// <see cref="TikCancellationMode.Cooperative"/> the router's command is let run out first), and
        /// <paramref name="cancellationToken"/> with <see cref="OperationCanceledException"/>.
        /// </remarks>
        public static IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(this ITikCommand command,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Guard.ArgumentNotNull(command, nameof(command));
            if (command.Connection == null)
                throw new System.InvalidOperationException("Connection is not assigned.");
            if (command.Connection.Supports(TikConnectionCapability.Streaming))
                return AsStreaming(command).ExecuteListUntilDoneAsync(timeout, cancellationToken);

            command.Connection.Require(TikConnectionCapability.AsyncCommands, "ExecuteListUntilDoneAsync");
            return AwaitedListAsync(command, timeout.HasValue ? tik4net.Connection.TikTimeSpans.ToDuration(timeout.Value) : (TimeSpan?)null,
                cancellationToken);
        }

        // Off the streaming transport: the awaited list under the timeout, linked with the caller's token, then its rows.
        private static async IAsyncEnumerable<ITikReSentence> AwaitedListAsync(ITikCommand command, TimeSpan? timeout,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IList<ITikReSentence> rows;
            using (var deadline = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken))
            {
                try
                {
                    rows = await command.ExecuteListAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    throw new TikCommandAbortException(command, "Timeout");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested)   // a cooperative cancel handed the rows over after the deadline
                    throw new TikCommandAbortException(command, "Timeout");
            }
            foreach (var row in rows)
                yield return row;
        }

        /// <summary>
        /// The command as an <see cref="ITikStreamingCommand"/>, or a capability error naming what is missing.
        /// </summary>
        public static ITikStreamingCommand AsStreaming(this ITikCommand command)
        {
            Guard.ArgumentNotNull(command, nameof(command));
            if (command.Connection == null)
                throw new System.InvalidOperationException("Connection is not assigned.");

            command.Connection.Require(TikConnectionCapability.Streaming,
                "streaming reads (ExecuteListWithDurationAsync/ExecuteListUntilDoneAsync)");

            if (command is ITikStreamingCommand streaming)
                return streaming;
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.Streaming,
                "The connection reports the 'Streaming' capability but its command implementation does not "
                + "provide ITikStreamingCommand.");
        }
    }
}
#endif
