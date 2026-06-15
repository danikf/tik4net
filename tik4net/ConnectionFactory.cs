using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Api;
using tik4net.MacTelnet;
using tik4net.Rest;
using tik4net.Telnet;
using tik4net.WinboxCli;
using tik4net.WinboxCliMac;
using tik4net.WinboxNative;
using tik4net.WinboxNativeMac;

namespace tik4net
{
    /// <summary>
    /// Factory to create and open mikrotik connection of given type.
    /// </summary>
    /// <remarks>Consider using <see cref="TikConnectionSetup"/> for new code.</remarks>
    public static class ConnectionFactory
    {
        // Factories for connection types implemented in satellite packages (e.g. tik4net.ssh), which
        // core cannot reference directly. Registered at startup via RegisterConnectionFactory and
        // consulted by CreateConnection before it gives up. ConcurrentDictionary keeps registration
        // thread-safe without locking the hot path.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<TikConnectionType, Func<ITikConnection>> _externalFactories
            = new System.Collections.Concurrent.ConcurrentDictionary<TikConnectionType, Func<ITikConnection>>();

        /// <summary>
        /// Registers a factory for a connection type whose implementation lives in a satellite package
        /// (one core cannot reference, e.g. <c>tik4net.ssh</c> with its <c>Renci.SshNet</c> dependency).
        /// Call once at startup before opening that connection type; thereafter
        /// <see cref="CreateConnection"/> / <see cref="OpenConnection(TikConnectionType, string, string, string)"/>
        /// can create it like any built-in type. Re-registering the same type replaces the previous factory.
        /// </summary>
        /// <param name="connectionType">The connection type the satellite package implements.</param>
        /// <param name="factory">Creates a fresh, unopened connection instance.</param>
        public static void RegisterConnectionFactory(TikConnectionType connectionType, Func<ITikConnection> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _externalFactories[connectionType] = factory;
        }

        /// <summary>
        /// Creates mikrotik Connection of given type.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <returns>Instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Open(string, string, string)"/>
        public static ITikConnection CreateConnection(TikConnectionType connectionType)
        {
            switch (connectionType)
            {
                case TikConnectionType.Api:
                    return new ApiConnection(false);
                case TikConnectionType.ApiSsl:
                    return new ApiConnection(true);
                case TikConnectionType.Rest:
                    return new RestConnection(useSsl: false);
                case TikConnectionType.RestSsl:
                    return new RestConnection(useSsl: true);
                case TikConnectionType.Telnet:
                    return new TelnetConnection();
                case TikConnectionType.MacTelnet:
                    return new MacTelnetConnection();
                case TikConnectionType.WinboxCli:
                    return new WinboxCliConnection();
                case TikConnectionType.WinboxCliMac:
                    return new WinboxCliMacConnection();
                case TikConnectionType.WinboxNative:
                    return new WinboxNativeConnection();
                case TikConnectionType.WinboxNativeMac:
                    return new WinboxNativeMacConnection();
                default:
                    if (_externalFactories.TryGetValue(connectionType, out var external))
                        return external();
                    throw new NotImplementedException(string.Format(
                        "Connection type '{0}' not supported. If it is implemented in a satellite package "
                        + "(e.g. tik4net.ssh), call ConnectionFactory.RegisterConnectionFactory(...) first.",
                        connectionType));
            }
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
        {
            ITikConnection result = CreateConnection(connectionType);
            result.Open(host, user, password);

            return result;
        }

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
        {
            ITikConnection result = CreateConnection(connectionType);
            result.Open(host, port, user, password);

            return result;
        }

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on default port and perform the logon operation.
        /// Async version.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        public static async System.Threading.Tasks.Task<ITikConnection> OpenConnectionAsync(TikConnectionType connectionType, string host, string user, string password)
        {
            ITikConnection result = CreateConnection(connectionType);
            await result.OpenAsync(host, user, password);

            return result;
        }

        /// <summary>
        /// Creates and opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// Async version.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <returns>Opened instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Close"/>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        public static async System.Threading.Tasks.Task<ITikConnection> OpenConnectionAsync(TikConnectionType connectionType, string host, int port, string user, string password)
        {
            ITikConnection result = CreateConnection(connectionType);
            await result.OpenAsync(host, port, user, password);

            return result;
        }
    }
}
