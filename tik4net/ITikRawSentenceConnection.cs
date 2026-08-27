using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// <see cref="TikConnectionCapability.RawCommand"/> is declared exactly there:
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

        /// <summary>
        /// Awaitable counterpart of <see cref="CallCommandSync(string[])"/>: the same command, in the same
        /// connection-specific format, without holding a thread while the router thinks.
        /// </summary>
        /// <param name="commandRows">Rows of one command, in connection-specific format.</param>
        /// <param name="cancellationToken">
        /// Cancels the command. What that means depends on the transport, and it is the same contract the
        /// <c>Execute*Async</c> command surface has: on the binary API cancelling sends
        /// <c>/cancel tag=N</c> and the connection stays usable
        /// (<see cref="TikConnectionCapability.CancelInFlight"/>); on a terminal it is honoured before
        /// dispatch and deferred to the next safe point after it, because an abandoned read would leave
        /// output for the next command to misparse. See <see cref="TikCancellationMode"/>.
        /// </param>
        /// <returns>The returned sentences, already read.</returns>
        /// <remarks>
        /// The low level was synchronous-only, which was backwards: it is where long commands live —
        /// <c>/export</c>, a script, a monitor — and the levels above it
        /// (<c>ITikCommandAsync</c>, and <c>ITikStreamingCommand</c> on <c>net8.0</c>) had been awaitable
        /// for some time. Every transport implementing this interface awaits its own I/O rather than
        /// wrapping the blocking call, which is the promise
        /// <see cref="TikConnectionCapability.AsyncCommands"/> makes.
        /// </remarks>
        /// <exception cref="TikConnectionNotOpenException" />
        Task<IList<ITikSentence>> CallCommandAsync(string[] commandRows,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <inheritdoc cref="CallCommandAsync(string[], CancellationToken)"/>
        Task<IList<ITikSentence>> CallCommandAsync(IEnumerable<string> commandRows,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
