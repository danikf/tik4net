namespace tik4net
{
    /// <summary>
    /// Raw command pass-through factory. <see cref="CreateRawCommand"/> builds an <see cref="ITikCommand"/> whose
    /// <see cref="ITikCommand.CommandText"/> is sent <b>verbatim in the transport's own dialect</b>, bypassing the
    /// structured command builder/mapper — for non-CRUD commands, one-off snippets and debugging.
    /// </summary>
    /// <remarks>
    /// The payload dialect is transport-specific (the whole point of "raw"):
    /// <list type="bullet">
    ///   <item><b>Binary API</b> — a <c>\n</c>-separated API sentence (e.g. <c>"/interface/print\n?type=ether"</c>);
    ///     <c>ExecuteList</c> returns the real <c>!re</c> rows (lossless).</item>
    ///   <item><b>CLI transports</b> (Telnet / SSH / MAC-Telnet / WinBox CLI ±Mac) — a verbatim CLI line
    ///     (e.g. <c>"/interface print as-value"</c>); <c>ExecuteList</c> parses the as-value output into rows,
    ///     <c>ExecuteScalar</c> returns the raw text (e.g. <c>"/export"</c>). Pass <c>wrapAsValue</c>
    ///     to have the line wrapped in <c>:put [ … as-value]</c> so a bare <c>print</c> still yields parseable rows.</item>
    /// </list>
    /// Gated by <see cref="TikConnectionCapability.RawCommand"/>: transports that do not declare it
    /// (REST, WinBox native) throw <see cref="TikConnectionCapabilityNotSupportedException"/> — WinBox native's raw
    /// form would be a numeric M2 message, not a string, so use a CLI transport for raw over that channel.
    /// Parameters added to the returned command are ignored — the whole payload lives in the command text.
    /// </remarks>
    public static class TikRawCommandExtensions
    {
        /// <summary>
        /// Creates a raw pass-through command (see the type remarks for the per-transport dialect). Requires the
        /// <see cref="TikConnectionCapability.RawCommand"/> capability.
        /// </summary>
        /// <param name="connection">An open connection on a transport that reports <see cref="TikConnectionCapability.RawCommand"/>.</param>
        /// <param name="raw">The raw payload in the transport's dialect (API sentence / CLI line).</param>
        /// <param name="wrapAsValue">CLI convenience: wrap the line in <c>:put [ … as-value]</c> so <c>print</c>
        ///   output materialises as parseable rows. No effect on the binary API. Default <c>false</c> (verbatim).</param>
        public static ITikCommand CreateRawCommand(this ITikConnection connection, string raw, bool wrapAsValue = false)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNullOrEmptyString(raw, nameof(raw));
            connection.Require(TikConnectionCapability.RawCommand, "raw command pass-through");

            var cmd = connection.CreateCommand(raw);
            // CLI / REST / WinBox commands are TikGenericCommand (ITikRawCommand) — flag them raw so the transport
            // sends verbatim. The binary API's ApiCommand already treats a \n-separated CommandText as raw sentence
            // words (and returns real !re rows), so it needs no flag — the capability gate above is the only gate.
            if (cmd is tik4net.Connection.ITikRawCommand rawCmd)
            {
                rawCmd.IsRaw = true;
                rawCmd.WrapAsValue = wrapAsValue;
            }
            return cmd;
        }
    }
}
