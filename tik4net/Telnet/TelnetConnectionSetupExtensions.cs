using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Telnet
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the Telnet CLI transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.Telnet;</c> to see them.
    /// </remarks>
    public static class TelnetConnectionSetupExtensions
    {
        /// <summary>Creates and opens a Telnet CLI connection (plain-text TCP port 23). Requires RouterOS telnet service enabled.</summary>
        public static ITikCliConnection CreateTelnetConnection(this TikConnectionSetup setup)
            => (ITikCliConnection)setup.Create(TikConnectionType.Telnet);

        /// <summary>Async version of <see cref="CreateTelnetConnection"/>.</summary>
        public static async Task<ITikCliConnection> CreateTelnetConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
        {
            return (ITikCliConnection)await setup.CreateAsync(TikConnectionType.Telnet, ct).ConfigureAwait(false);
        }
    }
}
