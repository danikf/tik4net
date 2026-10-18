using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.Telnet
{
    /// <summary>
    /// MikroTik RouterOS Telnet connection (TCP port 23).
    /// Implements CLI-based CRUD operations via <see cref="CliConnectionBase"/>.
    /// </summary>
    /// <remarks>
    /// Capability is <see cref="TikConnectionCapability.Crud"/> |
    /// <see cref="TikConnectionCapability.Listen"/> | <see cref="TikConnectionCapability.SafeMode"/> |
    /// <see cref="TikConnectionCapability.RawCommand"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/>, inherited whole from
    /// <see cref="CliConnectionBase"/>: Listen and the callback monitors are polled, and
    /// <c>Execute*Async</c> awaits the socket rather than wrapping a blocking call. There is no
    /// <see cref="TikConnectionCapability.Streaming"/> — use the binary API for that — and no
    /// <see cref="TikConnectionCapability.CancelInFlight"/>, which is intrinsic to an unframed terminal
    /// stream rather than a backlog item.
    /// <para><see cref="ITikConnection.ConnectTimeout"/> bounds the initial TCP handshake here.</para>
    /// <para><b>Thread safety.</b> Safe from several threads; commands queue rather than overlap, and a
    /// monitor running on this connection may miss a change made over the same one — see
    /// <see cref="CliConnectionBase"/>.</para>
    /// </remarks>
    public sealed class TelnetConnection : CliConnectionBase, ITikRomonConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal TelnetConnection() { }

        /// <summary>Default Telnet port.</summary>
        public const int DefaultPort = 23;

        /// <inheritdoc/>
        private protected override string TransportName => "Telnet";

        RomonRelayTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }

        TikConnectionType ITikRomonConnection.RomonAgentConnectionType => TikConnectionType.Telnet;

        TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

        // ── Open (Close and the terminal plumbing live in CliConnectionBase) ──

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenTerminal(() => Attempt(host, port, user, password), retryRefusedLogin: false);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenTerminalAsync(() => Attempt(host, port, user, password), retryRefusedLogin: false, cancellationToken);

        // One open attempt: a new Telnet client, and its connect plus login (through a RoMON agent, the relay too).
        private CliTerminalOpen Attempt(string host, int port, string user, string password)
        {
            var client = new TelnetClient(Encoding, ClientTimeouts);
            return new CliTerminalOpen(client, async ct =>
            {
                client.Connect(host, port);
                await LoginOrRelayAsync(TikConnectionType.Telnet, host, host, user,
                    c => client.LoginAsync(user, password, c), client.EnterRomonAsync, ct).ConfigureAwait(false);
            });
        }
    }
}
