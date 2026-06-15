using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.Telnet
{
    /// <summary>
    /// Low-level Telnet TCP client for RouterOS.
    /// Handles: IAC option negotiation, login prompt detection, command execution,
    /// ANSI stripping, echo/prompt removal, and paging prevention.
    /// </summary>
    internal sealed class TelnetClient : IDisposable
    {
        /// <summary>Silence required after a prompt before a command's response is considered complete (ms).</summary>
        private const int SettleMs = 120;

        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private readonly Encoding _encoding;
        private readonly int _receiveTimeoutMs;

        // Answers RouterOS VT100 cursor-probe negotiation (shared PTY logic). Without truthful
        // cursor replies RouterOS assumes a 1×1 terminal and emits no command output. The width is
        // advertised wide (not 80) so RouterOS does not wrap long ':put' as-value records into
        // multiple lines, which would corrupt parsing.
        private readonly Cli.Vt100State _vt100 = new Cli.Vt100State(4096, 25);

        internal TelnetClient(Encoding encoding, int receiveTimeoutMs)
        {
            _encoding = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
        }

        // ── Connect ───────────────────────────────────────────────────────────

        internal void Connect(string host, int port, int connectTimeoutMs)
        {
            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true;

            // ConnectAsync with manual timeout so we work on netstandard2.0
            var connectTask = _tcpClient.ConnectAsync(host, port);
            if (!connectTask.Wait(connectTimeoutMs))
            {
                _tcpClient.Close();
                throw new SocketException((int)SocketError.TimedOut);
            }
            // Rethrow any connect exception
            if (connectTask.IsFaulted && connectTask.Exception != null)
                throw connectTask.Exception.InnerException ?? connectTask.Exception;

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = _receiveTimeoutMs;
            _stream.WriteTimeout = _receiveTimeoutMs;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Performs the RouterOS interactive login. The RouterOS-specific prompt/nag/error semantics
        /// live in the shared <see cref="RouterOsCliLogin"/> (reused by all PTY transports); this
        /// method only supplies the Telnet byte I/O primitives.
        /// </summary>
        internal async Task LoginAsync(string user, string password, CancellationToken ct)
        {
            await RouterOsCliLogin.LoginAsync(
                user, password,
                useTerminalFlags: true,
                readUntil: ReadUntilAsync,
                sendLine: SendLineAsync,
                sendBytes: SendBytesAsync,
                ct: ct).ConfigureAwait(false);

            // RouterOS performs a VT100 cursor-probe / redraw right after login and emits the shell
            // prompt a *second* time. LoginAsync returns on the first prompt; if we don't consume the
            // residual redraw it leaks into the first command's read and is mistaken for that command's
            // completion → empty output. Drain until the stream goes quiet. (See winbox-terminal-findings.md §3.)
            await DrainAsync(250, ct).ConfigureAwait(false);
        }

        // ── SendCommandAndReadAsync ───────────────────────────────────────────

        /// <summary>
        /// Sends a CLI command to the router and returns the cleaned output:
        /// ANSI stripped, echo line (first line) removed, trailing prompt (last line) removed.
        /// If the command contains "print" but not "without-paging", the modifier is injected
        /// immediately after "print" to prevent paging from blocking the read.
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
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode
        /// toggling, where RouterOS reacts to the control key rather than a typed command.
        /// </summary>
        internal async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            await SendBytesAsync(raw, ct).ConfigureAwait(false);
            return await ReadCommandResponseAsync(ct).ConfigureAwait(false);
        }

        // ── Close / Dispose ───────────────────────────────────────────────────

        internal void Close()
        {
            // Ask RouterOS to exit cleanly before closing the socket. This releases the
            // interactive session on the router side immediately rather than waiting for
            // the TCP timeout to expire. Errors are silently ignored (e.g. already closed).
            try { _stream?.Write(_encoding.GetBytes("/quit\r\n"), 0, _encoding.GetByteCount("/quit\r\n")); } catch { /* ignore */ }
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _tcpClient?.Close(); } catch { /* ignore */ }
        }

        public void Dispose() => Close();

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Reads from the stream until the accumulated text (ANSI-stripped) satisfies
        /// <paramref name="predicate"/>, or until the stream read timeout expires.
        /// </summary>
        private async Task<string> ReadUntilAsync(Func<string, bool> predicate, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();

            // Deadline based on receive timeout
            var deadline = DateTime.UtcNow.AddMilliseconds(_receiveTimeoutMs);

            // IMPORTANT: On .NET Framework, NetworkStream.ReadAsync honours neither ReadTimeout
            // nor CancellationToken cancellation once the read is pending with no data — it would
            // block forever if the router sends nothing. We therefore poll DataAvailable (which is
            // non-blocking) and only ReadAsync when bytes are buffered, enforcing the deadline
            // ourselves between polls.
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool connectionClosed = false;

                // Drain all currently-available data without blocking on an empty socket.
                while (_stream.DataAvailable)
                {
                    int available = await _stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (available <= 0)
                    {
                        connectionClosed = true; // remote closed the connection
                        break;
                    }

                    accumulated.Append(await ProcessChunkAsync(buffer, available, ct).ConfigureAwait(false));
                }

                // Check predicate on ANSI-stripped accumulated text
                string stripped = VtStripper.StripAnsi(accumulated.ToString());
                if (predicate(stripped))
                    return stripped;

                if (connectionClosed || DateTime.UtcNow >= deadline)
                    return stripped; // timeout / closed — callers handle incomplete responses

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads a command's response. RouterOS in a PTY redraws the prompt line (e.g. <c>\r\r\r\r] &gt; </c>)
        /// BEFORE printing the command output, so a naive "ends with prompt" check matches the redrawn
        /// prompt and returns empty. Instead we require the prompt to be at the end AND the stream to fall
        /// silent for <see cref="SettleMs"/> afterwards — any output that follows a redraw prompt resets the
        /// settle window, so only the final, stable prompt terminates the read. Bounded by the receive deadline.
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
                while (_stream.DataAvailable)
                {
                    int n = await _stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
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
                        return stripped; // prompt has been stable (no further output) → done
                }

                if (closed || DateTime.UtcNow >= deadline)
                    return stripped;

                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Consumes and discards any residual bytes on the stream (post-login VT100 redraw, a second
        /// prompt, stray push frames) until the stream stays silent for <paramref name="quietMs"/>.
        /// IAC negotiation is still answered so the link stays in a sane state.
        /// </summary>
        private async Task DrainAsync(int quietMs, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);

            while (DateTime.UtcNow < quietDeadline)
            {
                ct.ThrowIfCancellationRequested();

                if (_stream.DataAvailable)
                {
                    int n = await _stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (n <= 0)
                        break; // connection closed

                    // Discard the text but still answer IAC + VT100 probes so the link stays sane.
                    await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false);

                    // Reset the quiet window — keep draining as long as data keeps arriving.
                    quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
                }
                else
                {
                    await Task.Delay(15, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Decodes a freshly-read chunk: strips Telnet IAC negotiation (answering it), feeds the
        /// remaining bytes to the VT100 state machine (answering cursor probes), and returns the
        /// decoded text to append to the caller's accumulator.
        /// </summary>
        private async Task<string> ProcessChunkAsync(byte[] buffer, int count, CancellationToken ct)
        {
            byte[] chunk = new byte[count];
            Array.Copy(buffer, 0, chunk, 0, count);

            byte[] cleanData = TelnetNegotiator.FilterAndRespond(chunk, out byte[] iacReply);
            if (iacReply.Length > 0)
                await SendBytesAsync(iacReply, ct).ConfigureAwait(false);

            string text = _encoding.GetString(cleanData);

            foreach (string vtReply in _vt100.Process(text))
                await SendBytesAsync(_encoding.GetBytes(vtReply), ct).ConfigureAwait(false);

            return text;
        }

        private async Task SendLineAsync(string text, CancellationToken ct)
        {
            byte[] bytes = _encoding.GetBytes(text + "\r\n");
            await SendBytesAsync(bytes, ct).ConfigureAwait(false);
        }

        private Task SendBytesAsync(byte[] bytes, CancellationToken ct)
            => _stream.WriteAsync(bytes, 0, bytes.Length, ct);

    }
}
