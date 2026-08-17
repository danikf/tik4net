using System;
using System.Collections.Concurrent;
using tik4net.Api;
using tik4net.MacTelnet;
using tik4net.Rest;
using tik4net.Telnet;
using tik4net.WinboxCli;
using tik4net.WinboxCliMac;
using tik4net.WinboxNative;
using tik4net.WinboxNativeMac;

namespace tik4net.Connection
{
    /// <summary>
    /// The one place that turns a <see cref="TikConnectionType"/> into an unopened connection instance.
    /// Both public entry points — <see cref="TikConnectionSetup"/> and the compatibility
    /// <see cref="ConnectionFactory"/> — go through it, so a new transport is reachable from both the
    /// moment it is added here, and neither can drift into knowing a transport the other does not.
    /// </summary>
    internal static class TikConnectionRegistry
    {
        // Factories for connection types implemented in satellite packages (e.g. tik4net.ssh), which core
        // cannot reference directly. Registered at startup via ConnectionFactory.RegisterConnectionFactory.
        // ConcurrentDictionary keeps registration thread-safe without locking the hot path.
        private static readonly ConcurrentDictionary<TikConnectionType, Func<ITikConnection>> _externalFactories
            = new ConcurrentDictionary<TikConnectionType, Func<ITikConnection>>();

        /// <summary>Registers (or replaces) the factory for a satellite-package transport.</summary>
        internal static void Register(TikConnectionType connectionType, Func<ITikConnection> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _externalFactories[connectionType] = factory;
        }

        /// <summary>Creates a fresh, unopened, unconfigured connection of the given type.</summary>
        /// <exception cref="NotImplementedException">
        /// The type is neither built in nor registered by a satellite package.
        /// </exception>
        internal static ITikConnection Create(TikConnectionType connectionType)
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
    }
}
