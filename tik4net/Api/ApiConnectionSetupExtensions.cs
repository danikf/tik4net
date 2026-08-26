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
        public static ITikConnection CreateApiConnection(this TikConnectionSetup setup)
            => setup.Create(TikConnectionType.Api);

        /// <summary>Creates and opens a MikroTik API-SSL connection (TLS TCP 8729).</summary>
        public static ITikConnection CreateApiSslConnection(this TikConnectionSetup setup)
            => setup.Create(TikConnectionType.ApiSsl);

        /// <summary>Async version of <see cref="CreateApiConnection"/>.</summary>
        public static Task<ITikConnection> CreateApiConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.Api, ct);

        /// <summary>Async version of <see cref="CreateApiSslConnection"/>.</summary>
        public static Task<ITikConnection> CreateApiSslConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.ApiSsl, ct);
    }
}
