using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.WinboxCliMac
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the WinBox CLI over MAC transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.WinboxCliMac;</c> to see them.
    /// </remarks>
    public static class WinboxCliMacConnectionSetupExtensions
    {
        /// <summary>
        /// Creates and opens a WinBox CLI connection over the MAC layer (UDP port 20561). Same encrypted
        /// WinBox terminal CLI as <c>CreateWinboxCliConnection</c> (<c>tik4net.WinboxCli</c>), but works without an IP route
        /// to the router. Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c>.
        /// The router MAC address is discovered via MNDP (up to 5 s) when neither
        /// <paramref name="routerMac"/> nor <see cref="TikConnectionSetup.RouterMac"/> is set.
        /// </summary>
        /// <param name="setup">The configured connection setup.</param>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>, overriding <see cref="TikConnectionSetup.RouterMac"/>
        /// for this connection.
        /// </param>
        public static ITikMacCliConnection CreateWinboxCliMacConnection(this TikConnectionSetup setup, string? routerMac = null)
            => (ITikMacCliConnection)setup.Create(TikConnectionType.WinboxCliMac, TikConnectionSetup.OverrideRouterMac(routerMac));

        /// <summary>Async version of <see cref="CreateWinboxCliMacConnection"/>.</summary>
        public static async Task<ITikMacCliConnection> CreateWinboxCliMacConnectionAsync(this TikConnectionSetup setup, string? routerMac = null, CancellationToken ct = default)
        {
            return (ITikMacCliConnection)await setup.CreateAsync(TikConnectionType.WinboxCliMac, TikConnectionSetup.OverrideRouterMac(routerMac), ct).ConfigureAwait(false);
        }
    }
}
