namespace tik4net
{
    /// <summary>
    /// Implemented by connections that carry a per-command tag on the wire and use it to tell one caller's
    /// reply from another's — in practice the binary API's <c>.tag</c> word
    /// (<see cref="TikConnectionCapability.Tagging"/>).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ITikConnection"/> because the other transports that permit concurrent
    /// commands correlate replies by their own means (a separate HTTP request, an M2 request id). The
    /// setting decides nothing there, so it is not part of the surface they have to implement.
    /// </remarks>
    public interface ITikTaggedConnection
    {
        /// <summary>
        /// When <c>true</c>, the <c>.tag</c> word is sent on <b>synchronous</b> commands as well — which is
        /// what lets one connection carry commands from several threads at once.
        /// </summary>
        /// <remarks>
        /// <b>Set it before using one connection from several threads.</b> The router echoes the tag back,
        /// and that is the only thing tying a reply to the caller that asked for it; without it, concurrent
        /// synchronous commands genuinely cross-deliver rows — one caller's <c>.id</c> and <c>address</c>
        /// words arriving inside another caller's sentence — rather than failing. It costs one tagged word
        /// per command and nothing else.
        /// </remarks>
        bool SendTagWithSyncCommand { get; set; }
    }
}
