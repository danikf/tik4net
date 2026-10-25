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
    /// <see cref="CreateRawCommand"/> extends <see cref="ITikRawSentenceConnection"/>, the facet of the transports that have a command language
    /// of their own (<see cref="TikConnectionCapability.RawCommand"/>): the binary API and the CLI family. REST and
    /// WinBox native do not implement it, so the call does not compile on their typed connections — WinBox native's
    /// raw form would be a numeric M2 message, not a string, so use a CLI transport for raw over that channel. Code
    /// holding a plain <see cref="ITikConnection"/> pattern-matches:
    /// <c>if (connection is ITikRawSentenceConnection raw) raw.CreateRawCommand(…)</c>.
    /// The whole payload lives in the command text. On the CLI transports parameters added to the returned command
    /// are ignored; on the binary API they are appended as sentence words, as on any API command.
    /// <para>
    /// A raw <b>write</b> prints nothing when it succeeds, so run it with <see cref="ITikCommand.ExecuteNonQuery"/>;
    /// <see cref="ITikCommand.ExecuteScalar()"/> has no value to return and throws
    /// <see cref="TikCommandEmptyResponseException"/> (use <see cref="ITikCommand.ExecuteScalarOrDefault()"/> when
    /// the output is optional).
    /// </para>
    /// </remarks>
    public static class TikRawCommandExtensions
    {
        /// <summary>
        /// Creates a raw pass-through command (see the type remarks for the per-transport dialect): the
        /// <see cref="ITikCommand"/>-level form of the facet's <see cref="ITikRawSentenceConnection.CallCommandSync(string[])"/>.
        /// </summary>
        /// <remarks>
        /// Like <see cref="ITikRawSentenceConnection.CallCommandSync(string[])"/>, it trusts the facet: implementing
        /// <see cref="ITikRawSentenceConnection"/> is the promise, and no capability flag is checked.
        /// </remarks>
        /// <param name="connection">A connection that has a command language of its own. It must also be an
        ///   <see cref="ITikConnection"/>, as every implementer in tik4net is.</param>
        /// <param name="raw">The raw payload in the transport's dialect (API sentence / CLI line).</param>
        /// <param name="wrapAsValue">CLI convenience: wrap the line in <c>:put [ … as-value]</c> so <c>print</c>
        ///   output materialises as parseable rows. No effect on the binary API. Default <c>false</c> (verbatim).</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="connection"/> or <paramref name="raw"/> is null.</exception>
        /// <exception cref="System.ArgumentException"><paramref name="raw"/> is empty, or <paramref name="connection"/> is not
        ///   an <see cref="ITikConnection"/>.</exception>
        public static ITikCommand CreateRawCommand(this ITikRawSentenceConnection connection, string raw,
            bool wrapAsValue = false)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNullOrEmptyString(raw, nameof(raw));
            var tikConnection = connection as ITikConnection
                ?? throw new System.ArgumentException(
                    "The connection must also be an ITikConnection: CreateRawCommand creates the command with "
                    + "ITikConnection.CreateCommand.", nameof(connection));

            var cmd = tikConnection.CreateCommand(raw);
            // CLI commands are TikGenericCommand (ITikRawCommand) — flag them raw so the transport sends verbatim.
            // The binary API's ApiCommand already treats a \n-separated CommandText as raw sentence words (and
            // returns real !re rows), so it needs no flag.
            if (cmd is tik4net.Connection.ITikRawCommand rawCmd)
            {
                rawCmd.IsRaw = true;
                rawCmd.WrapAsValue = wrapAsValue;
            }
            return cmd;
        }
    }
}
