using System;
using tik4net.Connection;

namespace tik4net
{
    /// <summary>
    /// Factory to create and open mikrotik connection of given type.
    /// </summary>
    /// <remarks>
    /// A compatibility shim over <see cref="TikConnectionSetup"/>'s machinery, kept so existing code keeps
    /// compiling. <b>Prefer <see cref="TikConnectionSetup"/> in new code</b>: the connections this class
    /// hands out carry their own defaults and there is nowhere to state an option — no connect timeout, no
    /// certificate policy, no router MAC — so each has to be set on the concrete connection object
    /// afterwards, if it is reachable at all. Both routes create connections through the same internal
    /// registry, so a transport is never available through one and not the other.
    /// </remarks>
    public static class ConnectionFactory
    {
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
            => TikConnectionRegistry.Register(connectionType, factory);

        /// <summary>
        /// Creates mikrotik Connection of given type.
        /// </summary>
        /// <param name="connectionType">Type of technology used to connect to mikrotik router.</param>
        /// <returns>Instance of mikrotik Connection.</returns>
        /// <seealso cref="ITikConnection.Open(string, string, string)"/>
        public static ITikConnection CreateConnection(TikConnectionType connectionType)
            => TikConnectionRegistry.Create(connectionType);

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
