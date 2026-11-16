using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects.Tracking;
using tik4net.Helpers;

namespace tik4net.Objects
{
    public static partial class TikConnectionAsyncExtensions
    {
#if NET8_0_OR_GREATER
        /// <summary>
        /// Streams entities for <paramref name="duration"/>, each one as the router sends its row, then cancels the
        /// command on the router. The streaming counterpart of
        /// <see cref="TikConnectionExtensions.LoadWithDuration{TEntity}(ITikConnection, TimeSpan, ITikCommandParameter[])"/>:
        /// each row is tracked as that method tracks it, so a later <c>Save</c> sends only what changed.
        /// <code>
        /// await foreach (var t in connection.LoadWithDurationAsync&lt;InterfaceMonitorTraffic&gt;(TimeSpan.FromSeconds(5),
        ///     cancellationToken, connection.CreateParameter("interface", "ether1")))
        ///     Console.WriteLine(t.RxBitsPerSecond);
        /// </code>
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.Streaming"/> (the binary API); without it this throws
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> when called, before anything is sent. The token comes
        /// before the parameters, as in <see cref="LoadListAsync{TEntity}(ITikConnection, CancellationToken, ITikCommandParameter[])"/>.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="duration">How long to keep the command open. A negative value is zero.</param>
        /// <param name="cancellationToken">Ends the read early; the command is cancelled on the router.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND).</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport does not stream.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error or the connection was lost, after the rows already delivered.</exception>
        public static IAsyncEnumerable<TEntity> LoadWithDurationAsync<TEntity>(this ITikConnection connection,
            TimeSpan duration, CancellationToken cancellationToken = default,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var sentences = command.ExecuteListWithDurationAsync(duration, cancellationToken);
            return TikObjectsCommandAsyncExtensions.MaterializeStream<TEntity>(command, sentences,
                entity => TikConnectionExtensions.RegisterSnapshots(connection, new[] { entity }, metadata, command),
                cancellationToken);
        }

        /// <summary>
        /// Streams entities until the command ends by itself (<c>!done</c>), or until <paramref name="timeout"/>
        /// elapses. Each row is tracked like any loaded entity.
        /// </summary>
        /// <remarks>
        /// net8.0 only. Needs <see cref="TikConnectionCapability.AsyncCommands"/>: streamed on the binary API, the
        /// awaited list on every other transport that declares it. Without it this throws
        /// <see cref="TikConnectionCapabilityNotSupportedException"/> when called.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="timeout">Optional upper bound. <c>null</c> waits for the command.</param>
        /// <param name="cancellationToken">Ends the read early.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND).</param>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no async commands.</exception>
        /// <exception cref="TikCommandAbortException">Thrown out of the enumeration when the router ended the command
        /// with an error, the connection was lost, or <paramref name="timeout"/> elapsed first.</exception>
        public static IAsyncEnumerable<TEntity> LoadUntilDoneAsync<TEntity>(this ITikConnection connection,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = TikConnectionExtensions.CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var sentences = command.ExecuteListUntilDoneAsync(timeout, cancellationToken);
            return TikObjectsCommandAsyncExtensions.MaterializeStream<TEntity>(command, sentences,
                entity => TikConnectionExtensions.RegisterSnapshots(connection, new[] { entity }, metadata, command),
                cancellationToken);
        }

#endif
    }
}
