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
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> covers the MNDP discovery and the EC-SRP5 handshake and
    /// then the wait for the RouterOS shell prompt — a stuck login fails inside it rather than inside the
    /// per-command <see cref="ITikConnection.ReceiveTimeout"/>, so a caller's connect-retry loop still gets
    /// its second attempt.
    /// </para>
    /// </remarks>
    public sealed class MacTelnetConnection : CliConnectionBase, ITikMacCliConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal MacTelnetConnection() { }

        /// <summary>Default MAC-Telnet UDP port.</summary>
        public const int DefaultPort = 20561;

        /// <inheritdoc/>
        public string? RouterMac { get; set; }

        /// <inheritdoc/>
        protected override string TransportName => "MAC-Telnet";

        /// <summary>
        /// Whether a command may be re-issued on a fresh session after RouterOS logged the idle console
        /// out. Safe Mode is the one case where it must not be: the whole point of Safe Mode is that
        /// dropping the session rolls the changes back, so silently opening a new one would hide exactly
        /// the event the caller asked to be protected by — and the new session would not hold Safe Mode
        /// either. There the caller gets <see cref="TikConnectionSessionClosedException"/> instead.
        /// </summary>
        private bool ReconnectAllowed => !SafeModeHeld;

        // ── Open (Close + driver plumbing live in CliConnectionBase) ───────────

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            // BuildTransport is inside the retry, not outside it: a refused login ends with the router
            // tearing the session down, so a retry needs a new client and a new session key — see
            // Winbox.RouterLoginRetry for why a login refusal is retried at all.
            Func<byte[], int, CancellationToken, Task<string>>? sendRawSettle = null;
            Func<string, Action<string>, CancellationToken, Task<string>>? sendStreaming = null;
            Winbox.RouterLoginRetry.Run(() =>
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
            await Winbox.RouterLoginRetry.RunAsync(async () =>
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

        // Build the MAC-Telnet client and the delegates that drive it. The port parameter is ignored —
        // MAC-Telnet always uses UDP 20561; login is by router MAC, discovered via MNDP or RouterMac.
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>,
            Func<string, Action<string>, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            // The client is held in a variable rather than captured once, because a session that RouterOS
            // has logged out cannot be revived — reconnecting means a whole new client, socket and
            // EC-SRP5 login, and every delegate below must then be talking to the new one.
            var client = new MacTelnetUdpClient(Encoding, ReceiveTimeout, ConnectTimeout, RouterMac);

            Func<CancellationToken, Task> login = ct => client.LoginAsync(host, user, password, ct);
            Action close = () => { client.TryCloseSession(); client.Dispose(); };

            Func<CancellationToken, Task> reopen = async ct =>
            {
                try { client.Dispose(); } catch { /* the old session is gone anyway */ }
                client = new MacTelnetUdpClient(Encoding, ReceiveTimeout, ConnectTimeout, RouterMac);
                await client.LoginAsync(host, user, password, ct).ConfigureAwait(false);
            };

            Func<string, CancellationToken, Task<string>> send = async (cmd, ct) =>
            {
                try
                {
                    return await client.SendCommandAndReadAsync(cmd, ct).ConfigureAwait(false);
                }
                catch (TikConnectionSessionClosedException) when (ReconnectAllowed)
                {
                    await reopen(ct).ConfigureAwait(false);
                    return await client.SendCommandAndReadAsync(cmd, ct).ConfigureAwait(false);
                }
            };

            // Streaming send (incremental monitor reads, P2.50). Same reconnect-on-logout treatment as
            // `send`, with one restriction: a retry re-runs the command from the start, so it is only safe
            // while NOTHING has been handed to the caller yet. Once a row has been emitted, re-running would
            // deliver the same sequence numbers twice — a silently wrong stream — so the close is reported
            // instead. A monitor that dies mid-stream is an error the caller must see.
            Func<string, Action<string>, CancellationToken, Task<string>> sendStreaming = async (cmd, onLine, ct) =>
            {
                bool anyLineDelivered = false;
                Action<string> track = line => { anyLineDelivered = true; onLine?.Invoke(line); };
                try
                {
                    return await client.SendCommandAndReadAsync(cmd, track, ct).ConfigureAwait(false);
                }
                catch (TikConnectionSessionClosedException) when (ReconnectAllowed && !anyLineDelivered)
                {
                    await reopen(ct).ConfigureAwait(false);
                    return await client.SendCommandAndReadAsync(cmd, track, ct).ConfigureAwait(false);
                }
            };

            // Raw sends are control keys (Safe Mode, Tab completion). They are not retried: a control key
            // is a keystroke against a specific terminal state, and replaying it on a fresh session would
            // be sending it to a console that never saw what came before.
            return (login, send,
                (raw, ct) => client.SendRawAndReadAsync(raw, ct),
                (raw, quietMs, ct) => client.SendRawAndReadUntilQuietAsync(raw, quietMs, ct),
                sendStreaming,
                close);
        }
    }
}
