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
        /// <c>CreateWinboxNativeConnection</c> in <c>tik4net.WinboxNative</c> for the case that
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

        // ── Per-transport factories live in the transport's own namespace ─────
        //
        // CreateApiConnection(), CreateTelnetConnection(), CreateWinboxNativeConnection(…) and the rest are
        // extension methods on this class, each in the namespace of the transport it creates
        // (tik4net.Api, tik4net.Telnet, …). They used to be members here, which meant this one class grew a
        // method pair per transport — 22 of them for 11 transports, all forwarding to Create(type) — and a
        // satellite package could not add its own without changing core.
        //
        // The SSH transport already worked that way (SshConnectionSetupExtensions.CreateSshConnection in
        // tik4net.Ssh, a different assembly); the built-in ones were the inconsistent half. Note that this
        // does mean a 'using tik4net.Api;' is needed to see them.

        // ── Internals ─────────────────────────────────────────────────────────

        /// <summary>
        /// Adapts a transport-typed configure callback to the untyped one <see cref="Create(TikConnectionType, Action{ITikConnection})"/>
        /// takes. Internal because the per-transport factory extensions are the only callers, and they are
        /// the only code that already knows the concrete type is right.
        /// </summary>
        internal static Action<ITikConnection>? Typed<TConnection>(Action<TConnection>? configure)
            where TConnection : class, ITikConnection
            => configure == null ? (Action<ITikConnection>?)null : conn => configure((TConnection)conn);

        // The per-transport routerMac argument beats the RouterMac option, and only when supplied — the
        // option is already on the connection by the time this runs (ApplyTo), so a null argument must
        // leave it alone rather than write the null back.
        /// <summary>
        /// Applies a per-call router MAC to a MAC-layer connection, for the factory extensions that take one.
        /// </summary>
        internal static Action<ITikConnection>? OverrideRouterMac(string? routerMac)
            => routerMac == null
                ? (Action<ITikConnection>?)null
                : conn => ((ITikMacLayerConnection)conn).RouterMac = routerMac;

        /// <summary>
        /// Runs both hooks, in order, skipping the nulls. For a factory that takes a per-call router MAC
        /// <b>and</b> a typed configure callback — the MAC is applied first, so the callback can still
        /// override it if the caller means to.
        /// </summary>
        internal static Action<ITikConnection>? Then(Action<ITikConnection>? first, Action<ITikConnection>? second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return conn => { first(conn); second(conn); };
        }

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
        /// <param name="ct">
        /// Cancels the open. Passed through to the connection, so how far it reaches is the transport's
        /// answer — see <see cref="ITikConnection.OpenAsync(string, int, string, string, CancellationToken)"/>.
        /// It used to be checked here and then dropped, which made it look like a stuck connect could be
        /// abandoned when only <see cref="ConnectTimeout"/> could end one.
        /// </param>
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
                await conn.OpenAsync(HostArgument, Port.Value, User, Password, ct).ConfigureAwait(false);
            else
                await conn.OpenAsync(HostArgument, User, Password, ct).ConfigureAwait(false);
            return conn;
        }
    }
}
