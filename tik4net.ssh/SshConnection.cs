using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.Ssh
{
    /// <summary>
    /// MikroTik RouterOS SSH connection (TCP port 22). Implements CLI-based CRUD operations via
    /// <see cref="CliConnectionBase"/>, driving the RouterOS CLI over an SSH PTY shell (powered by
    /// Renci.SshNet). Lives in the satellite package <c>tik4net.ssh</c> so core stays free of the
    /// SSH.NET dependency.
    /// </summary>
    /// <remarks>
    /// Supports CRUD, polled Listen and Safe Mode (capabilities inherited from <see cref="CliConnectionBase"/>:
    /// <see cref="TikConnectionCapability.Crud"/> | <see cref="TikConnectionCapability.Listen"/> |
    /// <see cref="TikConnectionCapability.SafeMode"/>). Streaming (<c>ExecuteListWithDuration</c>) is not
    /// supported — use the binary API for that. Terminal Tab-completion (<see cref="ITikCliCompletion"/>)
    /// is supported, like on the other CLI transports. Requires the <c>ssh</c> service enabled on the router.
    /// </remarks>
    public sealed class SshConnection : CliConnectionBase
    {
        // Only constructible via TikConnectionSetup (SshConnectionSetupExtensions)/ConnectionFactory (same assembly).
        internal SshConnection() { }

        /// <summary>Default SSH port.</summary>
        public const int DefaultPort = 22;

        /// <inheritdoc/>
        protected override string TransportName => "SSH";

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var (login, send, sendRaw, sendRawSettle, sendStreaming, close) = BuildTransport(host, port, user, password);
            OpenWith(login, send, sendRaw, close);
            RegisterCompletionDriver(sendRawSettle);
            RegisterStreamingDriver(sendStreaming);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password,
            CancellationToken cancellationToken = default)
            => OpenAsync(host, DefaultPort, user, password, cancellationToken);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
        {
            var (login, send, sendRaw, sendRawSettle, sendStreaming, close) = BuildTransport(host, port, user, password);
            var opened = OpenWithAsync(login, send, sendRaw, close, cancellationToken);
            RegisterCompletionDriver(sendRawSettle);
            RegisterStreamingDriver(sendStreaming);
            return opened;
        }

        // Build the SSH PTY-shell client (Renci.SshNet) and the delegates that drive it (connect+settle,
        // send, send-raw, send-raw-settle for Tab-completion, send-streaming for incremental monitor reads,
        // close).
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>,
            Func<string, Action<string>, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new SshShellClient(Encoding, ReceiveTimeout);
            Func<CancellationToken, Task> login = async ct =>
            {
                // ConnectTimeout, not SendTimeout: getting connected is what is being bounded here, and
                // reusing the send budget for it was how this transport ignored the option entirely (D1).
                client.Connect(host, port, user, password, ConnectTimeout);
                await client.SettleAfterConnectAsync(ct).ConfigureAwait(false);
            };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync,
                client.SendRawAndReadUntilQuietAsync, client.SendCommandAndReadAsync, client.Close);
        }

        // ── Safe Mode ───────────────────────────────────────────────────────────

        /// <summary>Ctrl+D — the RouterOS safe-mode discard key in the live terminal. Byte 0x04.</summary>
        private const byte CtrlD = 0x04;

        /// <summary>
        /// The pre-7.18 fallback for <see cref="CliConnectionBase.SafeModeUnroll"/>. Over SSH the terminal
        /// discard key <c>Ctrl+D</c> (0x04) is the SSH EOF convention, and RouterOS's SSH server interprets it
        /// as end-of-input and closes the channel — requested raw PTY modes do not change this — so unlike the
        /// other CLI transports it cannot roll back in place. It does still roll back: dropping an uncommitted
        /// safe-mode session discards it exactly like a disconnect. The connection is therefore closed here
        /// rather than left in an unusable state. Take/Release (Ctrl+X) work in place over SSH and are
        /// unaffected; the scriptable path in the base class keeps the connection open on 7.18+.
        /// </summary>
        protected override void SafeModeUnrollByControlKey()
        {
            try
            {
                SendRawAndReadAsync(new[] { CtrlD }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Expected: the SSH channel closes on the EOF byte. The change is rolled back regardless.
            }
            SafeModeHeld = false;
            Close();
        }
    }
}
