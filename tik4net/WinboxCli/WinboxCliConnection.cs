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
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> bounds the TCP connect handshake, the authentication
    /// exchange, and the wait for the RouterOS shell prompt.
    /// </para>
    /// </remarks>
    public sealed class WinboxCliConnection : CliConnectionBase
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal WinboxCliConnection() { }

        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <inheritdoc/>
        protected override string TransportName => "WinBox CLI";

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            // BuildTransport is inside the retry, not outside it: a refused handshake leaves the client
            // and its channel unusable, so a retry needs new ones (see Winbox.RouterLoginRetry).
            Func<byte[], int, CancellationToken, Task<string>>? sendRawSettle = null;
            Func<string, Action<string>, CancellationToken, Task<string>>? sendStreaming = null;
            tik4net.Winbox.RouterLoginRetry.Run(() =>
            {
                var (login, send, sendRaw, settle, streaming, close) = BuildTransport(host, port, user, password);
                OpenWith(login, send, sendRaw, close);
                sendRawSettle = settle;
                sendStreaming = streaming;
            });
            // Run() either assigns both delegates above or throws, so they are always set here.
            RegisterCompletionDriver(sendRawSettle!);
            RegisterStreamingDriver(sendStreaming!);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override async Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
        {
            Func<byte[], int, CancellationToken, Task<string>>? sendRawSettle = null;
            Func<string, Action<string>, CancellationToken, Task<string>>? sendStreaming = null;
            await tik4net.Winbox.RouterLoginRetry.RunAsync(async () =>
            {
                // Inside the retry loop on purpose: a cancelled attempt must end the whole open rather than
                // be retried as if the router had refused. OpenWithAsync rethrows OperationCanceledException
                // unwrapped, and RouterLoginRetry only retries a refusal, so cancelling stops here.
                cancellationToken.ThrowIfCancellationRequested();
                var (login, send, sendRaw, settle, streaming, close) = BuildTransport(host, port, user, password);
                await OpenWithAsync(login, send, sendRaw, close, cancellationToken).ConfigureAwait(false);
                sendRawSettle = settle;
                sendStreaming = streaming;
            }).ConfigureAwait(false);
            // RunAsync() either assigns both delegates above or throws, so they are always set here.
            RegisterCompletionDriver(sendRawSettle!);
            RegisterStreamingDriver(sendStreaming!);
        }

        // Build the WinBox-CLI client (mepty terminal over the TCP M2 channel) and the delegates that drive it.
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>,
            Func<string, Action<string>, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new WinboxCliClient(new tik4net.Winbox.WinboxM2Session(), Encoding, ReceiveTimeout, ConnectTimeout, SendTimeout);
            Func<CancellationToken, Task> login = ct => client.LoginAsync(host, port, user, password, ct);
            Action close = () => { client.TryCloseSession(); client.Dispose(); };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync,
                client.SendRawAndReadUntilQuietAsync, client.SendCommandAndReadAsync, close);
        }
    }
}
