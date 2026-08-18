namespace tik4net
{
    /// <summary>
    /// Implemented by connections that reach the router over the <b>MAC layer</b> rather than over IP
    /// (MAC-Telnet, WinBox CLI over MAC, WinBox native over MAC), and therefore address it by MAC address.
    /// </summary>
    /// <remarks>
    /// The MAC transports still take a host argument in <c>Open</c> — it selects the local network
    /// interface to broadcast from — but what identifies the router is <see cref="RouterMac"/>.
    /// <see cref="TikConnectionSetup"/> applies <see cref="TikConnectionSetup.RouterMac"/> through this
    /// interface, so an IP transport neither receives nor silently ignores it.
    /// </remarks>
    public interface ITikMacLayerConnection
    {
        /// <summary>
        /// Router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>. Leave <c>null</c> to discover it by MNDP
        /// broadcast, which costs up to 5 seconds on every open. Must be set before the connection is
        /// opened.
        /// </summary>
        string? RouterMac { get; set; }
    }
}
