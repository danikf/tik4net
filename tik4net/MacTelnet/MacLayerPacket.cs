namespace tik4net.MacTelnet
{
    /// <summary>
    /// The MikroTik MAC-layer packet: what MAC-Telnet, WinBox CLI over MAC and WinBox native over MAC all ride on
    /// (<c>Docs/mactelnet-protocol.md</c>).
    /// </summary>
    internal static class MacLayerPacket
    {
        /// <summary>
        /// The UDP port of the router's MAC server, for every MAC-layer session whatever it carries. The three public
        /// <c>DefaultPort</c> constants of the MAC transports are this value.
        /// </summary>
        internal const int Port = 20561;
    }
}
