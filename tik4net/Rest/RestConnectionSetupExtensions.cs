using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Rest
{
    /// <summary>
    /// <see cref="TikConnectionSetup"/> factories for the REST transport, kept in this transport's own
    /// namespace beside the connection they create.
    /// </summary>
    /// <remarks>
    /// Every option comes from the setup — these forward to
    /// <see cref="TikConnectionSetup.Create(TikConnectionType, Action{ITikConnection})"/> rather than
    /// building a connection by hand, so a new option reaches this transport without anyone remembering to
    /// copy it. Add a <c>using tik4net.Rest;</c> to see them.
    /// </remarks>
    public static class RestConnectionSetupExtensions
    {
        /// <summary>Creates and opens a REST API connection (HTTP, default port 80). Requires RouterOS 7.1+.</summary>
        public static ITikConnection CreateRestConnection(this TikConnectionSetup setup)
            => setup.Create(TikConnectionType.Rest);

        /// <summary>Creates and opens a REST API SSL connection (HTTPS, default port 443). Requires RouterOS 7.1+ with www-ssl enabled.</summary>
        public static ITikConnection CreateRestSslConnection(this TikConnectionSetup setup)
            => setup.Create(TikConnectionType.RestSsl);

        /// <summary>Async version of <see cref="CreateRestConnection"/>.</summary>
        public static Task<ITikConnection> CreateRestConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.Rest, ct);

        /// <summary>Async version of <see cref="CreateRestSslConnection"/>.</summary>
        public static Task<ITikConnection> CreateRestSslConnectionAsync(this TikConnectionSetup setup, CancellationToken ct = default)
            => setup.CreateAsync(TikConnectionType.RestSsl, ct);
    }
}
