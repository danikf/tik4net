using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.WinboxCli
{
    /// <summary>
    /// MikroTik RouterOS WinBox CLI connection (TCP port 8291).
    /// Drives the RouterOS CLI over the encrypted WinBox channel via the <c>mepty</c> terminal handler,
    /// implementing CRUD through <see cref="CliConnectionBase"/>.
    /// </summary>
    /// <remarks>
    /// WinBox uses EC-SRP5 authentication (with a legacy MD5 fallback for pre-6.43 RouterOS) and an
    /// AES-128-CBC encrypted channel. After auth the connection opens the <c>mepty</c> terminal handler
    /// and runs an interactive RouterOS CLI session — so all CRUD goes through <c>print as-value</c>,
    /// exactly like the Telnet and MAC-Telnet transports.
    /// <para>
    /// This is the terminal-driven ("Cli") WinBox mode. Three siblings share the M2 layer in
    /// <c>tik4net.Winbox</c> with it: <see cref="WinboxCliMac.WinboxCliMacConnection"/> carries the same
    /// terminal over the MAC layer, and <see cref="WinboxNative.WinboxNativeConnection"/> /
    /// <see cref="WinboxNativeMac.WinboxNativeMacConnection"/> do structured non-terminal CRUD over M2
    /// instead of driving the CLI.
    /// </para>
    /// <para>
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
    /// <see cref="ITikConnection.ConnectTimeout"/> bounds the TCP connect handshake, the authentication
    /// exchange, and the wait for the RouterOS shell prompt.
    /// </para>
    /// <para><b>Thread safety.</b> Safe from several threads; commands queue rather than overlap, and a
    /// monitor running on this connection may miss a change made over the same one — see
    /// <see cref="CliConnectionBase"/>.</para>
    /// </remarks>
    public sealed class WinboxCliConnection : CliConnectionBase
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal WinboxCliConnection() : base(TikConnectionType.WinboxCli) { }

        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

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

        // One open attempt: the WinBox-CLI client (mepty terminal over the TCP M2 channel) and its login. A refused
        // handshake leaves the client and its channel unusable, so each attempt builds new ones (Winbox.RouterLoginRetry).
        private CliTerminalOpen Attempt(string host, int port, string user, string password)
            => WinboxCliSession.Open(() => new tik4net.Winbox.WinboxM2Session(), RomonTarget, Encoding, ClientTimeouts,
                host, port, user, password,
                agentRomonId => RomonEntered(host, user, agentRomonId, TikRomonRelay.Winbox));
    }
}
