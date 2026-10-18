using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.WinboxCliMac
{
    /// <summary>
    /// MikroTik RouterOS WinBox CLI connection over the MAC layer (UDP port 20561,
    /// <c>client_type=0x0f90</c>). Same encrypted WinBox terminal (<c>mepty</c>) CLI as
    /// <see cref="WinboxCli.WinboxCliConnection"/>, but the M2 messages travel over the MAC layer
    /// instead of TCP — so it works without an IP route to the router.
    /// </summary>
    /// <remarks>
    /// Combines the WinBox-over-MAC channel (EC-SRP5 auth + AES-128-CBC, carried in MAC-layer DATA
    /// packets) with the shared WinBox CLI engine. The router MAC address is discovered via MNDP unless
    /// <see cref="RouterMac"/> is set. Requires
    /// <c>/tool/mac-server/mac-winbox set allowed-interface-list=all</c> on the router.
    /// <para>Capability is <see cref="TikConnectionCapability.Crud"/> |
    /// <see cref="TikConnectionCapability.Listen"/> | <see cref="TikConnectionCapability.SafeMode"/> |
    /// <see cref="TikConnectionCapability.RawCommand"/> |
    /// <see cref="TikConnectionCapability.AsyncCommands"/>, inherited whole from
    /// <see cref="CliConnectionBase"/>: Listen and the callback monitors are polled, and
    /// <c>Execute*Async</c> awaits the socket rather than wrapping a blocking call. There is no
    /// <see cref="TikConnectionCapability.Streaming"/> — use the binary API for that — and no
    /// <see cref="TikConnectionCapability.CancelInFlight"/>, which is intrinsic to an unframed terminal
    /// stream rather than a backlog item.</para>
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> bounds each wait for an EC-SRP5 handshake frame and the
    /// wait for the RouterOS shell prompt.
    /// </para>
    /// <para><b>Thread safety.</b> Safe from several threads; commands queue rather than overlap, and a
    /// monitor running on this connection may miss a change made over the same one — see
    /// <see cref="CliConnectionBase"/>.</para>
    /// </remarks>
    public sealed class WinboxCliMacConnection : CliConnectionBase, ITikMacCliConnection, ITikRomonConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        // The MAC layer pages by default, and that is a correctness setting rather than a tuning one: a
        // single-command read of a large table makes RouterOS discard part of its own output while replaying
        // the backlog and hand back a short answer that looks whole (findings-mactelnet.md §9.6). Slicing the
        // read means the router never builds a backlog there is anything to drop.
        internal WinboxCliMacConnection()
            => CliReadPageSize = TikConnectionSetup.DefaultCliReadPageSize;

        /// <summary>MAC-layer WinBox UDP port (informational — the transport is fixed to UDP 20561).</summary>
        public const int DefaultPort = 20561;

        /// <inheritdoc/>
        public string? RouterMac { get; set; }

        /// <inheritdoc/>
        private protected override string TransportName => "WinBox CLI MAC";

        RomonRelayTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }

        TikConnectionType ITikRomonConnection.RomonAgentConnectionType => TikConnectionType.WinboxCliMac;

        TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

        // ── Open (Close and the terminal plumbing live in CliConnectionBase) ──

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
            => OpenTerminal(() => Attempt(host, port, user, password), retryRefusedLogin: true);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenTerminalAsync(() => Attempt(host, port, user, password), retryRefusedLogin: true, cancellationToken);

        // One open attempt: a console that rebuilds its session when the router stops acknowledging the old one. A
        // refused handshake leaves the client and its channel unusable, so each attempt builds new ones
        // (Winbox.RouterLoginRetry).
        private CliTerminalOpen Attempt(string host, int port, string user, string password)
        {
            var terminal = new ReconnectingCliTerminal(Session(host, port, user, password),
                () => Session(host, port, user, password), () => SessionRebuildAllowed, "wbxclimac.session");
            return new CliTerminalOpen(terminal, terminal.Login);
        }

        // One WinBox CLI session over the MAC-layer M2 channel, and its login; through a RoMON agent a rebuilt session
        // builds a new relay, and a new link, with it. WinboxMacM2Session's routerMac parameter isn't annotated nullable
        // (it lives in Winbox/), but null is its documented meaning: discover the router via MNDP.
        private CliTerminalOpen Session(string host, int port, string user, string password)
            => WinboxCliSession.Open(() => new WinboxMacM2Session(RouterMac!), RomonTarget, Encoding, ClientTimeouts,
                host, port, user, password,
                agentRomonId => RomonEntered(TikConnectionType.WinboxCliMac, host, user, agentRomonId, TikRomonRelay.Winbox));
    }
}
