using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.WinboxNativeMac
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the WinBox native M2 over MAC transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.WinboxNativeMac;</c> to see them.
    /// </remarks>
    public static class WinboxNativeMacConnectionSetupExtensions
    {
        /// <summary>
        /// Creates and opens a WinBox native-M2 connection over the MAC layer (UDP port 20561). Same
        /// structured M2 CRUD as <c>CreateWinboxNativeConnection</c> (<c>tik4net.WinboxNative</c>), but works without an IP route
        /// to the router. Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c>.
        /// <para><b>Experimental</b>, for the same reason as <c>CreateWinboxNativeConnection</c> (<c>tik4net.WinboxNative</c>).
        /// <b>For production work prefer <c>CreateWinboxCliMacConnection</c> (<c>tik4net.WinboxCliMac</c>)</b> — the stable, proven
        /// transport on the same encrypted channel and the same MAC carrier.</para>
        /// </summary>
        /// <param name="setup">The configured connection setup.</param>
        /// <param name="configure">
        /// Optional hook to configure the connection before it opens — any of the mappings documented on
        /// <c>CreateWinboxNativeConnection</c> (<c>tik4net.WinboxNative</c>). The router MAC comes from <see cref="TikConnectionSetup.RouterMac"/>.
        /// </param>
        public static ITikConnection CreateWinboxNativeMacConnection(this TikConnectionSetup setup, Action<WinboxNativeMacConnection>? configure = null)
            => setup.Create(TikConnectionType.WinboxNativeMac, TikConnectionSetup.Typed(configure));

        /// <summary>Async version of <see cref="CreateWinboxNativeMacConnection"/>.</summary>
        public static Task<ITikConnection> CreateWinboxNativeMacConnectionAsync(this TikConnectionSetup setup, 
            Action<WinboxNativeMacConnection>? configure = null, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.WinboxNativeMac, TikConnectionSetup.Typed(configure), ct);
    }
}
