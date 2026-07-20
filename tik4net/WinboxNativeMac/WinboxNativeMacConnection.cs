using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.WinboxNativeMac
{
    /// <summary>
    /// MikroTik RouterOS WinBox <b>native-M2</b> connection over the MAC layer (UDP 20561,
    /// client_type 0x0f90). Same structured getall/get/set/add/remove/move CRUD as
    /// <see cref="WinboxNative.WinboxNativeConnection"/>, but the M2 messages travel over the MAC
    /// layer instead of TCP 8291 — so it works without an IP route to the router.
    /// </summary>
    /// <remarks>
    /// Reuses the whole native-M2 engine (.jg catalog resolver, field encode/decode, streaming
    /// monitors, Safe Mode) from <see cref="WinboxNative.WinboxNativeConnection"/>; only the channel
    /// is swapped to the MAC-layer <c>WinboxMacM2Session</c> (EC-SRP5 + AES in MAC DATA packets).
    /// The router MAC is discovered via MNDP unless <see cref="RouterMac"/> is set. Requires
    /// <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c> on the router.
    /// </remarks>
    public sealed class WinboxNativeMacConnection : WinboxNativeConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal WinboxNativeMacConnection() { }

        /// <summary>
        /// Optional: router MAC as "AA:BB:CC:DD:EE:FF" to bypass MNDP discovery (which can take up to 5 s).
        /// Set before calling Open.
        /// </summary>
        public string RouterMac { get; set; }

        /// <summary>
        /// MAC-layer WinBox UDP port (informational — <see cref="WinboxMacM2Session"/> ignores the forwarded
        /// port and always uses UDP 20561). Overrides the base seam instead of <c>new</c>-shadowing the const.
        /// </summary>
        private protected override int DefaultPortValue => 20561;

        /// <inheritdoc/>
        private protected override IWinboxM2Channel CreateChannel() => new WinboxMacM2Session(RouterMac);
    }
}
