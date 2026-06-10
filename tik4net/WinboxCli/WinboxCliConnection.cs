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
    /// This is the terminal-driven ("Cli") WinBox mode. A future native-M2 mode (non-terminal CRUD) and
    /// MAC-layer variants will live alongside it as <c>WinboxNative*</c> / <c>WinboxCliMac*</c> /
    /// <c>WinboxNativeMac*</c>, reusing the shared M2 layer in <c>tik4net.Winbox</c>.
    /// </para>
    /// <para>
    /// Supports all CRUD operations. Listen/Streaming/Async are not supported
    /// (capability: <see cref="TikConnectionCapability.Crud"/>).
    /// </para>
    /// </remarks>
    public sealed class WinboxCliConnection : CliConnectionBase
    {
        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <summary>
        /// Login timeout in milliseconds — the maximum time to wait for the RouterOS shell prompt
        /// after authentication (default 15 000 ms). Kept separate from
        /// <see cref="tik4net.Connection.TikCommandConnectionBase.ReceiveTimeout"/> (which bounds per-command reads) so a stuck
        /// login fails fast enough for a caller's connect-retry loop to try again.
        /// Set before calling <see cref="Open(string, string, string)"/>.
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
            var client = new WinboxCliClient(new tik4net.Winbox.WinboxM2Session(), Encoding, ReceiveTimeout, ConnectTimeout);
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
            var client = new WinboxCliClient(new tik4net.Winbox.WinboxM2Session(), Encoding, ReceiveTimeout, ConnectTimeout);
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
                throw new TikConnectionNotOpenException("WinBox CLI connection is not open.");
            return _client.SendCommandAndReadAsync(cliText, ct);
        }
    }
}
