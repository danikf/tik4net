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
        protected override string TransportName => "Telnet";

        RomonSshTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }

        TikConnectionType ITikRomonConnection.RomonAgentConnectionType => TikConnectionType.Telnet;

        TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, sendRawSettle, sendStreaming, close) = BuildTransport(host, port, user, password);
            OpenWith(login, send, sendRaw, close);
            RegisterCompletionDriver(sendRawSettle);
            RegisterStreamingDriver(sendStreaming);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override async Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
        {
            var (login, send, sendRaw, sendRawSettle, sendStreaming, close) = BuildTransport(host, port, user, password);
            await OpenWithAsync(login, send, sendRaw, close, cancellationToken).ConfigureAwait(false);
            RegisterCompletionDriver(sendRawSettle);
            RegisterStreamingDriver(sendStreaming);
        }

        // Build the Telnet client and the delegates that drive it (connect+login, send, send-raw,
        // send-raw-settle for Tab-completion, send-streaming for incremental monitor reads, close).
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>,
            Func<string, Action<string>, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new TelnetClient(Encoding, ReceiveTimeout, SendTimeout);
            var romonTarget = RomonTarget;
            Func<CancellationToken, Task> login = async ct =>
            {
                client.Connect(host, port, ConnectTimeout);
                if (romonTarget == null)
                {
                    await client.LoginAsync(user, password, ct).ConfigureAwait(false);
                    return;
                }

                // Through a RoMON agent: host/user/password are the agent's, the target comes after.
                try { await client.LoginAsync(user, password, ct).ConfigureAwait(false); }
                catch (TikConnectionLoginException ex) { throw AgentLoginFailed(host, ex); }
                string agentRomonId = await client.EnterRomonAsync(romonTarget, ct).ConfigureAwait(false);
                RomonEntered(TikConnectionType.Telnet, host, user, agentRomonId);
            };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync,
                client.SendRawAndReadUntilQuietAsync, client.SendCommandAndReadAsync, client.Close);
        }
    }
}
