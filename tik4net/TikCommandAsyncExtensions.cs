using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// The Task-based command surface as consumers see it: <c>Execute*Async</c> on any <see cref="ITikCommand"/>.
    /// <code>
    /// var cmd = connection.CreateCommand("/ip/address/print");
    /// IList&lt;ITikReSentence&gt; rows = await cmd.ExecuteListAsync(cancellationToken);
    /// </code>
    /// <para>
    /// Each method dispatches to the command's <see cref="ITikCommandAsync"/> implementation after a fail-closed
    /// check of <see cref="TikConnectionCapability.AsyncCommands"/>, so a transport that has no real async core
    /// throws <see cref="TikConnectionCapabilityNotSupportedException"/> instead of silently blocking a thread-pool
    /// thread. Read <see cref="ITikCommandAsync"/> for what the <see cref="CancellationToken"/> guarantees — it is
    /// not the same on every transport.
    /// </para>
    /// </summary>
    public static class TikCommandAsyncExtensions
    {
        /// <inheritdoc cref="ITikCommandAsync.ExecuteNonQueryAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task ExecuteNonQueryAsync(this ITikCommand command, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteNonQueryAsync(cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteScalarAsync(CancellationToken)"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<string> ExecuteScalarAsync(this ITikCommand command, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteScalarAsync(cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteScalarAsync(string, CancellationToken)"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="target">Name of the returned field.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<string> ExecuteScalarAsync(this ITikCommand command, string target, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteScalarAsync(target, cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteScalarOrDefaultAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="defaultValue">Value returned when no matching record was found.</param>
        /// <param name="target">Name of the returned field, or <c>null</c> for the first non-<c>.id</c> field.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<string> ExecuteScalarOrDefaultAsync(this ITikCommand command, string defaultValue = null,
            string target = null, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteScalarOrDefaultAsync(defaultValue, target, cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteSingleRowAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<ITikReSentence> ExecuteSingleRowAsync(this ITikCommand command, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteSingleRowAsync(cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteSingleRowOrDefaultAsync"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<ITikReSentence> ExecuteSingleRowOrDefaultAsync(this ITikCommand command, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteSingleRowOrDefaultAsync(cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteListAsync(CancellationToken)"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<IList<ITikReSentence>> ExecuteListAsync(this ITikCommand command, CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteListAsync(cancellationToken);

        /// <inheritdoc cref="ITikCommandAsync.ExecuteListAsync(string[], CancellationToken)"/>
        /// <param name="command">Command to execute.</param>
        /// <param name="proplistFields">List of fields to be returned.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static Task<IList<ITikReSentence>> ExecuteListAsync(this ITikCommand command, string[] proplistFields,
            CancellationToken cancellationToken = default(CancellationToken))
            => command.AsAsync().ExecuteListAsync(proplistFields, cancellationToken);

        /// <summary>
        /// Resolves the command's async implementation, or throws. Two ways to fail, deliberately reported as the
        /// same capability: the connection does not declare <see cref="TikConnectionCapability.AsyncCommands"/>, or
        /// the command object does not implement <see cref="ITikCommandAsync"/> at all (a consumer's own fake, say).
        /// </summary>
        internal static ITikCommandAsync AsAsync(this ITikCommand command)
        {
            Guard.ArgumentNotNull(command, "command");
            if (command.Connection == null)
                throw new System.InvalidOperationException("Connection is not assigned.");

            command.Connection.Require(TikConnectionCapability.AsyncCommands,
                "Task-based commands (ExecuteListAsync/ExecuteScalarAsync/…)");

            var asyncCommand = command as ITikCommandAsync;
            if (asyncCommand == null)
                throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.AsyncCommands,
                    "The connection reports the 'AsyncCommands' capability but its command implementation does not "
                    + "provide ITikCommandAsync. A custom ITikCommand must implement ITikCommandAsync to be awaited.");
            return asyncCommand;
        }
    }
}
