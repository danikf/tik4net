namespace tik4net.MacTelnet
{
    /// <summary>
    /// The router ended a MAC-Telnet login after the EC-SRP5 exchange with nothing on screen.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses a MAC-Telnet login with one data packet, <c>Login failed, incorrect username or password</c>,
    /// and sends <c>PKT_END</c> 1–4 ms later without waiting for the packet's acknowledgement or resending it
    /// (measured on 7.24). A session that ends with an empty screen is therefore a refusal whose text was lost, and
    /// <c>Winbox.RouterLoginRetry</c> retries it as it retries a refusal — which the router also gives about one valid
    /// login in a hundred. Internal: when the closure persists, the caller sees the
    /// <see cref="TikConnectionSessionClosedException"/> it always did.
    /// </remarks>
    internal sealed class MacTelnetLoginClosedSilentlyException : TikConnectionSessionClosedException
    {
        internal MacTelnetLoginClosedSilentlyException()
            : base("MAC-Telnet: the router closed the session during login.")
        {
        }
    }
}
