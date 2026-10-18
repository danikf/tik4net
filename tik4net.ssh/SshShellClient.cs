using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Renci.SshNet;
using Renci.SshNet.Common;
using tik4net.Cli;

namespace tik4net.Ssh
{
    /// <summary>
    /// Low-level SSH PTY client for RouterOS. SSH.NET performs the transport and authentication, then we
    /// open an interactive shell (<see cref="ShellStream"/>) and drive the RouterOS CLI over it exactly
    /// like the Telnet transport: VT100 cursor-probe negotiation, paging prevention, command execution,
    /// ANSI stripping and echo/prompt removal. Unlike Telnet there is no IAC negotiation (SSH has none)
    /// and no <c>Login:</c>/<c>Password:</c> exchange (SSH.NET authenticates before the shell starts).
    /// </summary>
    internal sealed class SshShellClient : ICliTerminal, ICliByteStream
    {
        private SshClient _ssh = null!; // assigned in Connect, not the constructor; read only afterwards
        private ShellStream _shell = null!; // assigned in Connect, not the constructor; read only afterwards

        // Every read and write after the shell opens: the console over this channel. One Enter is a bare CR: over a
        // PTY a CR LF is two Enters (Telnet's NVT reads it as one).
        private readonly CliPtySession _pty;

        internal SshShellClient(Encoding encoding, CliClientTimeouts timeouts)
        {
            _connectTimeoutMs = timeouts.ConnectMs;
            _pty = new CliPtySession(this, encoding ?? Encoding.UTF8, timeouts.ReceiveMs, "SSH", "ssh.pty", "\r");
        }

        private readonly int _connectTimeoutMs;

        // ── Connect ───────────────────────────────────────────────────────────

        /// <summary>
        /// Connects and authenticates over SSH, then opens the interactive PTY shell. RouterOS accepts
        /// terminal modifiers appended to the login name (<c>+c</c> = no ANSI colour) for cleaner output;
        /// if the router rejects the suffixed name we retry once with the plain user name.
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">Nothing answered: the port refused the connection,
        /// the host was unreachable, or the connection was not established within the timeout.</exception>
        /// <exception cref="TikConnectionLoginException">The router answered and refused the credentials (the
        /// <see cref="SshAuthenticationException"/> inside).</exception>
        internal void Connect(string host, int port, string user, string password, IPrivateKeySource? key = null)
        {
            try
            {
                _ssh = CreateClient(host, port, user + RouterOsCliLogin.TerminalLoginFlags, password, _connectTimeoutMs, key);
                ConnectOrTimeOut(_ssh);
            }
            catch (SshAuthenticationException)
            {
                // Router rejected the '+c' terminal-flag suffix on the user name — retry plain.
                SafeDispose(_ssh);
                _ssh = CreateClient(host, port, user, password, _connectTimeoutMs, key);
                try { ConnectOrTimeOut(_ssh); }
                catch (SshAuthenticationException ex) { throw new TikConnectionLoginException(ex); }
            }

            // cols/rows 0 → RouterOS uses the cursor-probe negotiation (Vt100State) to size the terminal.
            // Raw terminal modes: the server-side PTY must NOT do its own line discipline, otherwise the
            // control keys we send for Safe Mode are eaten before reaching RouterOS — most importantly
            // Ctrl+D (VEOF) would be turned into channel EOF and tear the session down (SafeModeUnroll),
            // and Ctrl+C (ISIG) into SIGINT. RouterOS runs its own interactive line editor and expects
            // raw keystrokes, exactly like the Telnet byte stream. So disable echo, canonical mode,
            // signal generation, extended input and flow control, letting every byte pass through.
            _shell = _ssh.CreateShellStream("vt100", 0, 0, 4096, 25, 8192, RawTerminalModes);
            _shell.Closed += (_, __) => _shellClosed = true;
        }

        // Set when the server closes the channel. A closed ShellStream has no data available and no read to
        // return 0 from, so without this a read would wait the whole receive deadline out on a session that is gone.
        private volatile bool _shellClosed;

        // DataAvailable of a stream whose channel has closed can throw rather than answer false.
        private bool ShellHasData()
        {
            try { return _shell.DataAvailable; }
            catch (ObjectDisposedException) { _shellClosed = true; return false; }
        }

        // The channel has closed and nothing it delivered is left to read.
        private bool ShellClosedAndDrained => _shellClosed && !ShellHasData();

        // PTY modes that make the server pass bytes through untouched (see Connect). Value 0 = off.
        private static readonly IDictionary<TerminalModes, uint> RawTerminalModes = new Dictionary<TerminalModes, uint>
        {
            { TerminalModes.ECHO,   0 }, // no PTY echo (RouterOS echoes at the application level)
            { TerminalModes.ICANON, 0 }, // no line buffering / special EOF/EOL handling
            { TerminalModes.ISIG,   0 }, // Ctrl+C/Ctrl+\ delivered as bytes, not signals
            { TerminalModes.IEXTEN, 0 }, // no extended input processing
            { TerminalModes.IXON,   0 }, // no XON/XOFF output flow control
            { TerminalModes.IXOFF,  0 }, // no XON/XOFF input flow control
            { TerminalModes.ICRNL,  0 }, // do not translate CR→NL on input
            { TerminalModes.INLCR,  0 }, // do not translate NL→CR on input
            { TerminalModes.OPOST,  0 }, // no output post-processing
        };

        // SSH.NET reports a refused port as the SocketException itself, but a connection that is not
        // established in time as its own SshOperationTimeoutException. That one is turned into the
        // SocketException the binary API and Telnet throw for the same case, so a timeout is a network failure
        // on every transport rather than — wrapped by the connection's open — a login failure on this one.
        private static void ConnectOrTimeOut(SshClient client)
        {
            try
            {
                client.Connect();
            }
            catch (SshOperationTimeoutException)
            {
                SafeDispose(client);
                throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.TimedOut);
            }
        }

        /// <summary>
        /// Reads <see cref="ITikSshKeyConnection.SshPrivateKey"/> once, before connecting, so a key that cannot be read
        /// (a wrong passphrase, an unknown format) is reported as that rather than as a refused login.
        /// </summary>
        internal static IPrivateKeySource ReadPrivateKey(string keyText, string? passphrase)
        {
            try
            {
                using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(keyText)))
                    return new PrivateKeyFile(stream, passphrase);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("SshPrivateKey could not be read: " + ex.Message
                    + " It must be the text of an OpenSSH or PEM private key file, with SshPrivateKeyPassphrase when it is encrypted.",
                    "SshPrivateKey", ex);
            }
        }

        private SshClient CreateClient(string host, int port, string user, string password, int connectTimeoutMs,
            IPrivateKeySource? key)
        {
            // With a key: the key first, and the password second only when one was given — an empty password would be
            // one more refused attempt in the router's log.
            var methods = new List<AuthenticationMethod>();
            if (key != null)
                methods.Add(new PrivateKeyAuthenticationMethod(user, key));
            if (key == null || !string.IsNullOrEmpty(password))
                methods.Add(new PasswordAuthenticationMethod(user, password ?? string.Empty));
            var info = new ConnectionInfo(host, port, user, methods.ToArray())
            {
                Timeout = TimeSpan.FromMilliseconds(connectTimeoutMs),
            };
            return new SshClient(info);
        }

        /// <summary>
        /// Settles the freshly-opened shell to a usable prompt: dismisses the RouterOS "change your
        /// password" nag with Ctrl-C and drains the post-login VT100 redraw. The shared
        /// <see cref="RouterOsCliLogin.ResolveToPromptAsync"/> owns the RouterOS-specific semantics;
        /// SSH skips the Login:/Password: exchange because SSH.NET already authenticated.
        /// </summary>
        internal async Task SettleAfterConnectAsync(CancellationToken ct)
        {
            await RouterOsCliLogin.ResolveToPromptAsync(_pty.ReadUntilAsync, _pty.SendBytesAsync, ct).ConfigureAwait(false);

            // RouterOS performs a VT100 cursor-probe / redraw right after the shell opens and may emit the
            // prompt a second time; consume the residual so it does not leak into the first command's read.
            await _pty.DrainAsync(RouterOsCliLogin.PostLoginDrainMs, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// From the agent's shell, continues this terminal into a RoMON target through <c>/tool romon ssh</c>
        /// (<see cref="CliPtySession.EnterRomonAsync"/>). Every command after it runs on the target.
        /// </summary>
        /// <returns>The agent's own RoMON id.</returns>
        internal Task<string> EnterRomonAsync(RomonRelayTarget target, CancellationToken ct)
            => _pty.EnterRomonAsync(target, ct);

        // ── Close / Dispose ───────────────────────────────────────────────────

        /// <summary>Asks RouterOS to leave the console (<c>/quit</c>), then tears the shell down.</summary>
        /// <param name="answerSafeModeQuestion">The session holds Safe Mode. RouterOS then answers <c>/quit</c> with
        /// "You are in Safe Mode. Quitting will unroll changes. Quit? [y/N]" (7.24.4) instead of quitting, so <c>y</c> is
        /// typed after it: the unroll a session ending without a release is owed anyway, done now. Left unanswered, the
        /// console waits on a terminal that is gone - over WinBox CLI that wedged RouterOS 6.49.13's console until a
        /// reboot.</param>
        internal void Close(bool answerSafeModeQuestion = false)
        {
            // Ask RouterOS to exit cleanly before tearing down, releasing the interactive session
            // on the router immediately. Errors are silently ignored (e.g. already closed). The "\r\n" is two Enters
            // over the PTY, unlike SendLineAsync's one: in Safe Mode the second answers "Quit? [y/N]" with the default
            // N, so the session is not torn down inside that question, which wedged RouterOS 6.49.13's console (X20).
            try { _shell?.Write(RouterOsCliLogin.QuitKeys(answerSafeModeQuestion, "\r\n")); _shell?.Flush(); } catch { /* ignore */ }
            try { _shell?.Dispose(); } catch { /* ignore */ }
            try { if (_ssh?.IsConnected == true) _ssh.Disconnect(); } catch { /* ignore */ }
            SafeDispose(_ssh);
            _ssh = null!; // cleared after Close so a disposed client is never reused
            _shell = null!; // cleared after Close so a disposed shell is never reused
        }

        public void Dispose() => Close();

        // ── ICliTerminal ──────────────────────────────────────────────────────

        public Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
            => _pty.SendCommandAndReadAsync(command, onLine, ct);

        public Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
            => _pty.SendRawAndReadAsync(raw, ct);

        public Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
            => _pty.SendRawAndReadUntilQuietAsync(raw, quietMs, ct);

        public bool SupportsQuietRead => true;

        public bool SupportsLineStreaming => true;

        public void End(bool answerSafeModeQuestion) => Close(answerSafeModeQuestion);

        private static void SafeDispose(SshClient? client)
        {
            try { client?.Dispose(); } catch { /* ignore */ }
        }

        // ── ICliByteStream ────────────────────────────────────────────────────

        bool ICliByteStream.HasData => ShellHasData();

        // SSH.NET's Read returns what is buffered; it is only called while data is available, so it does not block.
        Task<int> ICliByteStream.ReadAsync(byte[] buffer, CancellationToken ct)
            => Task.FromResult(_shell.Read(buffer, 0, buffer.Length));

        bool ICliByteStream.ClosedAndDrained => ShellClosedAndDrained;

        Task ICliByteStream.WriteAsync(byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _shell.Write(bytes, 0, bytes.Length);
            _shell.Flush();
            return Task.CompletedTask;
        }
    }
}
