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
    /// Supports all CRUD operations. Listen/Streaming/Async are not supported
    /// (capability: <see cref="TikConnectionCapability.Crud"/>).
    /// </remarks>
    public sealed class TelnetConnection : CliConnectionBase
    {
        /// <summary>Default Telnet port.</summary>
        public const int DefaultPort = 23;

        private TelnetClient _client;

        // ── Open ──────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var client = new TelnetClient(Encoding, ReceiveTimeout);
            client.Connect(host, port, SendTimeout);
            try
            {
                client.LoginAsync(user, password, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (TikConnectionLoginException)
            {
                client.Close();
                throw;
            }
            catch (Exception ex)
            {
                client.Close();
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
            var client = new TelnetClient(Encoding, ReceiveTimeout);
            client.Connect(host, port, SendTimeout);
            try
            {
                await client.LoginAsync(user, password, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TikConnectionLoginException)
            {
                client.Close();
                throw;
            }
            catch (Exception ex)
            {
                client.Close();
                throw new TikConnectionLoginException(ex);
            }
            _client = client;
            SetOpened();
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void Close()
        {
            _client?.Close();
            _client = null;
            SetClosed();
        }

        // ── Core execution ────────────────────────────────────────────────────

        /// <inheritdoc/>
        protected override Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
        {
            if (_client == null)
                throw new TikConnectionNotOpenException("Telnet connection is not open.");
            return _client.SendCommandAndReadAsync(cliText, ct);
        }

        /// <inheritdoc/>
        protected override Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            if (_client == null)
                throw new TikConnectionNotOpenException("Telnet connection is not open.");
            return _client.SendRawAndReadAsync(raw, ct);
        }
    }
}
