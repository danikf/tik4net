using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Connection;

namespace tik4net.Telnet
{
    /// <summary>
    /// Low-level Telnet TCP client for RouterOS.
    /// Handles: IAC option negotiation, login prompt detection, command execution,
    /// ANSI stripping, echo/prompt removal, and paging prevention.
    /// </summary>
    internal sealed class TelnetClient : ICliTerminal, ICliByteStream
    {
        private TcpClient _tcpClient = null!; // assigned by Connect(), called right after construction
        private NetworkStream _stream = null!; // assigned by Connect(), called right after construction
        private readonly Encoding _encoding;
        private readonly Func<int> _receiveTimeout;
        // Read at each use rather than taken once: ITikConnection.ReceiveTimeout may change on an open connection,
        // and the API and REST honour that per command.
        private int ReceiveTimeoutMs => _receiveTimeout();

        // Every read and write after the connect: the console over this socket, IAC negotiation filtered and
        // answered on the way in. One Enter is CR LF, which the Telnet NVT reads as one.
        private readonly CliPtySession _pty;

        internal TelnetClient(Encoding encoding, CliClientTimeouts timeouts)
        {
            _encoding = encoding ?? Encoding.UTF8;
            _receiveTimeout = timeouts.ReceiveMs;
            _connectTimeoutMs = timeouts.ConnectMs;
            _sendTimeoutMs = timeouts.SendMs;
            _pty = new CliPtySession(this, _encoding, _receiveTimeout, "Telnet", "telnet.sock", "\r\n",
                TelnetNegotiator.FilterAndRespond);
        }

        private readonly int _connectTimeoutMs;

        // Separate from ReceiveTimeoutMs, which is what the write side used to be given. They are two
        // different questions - how long the router may take to answer, and how long a write may block -
        // and ITikConnection exposes them as two properties, so honouring one with the other made
        // SendTimeout a setting the caller could change with no effect.
        private readonly int _sendTimeoutMs;

        // ── Connect ───────────────────────────────────────────────────────────

        internal void Connect(string host, int port)
        {
            _tcpClient = new TcpClient { NoDelay = true };
            try
            {
                TikTcp.Connect(_tcpClient, host, port, _connectTimeoutMs);
            }
            catch
            {
                _tcpClient.Close();
                throw;
            }

            _stream = _tcpClient.GetStream();
            _stream.ReadTimeout = ReceiveTimeoutMs;
            _stream.WriteTimeout = _sendTimeoutMs > 0 ? _sendTimeoutMs : Timeout.Infinite;
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
                readUntil: _pty.ReadUntilAsync,
                sendLine: _pty.SendLineAsync,
                sendBytes: _pty.SendBytesAsync,
                ct: ct).ConfigureAwait(false);

            // RouterOS performs a VT100 cursor-probe / redraw right after login and emits the shell
            // prompt a *second* time. LoginAsync returns on the first prompt; if we don't consume the
            // residual redraw it leaks into the first command's read and is mistaken for that command's
            // completion → empty output. Drain until the stream goes quiet. (See Docs/findings-winbox-terminal.md §3.)
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

        /// <summary>Asks RouterOS to leave the console (<c>/quit</c>), then closes the socket.</summary>
        /// <param name="answerSafeModeQuestion">The session holds Safe Mode: <c>/quit</c>'s question is answered with
        /// <c>y</c>, and the socket is closed only once the router has ended the session
        /// (<see cref="CliPtySession.EndSessionAsync"/>).</param>
        internal void Close(bool answerSafeModeQuestion = false)
        {
            // Asking RouterOS to exit releases the interactive session on the router at once rather than when the TCP
            // connection times out.
            if (_stream != null)
                _pty.EndSession(answerSafeModeQuestion);
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _tcpClient?.Close(); } catch { /* ignore */ }
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

        // ── ICliByteStream ────────────────────────────────────────────────────

        bool ICliByteStream.HasData => _stream.DataAvailable;

        Task<int> ICliByteStream.ReadAsync(byte[] buffer, CancellationToken ct)
            => _stream.ReadAsync(buffer, 0, buffer.Length, ct);

        // DataAvailable stays false once the router has closed, so a close never arrives as a 0 read here: the socket is
        // readable with nothing to read instead.
        bool ICliByteStream.ClosedAndDrained
        {
            get
            {
                try
                {
                    Socket socket = _tcpClient.Client;
                    return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
                }
                catch (ObjectDisposedException) { return true; }
                catch (SocketException) { return true; }
            }
        }

        Task ICliByteStream.WriteAsync(byte[] bytes, CancellationToken ct)
            => _stream.WriteAsync(bytes, 0, bytes.Length, ct);
    }
}
