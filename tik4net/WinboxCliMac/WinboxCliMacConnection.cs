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
    /// <para>Supports CRUD only — Listen/Streaming/Async throw <see cref="NotSupportedException"/>.</para>
    /// </remarks>
    public sealed class WinboxCliMacConnection : CliConnectionBase
    {
        /// <summary>MAC-layer WinBox UDP port (informational — the transport is fixed to UDP 20561).</summary>
        public const int DefaultPort = 20561;

        /// <summary>
        /// Optional: router MAC address as <c>"AA:BB:CC:DD:EE:FF"</c> to bypass MNDP discovery
        /// (MNDP takes up to 5 s). Set before calling <see cref="Open(string, string, string)"/>.
        /// </summary>
        public string RouterMac { get; set; }

        /// <summary>
        /// Login timeout in milliseconds — the maximum time to wait for the RouterOS shell prompt after
        /// authentication (default 15 000 ms). Separate from <see cref="CliConnectionBase.ReceiveTimeout"/>.
        /// </summary>
        public int ConnectTimeout { get; set; } = 15000;

        private WinboxCliClient _client;

        // ── Open ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var client = new WinboxCliClient(new WinboxMacM2Session(RouterMac), Encoding, ReceiveTimeout, ConnectTimeout);
            try
            {
                client.LoginAsync(host, port, user, password, CancellationToken.None).GetAwaiter().GetResult();
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
            var client = new WinboxCliClient(new WinboxMacM2Session(RouterMac), Encoding, ReceiveTimeout, ConnectTimeout);
            try
            {
                await client.LoginAsync(host, port, user, password, CancellationToken.None).ConfigureAwait(false);
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
                throw new TikConnectionNotOpenException("WinBox CLI MAC connection is not open.");
            return _client.SendCommandAndReadAsync(cliText, ct);
        }
    }
}
