using System;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.Ssh
{
    /// <summary>
    /// MikroTik RouterOS SSH connection (TCP port 22). Implements CLI-based CRUD operations via
    /// <see cref="CliConnectionBase"/>, driving the RouterOS CLI over an SSH PTY shell (powered by
    /// Renci.SshNet). Lives in the satellite package <c>tik4net.ssh</c> so core stays free of the
    /// SSH.NET dependency.
    /// </summary>
    /// <remarks>
    /// Capability is inherited whole from <see cref="CliConnectionBase"/>:
    /// <see cref="TikConnectionCapability.Crud"/> | <see cref="TikConnectionCapability.Listen"/> |
    /// <see cref="TikConnectionCapability.SafeMode"/> | <see cref="TikConnectionCapability.RawCommand"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/> — Listen and the monitors are polled,
    /// and <c>Execute*Async</c> awaits the socket rather than wrapping a blocking call. Streaming
    /// (<c>ExecuteListWithDuration</c>) is not supported — use the binary API for that — and neither is
    /// <see cref="TikConnectionCapability.CancelInFlight"/>, which is intrinsic to an unframed terminal
    /// stream rather than a backlog item. Terminal Tab-completion (<see cref="ITikCliCompletion"/>)
    /// is supported, like on the other CLI transports. Requires the <c>ssh</c> service enabled on the router.
    /// <para><b>Thread safety.</b> Safe from several threads; commands queue rather than overlap, and a
    /// monitor running on this connection may miss a change made over the same one — see
    /// <see cref="CliConnectionBase"/>.</para>
    /// </remarks>
    public sealed class SshConnection : CliConnectionBase, ITikSshKeyConnection
    {
        // Only constructible via TikConnectionSetup (SshConnectionSetupExtensions)/ConnectionFactory (same assembly).
        internal SshConnection() : base(TikConnectionType.Ssh) { }

        /// <summary>Default SSH port.</summary>
        public const int DefaultPort = 22;

        /// <inheritdoc/>
        public string? SshPrivateKey { get; set; }

        /// <inheritdoc/>
        public string? SshPrivateKeyPassphrase { get; set; }

        // ── Open (Close and the terminal plumbing live in CliConnectionBase) ──

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var key = ReadKey();
            OpenTerminal(() => Attempt(host, port, user, password, key), retryRefusedLogin: false);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
        {
            var key = ReadKey();
            return OpenTerminalAsync(() => Attempt(host, port, user, password, key), retryRefusedLogin: false, cancellationToken);
        }

        // Read before the open starts, so a key that cannot be read is an ArgumentException out of Open itself.
        private IPrivateKeySource? ReadKey()
            => SshPrivateKey == null ? null : SshShellClient.ReadPrivateKey(SshPrivateKey, SshPrivateKeyPassphrase);

        // One open attempt: a new SSH PTY-shell client (Renci.SshNet), and its connect plus settle (through a RoMON
        // agent, the relay too).
        private CliTerminalOpen Attempt(string host, int port, string user, string password, IPrivateKeySource? key)
        {
            var client = new SshShellClient(Encoding, ClientTimeouts);
            // Through a RoMON agent the SSH login is the agent's, with its password (the setup refuses a key there).
            var loginKey = RomonTarget == null ? key : null;
            return new CliTerminalOpen(client, ct => LoginOrRelayAsync(host, host, user,
                async c =>
                {
                    // ConnectTimeout, not SendTimeout: getting connected is what is being bounded here, and
                    // reusing the send budget for it was how this transport ignored the option entirely.
                    // A refusal is a TikConnectionLoginException; a port that refuses or never answers is a
                    // SocketException and leaves as one, through a RoMON agent as on a direct connection.
                    client.Connect(host, port, user, password, loginKey);
                    await client.SettleAfterConnectAsync(c).ConfigureAwait(false);
                },
                client.EnterRomonAsync, ct));
        }
    }
}
