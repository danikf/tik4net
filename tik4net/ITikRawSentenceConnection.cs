using System.Collections.Generic;

namespace tik4net
{
    /// <summary>
    /// Low-level access to a transport's own request/response form: a command written as rows and the
    /// answer handed back as <see cref="ITikSentence"/>s, below <see cref="ITikCommand"/> and below the
    /// O/R mapper.
    /// </summary>
    /// <remarks>
    /// Split off <see cref="ITikConnection"/> in 4.0. It is the one part of the old connection surface
    /// that a transport can reasonably not have — a custom transport, or a test double, has a command
    /// factory and a lifecycle long before it has a sentence dialect — and requiring every implementor
    /// to provide it is what made the interface hard to implement.
    /// <para>
    /// Every transport tik4net ships does implement it, and the sentence dialect differs: the binary API
    /// returns the router's own <c>!re</c>/<c>!done</c>/<c>!trap</c> words, while the command transports
    /// synthesize equivalent sentences from what they parsed. The stronger promise — that the sentences
    /// are the router's, losslessly — is the separate
    /// <see cref="TikConnectionCapability.RawSentences"/> capability, which only the binary API declares.
    /// </para>
    /// </remarks>
    public interface ITikRawSentenceConnection
    {
        /// <summary>
        /// Calls a command (in connection-specific format) and waits for the response. The command is sent
        /// without a <c>.tag</c>; to use one, add it as an ordinary <c>.tag=1234</c> row at the end.
        /// </summary>
        /// <param name="commandRows">Rows of one command, in connection-specific format.</param>
        /// <returns>The returned sentences.</returns>
        /// <remarks>This is extremely low-level and should be used only when <see cref="ITikCommand"/> cannot do the job.</remarks>
        /// <exception cref="TikConnectionNotOpenException" />
        /// <seealso cref="ITikReSentence"/>
        /// <seealso cref="ITikDoneSentence"/>
        /// <seealso cref="ITikTrapSentence"/>
        IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows);

        /// <summary>
        /// Calls a command (in connection-specific format) and waits for the response. The command is sent
        /// without a <c>.tag</c>; to use one, add it as an ordinary <c>.tag=1234</c> row at the end.
        /// </summary>
        /// <param name="commandRows">Rows of one command, in connection-specific format.</param>
        /// <returns>The returned sentences.</returns>
        /// <exception cref="TikConnectionNotOpenException" />
        IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows);
    }

    /// <summary>
    /// Reaches <see cref="ITikRawSentenceConnection"/> from a plain <see cref="ITikConnection"/>, so the
    /// low-level call reads the same as it did before the interface was split off, and a transport that
    /// does not offer it fails with a capability error rather than a cast exception.
    /// </summary>
    public static class TikRawSentenceExtensions
    {
        /// <inheritdoc cref="ITikRawSentenceConnection.CallCommandSync(string[])"/>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no sentence dialect.</exception>
        public static IEnumerable<ITikSentence> CallCommandSync(this ITikConnection connection, params string[] commandRows)
            => AsRawSentenceConnection(connection).CallCommandSync(commandRows);

        /// <inheritdoc cref="ITikRawSentenceConnection.CallCommandSync(IEnumerable{string})"/>
        /// <exception cref="TikConnectionCapabilityNotSupportedException">The transport has no sentence dialect.</exception>
        public static IEnumerable<ITikSentence> CallCommandSync(this ITikConnection connection, IEnumerable<string> commandRows)
            => AsRawSentenceConnection(connection).CallCommandSync(commandRows);

        /// <summary>
        /// The connection as an <see cref="ITikRawSentenceConnection"/>, or a capability error naming what
        /// is missing.
        /// </summary>
        public static ITikRawSentenceConnection AsRawSentenceConnection(this ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            if (connection is ITikRawSentenceConnection raw)
                return raw;
            throw new TikConnectionCapabilityNotSupportedException(TikConnectionCapability.RawSentences,
                "This connection does not implement ITikRawSentenceConnection, so it has no low-level "
                + "sentence dialect to call. Use ITikCommand (connection.CreateCommand(...)) or the O/R "
                + "mapper instead.");
        }
    }
}
