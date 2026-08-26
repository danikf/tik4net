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
    /// <para>
    /// <see cref="ITikConnection.ConnectTimeout"/> bounds each wait for an EC-SRP5 handshake frame and the
    /// wait for the RouterOS shell prompt.
    /// </para>
    /// </remarks>
    public sealed class WinboxCliMacConnection : CliConnectionBase, ITikMacCliConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly).
        internal WinboxCliMacConnection() { }

        /// <summary>MAC-layer WinBox UDP port (informational — the transport is fixed to UDP 20561).</summary>
        public const int DefaultPort = 20561;

        /// <inheritdoc/>
        public string? RouterMac { get; set; }

        /// <inheritdoc/>
        protected override string TransportName => "WinBox CLI MAC";

        /// <summary>
        /// Whether a command may be re-issued on a fresh session after the router stopped acknowledging the
        /// old one. Safe Mode is the one case where it must not be: dropping the session is what rolls Safe
        /// Mode's changes back, so silently opening a new one would hide exactly the event the caller asked
        /// to be protected by — and the new session would not hold Safe Mode either. There the caller gets
        /// <see cref="TikConnectionSessionClosedException"/> instead. Mirrors
        /// <c>MacTelnetConnection.ReconnectAllowed</c>, which faces the same carrier.
        /// </summary>
        private bool ReconnectAllowed => !SafeModeHeld;

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
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override async Task OpenAsync(string host, int port, string user, string password)
        {
            Func<byte[], int, CancellationToken, Task<string>>? sendRawSettle = null;
            Func<string, Action<string>, CancellationToken, Task<string>>? sendStreaming = null;
            await tik4net.Winbox.RouterLoginRetry.RunAsync(async () =>
            {
                var (login, send, sendRaw, settle, streaming, close) = BuildTransport(host, port, user, password);
                await OpenWithAsync(login, send, sendRaw, close).ConfigureAwait(false);
                sendRawSettle = settle;
                sendStreaming = streaming;
            }).ConfigureAwait(false);
            // RunAsync() either assigns both delegates above or throws, so they are always set here.
            RegisterCompletionDriver(sendRawSettle!);
            RegisterStreamingDriver(sendStreaming!);
        }

        // Build the WinBox-CLI client over the MAC-layer M2 channel and the delegates that drive it.
        private (Func<CancellationToken, Task>, Func<string, CancellationToken, Task<string>>,
            Func<byte[], CancellationToken, Task<string>>, Func<byte[], int, CancellationToken, Task<string>>,
            Func<string, Action<string>?, CancellationToken, Task<string>>, Action)
            BuildTransport(string host, int port, string user, string password)
        {
            // The client is held in a variable rather than captured once, because a session the router has
            // dropped cannot be revived — reconnecting means a whole new client, socket and EC-SRP5 login,
            // and every delegate below must then be talking to the new one.
            // WinboxMacM2Session's routerMac parameter isn't annotated nullable (it lives in Winbox/, out of
            // scope here), but null is its documented meaning: discover the router via MNDP.
            var client = new WinboxCliClient(new WinboxMacM2Session(RouterMac!), Encoding, ReceiveTimeout, ConnectTimeout);
            Func<CancellationToken, Task> login = ct => client.LoginAsync(host, port, user, password, ct);
            Action close = () => { client.TryCloseSession(); client.Dispose(); };

            // The re-login goes through RouterLoginRetry for the same reason the initial one does: RouterOS
            // refuses roughly 1 % of WinBox logins that use correct credentials (P2.41), and a recovery path
            // that inherits that failure rate is worse than no recovery path at all.
            Func<CancellationToken, Task> reopen = async ct =>
            {
                // Marked in the trace because the retry is otherwise invisible: it turns a failure into a
                // slow success, and the question P2.55 has to answer — how often does the router drop a
                // session, and always in the same places? — cannot be read off the test results any more.
                if (tik4net.Diagnostics.TikWireTrace.Enabled)
                    tik4net.Diagnostics.TikWireTrace.Emit("wbxclimac.session", tik4net.Diagnostics.TikWireDir.Note,
                        "SESSION DROPPED BY ROUTER — reopening");

                try { client.Dispose(); } catch { /* the old session is gone anyway */ }
                await RouterLoginRetry.RunAsync(async () =>
                {
                    // See the comment on the first WinboxMacM2Session construction above.
                    client = new WinboxCliClient(new WinboxMacM2Session(RouterMac!), Encoding, ReceiveTimeout, ConnectTimeout);
                    await client.LoginAsync(host, port, user, password, ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
            };

            // A retry re-runs the command from the start, so it is only safe while NOTHING has been handed to
            // the caller yet. Once a row has been emitted, re-running would deliver the same rows twice — a
            // silently wrong stream — so the close is reported instead. Same rule as MacTelnetConnection.
            Func<string, Action<string>?, CancellationToken, Task<string>> send = async (cmd, onLine, ct) =>
            {
                bool anyLineDelivered = false;
                Action<string>? track = onLine == null ? null : new Action<string>(line => { anyLineDelivered = true; onLine(line); });
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

            // Raw sends are control keys (Safe Mode, Tab completion). They are not retried: a control key is a
            // keystroke against a specific terminal state, and replaying it on a fresh session would be
            // sending it to a console that never saw what came before.
            return (login,
                (cmd, ct) => send(cmd, null, ct),
                (raw, ct) => client.SendRawAndReadAsync(raw, ct),
                (raw, quietMs, ct) => client.SendRawAndReadUntilQuietAsync(raw, quietMs, ct),
                send,
                close);
        }
    }
}
