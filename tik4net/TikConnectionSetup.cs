using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Api;
using tik4net.MacTelnet;
using tik4net.Rest;
using tik4net.Telnet;
using tik4net.WinboxCli;
using tik4net.WinboxCliMac;

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

        // ── MAC-Telnet ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a MAC-Telnet CLI connection (UDP port 20561).
        /// Requires <c>/tool/mac-server set allowed-interface-list=all</c> on the router.
        /// The router MAC address is discovered via MNDP (up to 5 s) when <paramref name="routerMac"/>
        /// is not provided.
        /// </summary>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> to bypass MNDP discovery.
        /// </param>
        public ITikConnection CreateMacTelnetConnection(string routerMac = null)
        {
            var conn = new MacTelnetConnection
            {
                RouterMac = routerMac,
                ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
            };
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateMacTelnetConnection"/>.</summary>
        public Task<ITikConnection> CreateMacTelnetConnectionAsync(string routerMac = null, CancellationToken ct = default)
            => OpenAsync(
                new MacTelnetConnection
                {
                    RouterMac = routerMac,
                    ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
                },
                ct);

        // ── WinBox CLI ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox CLI connection (encrypted TCP port 8291). Drives the RouterOS CLI
        /// over the WinBox <c>mepty</c> terminal handler (EC-SRP5 auth, AES-128-CBC). Requires the
        /// <c>winbox</c> service to be enabled on the router (enabled by default).
        /// </summary>
        public ITikConnection CreateWinboxCliConnection()
        {
            var conn = new WinboxCliConnection
            {
                ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
            };
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateWinboxCliConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxCliConnectionAsync(CancellationToken ct = default)
            => OpenAsync(
                new WinboxCliConnection
                {
                    ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
                },
                ct);

        // ── WinBox CLI over MAC ─────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox CLI connection over the MAC layer (UDP port 20561). Same encrypted
        /// WinBox terminal CLI as <see cref="CreateWinboxCliConnection"/>, but works without an IP route
        /// to the router. Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c>.
        /// The router MAC address is discovered via MNDP (up to 5 s) when <paramref name="routerMac"/>
        /// is not provided.
        /// </summary>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> to bypass MNDP discovery.
        /// </param>
        public ITikConnection CreateWinboxCliMacConnection(string routerMac = null)
        {
            var conn = new WinboxCliMacConnection
            {
                RouterMac = routerMac,
                ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
            };
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateWinboxCliMacConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxCliMacConnectionAsync(string routerMac = null, CancellationToken ct = default)
            => OpenAsync(
                new WinboxCliMacConnection
                {
                    RouterMac = routerMac,
                    ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
                },
                ct);

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
