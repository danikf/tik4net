using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// Task-based sibling of <see cref="ITikCommand"/>'s <c>Execute*</c> methods, implemented by commands whose
    /// transport reports <see cref="TikConnectionCapability.AsyncCommands"/>.
    /// <para>
    /// <b>Consumers do not use this interface directly and never cast to it</b> — call the <c>Execute*Async</c>
    /// extension methods on <see cref="ITikCommand"/> (<see cref="TikCommandAsyncExtensions"/>), which do the
    /// dispatch and the fail-closed capability check. The interface exists because <c>netstandard2.0</c> has no
    /// default interface methods, so adding these members to <see cref="ITikCommand"/> would break every external
    /// implementer — including <c>tik4net.testing</c>'s fake connection and any consumer's own fake.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the <see cref="CancellationToken"/> guarantees.</b> Before dispatch, on every transport: nothing is
    /// written, <see cref="System.OperationCanceledException"/> is thrown, the connection is untouched. After
    /// dispatch it depends on the protocol, which is what <see cref="TikConnectionCapability.CancelInFlight"/>
    /// declares. Without that capability, a token cancelled mid-command does <b>not</b> abort the operation: the
    /// returned <see cref="Task"/> completes only once the response has been drained, because abandoning an
    /// unframed byte stream would leave output for the next command to misparse. Passing a token and observing
    /// nothing happen is therefore expected behaviour there, not a bug.
    /// </para>
    /// <para>
    /// Cancellation surfaces as <see cref="System.OperationCanceledException"/> (or its
    /// <see cref="TaskCanceledException"/> subclass) — never wrapped in a <c>Tik*</c> exception, so generic async
    /// code can catch it. Everything else throws exactly what the synchronous methods throw. A timeout is not a
    /// cancellation and keeps its own exception (<see cref="TikConnectionReceiveTimeoutException"/> / socket
    /// errors), so a misconfigured timeout cannot masquerade as a caller-requested cancel.
    /// </para>
    /// </remarks>
    /// <seealso cref="TikCommandAsyncExtensions"/>
    public interface ITikCommandAsync
    {
        /// <summary>Async <see cref="ITikCommand.ExecuteNonQuery"/>.</summary>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task ExecuteNonQueryAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteScalar()"/>.</summary>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<string> ExecuteScalarAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteScalar(string)"/>.</summary>
        /// <param name="target">Name of the returned field.</param>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<string> ExecuteScalarAsync(string target, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteScalarOrDefault(string, string)"/>.</summary>
        /// <param name="defaultValue">Value returned when no matching record was found.</param>
        /// <param name="target">Name of the returned field, or <c>null</c> for the first non-<c>.id</c> field.</param>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<string?> ExecuteScalarOrDefaultAsync(string? defaultValue = null, string? target = null, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteSingleRow"/>.</summary>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<ITikReSentence> ExecuteSingleRowAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteSingleRowOrDefault"/>.</summary>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<ITikReSentence?> ExecuteSingleRowOrDefaultAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Async <see cref="ITikCommand.ExecuteList()"/>. Returns <see cref="IList{T}"/> rather than
        /// <see cref="IEnumerable{T}"/> on purpose: the rows are already materialized when the task completes, and
        /// hiding a finished collection behind a lazy interface invites "why does awaiting it not do the work".
        /// </summary>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<IList<ITikReSentence>> ExecuteListAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Async <see cref="ITikCommand.ExecuteList(string[])"/>.</summary>
        /// <param name="proplistFields">List of fields to be returned (ignored by transports that always return all fields).</param>
        /// <param name="cancellationToken">Cancellation token — see the interface remarks for what it guarantees per transport.</param>
        Task<IList<ITikReSentence>> ExecuteListAsync(string[] proplistFields, CancellationToken cancellationToken = default(CancellationToken));
    }
}
