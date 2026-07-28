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
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal MacTelnetConnection() { }

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

            // Raw sends are control keys (Safe Mode, Tab completion). They are not retried: a control key
            // is a keystroke against a specific terminal state, and replaying it on a fresh session would
            // be sending it to a console that never saw what came before.
            return (login, send,
                (raw, ct) => client.SendRawAndReadAsync(raw, ct),
                (raw, quietMs, ct) => client.SendRawAndReadUntilQuietAsync(raw, quietMs, ct),
                close);
        }
    }
}
