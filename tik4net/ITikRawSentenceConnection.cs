using System.Collections.Generic;

namespace tik4net
{
    /// <summary>
    /// Low-level access to a transport's own request/response form: a command written as rows and the
    /// answer handed back as <see cref="ITikSentence"/>s, below <see cref="ITikCommand"/> and below the
    /// O/R mapper.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ITikConnection"/> because it is the part of the connection surface a
    /// transport can reasonably not have — a custom transport, or a test double, has a command factory and
    /// a lifecycle long before it has a sentence dialect, and requiring every implementor to provide one
    /// would make the connection interface hard to implement.
    /// <para>
    /// <b>"Connection-specific format" is meant literally: the command is written in the transport's own
    /// language and sent unchanged.</b> On the binary API that is API sentence words, and the rows are the
    /// wire format. On the five CLI transports it is RouterOS CLI text — <c>:put [/interface print
    /// as-value]</c> — and that exact line goes to the terminal, with no path rewriting, no <c>where</c>
    /// building and no <c>proplist</c>. Nothing here translates an API path into a CLI command; a caller who
    /// wants that already has the O/R mapper and <see cref="ITikCommand"/>.
    /// </para>
    /// <para>
    /// So this interface is implemented only where such a language exists, and
    /// <see cref="TikConnectionCapability.RawSentences"/> is declared exactly there:
    /// <c>Api</c>/<c>ApiSsl</c> and the CLI family. REST and native WinBox have a request shape rather than a
    /// command language — an HTTP path, numeric M2 handlers and field keys — so they implement neither this
    /// interface nor <see cref="TikConnectionCapability.RawCommand"/>, its counterpart at the
    /// <see cref="ITikCommand"/> level. The two flags are the low-level and ADO-level halves of one promise
    /// and travel together.
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
