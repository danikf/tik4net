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
    /// supported — use the binary API for that. Requires the <c>ssh</c> service enabled on the router.
    /// </remarks>
    public sealed class SshConnection : CliConnectionBase
    {
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

        // Build the SSH PTY-shell client (Renci.SshNet) and the delegates that drive it (connect+settle,
        // send, send-raw, close).
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            var client = new SshShellClient(Encoding, ReceiveTimeout);
            Func<CancellationToken, Task> login = async ct =>
            {
                client.Connect(host, port, user, password, SendTimeout);
                await client.SettleAfterConnectAsync(ct).ConfigureAwait(false);
            };
            return (login, client.SendCommandAndReadAsync, client.SendRawAndReadAsync, client.Close);
        }

        // ── Safe Mode ───────────────────────────────────────────────────────────

        /// <summary>Ctrl+D — the RouterOS safe-mode discard key in the live terminal. Byte 0x04.</summary>
        private const byte CtrlD = 0x04;

        /// <summary>
        /// Discards the safe-mode changes. Over SSH the terminal discard key <c>Ctrl+D</c> (0x04) is the SSH
        /// EOF convention, and RouterOS's SSH server interprets it as end-of-input and closes the channel —
        /// requested raw PTY modes do not change this — so it cannot be used for an in-place rollback. Instead
        /// we prefer the scriptable <c>/safe-mode/unroll</c> command (RouterOS 7.18+), which discards in place
        /// over the live shell with no control byte and keeps the connection open. On older RouterOS (no such
        /// command) we fall back to the Ctrl+D key, which over SSH doubles as a disconnect-rollback (dropping an
        /// uncommitted safe-mode session rolls it back, exactly like a disconnect). Take/Release (Ctrl+X) work
        /// in place over SSH and are unaffected.
        /// </summary>
        public override void SafeModeUnroll()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;

            try
            {
                // Scriptable safe-mode (RouterOS 7.18+): a normal command, so no EOF / channel close.
                string output = ExecuteCliCommand("/safe-mode/unroll");
                CliErrorParser.ThrowIfError(output, new TikGenericCommand(this, "/safe-mode/unroll"));
                SafeModeHeld = false;
                return; // rolled back in place — connection stays open
            }
            catch (TikNoSuchCommandException)
            {
                // RouterOS predates scriptable /safe-mode → fall back to the Ctrl+D key below.
            }

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
