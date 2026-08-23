using tik4net.Winbox;
using tik4net.WinboxNative;

namespace tik4net.WinboxNativeMac
{
    /// <summary>
    /// MikroTik RouterOS WinBox <b>native-M2</b> connection over the MAC layer (UDP 20561,
    /// client_type 0x0f90). Same structured getall/get/set/add/remove/move CRUD as
    /// <see cref="WinboxNative.WinboxNativeConnection"/>, but the M2 messages travel over the MAC
    /// layer instead of TCP 8291 — so it works without an IP route to the router.
    /// <para><b>Experimental.</b> See the remarks for what that means and what to use instead in
    /// production.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>Experimental</b>, for the same reason as
    /// <see cref="WinboxNative.WinboxNativeConnection"/>: the API name ↔ WinBox key mapping is
    /// reconstructed from the router's <c>.jg</c> catalog, not published, so the translation from RouterOS
    /// API syntax is not a straightforward one.
    /// <b>For production work prefer <see cref="WinboxCliMac.WinboxCliMacConnection"/></b> — the stable,
    /// proven transport on the same encrypted channel and the same MAC carrier, which needs no name mapping
    /// at all and is interchangeable at the <see cref="ITikConnection"/> level. See the wiki page
    /// <i>WinBox-Native-MAC-connection</i>.</para>
    /// Reuses the whole native-M2 engine (.jg catalog resolver, field encode/decode, streaming
    /// monitors, Safe Mode) from <see cref="WinboxNative.WinboxNativeConnection"/>; only the channel
    /// is swapped to the MAC-layer <c>WinboxMacM2Session</c> (EC-SRP5 + AES in MAC DATA packets).
    /// The router MAC is discovered via MNDP unless <see cref="RouterMac"/> is set. Requires
    /// <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c> on the router.
    /// </remarks>
    public sealed class WinboxNativeMacConnection : WinboxNativeConnection, ITikMacLayerConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal WinboxNativeMacConnection() { }

        /// <inheritdoc/>
        public string? RouterMac { get; set; }

        /// <summary>
        /// MAC-layer WinBox UDP port (informational — <see cref="WinboxMacM2Session"/> ignores the forwarded
        /// port and always uses UDP 20561). Overrides the base seam instead of <c>new</c>-shadowing the const.
        /// </summary>
        private protected override int DefaultPortValue => 20561;

        /// <inheritdoc/>
        // WinboxMacM2Session's routerMac parameter isn't annotated nullable (it lives in Winbox/, out of
        // scope here), but null is its documented meaning: discover the router via MNDP.
        private protected override IWinboxM2Channel CreateChannel() => new WinboxMacM2Session(RouterMac!);
    }
}
