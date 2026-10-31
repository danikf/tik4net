namespace tik4net.MacTelnet
{
    /// <summary>
    /// The router ended a MAC-layer login after the EC-SRP5 exchange began, before it said anything of its own: a
    /// MAC-Telnet session ended with nothing on screen, or a WinBox-over-MAC session ended before its confirmation.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses about one valid EC-SRP5 login in a hundred, and on the MAC layer the refusal is one datagram the
    /// router does not resend: MAC-Telnet's <c>Login failed, incorrect username or password</c>, followed by
    /// <c>PKT_END</c> 1–4 ms later without waiting for its acknowledgement (measured on 7.24); WinBox's
    /// <c>invalid user name or password (6)</c> in place of the confirmation, followed by the session's end. A session
    /// that ends with that datagram missing is therefore a refusal whose text was lost, and <c>TikOpenRetry</c> retries
    /// it as it retries a refusal. Internal: when the closure persists, the caller sees the
    /// <see cref="TikConnectionSessionClosedException"/> it always did.
    /// </remarks>
    internal sealed class MacLayerLoginClosedSilentlyException : TikConnectionSessionClosedException
    {
        /// <param name="transport">The transport's name in the message and the retry trace, e.g. <c>"MAC-Telnet"</c>.</param>
        internal MacLayerLoginClosedSilentlyException(string transport)
            : base(transport + ": the router closed the session during login.")
        {
            Transport = transport;
        }

        /// <summary>The transport whose login the router ended.</summary>
        internal string Transport { get; }
    }
}
