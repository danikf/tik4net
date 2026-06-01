using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Api;
using tik4net.Rest;
using tik4net.Telnet;

namespace tik4net
{
    /// <summary>
    /// Primary entry point for creating and opening MikroTik connections.
    /// Replaces the static <see cref="ConnectionFactory"/> (which is retained for backwards compatibility).
    /// </summary>
    public sealed class TikConnectionSetup
    {
        public string Host { get; }
        public string User { get; }
        public string Password { get; }

        /// <summary>Optional port override. When null the transport default is used (API=8728/8729, REST=80/443).</summary>
        public int? Port { get; set; }

        /// <summary>Connect timeout. Applies to the Open call.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// When true, self-signed / invalid SSL certificates on the router are accepted.
        /// Default is true (matching ApiConnection API-SSL behaviour).
        /// </summary>
        public bool AllowInvalidCertificate { get; set; } = true;

        public TikConnectionSetup(string host, string user, string password)
        {
            Guard.ArgumentNotNullOrEmptyString(host, nameof(host));
            Guard.ArgumentNotNull(user, nameof(user));
            Guard.ArgumentNotNull(password, nameof(password));
            Host = host;
            User = user;
            Password = password;
        }

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a plain MikroTik API connection (TCP 8728).</summary>
        public ITikConnection CreateApiConnection()
        {
            var conn = new ApiConnection(false);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Creates and opens a MikroTik API-SSL connection (TLS TCP 8729).</summary>
        public ITikConnection CreateApiSslConnection()
        {
            var conn = new ApiConnection(true);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateApiConnection"/>.</summary>
        public Task<ITikConnection> CreateApiConnectionAsync(CancellationToken ct = default)
            => OpenAsync(new ApiConnection(false), ct);

        /// <summary>Async version of <see cref="CreateApiSslConnection"/>.</summary>
        public Task<ITikConnection> CreateApiSslConnectionAsync(CancellationToken ct = default)
            => OpenAsync(new ApiConnection(true), ct);

        // ── REST ──────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a REST API connection (HTTP, default port 80). Requires RouterOS 7.1+.</summary>
        public ITikConnection CreateRestConnection()
        {
            var conn = new RestConnection(useSsl: false, allowInvalidCert: AllowInvalidCertificate);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Creates and opens a REST API SSL connection (HTTPS, default port 443). Requires RouterOS 7.1+ with www-ssl enabled.</summary>
        public ITikConnection CreateRestSslConnection()
        {
            var conn = new RestConnection(useSsl: true, allowInvalidCert: AllowInvalidCertificate);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateRestConnection"/>.</summary>
        public Task<ITikConnection> CreateRestConnectionAsync(CancellationToken ct = default)
            => OpenAsync(new RestConnection(useSsl: false, allowInvalidCert: AllowInvalidCertificate), ct);

        /// <summary>Async version of <see cref="CreateRestSslConnection"/>.</summary>
        public Task<ITikConnection> CreateRestSslConnectionAsync(CancellationToken ct = default)
            => OpenAsync(new RestConnection(useSsl: true, allowInvalidCert: AllowInvalidCertificate), ct);

        // ── Telnet ────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a Telnet CLI connection (plain-text TCP port 23). Requires RouterOS telnet service enabled.</summary>
        public ITikConnection CreateTelnetConnection()
        {
            var conn = new TelnetConnection();
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateTelnetConnection"/>.</summary>
        public Task<ITikConnection> CreateTelnetConnectionAsync(CancellationToken ct = default)
            => OpenAsync(new TelnetConnection(), ct);

        // ── Internals ─────────────────────────────────────────────────────────

        private void OpenSync(ITikConnection conn)
        {
            if (Port.HasValue)
                conn.Open(Host, Port.Value, User, Password);
            else
                conn.Open(Host, User, Password);
        }

        private async Task<ITikConnection> OpenAsync(ITikConnection conn, CancellationToken ct)
        {
            if (Port.HasValue)
                await conn.OpenAsync(Host, Port.Value, User, Password).ConfigureAwait(false);
            else
                await conn.OpenAsync(Host, User, Password).ConfigureAwait(false);
            return conn;
        }
    }
}
