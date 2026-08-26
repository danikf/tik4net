using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Api
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the binary API transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.Api;</c> to see them.
    /// </remarks>
    public static class ApiConnectionSetupExtensions
    {
        /// <summary>Creates and opens a plain MikroTik API connection (TCP 8728).</summary>
        public static ITikApiConnection CreateApiConnection(this TikConnectionSetup setup)
            => (ITikApiConnection)setup.Create(TikConnectionType.Api);

        /// <summary>Creates and opens a MikroTik API-SSL connection (TLS TCP 8729).</summary>
        public static ITikApiConnection CreateApiSslConnection(this TikConnectionSetup setup)
            => (ITikApiConnection)setup.Create(TikConnectionType.ApiSsl);

        /// <summary>Async version of <see cref="CreateApiConnection"/>.</summary>
        public static async Task<ITikApiConnection> CreateApiConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
        {
            return (ITikApiConnection)await setup.CreateAsync(TikConnectionType.Api, ct).ConfigureAwait(false);
        }

        /// <summary>Async version of <see cref="CreateApiSslConnection"/>.</summary>
        public static async Task<ITikApiConnection> CreateApiSslConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
        {
            return (ITikApiConnection)await setup.CreateAsync(TikConnectionType.ApiSsl, ct).ConfigureAwait(false);
        }
    }
}
