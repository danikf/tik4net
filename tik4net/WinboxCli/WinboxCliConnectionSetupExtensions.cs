using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.WinboxCli
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the WinBox CLI transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.WinboxCli;</c> to see them.
    /// </remarks>
    public static class WinboxCliConnectionSetupExtensions
    {
        /// <summary>
        /// Creates and opens a WinBox CLI connection (encrypted TCP port 8291). Drives the RouterOS CLI
        /// over the WinBox <c>mepty</c> terminal handler (EC-SRP5 auth, AES-128-CBC). Requires the
        /// <c>winbox</c> service to be enabled on the router (enabled by default).
        /// </summary>
        public static ITikConnection CreateWinboxCliConnection(this TikConnectionSetup setup)
            => setup.Create(TikConnectionType.WinboxCli);

        /// <summary>Async version of <see cref="CreateWinboxCliConnection"/>.</summary>
        public static Task<ITikConnection> CreateWinboxCliConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.WinboxCli, ct);
    }
}
