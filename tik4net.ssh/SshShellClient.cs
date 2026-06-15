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
    internal sealed class SshShellClient : IDisposable
    {
        /// <summary>Silence required after a prompt before a command's response is considered complete (ms).</summary>
        private const int SettleMs = 120;

        private SshClient _ssh;
        private ShellStream _shell;
        private readonly Encoding _encoding;
        private readonly int _receiveTimeoutMs;

        // Answers RouterOS VT100 cursor-probe negotiation (shared PTY logic). Without truthful cursor
        // replies RouterOS assumes a 1×1 terminal and emits no command output. The width is advertised
        // wide (not 80) so RouterOS does not wrap long ':put' as-value records into multiple lines.
        private readonly Vt100State _vt100 = new Vt100State(4096, 25);

        internal SshShellClient(Encoding encoding, int receiveTimeoutMs)
        {
            _encoding = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
        }

        // ── Connect ───────────────────────────────────────────────────────────

        /// <summary>
        /// Connects and authenticates over SSH, then opens the interactive PTY shell. RouterOS accepts
        /// terminal modifiers appended to the login name (<c>+c</c> = no ANSI colour) for cleaner output;
        /// if the router rejects the suffixed name we retry once with the plain user name.
        /// </summary>
        internal void Connect(string host, int port, string user, string password, int connectTimeoutMs)
        {
            try
            {
                _ssh = CreateClient(host, port, user + RouterOsCliLogin.TerminalLoginFlags, password, connectTimeoutMs);
                _ssh.Connect();
            }
            catch (SshAuthenticationException)
            {
                // Router rejected the '+c' terminal-flag suffix on the user name — retry plain.
                SafeDispose(ref _ssh);
                _ssh = CreateClient(host, port, user, password, connectTimeoutMs);
                _ssh.Connect();
            }

            // cols/rows 0 → RouterOS uses the cursor-probe negotiation (Vt100State) to size the terminal.
            // Raw terminal modes: the server-side PTY must NOT do its own line discipline, otherwise the
            // control keys we send for Safe Mode are eaten before reaching RouterOS — most importantly
            // Ctrl+D (VEOF) would be turned into channel EOF and tear the session down (SafeModeUnroll),
            // and Ctrl+C (ISIG) into SIGINT. RouterOS runs its own interactive line editor and expects
            // raw keystrokes, exactly like the Telnet byte stream. So disable echo, canonical mode,
            // signal generation, extended input and flow control, letting every byte pass through.
            _shell = _ssh.CreateShellStream("vt100", 0, 0, 4096, 25, 8192, RawTerminalModes);
        }

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

        private SshClient CreateClient(string host, int port, string user, string password, int connectTimeoutMs)
        {
            var info = new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, password ?? string.Empty))
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
            await RouterOsCliLogin.ResolveToPromptAsync(ReadUntilAsync, SendBytesAsync, ct).ConfigureAwait(false);

            // RouterOS performs a VT100 cursor-probe / redraw right after the shell opens and may emit the
            // prompt a second time; consume the residual so it does not leak into the first command's read.
            await DrainAsync(250, ct).ConfigureAwait(false);
        }

        // ── SendCommandAndReadAsync ───────────────────────────────────────────

        /// <summary>
        /// Sends a CLI command and returns the cleaned output: ANSI stripped, echo line (first line)
        /// removed, trailing prompt (last line) removed. If the command contains "print" but not
        /// "without-paging", the modifier is injected to prevent paging from blocking the read.
        /// </summary>
        internal async Task<string> SendCommandAndReadAsync(string command, CancellationToken ct)
        {
            string cmd = CliOutputHelper.InjectWithoutPaging(command);
            await SendLineAsync(cmd, ct).ConfigureAwait(false);
            string raw = await ReadCommandResponseAsync(ct).ConfigureAwait(false);
            return CliOutputHelper.CleanOutput(raw, cmd);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no line terminator, no paging injection) and
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode.
        /// </summary>
        internal async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            await SendBytesAsync(raw, ct).ConfigureAwait(false);
            return await ReadCommandResponseAsync(ct).ConfigureAwait(false);
        }

        // ── Close / Dispose ───────────────────────────────────────────────────

        internal void Close()
        {
            // Ask RouterOS to exit cleanly before tearing down, releasing the interactive session
            // on the router immediately. Errors are silently ignored (e.g. already closed).
            try { _shell?.Write("/quit\r\n"); _shell?.Flush(); } catch { /* ignore */ }
            try { _shell?.Dispose(); } catch { /* ignore */ }
            try { if (_ssh?.IsConnected == true) _ssh.Disconnect(); } catch { /* ignore */ }
            SafeDispose(ref _ssh);
            _shell = null;
        }

        public void Dispose() => Close();

        private static void SafeDispose(ref SshClient client)
        {
            try { client?.Dispose(); } catch { /* ignore */ }
            client = null;
        }

        // ── Private read/write helpers (mirror TelnetClient, minus IAC) ────────

        /// <summary>
        /// Reads from the shell until the accumulated text (ANSI-stripped) satisfies
        /// <paramref name="predicate"/>, or until the receive deadline expires.
        /// </summary>
        private async Task<string> ReadUntilAsync(Func<string, bool> predicate, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(_receiveTimeoutMs);

            // Poll DataAvailable (non-blocking) and only Read when bytes are buffered, enforcing the
            // deadline ourselves between polls — a pending blocking Read could otherwise hang if the
            // router sends nothing.
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool connectionClosed = false;
                while (_shell.DataAvailable)
                {
                    int available = _shell.Read(buffer, 0, buffer.Length);
                    if (available <= 0) { connectionClosed = true; break; }
                    accumulated.Append(await ProcessChunkAsync(buffer, available, ct).ConfigureAwait(false));
                }

                string stripped = VtStripper.StripAnsi(accumulated.ToString());
                if (predicate(stripped))
                    return stripped;

                if (connectionClosed || DateTime.UtcNow >= deadline)
                    return stripped; // timeout / closed — callers handle incomplete responses

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads a command's response. RouterOS in a PTY redraws the prompt line BEFORE printing the
        /// command output, so a naive "ends with prompt" check matches the redrawn prompt and returns
        /// empty. Instead we require the prompt to be at the end AND the stream to fall silent for
        /// <see cref="SettleMs"/> afterwards — any output following a redraw prompt resets the settle
        /// window, so only the final, stable prompt terminates the read. Bounded by the receive deadline.
        /// </summary>
        private async Task<string> ReadCommandResponseAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(_receiveTimeoutMs);
            DateTime? settleUntil = null;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool gotData = false;
                bool closed = false;
                while (_shell.DataAvailable)
                {
                    int n = _shell.Read(buffer, 0, buffer.Length);
                    if (n <= 0) { closed = true; break; }
                    accumulated.Append(await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false));
                    gotData = true;
                }

                if (gotData)
                    settleUntil = null; // fresh data → the prompt (if any) is not yet stable

                string stripped = VtStripper.StripAnsi(accumulated.ToString());

                if (RouterOsCliLogin.IsShellPrompt(stripped))
                {
                    if (settleUntil == null)
                        settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                    else if (DateTime.UtcNow >= settleUntil.Value)
                        return stripped; // prompt stable (no further output) → done
                }

                if (closed || DateTime.UtcNow >= deadline)
                    return stripped;

                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Consumes and discards residual bytes (post-login VT100 redraw, a second prompt) until the
        /// stream stays silent for <paramref name="quietMs"/>. VT100 probes are still answered.
        /// </summary>
        private async Task DrainAsync(int quietMs, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);

            while (DateTime.UtcNow < quietDeadline)
            {
                ct.ThrowIfCancellationRequested();

                if (_shell.DataAvailable)
                {
                    int n = _shell.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break; // connection closed
                    await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false);
                    quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs); // keep draining while data arrives
                }
                else
                {
                    await Task.Delay(15, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Decodes a freshly-read chunk: feeds the bytes to the VT100 state machine (answering cursor
        /// probes) and returns the decoded text to append to the caller's accumulator. No IAC handling —
        /// SSH carries no Telnet negotiation.
        /// </summary>
        private async Task<string> ProcessChunkAsync(byte[] buffer, int count, CancellationToken ct)
        {
            string text = _encoding.GetString(buffer, 0, count);

            foreach (string vtReply in _vt100.Process(text))
                await SendBytesAsync(_encoding.GetBytes(vtReply), ct).ConfigureAwait(false);

            return text;
        }

        private Task SendLineAsync(string text, CancellationToken ct)
            => SendBytesAsync(_encoding.GetBytes(text + "\r\n"), ct);

        private Task SendBytesAsync(byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _shell.Write(bytes, 0, bytes.Length);
            _shell.Flush();
            return Task.CompletedTask;
        }
    }
}
