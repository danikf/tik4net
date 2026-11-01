namespace tik4net
{
    /// <summary>
    /// What a <see cref="System.Threading.CancellationToken"/> is allowed to do to a command that is already
    /// on the wire, on a transport that does <b>not</b> report
    /// <see cref="TikConnectionCapability.CancelInFlight"/> — today the CLI family (Telnet, SSH, MAC-Telnet,
    /// WinBox CLI over TCP and over MAC).
    /// </summary>
    /// <remarks>
    /// A RouterOS terminal answers with an unframed byte stream: there is no sentence boundary, no request id
    /// and no marker to skip to. Abandoning a read therefore leaves output in the channel that the
    /// <i>next</i> command reads as its own — a silently wrong result, which is worse than waiting. So the
    /// default is to finish draining the response and only then report the cancel
    /// (<see cref="Cooperative"/>), and a caller who would rather lose the connection than wait says so
    /// explicitly (<see cref="AbandonAndClose"/>). The choice is a property of how much the caller values the
    /// session, not of one command, so it is set once on the connection
    /// (<see cref="TikConnectionSetup.CancellationMode"/>) rather than passed per call.
    /// <para>
    /// Transports that <i>do</i> report <see cref="TikConnectionCapability.CancelInFlight"/> (binary API,
    /// REST) ignore this setting — they cancel for real and stay usable, which is what the capability means.
    /// </para>
    /// </remarks>
    public enum TikCancellationMode
    {
        /// <summary>
        /// Default. A token cancelled after dispatch takes effect at the next safe point: the response is
        /// read to its prompt, and <see cref="System.OperationCanceledException"/> is thrown once the channel is
        /// back in step, so the connection is always left consistent and reusable. A running read is interrupted with
        /// Ctrl-C, which brings the prompt in about 50 ms (a <c>ping</c>, a long <c>print</c>, a monitor). A write is
        /// never interrupted, because its outcome would become unknown, and the call then does not return any sooner
        /// than the command itself would have. Neither is a command the library does not know to be a read.
        /// </summary>
        Cooperative,

        /// <summary>
        /// A token cancelled after dispatch abandons the in-flight read <b>and closes the connection</b>,
        /// which cannot be reused afterwards (open a new one). Never a silent desynchronization: it is a
        /// close, not a skip. Choose this when a stuck command must return control promptly and the session
        /// is cheap to replace.
        /// </summary>
        AbandonAndClose,
    }
}
