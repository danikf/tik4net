using System;
using System.IO;
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
        /// <param name="answerSafeModeQuestion">The session holds Safe Mode. RouterOS then answers <c>/quit</c> with
        /// "You are in Safe Mode. Quitting will unroll changes. Quit? [y/N]" (7.24.4) instead of quitting, so <c>y</c> is
        /// typed after it: the unroll a session ending without a release is owed anyway, done now. Left unanswered, the
        /// console waits on a terminal that is gone - over WinBox CLI that wedged RouterOS 6.49.13's console until a
        /// reboot.</param>
        internal void Close(bool answerSafeModeQuestion = false)
        {
            // Ask RouterOS to exit cleanly before closing the socket. This releases the
            // interactive session on the router side immediately rather than waiting for
            // the TCP timeout to expire. Errors are silently ignored (e.g. already closed).
            string quit = RouterOsCliLogin.QuitKeys(answerSafeModeQuestion, "\r\n");
            try { _stream?.Write(_encoding.GetBytes(quit), 0, _encoding.GetByteCount(quit)); } catch { /* ignore */ }
            if (answerSafeModeQuestion)
                WaitForRouterToEndSession(RouterOsCliLogin.SafeModeQuitWaitMs);
            try { _stream?.Close(); } catch { /* ignore */ }
            try { _tcpClient?.Close(); } catch { /* ignore */ }
        }

        /// <summary>
        /// Reads, discarding, until RouterOS closes the connection or <paramref name="maxMs"/> passes.
        /// </summary>
        /// <remarks>
        /// The <c>y</c> that answers <c>/quit</c>'s Safe Mode question must be read by the console before the connection
        /// goes: a close that races it leaves the session ending inside the question, and on RouterOS 6.49.13 that stops
        /// the console from serving any new Telnet, SSH or API login until a reboot (2 in 36 racing closes; 0 in 60 that
        /// waited for the router to close first, which it does in well under a second).
        /// </remarks>
        private void WaitForRouterToEndSession(int maxMs)
        {
            var buffer = new byte[4096];
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _stream.ReadTimeout = 200;
                while (deadline.ElapsedMilliseconds < maxMs)
                {
                    try
                    {
                        if (_stream.Read(buffer, 0, buffer.Length) == 0)
                            return;
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut) { }
                }
            }
            catch { /* the connection is gone or going: either way the wait is over */ }
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

        // A closed socket arrives as a 0 read.
        bool ICliByteStream.ClosedAndDrained => false;

        Task ICliByteStream.WriteAsync(byte[] bytes, CancellationToken ct)
            => _stream.WriteAsync(bytes, 0, bytes.Length, ct);
    }
}
