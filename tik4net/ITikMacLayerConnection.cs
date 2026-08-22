namespace tik4net
{
    /// <summary>
    /// Implemented by connections that reach the router over the <b>MAC layer</b> rather than over IP
    /// (MAC-Telnet, WinBox CLI over MAC, WinBox native over MAC), and therefore address it by MAC address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the transports that work when the router has <b>no IP address at all</b> — a freshly reset
    /// device, or one whose addressing you are about to lock yourself out of — so the host argument of
    /// <c>Open</c> is optional here: pass an empty string and the session is addressed by
    /// <see cref="RouterMac"/> alone. Everything then travels to subnet broadcast, and the session latches
    /// onto the router's own address only if its first reply carries one.
    /// </para>
    /// <para>
    /// When a host <i>is</i> given it is not the router's identity — <see cref="RouterMac"/> is — but it
    /// still does two things: it names the local network interface to send from (rather than letting the
    /// host's broadcast route choose, which can pick a dead adapter), and it lets MNDP look the router's
    /// MAC up when <see cref="RouterMac"/> was not set.
    /// </para>
    /// <para>
    /// <see cref="TikConnectionSetup"/> applies <see cref="TikConnectionSetup.RouterMac"/> through this
    /// interface, so an IP transport neither receives nor silently ignores it.
    /// </para>
    /// </remarks>
    public interface ITikMacLayerConnection
    {
        /// <summary>
        /// Router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>. Leave <c>null</c> to discover it by MNDP
        /// broadcast from the host address, which costs up to 5 seconds on every open — and is not
        /// possible at all without a host, where this property is the only thing naming the router. Must
        /// be set before the connection is opened.
        /// </summary>
        string? RouterMac { get; set; }
    }
}
