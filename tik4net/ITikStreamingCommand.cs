#if NET8_0_OR_GREATER
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
    /// <see cref="TikCommandStreamingExtensions"/>, which checks the capability first.
    /// </para>
    /// <para>
    /// The rows are the same ones the synchronous <c>ExecuteListWithDuration</c>/<c>ExecuteListUntilDone</c>
    /// produce, and the read ends on the same events — the requested duration, the command's own
    /// <c>!done</c>, or a router error, which surfaces as <see cref="TikCommandAbortException"/> thrown out
    /// of the enumeration.
    /// </para>
    /// </remarks>
    public interface ITikStreamingCommand
    {
        /// <summary>
        /// Streams rows for <paramref name="durationSec"/> seconds, then cancels the command on the router
        /// (<c>/cancel</c>) and ends the enumeration. The async form of
        /// <see cref="ITikCommand.ExecuteListWithDuration(int)"/>.
        /// </summary>
        /// <param name="durationSec">How long to keep the command open, in seconds.</param>
        /// <param name="cancellationToken">
        /// Ends the read early. The command is cancelled on the router exactly as the duration would have
        /// cancelled it, so the connection is left usable (<see cref="TikConnectionCapability.CancelInFlight"/>).
        /// </param>
        /// <exception cref="TikCommandAbortException">The router ended the command with an error.</exception>
        IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(int durationSec,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams rows until the command ends by itself (<c>!done</c>), or until
        /// <paramref name="timeoutSec"/> elapses. The async form of
        /// <see cref="ITikCommand.ExecuteListUntilDone(int?)"/>; it does <b>not</b> send <c>/cancel</c>.
        /// </summary>
        /// <param name="timeoutSec">Optional upper bound, in seconds. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <exception cref="TikCommandAbortException">The router ended the command with an error.</exception>
        IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(int? timeoutSec = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reaches <see cref="ITikStreamingCommand"/> from a plain <see cref="ITikCommand"/>, checking the
    /// transport's <see cref="TikConnectionCapability.Streaming"/> capability first — the same fail-closed
    /// shape as the <c>Execute*Async</c> extensions.
    /// </summary>
    public static class TikCommandStreamingExtensions
    {
        /// <inheritdoc cref="ITikStreamingCommand.ExecuteListWithDurationAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="durationSec">How long to keep the command open, in seconds.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        public static IAsyncEnumerable<ITikReSentence> ExecuteListWithDurationAsync(this ITikCommand command,
            int durationSec, CancellationToken cancellationToken = default)
            => AsStreaming(command).ExecuteListWithDurationAsync(durationSec, cancellationToken);

        /// <inheritdoc cref="ITikStreamingCommand.ExecuteListUntilDoneAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="timeoutSec">Optional upper bound, in seconds.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        public static IAsyncEnumerable<ITikReSentence> ExecuteListUntilDoneAsync(this ITikCommand command,
            int? timeoutSec = null, CancellationToken cancellationToken = default)
            => AsStreaming(command).ExecuteListUntilDoneAsync(timeoutSec, cancellationToken);

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
