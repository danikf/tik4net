using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net
{
    /// <summary>
    /// Factory to create and open mikrotik connection of given type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A thin front for <see cref="TikConnectionSetup"/>: every overload that opens a connection builds a
    /// setup and asks it to do the work, so a connection from here and a connection from a setup are
    /// configured by the same code and differ only in what the caller was able to say. <b>Prefer
    /// <see cref="TikConnectionSetup"/> in new code</b> — the short overloads here take a host, a user and a
    /// password and nothing else, so a certificate policy, a router MAC or a timeout has no parameter to
    /// arrive through.
    /// </para>
    /// <para>
    /// When you have a setup already, hand it over:
    /// <see cref="OpenConnection(TikConnectionType, TikConnectionSetup)"/> takes one and applies it in full.
    /// That overload is the reason this class is not a dead end — code written against
    /// <c>ConnectionFactory</c> can start stating options without being rewritten around a different entry
    /// point.
    /// </para>
    /// </remarks>
    public static class ConnectionFactory
    {
        /// <summary>
        /// Registers a factory for a connection type whose implementation lives in a satellite package
        /// (one core cannot reference, e.g. <c>tik4net.ssh</c> with its <c>Renci.SshNet</c> dependency).
        /// Call once at startup before opening that connection type; thereafter
        /// <see cref="CreateConnection(TikConnectionType)"/> /
        /// <see cref="OpenConnection(TikConnectionType, string, string, string)"/>
        /// can create it like any built-in type. Re-registering the same type replaces the previous factory.
        /// </summary>
        /// <param name="connectionType">The connection type the satellite package implements.</param>
        /// <param name="factory">Creates a fresh, unopened connection instance.</param>
        public static void RegisterConnectionFactory(TikConnectionType connectionType, Func<ITikConnection> factory)
            => TikConnectionRegistry.Register(connectionType, factory);

        /// <summary>
        /// Creates mikrotik Connection of given type. The connection is <b>not</b> opened and carries the
        /// transport's own defaults.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <returns>Instance of mikrotik Connection.</returns>
        /// <remarks>
        /// The one overload that cannot go through a <see cref="TikConnectionSetup"/>, because a setup is
        /// built around the router address and this method is not told one. The transport defaults it leaves
        /// in place are the same values a setup would apply (15 s connect, 30 s send and receive, UTF-8,
        /// tagged sync commands), so the two routes agree today — but only
        /// <see cref="CreateConnection(TikConnectionType, TikConnectionSetup)"/> promises they will.
        /// </remarks>
        /// <seealso cref="ITikConnection.Open(string, string, string)"/>
        public static ITikConnection CreateConnection(TikConnectionType connectionType)
            => TikConnectionRegistry.Create(connectionType);

        /// <summary>
        /// Creates mikrotik Connection of given type with every option of <paramref name="setup"/> applied,
        /// and does <b>not</b> open it — for a caller who needs to touch the concrete connection type before
        /// the login happens.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="setup">Router coordinates and connection options.</param>
        /// <returns>Configured but unopened instance of mikrotik Connection.</returns>
        /// <exception cref="InvalidOperationException">
        /// The setup does not carry the coordinate this transport addresses the router by — a host for the
        /// IP transports, a host or a MAC for the MAC-layer ones.
        /// </exception>
        public static ITikConnection CreateConnection(TikConnectionType connectionType, TikConnectionSetup setup)
        {
            Guard.ArgumentNotNull(setup, nameof(setup));
            return setup.CreateUnopened(connectionType);
        }

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on default port and perform the logon operation.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <exception cref="TikConnectionLoginException">Invalid credentials.</exception>
        /// <exception cref="System.Net.Sockets.SocketException">Network connection failed.</exception>
        /// <exception cref="TikCommandTrapException">Some other Tik4Net error.</exception>
        public static ITikConnection OpenConnection(TikConnectionType connectionType, string host, string user, string password)
            => SetupFor(host, null, user, password).Create(connectionType);

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <exception cref="TikConnectionLoginException">Invalid credentials.</exception>
        /// <exception cref="System.Net.Sockets.SocketException">Network connection failed.</exception>
        /// <exception cref="TikCommandTrapException">Some other Tik4Net error.</exception>
        public static ITikConnection OpenConnection(TikConnectionType connectionType, string host, int port, string user, string password)
            => SetupFor(host, port, user, password).Create(connectionType);

        /// <summary>
        /// Creates and opens a connection of the given type with every option of <paramref name="setup"/>
        /// applied — the full <see cref="TikConnectionSetup"/> surface, reached from this class.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="setup">Router coordinates and connection options.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <remarks>
        /// Identical to <c>setup.Create(connectionType)</c>, and here so that code already written around
        /// <c>ConnectionFactory</c> can state an option without being restructured. New code can call the
        /// setup directly and skip the detour.
        /// </remarks>
        /// <exception cref="TikConnectionLoginException">Invalid credentials.</exception>
        /// <exception cref="System.Net.Sockets.SocketException">Network connection failed.</exception>
        /// <exception cref="TikCommandTrapException">Some other Tik4Net error.</exception>
        public static ITikConnection OpenConnection(TikConnectionType connectionType, TikConnectionSetup setup)
        {
            Guard.ArgumentNotNull(setup, nameof(setup));
            return setup.Create(connectionType);
        }

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on default port and perform the logon operation.
        /// Async version.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <param name="cancellationToken">Cancels the open; how far it reaches is the transport's answer.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        public static Task<ITikConnection> OpenConnectionAsync(TikConnectionType connectionType, string host, string user, string password,
            CancellationToken cancellationToken = default)
            => SetupFor(host, null, user, password).CreateAsync(connectionType, cancellationToken);

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// Async version.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <param name="cancellationToken">Cancels the open; how far it reaches is the transport's answer.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        public static Task<ITikConnection> OpenConnectionAsync(TikConnectionType connectionType, string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
            => SetupFor(host, port, user, password).CreateAsync(connectionType, cancellationToken);

        /// <summary>
        /// Creates and opens a connection of the given type with every option of <paramref name="setup"/>
        /// applied. Async version of <see cref="OpenConnection(TikConnectionType, TikConnectionSetup)"/>.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="setup">Router coordinates and connection options.</param>
        /// <param name="cancellationToken">Cancels the open; how far it reaches is the transport's answer.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        public static Task<ITikConnection> OpenConnectionAsync(TikConnectionType connectionType, TikConnectionSetup setup,
            CancellationToken cancellationToken = default)
        {
            Guard.ArgumentNotNull(setup, nameof(setup));
            return setup.CreateAsync(connectionType, cancellationToken);
        }

        // The whole content of the four short overloads: a setup carrying nothing but the coordinates.
        // Written once so they cannot drift into applying different defaults from each other — which is
        // exactly what this class did before it went through TikConnectionSetup at all, each overload
        // opening a connection by hand and inheriting whatever that transport happened to default to.
        //
        // FromHost, not the implicit string conversion: these overloads have always documented the
        // parameter as "the host (name or ip)", and the conversion guesses between host and MAC. A router
        // whose host name is bare hexadecimal has no business being read as a MAC address here.
        private static TikConnectionSetup SetupFor(string host, int? port, string user, string password)
        {
            Guard.ArgumentNotNullOrEmptyString(host, nameof(host));
            return new TikConnectionSetup(TikRouterAddress.FromHost(host), user, password) { Port = port };
        }
    }
}
