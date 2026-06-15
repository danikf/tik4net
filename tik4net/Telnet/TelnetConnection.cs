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

        /// <inheritdoc/>
        protected override string TransportName => "Telnet";

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, close) = BuildTransport(host, port, user, password);
            OpenWith(login, send, sendRaw, close);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, close) = BuildTransport(host, port, user, password);
            return OpenWithAsync(login, send, sendRaw, close);
        }

        // Build the Telnet client and the delegates that drive it (connect+login, send, send-raw, close).
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new TelnetClient(Encoding, ReceiveTimeout);
            Func<CancellationToken, Task> login = async ct =>
            {
                client.Connect(host, port, SendTimeout);
                await client.LoginAsync(user, password, ct).ConfigureAwait(false);
            };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync, client.Close);
        }
    }
}
