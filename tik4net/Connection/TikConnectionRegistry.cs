using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
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
        // Factories for connection types implemented in a satellite package (Ssh, by tik4net.ssh), which core
        // cannot reference directly. Filled by the package's Tik4NetSsh.Register, which Create calls itself the
        // first time the slot is asked for. ConcurrentDictionary keeps registration thread-safe without locking the
        // hot path.
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
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, 
                    "'{0}' is not a declared TikConnectionType value. A transport of your own has no enum value: "
                    + "create it directly and configure it with TikConnectionSetup.ApplyTo.",
                    connectionType), nameof(connectionType));
            // A built-in type is answered before registrations are consulted, so accepting one would be a
            // registration that silently never takes effect.
            if (BuiltIn(connectionType) != null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, 
                    "Connection type '{0}' is implemented by tik4net itself and cannot be replaced by a registered "
                    + "factory. Only a type implemented in a satellite package (Ssh, by tik4net.ssh) can be registered.",
                    connectionType), nameof(connectionType));
            _externalFactories[connectionType] = factory;
        }

        /// <summary>Creates a fresh, unopened, unconfigured connection of the given type.</summary>
        /// <remarks>
        /// <see cref="TikConnectionType.Ssh"/> needs no registration when <c>tik4net.ssh</c> is referenced: the first
        /// time the empty slot is asked for, its <c>Tik4NetSsh.Register</c> is found by name and called, once.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// The type is <see cref="TikConnectionType.Ssh"/> and the <c>tik4net.ssh</c> package cannot be loaded, or the
        /// value is not a declared <see cref="TikConnectionType"/>.
        /// </exception>
        internal static ITikConnection Create(TikConnectionType connectionType)
        {
            var builtIn = BuiltIn(connectionType);
            if (builtIn != null)
                return builtIn();
            if (_externalFactories.TryGetValue(connectionType, out var external))
                return external();
            if (connectionType == TikConnectionType.Ssh)
            {
                var loadFailure = _sshSatellite.Value;
                if (_externalFactories.TryGetValue(connectionType, out external))
                    return external();
                throw new InvalidOperationException(
                    "TikConnectionType.Ssh is implemented in the tik4net.ssh package, which could not be loaded. "
                    + "Add it (dotnet add package tik4net.ssh), then create the connection with "
                    + "setup.CreateSshConnection(), or call tik4net.Ssh.Tik4NetSsh.Register() once at startup if the "
                    + "package is loaded in a way that cannot be found by name.", loadFailure);
            }
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "Connection type '{0}' is not a declared TikConnectionType value.", connectionType));
        }

        // The satellite's entry point, by name: core cannot reference tik4net.ssh. A constant type name is visible to
        // the trimmer, so the type and its Register survive trimming whenever the package is part of the application.
        private const string SshSatelliteType = "tik4net.Ssh.Tik4NetSsh, tik4net.ssh";

        // Loaded at most once per process. The value is why the load failed (null when it worked, or when the package
        // is simply absent), kept as the refusal's inner exception.
        private static readonly Lazy<Exception?> _sshSatellite = new Lazy<Exception?>(LoadSshSatellite);

        private static Exception? LoadSshSatellite()
        {
            try
            {
                Type.GetType(SshSatelliteType, throwOnError: false)
                    ?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                    ?.Invoke(null, null);
                return null;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                return ex.InnerException;
            }
            // A package that is present but cannot be loaded (a version mismatch, a load context that refuses it)
            // ends in the same refusal as an absent one; the reason rides along as the inner exception.
            catch (Exception ex)
            {
                return ex;
            }
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
