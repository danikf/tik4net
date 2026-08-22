using System;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.WinboxNative;
using tik4net.WinboxNativeMac;

namespace tik4net
{
    /// <summary>
    /// The entry point for creating and opening MikroTik connections: one object carrying the router
    /// coordinates and every connection option, and one <see cref="Create(TikConnectionType)"/> that applies
    /// them and opens the transport you name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every option here applies on every transport it can mean anything on</b>, and that is enforced by a
    /// unit-test matrix rather than by the property names lining up. The options that are not universal are
    /// the ones a transport declares an interest in by implementing an interface —
    /// <see cref="ITikTlsConnection"/> (the certificate options), <see cref="ITikMacLayerConnection"/>
    /// (<see cref="RouterMac"/>) and <see cref="ITikCancellationModeConnection"/>
    /// (<see cref="CancellationMode"/>) — so a transport either receives an option or provably has no use
    /// for it. There is no third case where a value is set here and quietly dropped, which is what used to
    /// happen (through 4.0, <see cref="AllowInvalidCertificate"/> reached REST and not API-SSL, and the SSH
    /// satellite transport received only <see cref="CancellationMode"/>).
    /// </para>
    /// <para>
    /// <see cref="ConnectionFactory"/> remains as a compatibility shim over the same machinery. It creates
    /// connections with their <b>own defaults</b> — it has no options object — which is the reason to prefer
    /// this class in new code.
    /// </para>
    /// <para>
    /// The router is named by a <see cref="TikRouterAddress"/> — a host, a MAC, or both. A MAC-only setup is
    /// legitimate and is what the MAC-layer transports exist for (a router with no IP address); asking an IP
    /// transport for one fails at <see cref="CreateUnopened"/> with a message naming the missing coordinate.
    /// </para>
    /// <example>
    /// <code>
    /// var setup = new TikConnectionSetup("192.168.88.1", "admin", "")
    /// {
    ///     ConnectTimeout = TimeSpan.FromSeconds(5),
    ///     AllowInvalidCertificate = false,
    /// };
    /// using var conn = setup.Create(TikConnectionType.ApiSsl);
    ///
    /// // A router with no IP address — reachable only over the MAC layer:
    /// var macSetup = new TikConnectionSetup(TikRouterAddress.FromMac("AA:BB:CC:DD:EE:FF"), "admin", "");
    /// using var macConn = macSetup.CreateMacTelnetConnection();
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class TikConnectionSetup
    {
        /// <summary>
        /// Where the router is: a host name / IP address, a MAC address, or both. Which of the two a
        /// transport needs is decided when the connection is created — see <see cref="CreateUnopened"/>.
        /// </summary>
        public TikRouterAddress Address { get; }

        /// <summary>
        /// Router host name or IP address, or <c>null</c> when the setup addresses the router by MAC alone
        /// (which only the MAC-layer transports can use). Shorthand for <see cref="Address"/>.
        /// </summary>
        public string? Host => Address.Host;

        /// <summary>RouterOS user name used for authentication.</summary>
        public string User { get; }
        /// <summary>Password for <see cref="User"/> (may be empty).</summary>
        public string Password { get; }

        /// <summary>Optional port override. When null the transport default is used (API=8728/8729, REST=80/443).</summary>
        public int? Port { get; set; }

        /// <summary>
        /// How long opening the connection may take before it fails. Default 15 s.
        /// Applied to <see cref="ITikConnection.ConnectTimeout"/> on every transport.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How long one command may wait for its answer. Default 30 s. Applied to
        /// <see cref="ITikConnection.ReceiveTimeout"/>. Bounds a command, not the connection: an idle
        /// connection with nothing in flight is not subject to it.
        /// </summary>
        public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How long a send may take before it fails. Default 30 s. Applied to
        /// <see cref="ITikConnection.SendTimeout"/>.
        /// </summary>
        public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Wire text encoding, applied to <see cref="ITikConnection.Encoding"/>. Default UTF-8, which is
        /// what RouterOS 7 speaks; set <see cref="System.Text.Encoding.ASCII"/> only for a RouterOS 6.x
        /// router that predates UTF-8 support.
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        /// <summary>
        /// Applied to <see cref="ITikTaggedConnection.SendTagWithSyncCommand"/>. The transports that
        /// correlate replies by their own means do not implement that interface and never see it.
        /// </summary>
        /// <remarks>
        /// <b>The default is <c>true</c> since 4.0.</b> It costs one tagged word per command, and without it
        /// two threads sharing one API connection cross-deliver rows — a wrong <i>answer</i> rather than an
        /// error, which is the worst way for a default to be wrong. Set it to <c>false</c> only to keep the
        /// wire byte-identical to 3.x.
        /// </remarks>
        public bool SendTagWithSyncCommand { get; set; } = true;

        /// <summary>
        /// Applied to <see cref="ITikConnection.DebugEnabled"/> when set. <c>null</c> (the default) leaves
        /// the transport's own default, which is "on when a debugger is attached".
        /// </summary>
        public bool? DebugEnabled { get; set; }

        /// <summary>
        /// Router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> for the MAC-layer transports (MAC-Telnet,
        /// WinBox CLI over MAC, WinBox native over MAC), which is what identifies the router there.
        /// Applied through <see cref="ITikMacLayerConnection"/>, so an IP transport never sees it.
        /// </summary>
        /// <remarks>
        /// Setting it here is the same thing as putting the MAC in <see cref="Address"/> and overrides it;
        /// <c>null</c> (the default) means "whatever the address says". With no MAC from either, a MAC
        /// transport discovers one by MNDP broadcast from <see cref="Host"/>, which costs up to 5 s on
        /// every open — and a setup that has no host either cannot do that and is rejected at
        /// <see cref="CreateUnopened"/>.
        /// </remarks>
        public string? RouterMac { get; set; }

        /// <summary>The MAC actually applied to a MAC-layer connection: the explicit option, else the address.</summary>
        private string? EffectiveRouterMac => RouterMac ?? Address.Mac;

        /// <summary>
        /// What a <see cref="CancellationToken"/> cancelled <b>after</b> a command was dispatched may do to
        /// the connection. Applies to the CLI transports (Telnet, SSH, MAC-Telnet, WinBox CLI over TCP and
        /// over MAC), whose terminal byte stream has no point to resynchronize on; the API, REST and native
        /// WinBox cancel for real and do not implement <see cref="ITikCancellationModeConnection"/>
        /// (<see cref="TikConnectionCapability.CancelInFlight"/>). Defaults to
        /// <see cref="TikCancellationMode.Cooperative"/> — the connection is never left desynchronized.
        /// </summary>
        public TikCancellationMode CancellationMode { get; set; } = TikCancellationMode.Cooperative;

        /// <summary>
        /// When true, self-signed / invalid SSL certificates on the router are accepted. Applies to both
        /// API-SSL and REST-SSL; ignored when <see cref="CertificateValidationCallback"/> is set.
        /// </summary>
        /// <remarks>
        /// <b>The default is <c>false</c> since 4.0</b> — the certificate is validated against the OS trust
        /// store. A RouterOS device usually presents a self-signed certificate, so a lab or an internal
        /// deployment that has not installed a trusted one has to say so: set this to <c>true</c>, or better,
        /// pin the router's certificate through <see cref="CertificateValidationCallback"/>. "Encrypted but
        /// unauthenticated" is the setting that has to be asked for, not the one you get by not asking.
        /// </remarks>
        public bool AllowInvalidCertificate { get; set; }

        /// <summary>
        /// Optional custom certificate validation, applied to both API-SSL and REST-SSL. When set, it
        /// takes full control over accept/reject and <see cref="AllowInvalidCertificate"/> is ignored.
        /// Useful for certificate pinning or trusting a private CA.
        /// </summary>
        public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

        /// <summary>Creates a connection setup for the given router address and credentials.</summary>
        /// <param name="address">
        /// Where the router is. A bare string converts implicitly and is read for what it looks like —
        /// <c>"192.168.88.1"</c> is a host, <c>"AA:BB:CC:DD:EE:FF"</c> is a MAC — or say it outright with
        /// <see cref="TikRouterAddress.FromHost"/> / <see cref="TikRouterAddress.FromMac"/> /
        /// <see cref="TikRouterAddress.FromHostAndMac"/>.
        /// </param>
        /// <param name="user">RouterOS user name.</param>
        /// <param name="password">Password for <paramref name="user"/> (may be empty).</param>
        /// <remarks>
        /// A MAC-only address is accepted here and rejected later, by the transports that cannot use it —
        /// see <see cref="CreateUnopened"/>. It has to be that way round: which coordinate is required is a
        /// property of the transport, and the transport is not named until <c>Create</c>.
        /// </remarks>
        public TikConnectionSetup(TikRouterAddress address, string user, string password)
        {
            if (address.IsEmpty)
                throw new ArgumentException(
                    "The router address must carry a host name / IP address, a MAC address, or both.", nameof(address));
            Guard.ArgumentNotNull(user, nameof(user));
            Guard.ArgumentNotNull(password, nameof(password));
            Address = address;
            User = user;
            Password = password;
        }

        // ── The general entry point ───────────────────────────────────────────

        /// <summary>
        /// Creates a connection of the given type with every option of this setup applied, and opens it.
        /// </summary>
        /// <param name="connectionType">Which transport to open.</param>
        /// <exception cref="NotImplementedException">
        /// The type lives in a satellite package that has not been registered — call
        /// <c>tik4net.Ssh.Tik4NetSsh.Register()</c> (or the package's own equivalent) once at startup, or use
        /// that package's <c>Create…Connection</c> extension method instead.
        /// </exception>
        public ITikConnection Create(TikConnectionType connectionType)
            => Create(connectionType, null);

        /// <summary>
        /// Creates a connection of the given type with every option of this setup applied, hands it to
        /// <paramref name="configure"/>, and opens it.
        /// </summary>
        /// <param name="connectionType">Which transport to open.</param>
        /// <param name="configure">
        /// Optional hook run <b>after</b> the options are applied and <b>before</b> the connection opens —
        /// the place for transport-specific settings that are not options of this setup (see
        /// <see cref="CreateWinboxNativeConnection(Action{WinboxNativeConnection})"/> for the case that
        /// needs it).
        /// </param>
        public ITikConnection Create(TikConnectionType connectionType, Action<ITikConnection>? configure)
        {
            var conn = CreateUnopened(connectionType, configure);
            OpenSync(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="Create(TikConnectionType)"/>.</summary>
        public Task<ITikConnection> CreateAsync(TikConnectionType connectionType, CancellationToken ct = default)
            => CreateAsync(connectionType, null, ct);

        /// <summary>Async version of <see cref="Create(TikConnectionType, Action{ITikConnection})"/>.</summary>
        public Task<ITikConnection> CreateAsync(TikConnectionType connectionType,
            Action<ITikConnection>? configure, CancellationToken ct = default)
            => OpenCoreAsync(CreateUnopened(connectionType, configure), ct);

        /// <summary>
        /// Creates a connection of the given type with every option of this setup applied but <b>does not
        /// open it</b> — for the caller who needs to touch something on the concrete connection type that is
        /// not an option here, or to inspect what the options did.
        /// </summary>
        /// <param name="connectionType">Which transport to create.</param>
        /// <param name="configure">Optional hook run after the options are applied.</param>
        public ITikConnection CreateUnopened(TikConnectionType connectionType, Action<ITikConnection>? configure = null)
        {
            var conn = TikConnectionRegistry.Create(connectionType);
            ApplyTo(conn);
            configure?.Invoke(conn);
            return conn;
        }

        /// <summary>
        /// Applies every option of this setup to an already-created connection. Options that only some
        /// transports can honour are applied through the interface that declares them
        /// (<see cref="ITikTlsConnection"/>, <see cref="ITikMacLayerConnection"/>,
        /// <see cref="ITikCancellationModeConnection"/>), so a transport that does not implement one is
        /// deliberately, and visibly, skipped.
        /// </summary>
        /// <remarks>
        /// Public because the satellite transport packages (<c>tik4net.ssh</c>) create their own connection
        /// types and must configure them exactly as the built-in ones are configured; doing it by hand is
        /// how SSH ended up honouring one option out of ten.
        /// </remarks>
        /// <param name="connection">Connection to configure. Must not be open yet.</param>
        public void ApplyTo(ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            RequireUsableAddress(connection);

            connection.ConnectTimeout = ToMilliseconds(ConnectTimeout);
            connection.ReceiveTimeout = ToMilliseconds(ReceiveTimeout);
            connection.SendTimeout = ToMilliseconds(SendTimeout);
            connection.Encoding = Encoding;
            if (DebugEnabled.HasValue)
                connection.DebugEnabled = DebugEnabled.Value;

            if (connection is ITikTlsConnection tls)
            {
                tls.AllowInvalidCertificate = AllowInvalidCertificate;
                tls.CertificateValidationCallback = CertificateValidationCallback;
            }

            if (connection is ITikMacLayerConnection mac)
                mac.RouterMac = EffectiveRouterMac;

            if (connection is ITikCancellationModeConnection cancellable)
                cancellable.CancellationMode = CancellationMode;

            if (connection is ITikTaggedConnection tagged)
                tagged.SendTagWithSyncCommand = SendTagWithSyncCommand;
        }

        /// <summary>
        /// Checks that this setup carries the coordinate the given connection actually addresses the router
        /// by. Which one that is depends on the transport, so it cannot be settled in the constructor —
        /// the same setup is legitimate for one transport and unusable for another.
        /// </summary>
        /// <remarks>
        /// A MAC-layer connection takes either: the MAC identifies the router, and a host on its own is
        /// still enough because MNDP can look the MAC up from it. Every other transport speaks IP and needs
        /// the host. The failure is raised here, at <c>Create</c>, rather than several layers down inside a
        /// socket call that cannot say which property was missing.
        /// </remarks>
        private void RequireUsableAddress(ITikConnection connection)
        {
            if (connection is ITikMacLayerConnection)
            {
                if (Address.HasHost || !string.IsNullOrEmpty(EffectiveRouterMac))
                    return;

                throw new InvalidOperationException(
                    "A MAC-layer connection needs the router's MAC address or its host address. "
                    + "Create the setup with TikRouterAddress.FromMac(\"AA:BB:CC:DD:EE:FF\"), or set RouterMac.");
            }

            if (!Address.HasHost)
                throw new InvalidOperationException(
                    $"{connection.GetType().Name} connects over IP and needs the router's host name or IP "
                    + $"address, but this setup was created from a MAC address ({Address}). Only the "
                    + "MAC-layer transports (MAC-Telnet, WinBox CLI over MAC, WinBox native over MAC) can "
                    + "reach a router by MAC alone.");
        }

        // A TimeSpan is the friendlier option type; the connections take milliseconds. Saturating rather
        // than overflowing keeps "effectively no bound" (TimeSpan.MaxValue) from arriving as a negative
        // millisecond count, which several transports would read as "no wait at all".
        private static int ToMilliseconds(TimeSpan value)
        {
            double ms = value.TotalMilliseconds;
            if (ms >= int.MaxValue) return int.MaxValue;
            if (ms <= int.MinValue) return int.MinValue;
            return (int)ms;
        }

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a plain MikroTik API connection (TCP 8728).</summary>
        public ITikConnection CreateApiConnection()
            => Create(TikConnectionType.Api);

        /// <summary>Creates and opens a MikroTik API-SSL connection (TLS TCP 8729).</summary>
        public ITikConnection CreateApiSslConnection()
            => Create(TikConnectionType.ApiSsl);

        /// <summary>Async version of <see cref="CreateApiConnection"/>.</summary>
        public Task<ITikConnection> CreateApiConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.Api, ct);

        /// <summary>Async version of <see cref="CreateApiSslConnection"/>.</summary>
        public Task<ITikConnection> CreateApiSslConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.ApiSsl, ct);

        // ── REST ──────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a REST API connection (HTTP, default port 80). Requires RouterOS 7.1+.</summary>
        public ITikConnection CreateRestConnection()
            => Create(TikConnectionType.Rest);

        /// <summary>Creates and opens a REST API SSL connection (HTTPS, default port 443). Requires RouterOS 7.1+ with www-ssl enabled.</summary>
        public ITikConnection CreateRestSslConnection()
            => Create(TikConnectionType.RestSsl);

        /// <summary>Async version of <see cref="CreateRestConnection"/>.</summary>
        public Task<ITikConnection> CreateRestConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.Rest, ct);

        /// <summary>Async version of <see cref="CreateRestSslConnection"/>.</summary>
        public Task<ITikConnection> CreateRestSslConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.RestSsl, ct);

        // ── Telnet ────────────────────────────────────────────────────────────

        /// <summary>Creates and opens a Telnet CLI connection (plain-text TCP port 23). Requires RouterOS telnet service enabled.</summary>
        public ITikConnection CreateTelnetConnection()
            => Create(TikConnectionType.Telnet);

        /// <summary>Async version of <see cref="CreateTelnetConnection"/>.</summary>
        public Task<ITikConnection> CreateTelnetConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.Telnet, ct);

        // ── MAC-Telnet ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a MAC-Telnet CLI connection (UDP port 20561).
        /// Requires <c>/tool/mac-server set allowed-interface-list=all</c> on the router.
        /// The router MAC address is discovered via MNDP (up to 5 s) when neither
        /// <paramref name="routerMac"/> nor <see cref="RouterMac"/> nor <see cref="Address"/> carries one —
        /// which needs a host to look it up by, so a MAC-only setup must name the MAC itself.
        /// </summary>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>, overriding <see cref="RouterMac"/>
        /// for this connection.
        /// </param>
        public ITikConnection CreateMacTelnetConnection(string? routerMac = null)
            => Create(TikConnectionType.MacTelnet, OverrideRouterMac(routerMac));

        /// <summary>Async version of <see cref="CreateMacTelnetConnection"/>.</summary>
        public Task<ITikConnection> CreateMacTelnetConnectionAsync(string? routerMac = null, CancellationToken ct = default)
            => CreateAsync(TikConnectionType.MacTelnet, OverrideRouterMac(routerMac), ct);

        // ── WinBox CLI ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox CLI connection (encrypted TCP port 8291). Drives the RouterOS CLI
        /// over the WinBox <c>mepty</c> terminal handler (EC-SRP5 auth, AES-128-CBC). Requires the
        /// <c>winbox</c> service to be enabled on the router (enabled by default).
        /// </summary>
        public ITikConnection CreateWinboxCliConnection()
            => Create(TikConnectionType.WinboxCli);

        /// <summary>Async version of <see cref="CreateWinboxCliConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxCliConnectionAsync(CancellationToken ct = default)
            => CreateAsync(TikConnectionType.WinboxCli, ct);

        // ── WinBox CLI over MAC ─────────────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox CLI connection over the MAC layer (UDP port 20561). Same encrypted
        /// WinBox terminal CLI as <see cref="CreateWinboxCliConnection"/>, but works without an IP route
        /// to the router. Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c>.
        /// The router MAC address is discovered via MNDP (up to 5 s) when neither
        /// <paramref name="routerMac"/> nor <see cref="RouterMac"/> is set.
        /// </summary>
        /// <param name="routerMac">
        /// Optional router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c>, overriding <see cref="RouterMac"/>
        /// for this connection.
        /// </param>
        public ITikConnection CreateWinboxCliMacConnection(string? routerMac = null)
            => Create(TikConnectionType.WinboxCliMac, OverrideRouterMac(routerMac));

        /// <summary>Async version of <see cref="CreateWinboxCliMacConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxCliMacConnectionAsync(string? routerMac = null, CancellationToken ct = default)
            => CreateAsync(TikConnectionType.WinboxCliMac, OverrideRouterMac(routerMac), ct);

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
        public ITikConnection CreateWinboxNativeConnection(Action<WinboxNativeConnection>? configure = null)
            => Create(TikConnectionType.WinboxNative, Typed(configure));

        /// <summary>Async version of <see cref="CreateWinboxNativeConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxNativeConnectionAsync(
            Action<WinboxNativeConnection>? configure = null, CancellationToken ct = default)
            => CreateAsync(TikConnectionType.WinboxNative, Typed(configure), ct);

        // ── WinBox Native (M2) over MAC ──────────────────────────────────────────

        /// <summary>
        /// Creates and opens a WinBox native-M2 connection over the MAC layer (UDP port 20561). Same
        /// structured M2 CRUD as <see cref="CreateWinboxNativeConnection"/>, but works without an IP route
        /// to the router. Requires <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c>.
        /// </summary>
        /// <param name="configure">
        /// Optional hook to configure the connection before it opens — any of the mappings documented on
        /// <see cref="CreateWinboxNativeConnection"/>. The router MAC comes from <see cref="RouterMac"/>.
        /// </param>
        public ITikConnection CreateWinboxNativeMacConnection(Action<WinboxNativeMacConnection>? configure = null)
            => Create(TikConnectionType.WinboxNativeMac, Typed(configure));

        /// <summary>Async version of <see cref="CreateWinboxNativeMacConnection"/>.</summary>
        public Task<ITikConnection> CreateWinboxNativeMacConnectionAsync(
            Action<WinboxNativeMacConnection>? configure = null, CancellationToken ct = default)
            => CreateAsync(TikConnectionType.WinboxNativeMac, Typed(configure), ct);

        // ── Internals ─────────────────────────────────────────────────────────

        private static Action<ITikConnection>? Typed<TConnection>(Action<TConnection>? configure)
            where TConnection : class, ITikConnection
            => configure == null ? (Action<ITikConnection>?)null : conn => configure((TConnection)conn);

        // The per-transport routerMac argument beats the RouterMac option, and only when supplied — the
        // option is already on the connection by the time this runs (ApplyTo), so a null argument must
        // leave it alone rather than write the null back.
        private static Action<ITikConnection>? OverrideRouterMac(string? routerMac)
            => routerMac == null
                ? (Action<ITikConnection>?)null
                : conn => ((ITikMacLayerConnection)conn).RouterMac = routerMac;

        // The MAC-layer transports accept an empty host — it is the "no IP anywhere" case they exist for,
        // and ITikConnection.Open has nowhere else to say it. RequireUsableAddress has already established
        // that the connection about to be opened is one of them.
        private string HostArgument => Address.Host ?? string.Empty;

        /// <summary>
        /// Opens a connection this setup has already configured, using its address, port and credentials.
        /// </summary>
        /// <remarks>
        /// Public for the same reason <see cref="ApplyTo"/> is: a satellite transport package must open its
        /// connection exactly as the built-in ones are opened. Doing it by hand is how the SSH transport
        /// ended up honouring one option out of ten — and how it would now be the one transport that does
        /// not know what an empty host means.
        /// </remarks>
        /// <param name="connection">A connection created and configured by this setup.</param>
        public void Open(ITikConnection connection)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            OpenSync(connection);
        }

        /// <summary>Async version of <see cref="Open(ITikConnection)"/>.</summary>
        /// <param name="connection">A connection created and configured by this setup.</param>
        /// <param name="ct">Cancellation token, checked before the connection is opened.</param>
        public async Task OpenAsync(ITikConnection connection, CancellationToken ct = default)
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            await OpenCoreAsync(connection, ct).ConfigureAwait(false);
        }

        private void OpenSync(ITikConnection conn)
        {
            if (Port.HasValue)
                conn.Open(HostArgument, Port.Value, User, Password);
            else
                conn.Open(HostArgument, User, Password);
        }

        private async Task<ITikConnection> OpenCoreAsync(ITikConnection conn, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Port.HasValue)
                await conn.OpenAsync(HostArgument, Port.Value, User, Password).ConfigureAwait(false);
            else
                await conn.OpenAsync(HostArgument, User, Password).ConfigureAwait(false);
            return conn;
        }
    }
}
