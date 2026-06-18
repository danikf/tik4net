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
    /// Supports all CRUD operations. Listen/Streaming/Async are not supported
    /// (capability: <see cref="TikConnectionCapability.Crud"/>).
    /// </para>
    /// </remarks>
    public sealed class MacTelnetConnection : CliConnectionBase
    {
        /// <summary>Default MAC-Telnet UDP port.</summary>
        public const int DefaultPort = 20561;

        /// <summary>
        /// Optional: router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> to bypass MNDP discovery.
        /// MNDP discovery takes up to 5 seconds — set this property before calling
        /// <see cref="Open(string, string, string)"/> to avoid that delay.
        /// </summary>
        public string RouterMac { get; set; }

        /// <summary>
        /// Login timeout in milliseconds — the maximum time to wait for the RouterOS shell prompt
        /// after authentication (default 15 000 ms). This is intentionally separate from
        /// <see cref="tik4net.Connection.TikCommandConnectionBase.ReceiveTimeout"/> (which bounds per-command reads): a stuck
        /// login should fail fast enough that a caller's connect-retry loop can make a second attempt.
        /// Set before calling <see cref="Open(string, string, string)"/>.
        /// </summary>
        public int ConnectTimeout { get; set; } = 15000;

        /// <inheritdoc/>
        protected override string TransportName => "MAC-Telnet";

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, sendRawSettle, close) = BuildTransport(host, port, user, password);
            OpenWith(login, send, sendRaw, close);
            RegisterCompletionDriver(sendRawSettle);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override async Task OpenAsync(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, sendRawSettle, close) = BuildTransport(host, port, user, password);
            await OpenWithAsync(login, send, sendRaw, close).ConfigureAwait(false);
            RegisterCompletionDriver(sendRawSettle);
        }

        // Build the MAC-Telnet client and the delegates that drive it. The port parameter is ignored —
        // MAC-Telnet always uses UDP 20561; login is by router MAC, discovered via MNDP or RouterMac.
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new MacTelnetUdpClient(Encoding, ReceiveTimeout, ConnectTimeout, RouterMac);
            Func<CancellationToken, Task> login = ct => client.LoginAsync(host, user, password, ct);
            Action close = () => { client.TryCloseSession(); client.Dispose(); };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync,
                client.SendRawAndReadUntilQuietAsync, close);
        }
    }
}
