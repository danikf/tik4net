using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Ssh
{
    /// <summary>
    /// Entry points for the SSH transport. <see cref="SshConnection"/>'s constructor is internal — like
    /// every other tik4net connection type, it is created only via the <see cref="TikConnectionSetup"/>
    /// extension methods (<see cref="Tik4NetSshExtensions.CreateSshConnection"/>) or — after calling
    /// <see cref="Register"/> once — through the standard <see cref="ConnectionFactory"/> using
    /// <see cref="TikConnectionType.Ssh"/>.
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
            var conn = NewSshConnection(setup);
            setup.Open(conn);
            return conn;
        }

        /// <summary>Async version of <see cref="CreateSshConnection"/>.</summary>
        public static async Task<ITikConnection> CreateSshConnectionAsync(
            this TikConnectionSetup setup, CancellationToken ct = default)
        {
            var conn = NewSshConnection(setup);
            await setup.OpenAsync(conn, ct).ConfigureAwait(false);
            return conn;
        }

        // Every option comes from the setup's own ApplyTo, and the open itself from its own Open, rather
        // than being reproduced property by property here: this transport lives in another assembly and was
        // the proof that hand-copying rots — it carried CancellationMode across and silently dropped the
        // timeouts and the encoding.
        private static SshConnection NewSshConnection(TikConnectionSetup setup)
        {
            if (setup == null) throw new System.ArgumentNullException(nameof(setup));
            var conn = new SshConnection();
            setup.ApplyTo(conn);
            return conn;
        }
    }
}
