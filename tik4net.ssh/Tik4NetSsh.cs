using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Ssh
{
    /// <summary>
    /// Entry points for the SSH transport. Because the SSH implementation lives in a satellite package
    /// (core cannot reference its <c>Renci.SshNet</c> dependency), an SSH connection is created either
    /// directly (<c>new SshConnection()</c>), via the <see cref="TikConnectionSetup"/> extension methods
    /// (<see cref="Tik4NetSshExtensions.CreateSshConnection"/>), or — after calling <see cref="Register"/>
    /// once — through the standard <see cref="ConnectionFactory"/> using <see cref="TikConnectionType.Ssh"/>.
    /// </summary>
    public static class Tik4NetSsh
    {
        /// <summary>
        /// Registers the SSH transport with <see cref="ConnectionFactory"/> so that
        /// <c>ConnectionFactory.CreateConnection(TikConnectionType.Ssh)</c> /
        /// <c>ConnectionFactory.OpenConnection(TikConnectionType.Ssh, …)</c> work like any built-in type.
        /// Idempotent — safe to call more than once. Call once at application startup.
        /// </summary>
        public static void Register()
            => ConnectionFactory.RegisterConnectionFactory(TikConnectionType.Ssh, () => new SshConnection());
    }

    /// <summary>
    /// <see cref="TikConnectionSetup"/> extension methods for the SSH transport, kept in the satellite
    /// package alongside the implementation. Mirror the built-in <c>CreateTelnetConnection</c> helpers.
    /// </summary>
    public static class Tik4NetSshExtensions
    {
        /// <summary>
        /// Creates and opens an SSH CLI connection (PTY shell, default port 22). Requires the RouterOS
        /// <c>ssh</c> service to be enabled.
        /// </summary>
        public static ITikConnection CreateSshConnection(this TikConnectionSetup setup)
        {
            var conn = new SshConnection();
            if (setup.Port.HasValue)
                conn.Open(setup.Host, setup.Port.Value, setup.User, setup.Password);
            else
                conn.Open(setup.Host, setup.User, setup.Password);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateSshConnection"/>.</summary>
        public static async Task<ITikConnection> CreateSshConnectionAsync(
            this TikConnectionSetup setup, CancellationToken ct = default)
        {
            var conn = new SshConnection();
            ct.ThrowIfCancellationRequested();
            if (setup.Port.HasValue)
                await conn.OpenAsync(setup.Host, setup.Port.Value, setup.User, setup.Password).ConfigureAwait(false);
            else
                await conn.OpenAsync(setup.Host, setup.User, setup.Password).ConfigureAwait(false);
            return conn;
        }
    }
}
