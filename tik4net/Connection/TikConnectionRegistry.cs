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
        /// <exception cref="ArgumentException">
        /// The type is implemented by tik4net itself, or is not a declared <see cref="TikConnectionType"/> value.
        /// </exception>
        internal static void Register(TikConnectionType connectionType, Func<ITikConnection> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!Enum.IsDefined(typeof(TikConnectionType), connectionType))
                throw new ArgumentException(string.Format(
                    "'{0}' is not a declared TikConnectionType value. A transport of your own has no enum value: "
                    + "create it directly and configure it with TikConnectionSetup.ApplyTo.",
                    connectionType), nameof(connectionType));
            // A built-in type is answered before registrations are consulted, so accepting one would be a
            // registration that silently never takes effect.
            if (BuiltIn(connectionType) != null)
                throw new ArgumentException(string.Format(
                    "Connection type '{0}' is implemented by tik4net itself and cannot be replaced by a registered "
                    + "factory. Only a type implemented in a satellite package (Ssh, by tik4net.ssh) can be registered.",
                    connectionType), nameof(connectionType));
            _externalFactories[connectionType] = factory;
        }

        /// <summary>Creates a fresh, unopened, unconfigured connection of the given type.</summary>
        /// <exception cref="NotImplementedException">
        /// The type is neither built in nor registered by a satellite package.
        /// </exception>
        internal static ITikConnection Create(TikConnectionType connectionType)
        {
            var builtIn = BuiltIn(connectionType);
            if (builtIn != null)
                return builtIn();
            if (_externalFactories.TryGetValue(connectionType, out var external))
                return external();
            throw new NotImplementedException(string.Format(
                "Connection type '{0}' not supported. If it is implemented in a satellite package "
                + "(e.g. tik4net.ssh), call ConnectionFactory.RegisterConnectionFactory(...) first.",
                connectionType));
        }

        // The one list of what core implements — Create and Register both read it, so the refusal to
        // register a built-in type cannot drift from what Create actually answers.
        private static Func<ITikConnection>? BuiltIn(TikConnectionType connectionType)
        {
            switch (connectionType)
            {
                case TikConnectionType.Api: return () => new ApiConnection(false);
                case TikConnectionType.ApiSsl: return () => new ApiConnection(true);
                case TikConnectionType.Rest: return () => new RestConnection(useSsl: false);
                case TikConnectionType.RestSsl: return () => new RestConnection(useSsl: true);
                case TikConnectionType.Telnet: return () => new TelnetConnection();
                case TikConnectionType.MacTelnet: return () => new MacTelnetConnection();
                case TikConnectionType.WinboxCli: return () => new WinboxCliConnection();
                case TikConnectionType.WinboxCliMac: return () => new WinboxCliMacConnection();
                case TikConnectionType.WinboxNative: return () => new WinboxNativeConnection();
                case TikConnectionType.WinboxNativeMac: return () => new WinboxNativeMacConnection();
                default: return null;
            }
        }
    }
}
