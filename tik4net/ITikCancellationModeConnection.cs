namespace tik4net
{
    /// <summary>
    /// Implemented by connections whose behaviour on a <b>late</b> cancellation — one that arrives after the
    /// command was already dispatched — is a choice rather than a given: the CLI family (Telnet, SSH,
    /// MAC-Telnet, WinBox CLI over TCP and over MAC), whose terminal byte stream has no framing to
    /// resynchronize on.
    /// </summary>
    /// <remarks>
    /// A transport that reports <see cref="TikConnectionCapability.CancelInFlight"/> (the binary API, REST,
    /// native WinBox) does not implement this: there the cancel is real and there is nothing to choose.
    /// That is the point of expressing the option as an interface — <see cref="TikConnectionSetup"/> applies
    /// <see cref="TikConnectionSetup.CancellationMode"/> only where it decides something.
    /// </remarks>
    /// <seealso cref="TikCancellationMode"/>
    public interface ITikCancellationModeConnection
    {
        /// <summary>
        /// What a <see cref="System.Threading.CancellationToken"/> cancelled after dispatch does to this
        /// connection. Defaults to <see cref="TikCancellationMode.Cooperative"/> — the response is drained
        /// and the cancel reported afterwards, so the session stays consistent.
        /// </summary>
        TikCancellationMode CancellationMode { get; set; }
    }
}
