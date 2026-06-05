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
        /// <see cref="CliConnectionBase.ReceiveTimeout"/> (which bounds per-command reads): a stuck
        /// login should fail fast enough that a caller's connect-retry loop can make a second attempt.
        /// Set before calling <see cref="Open(string, string, string)"/>.
        /// </summary>
        public int ConnectTimeout { get; set; } = 15000;

        private MacTelnetUdpClient _client;

        // ── Open ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            // port parameter kept for interface compatibility; MAC-Telnet always uses UDP 20561
            var client = new MacTelnetUdpClient(Encoding, ReceiveTimeout, ConnectTimeout, RouterMac);
            try
            {
                client.LoginAsync(host, user, password, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (TikConnectionLoginException)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new TikConnectionLoginException(ex);
            }
            _client = client;
            SetOpened();
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override async Task OpenAsync(string host, int port, string user, string password)
        {
            var client = new MacTelnetUdpClient(Encoding, ReceiveTimeout, ConnectTimeout, RouterMac);
            try
            {
                await client.LoginAsync(host, user, password, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TikConnectionLoginException)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new TikConnectionLoginException(ex);
            }
            _client = client;
            SetOpened();
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Close()
        {
            _client?.TryCloseSession();
            _client?.Dispose();
            _client = null;
            SetClosed();
        }

        // ── Core execution ────────────────────────────────────────────────────

        /// <inheritdoc/>
        protected override Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
        {
            if (_client == null)
                throw new TikConnectionNotOpenException("MAC-Telnet connection is not open.");
            return _client.SendCommandAndReadAsync(cliText, ct);
        }
    }
}
