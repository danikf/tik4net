using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the MAC-Telnet CLI transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.MacTelnet;</c> to see them.
    /// </remarks>
    public static class MacTelnetConnectionSetupExtensions
    {
        /// <summary>
        /// Creates and opens a MAC-Telnet CLI connection (UDP port 20561).
        /// Requires <c>/tool/mac-server set allowed-interface-list=all</c> on the router.
        /// The router MAC address is discovered via MNDP (up to 5 s) when neither
        /// <paramref name="routerMac"/> nor <see cref="TikConnectionSetup.RouterMac"/> nor <see cref="TikConnectionSetup.Address"/> carries one —
        /// which needs a host to look it up by, so a MAC-only setup must name the MAC itself.
        /// </summary>
        /// <param name="setup">The configured connection setup.</param>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>, overriding <see cref="TikConnectionSetup.RouterMac"/>
        /// for this connection.
        /// </param>
        public static ITikMacCliConnection CreateMacTelnetConnection(this TikConnectionSetup setup, string? routerMac = null)
            => (ITikMacCliConnection)setup.Create(TikConnectionType.MacTelnet, TikConnectionSetup.OverrideRouterMac(routerMac));

        /// <summary>Async version of <see cref="CreateMacTelnetConnection"/>.</summary>
        public static async Task<ITikMacCliConnection> CreateMacTelnetConnectionAsync(this TikConnectionSetup setup, string? routerMac = null, CancellationToken ct = default)
        {
            return (ITikMacCliConnection)await setup.CreateAsync(TikConnectionType.MacTelnet, TikConnectionSetup.OverrideRouterMac(routerMac), ct).ConfigureAwait(false);
        }
    }
}
