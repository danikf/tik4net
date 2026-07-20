using System;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Api;
using tik4net.MacTelnet;
using tik4net.Rest;
using tik4net.Telnet;
using tik4net.WinboxCli;
using tik4net.WinboxCliMac;
using tik4net.WinboxNative;

namespace tik4net
{
    /// <summary>
    /// Primary entry point for creating and opening MikroTik connections.
    /// Replaces the static <see cref="ConnectionFactory"/> (which is retained for backwards compatibility).
    /// </summary>
    public sealed class TikConnectionSetup
    {
        /// <summary>Router host name or IP address.</summary>
        public string Host { get; }
        /// <summary>RouterOS user name used for authentication.</summary>
        public string User { get; }
        /// <summary>Password for <see cref="User"/> (may be empty).</summary>
        public string Password { get; }

        /// <summary>Optional port override. When null the transport default is used (API=8728/8729, REST=80/443).</summary>
        public int? Port { get; set; }

        /// <summary>Connect timeout. Applies to the Open call.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// When true, self-signed / invalid SSL certificates on the router are accepted.
        /// Default is true (matching prior behaviour). Applies to both API-SSL and REST-SSL.
        /// Ignored when <see cref="CertificateValidationCallback"/> is set.
        /// </summary>
        public bool AllowInvalidCertificate { get; set; } = true;

        /// <summary>
        /// Optional custom certificate validation, applied to both API-SSL and REST-SSL. When set, it
        /// takes full control over accept/reject and <see cref="AllowInvalidCertificate"/> is ignored.
        /// Useful for certificate pinning or trusting a private CA.
        /// </summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }

        /// <summary>Creates a connection setup for the given router host and credentials.</summary>
        /// <param name="host">Router host name or IP address.</param>
        /// <param name="user">RouterOS user name.</param>
        /// <param name="password">Password for <paramref name="user"/> (may be empty).</param>
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
            var conn = NewApiConnection(false);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Creates and opens a MikroTik API-SSL connection (TLS TCP 8729).</summary>
        public ITikConnection CreateApiSslConnection()
        {
            var conn = NewApiConnection(true);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateApiConnection"/>.</summary>
        public Task<ITikConnection> CreateApiConnectionAsync(CancellationToken ct = default)
            => OpenAsync(NewApiConnection(false), ct);

        /// <summary>Async version of <see cref="CreateApiSslConnection"/>.</summary>
        public Task<ITikConnection> CreateApiSslConnectionAsync(CancellationToken ct = default)
            => OpenAsync(NewApiConnection(true), ct);

        private ApiConnection NewApiConnection(bool isSsl)
            => new ApiConnection(isSsl)
            {
                ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
                AllowInvalidCertificate = AllowInvalidCertificate,
                CertificateValidationCallback = CertificateValidationCallback,
            };

        // ── REST ──────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a REST API connection (HTTP, default port 80). Requires RouterOS 7.1+.</summary>
        public ITikConnection CreateRestConnection()
        {
            var conn = NewRestConnection(useSsl: false);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Creates and opens a REST API SSL connection (HTTPS, default port 443). Requires RouterOS 7.1+ with www-ssl enabled.</summary>
        public ITikConnection CreateRestSslConnection()
        {
            var conn = NewRestConnection(useSsl: true);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateRestConnection"/>.</summary>
        public Task<ITikConnection> CreateRestConnectionAsync(CancellationToken ct = default)
            => OpenAsync(NewRestConnection(useSsl: false), ct);

        /// <summary>Async version of <see cref="CreateRestSslConnection"/>.</summary>
        public Task<ITikConnection> CreateRestSslConnectionAsync(CancellationToken ct = default)
            => OpenAsync(NewRestConnection(useSsl: true), ct);

        private RestConnection NewRestConnection(bool useSsl)
            => new RestConnection(useSsl, allowInvalidCert: AllowInvalidCertificate,
                certificateValidationCallback: CertificateValidationCallback);

        // ── Telnet ────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a Telnet CLI connection (plain-text TCP port 23). Requires RouterOS telnet service enabled.</summary>
        public ITikConnection CreateTelnetConnection()
        {
            var conn = NewTelnetConnection();
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateTelnetConnection"/>.</summary>
        public Task<ITikConnection> CreateTelnetConnectionAsync(CancellationToken ct = default)
            => OpenAsync(NewTelnetConnection(), ct);

        private TelnetConnection NewTelnetConnection()
            => new TelnetConnection { ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds };

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

        // ── WinBox Native (M2) ──────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox <b>native-M2</b> connection (encrypted TCP port 8291). Issues
        /// structured M2 CRUD calls (no terminal), translating API paths/field names to/from WinBox handler
        /// and field keys via the router's version-matched <c>.jg</c> catalog. Requires the <c>winbox</c>
        /// service to be enabled (default).
        /// </summary>
        /// <param name="configure">
        /// Optional hook to configure the connection <b>before it opens</b> — the place to register
        /// <see cref="WinboxNativeConnection.PathAlias"/> / <see cref="WinboxNativeConnection.FieldOverride"/>
        /// mappings or set <see cref="WinboxNativeConnection.CatalogCachePath"/>. These must be set before
        /// <c>Open</c>, which is why this factory exposes a callback rather than only returning the connection.
        /// </param>
        /// <example>
        /// <para>The mappings are written in the <b>labels WinBox shows you</b>, not in raw handler numbers.
        /// Open the window in WinBox, read its menu breadcrumb and field captions, and lower-case them with
        /// spaces as dashes:</para>
        /// <code>
        /// using var conn = setup.CreateWinboxNativeConnection(c =>
        /// {
        ///     // WinBox menu:  PPP ▸ Secrets ▸ (window) PPP Secret     API path: /ppp/secret
        ///     c.PathAlias("/ppp/secret", "/ppp/secrets/ppp-secret");
        ///
        ///     // Accept field captions as typed in the GUI ("MAC Address" → mac-address, "Dst. Address" → dst-address).
        ///     c.UseGuiNames = true;
        ///
        ///     // Escape hatches, only when the label route fails:
        ///     c.FieldOverride("/ip/hotspot/user", "mac-address", 0x1);   // pin one field to its M2 key
        ///     c.PathOverride("/tool/sniffer", new[] { 27, 101 });        // pin a whole path to its handler
        /// });
        /// </code>
        /// <para><see cref="WinboxNativeConnection.PathAlias"/> keeps working after a RouterOS upgrade (only the
        /// text is pinned; the handler number is read live from the router's <c>.jg</c> catalog), whereas the
        /// numeric <c>*Override</c> forms pin values that can move between versions.</para>
        /// </example>
        public ITikConnection CreateWinboxNativeConnection(Action<WinboxNativeConnection> configure = null)
        {
            var conn = NewWinboxNative(configure);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateWinboxNativeConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxNativeConnectionAsync(
            Action<WinboxNativeConnection> configure = null, CancellationToken ct = default)
            => OpenAsync(NewWinboxNative(configure), ct);

        private WinboxNativeConnection NewWinboxNative(Action<WinboxNativeConnection> configure)
        {
            var conn = new WinboxNativeConnection
            {
                ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds,
            };
            configure?.Invoke(conn);
            return conn;
        }

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
