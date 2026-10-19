using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// MikroTik RouterOS MAC-Telnet connection (UDP port 20561).
    /// Implements CLI-based CRUD operations via <see cref="CliConnectionBase"/>.
    /// </summary>
    /// <remarks>
    /// MAC-Telnet uses EC-SRP5 authentication over the MAC layer and carries a raw VT100 terminal
    /// session (unencrypted) after auth. The MAC address of the router is discovered via MNDP
    /// (MikroTik Neighbour Discovery Protocol) unless <see cref="RouterMac"/> is set explicitly.
    /// <para>
    /// Requires <c>/tool/mac-server set allowed-interface-list=all</c> on the router.
    /// Capability is <see cref="TikConnectionCapability.Crud"/> |
    /// <see cref="TikConnectionCapability.Listen"/> | <see cref="TikConnectionCapability.SafeMode"/> |
    /// <see cref="TikConnectionCapability.RawCommand"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/>, inherited whole from
    /// <see cref="CliConnectionBase"/>: Listen and the callback monitors are polled, and
    /// <c>Execute*Async</c> awaits the socket rather than wrapping a blocking call. There is no
    /// <see cref="TikConnectionCapability.Streaming"/> — use the binary API for that — and no
    /// <see cref="TikConnectionCapability.CancelInFlight"/>, which is intrinsic to an unframed terminal
    /// stream rather than a backlog item.
    /// </para>
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> covers the EC-SRP5 handshake and then the wait for the
    /// RouterOS shell prompt — a stuck login fails inside it rather than inside the per-command
    /// <see cref="ITikConnection.ReceiveTimeout"/>, so a caller's connect-retry loop still gets its second
    /// attempt.
    /// <b>MNDP discovery is not part of that budget.</b> When <see cref="RouterMac"/> is not set the MAC
    /// is resolved by <c>MndpHelper.FindMacByHost</c>, which takes its own timeout and is called without
    /// one — a fixed 5 s, whatever <see cref="ITikConnection.ConnectTimeout"/> says. Set
    /// <see cref="RouterMac"/> to skip discovery entirely when the open has to be bounded exactly.
    /// </para>
    /// <para>
    /// <see cref="OpenAsync(string, string, string, CancellationToken)"/> takes its token into every wait after
    /// the shell prompt — the relay to a RoMON target included. The EC-SRP5 login before it is synchronous and
    /// runs to the end first, so a cancel landing there takes effect as it finishes (under a second on a LAN).
    /// A cancelled open closes the session it started.
    /// </para>
    /// <para><b>Thread safety.</b> Safe from several threads; commands queue rather than overlap, and a
    /// monitor running on this connection may miss a change made over the same one — see
    /// <see cref="CliConnectionBase"/>.</para>
    /// <para><b>RoMON.</b> Can be the leg to a RoMON agent (<see cref="TikConnectionSetup.RomonAgentSetup"/>), so a
    /// router with no IP route from here is still reachable: MAC layer to the agent, RoMON beyond it. A reconnect
    /// after an idle logout relays to the target again before the command is resent.</para>
    /// </remarks>
    public sealed class MacTelnetConnection : CliConnectionBase, ITikMacCliConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        // The MAC layer pages by default, and that is a correctness setting rather than a tuning one: a
        // single-command read of a large table makes RouterOS discard part of its own output while replaying
        // the backlog and hand back a short answer that looks whole (findings-mactelnet.md §9.6). Slicing the
        // read means the router never builds a backlog there is anything to drop.
        internal MacTelnetConnection() : base(TikConnectionType.MacTelnet)
            => CliReadPageSize = TikConnectionSetup.DefaultCliReadPageSize;

        /// <summary>Default MAC-Telnet UDP port.</summary>
        public const int DefaultPort = 20561;

        /// <inheritdoc/>
        public string? RouterMac { get; set; }

        // The agent as a message names it: a MAC-layer agent may have been given by MAC alone.
        private string AgentName(string host) => !string.IsNullOrEmpty(host) ? host : RouterMac ?? "(unnamed)";

        // ── Open (Close and the terminal plumbing live in CliConnectionBase) ──

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenTerminal(() => Attempt(host, user, password), retryRefusedLogin: true);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenTerminalAsync(() => Attempt(host, user, password), retryRefusedLogin: true, cancellationToken);

        // One open attempt: a console that rebuilds its session when RouterOS logs the idle console out. A refused login
        // ends with the router tearing the session down, so each attempt needs a new client and session key (see
        // Winbox.RouterLoginRetry for why a refusal is retried at all). The port is not used: MAC-Telnet always uses UDP
        // 20561, and the router is addressed by MAC, discovered via MNDP or RouterMac.
        private CliTerminalOpen Attempt(string host, string user, string password)
        {
            var terminal = new ReconnectingCliTerminal(Session(host, user, password), () => Session(host, user, password),
                () => SessionRebuildAllowed, "mactelnet.session", Vetted);
            return new CliTerminalOpen(terminal, terminal.Login);
        }

        // One MAC-Telnet session: a new client, and its login. Through a RoMON agent host/user/password are the agent's
        // and the session continues into the target; a rebuilt session goes through here too, because it is the agent's
        // until it has been relayed again, and a command sent before that would run on the agent.
        private CliTerminalOpen Session(string host, string user, string password)
        {
            var client = new MacTelnetUdpClient(Encoding, ClientTimeouts, RouterMac);
            return new CliTerminalOpen(client, ct => LoginOrRelayAsync(host, AgentName(host),
                user, c => client.LoginAsync(host, user, password, c), client.EnterRomonAsync, ct));
        }

        // The datagram-loss check belongs here rather than in the client, because only this layer knows whether THIS
        // command was a counted read. Every print — a window or a whole table — carries the router's own count of its
        // records and is checked against it exactly, so the heuristic, which condemns complete answers now and then,
        // stands down for them. It still vets everything else. A streamed read is not vetted.
        private string Vetted(ICliTerminal session, string command, string answer)
        {
            if (!IsCountedReadInFlight)
                ((MacTelnetUdpClient)session).ThrowIfResponseLostDatagrams(command, answer);
            return answer;
        }
    }
}
