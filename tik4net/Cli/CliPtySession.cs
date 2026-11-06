using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The PTY carrier of the RouterOS console, for the Telnet and SSH clients: the console's poll over an
    /// <see cref="ICliByteStream"/> (<see cref="ICliConsoleCarrier"/>), the login reads, the writes and the end of the
    /// session. The terminal the connection holds is <see cref="Terminal"/>. The client keeps its connect, login and
    /// close, and hands this its byte stream and its close.
    /// </summary>
    /// <remarks>
    /// The reads answer RouterOS's VT100 cursor probes as they go (<see cref="Vt100State"/>): without truthful cursor
    /// replies RouterOS assumes a 1x1 terminal and emits no command output, and the width the shared state advertises
    /// is what keeps RouterOS from wrapping long <c>:put</c> as-value records into the data
    /// (<see cref="Vt100State.RouterOsWidth"/> explains why it is a reachable column, not a huge one).
    /// <para>The reads poll <see cref="ICliByteStream.HasData"/> and only read when bytes are buffered, enforcing the
    /// deadline themselves between polls. On .NET Framework, <c>NetworkStream.ReadAsync</c> honours neither
    /// <c>ReadTimeout</c> nor cancellation once the read is pending with no data, so a pending read would block
    /// forever if the router sends nothing; SSH.NET's blocking <c>Read</c> likewise.</para>
    /// </remarks>
    internal sealed class CliPtySession : ICliConsoleCarrier
    {
        /// <summary>Silence required after a prompt before a command's response is considered complete (ms).</summary>
        internal const int SettleMs = 120;

        private readonly ICliByteStream _stream;
        private readonly Encoding _encoding;
        private readonly Func<int> _receiveTimeout;
        private readonly string _transportName;
        private readonly string _traceChannel;
        private readonly string _enter;
        private readonly CliInboundFilter? _inboundFilter;
        private readonly Action<bool> _closeCarrier;
        private readonly Vt100State _vt100 = Vt100State.ForRouterOs();
        private readonly CliPasswordPromptGuard _passwordPrompt = new CliPasswordPromptGuard();

        /// <param name="stream">The carrier.</param>
        /// <param name="encoding">The console's text encoding.</param>
        /// <param name="receiveTimeoutMs">Read at each use rather than taken once: <see cref="ITikConnection.ReceiveTimeout"/>
        /// may change on an open connection, and the API and REST honour that per command.</param>
        /// <param name="transportName">"Telnet" / "SSH": named in exception texts.</param>
        /// <param name="traceChannel">"telnet.sock" / "ssh.pty": the wire-trace channel.</param>
        /// <param name="enter">One Enter: <c>"\r\n"</c> on Telnet, whose NVT reads it as one; <c>"\r"</c> over a PTY, where
        /// CR LF is two Enters and the second, on an empty line, makes RouterOS 6.49 repaint the prompt in place after the
        /// real end of every response.</param>
        /// <param name="closeCarrier">The client's close, for <see cref="ICliTerminal.End"/> (with whether to answer the
        /// Safe Mode question) and the terminal's dispose (without): it ends the session (<see cref="EndSession"/>) while the carrier is
        /// up, then releases the carrier. Never throws.</param>
        /// <param name="inboundFilter">Strips carrier negotiation from each received chunk and gives its answer (Telnet
        /// IAC); null when the carrier has none.</param>
        internal CliPtySession(ICliByteStream stream, Encoding encoding, Func<int> receiveTimeoutMs,
            string transportName, string traceChannel, string enter, Action<bool> closeCarrier,
            CliInboundFilter? inboundFilter = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _encoding = encoding ?? Encoding.UTF8;
            _receiveTimeout = receiveTimeoutMs;
            _transportName = transportName;
            _traceChannel = traceChannel;
            _enter = enter;
            _inboundFilter = inboundFilter;
            _closeCarrier = closeCarrier ?? throw new ArgumentNullException(nameof(closeCarrier));
            Terminal = new CliConsole(this);
        }

        /// <summary>The console over this stream: the terminal the connection holds.</summary>
        internal CliConsole Terminal { get; }

        private int ReceiveTimeoutMs => _receiveTimeout();

        /// <summary>The target's RoMON id once <see cref="EnterRomonAsync"/> has relayed this terminal; null for a direct session.</summary>
        internal string? RomonTarget { get; set; }

        // ── Login helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// From the agent's shell, continues this terminal into a RoMON target through <c>/tool romon ssh</c>
        /// (<see cref="RouterOsCliLogin.RomonSshLoginAsync"/>). Every command after it runs on the target.
        /// </summary>
        /// <returns>The agent's own RoMON id.</returns>
        internal async Task<string> EnterRomonAsync(RomonRelayTarget target, CancellationToken ct)
        {
            string agentRomonId = await RouterOsCliLogin.RomonSshLoginAsync(
                target.RomonId, target.User, target.Password,
                useTerminalFlags: true,
                readUntil: ReadUntilAsync,
                sendLine: SendLineAsync,
                sendBytes: SendBytesAsync,
                ct: ct).ConfigureAwait(false);

            // The target repaints its prompt after the login just as a direct login does (CliConsole.DrainAsync).
            await Terminal.DrainAsync(RouterOsCliLogin.PostLoginDrainMs, ct).ConfigureAwait(false);
            RomonTarget = target.RomonId;
            return agentRomonId;
        }

        // ── Login reads ───────────────────────────────────────────────────────

        /// <summary>
        /// Reads from the stream until the accumulated text (ANSI-stripped) satisfies
        /// <paramref name="predicate"/>, or until the receive deadline expires or the carrier closes.
        /// </summary>
        internal async Task<string> ReadUntilAsync(Func<string, bool> predicate, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeoutMs);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool connectionClosed = false;

                // Drain all currently-available data without blocking on an empty carrier.
                while (_stream.HasData)
                {
                    int available = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (available <= 0)
                    {
                        connectionClosed = true; // remote closed the connection
                        break;
                    }

                    accumulated.Append(await ProcessChunkAsync(buffer, available).ConfigureAwait(false));
                }
                connectionClosed |= _stream.ClosedAndDrained;

                // Check predicate on ANSI-stripped accumulated text
                string stripped = VtStripper.StripAnsi(RouterOsCliLogin.WithoutRomonRelayNoise(RomonTarget, accumulated.ToString()));
                if (predicate(stripped))
                    return stripped;

                if (connectionClosed || DateTime.UtcNow >= deadline)
                    return stripped; // timeout / closed — callers handle incomplete responses

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }

        // ── The console's carrier ─────────────────────────────────────────────

        string ICliConsoleCarrier.TransportName => _transportName;

        string ICliConsoleCarrier.TraceChannel => _traceChannel;

        Encoding ICliConsoleCarrier.Encoding => _encoding;

        string ICliConsoleCarrier.Enter => _enter;

        int ICliConsoleCarrier.SettleMs => SettleMs;

        int ICliConsoleCarrier.ReceiveTimeoutMs => ReceiveTimeoutMs;

        string? ICliConsoleCarrier.RomonTarget => RomonTarget;

        // The residue is drained, gated on what the stream has buffered.
        bool ICliConsoleCarrier.DiscardsResidue => false;

        bool ICliConsoleCarrier.HasData => _stream.HasData;

        string ICliConsoleCarrier.PeekResidue() => string.Empty;

        void ICliConsoleCarrier.DiscardResidue(bool beforeCommand) { }

        // The ended carrier is found by the command's write itself (WriteRefusedByClose).
        void ICliConsoleCarrier.ThrowIfEnded(string what) { }

        bool ICliConsoleCarrier.WriteRefusedByClose => _stream.ClosedAndDrained;

        Task ICliConsoleCarrier.SendAsync(byte[] keys, CancellationToken ct) => SendBytesAsync(keys, ct);

        // Never asked: a TCP carrier delivers what is written or fails the write.
        TikConnectionSessionClosedException ICliConsoleCarrier.NotAcknowledged(string? sentCommand)
            => TikConnectionSessionClosedException.NotAcknowledged(_transportName, sentCommand, string.Empty);

        ICliCarrierRead ICliConsoleCarrier.BeginRead(CliReadKind kind) => new PtyRead(this);

        /// <summary>The client's close with the question answered when Safe Mode is held (<see cref="ICliTerminal.End"/>).</summary>
        void ICliConsoleCarrier.End(bool answerSafeModeQuestion) => _closeCarrier(answerSafeModeQuestion);

        /// <summary>The client's close without answering: Telnet and SSH still type <c>/quit</c> (<see cref="ICliTerminal.End"/>).</summary>
        void ICliConsoleCarrier.Release() => _closeCarrier(false);

        /// <summary>
        /// One read: everything the stream has buffered at each poll, decoded and answered, appended to what the read has
        /// so far. Ended when a read returns 0 or the stream says it closed (<see cref="ICliByteStream.ClosedAndDrained"/>);
        /// a closed carrier is any read's end, since it is never reopened. The wait between polls is 15 ms.
        /// </summary>
        /// <remarks>Polled rather than awaited: on .NET Framework, <c>NetworkStream.ReadAsync</c> honours neither
        /// <c>ReadTimeout</c> nor cancellation once the read is pending with no data, and SSH.NET's <c>Read</c>
        /// blocks.</remarks>
        private sealed class PtyRead : ICliCarrierRead
        {
            private readonly CliPtySession _pty;
            private readonly byte[] _buffer = new byte[4096];
            private readonly StringBuilder _accumulated = new StringBuilder();

            internal PtyRead(CliPtySession pty) { _pty = pty; }

            public string Text => _accumulated.ToString();

            public async Task<CliCarrierPoll> PollAsync(bool atPrompt, long elapsedMs, CancellationToken ct)
            {
                bool received = false;
                bool closed = false;
                while (_pty._stream.HasData)
                {
                    int n = await _pty._stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
                    if (n <= 0) { closed = true; break; }

                    _accumulated.Append(await _pty.ProcessChunkAsync(_buffer, n).ConfigureAwait(false));
                    received = true;
                }
                closed |= _pty._stream.ClosedAndDrained;
                return new CliCarrierPoll(received, closed);
            }

            public Task WaitAsync(CancellationToken ct) => Task.Delay(15, ct);
        }

        /// <summary>
        /// Decodes a freshly-read chunk: strips carrier negotiation (answering it) when there is a filter, feeds the
        /// remaining bytes to the VT100 state machine (answering cursor probes, except while a change-password prompt
        /// is on screen: <see cref="CliPasswordPromptGuard"/>), and returns the decoded text to append to the caller's
        /// accumulator.
        /// </summary>
        /// <remarks>The replies are not the caller's to cancel: the bytes they answer are consumed, so a cancel between
        /// two of them would leave the router waiting for the second (a cursor-position reply) with nothing to resend
        /// it.</remarks>
        private async Task<string> ProcessChunkAsync(byte[] buffer, int count)
        {
            byte[] chunk = new byte[count];
            Array.Copy(buffer, 0, chunk, 0, count);

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Recv, chunk, 0, count);

            if (_inboundFilter != null)
            {
                chunk = _inboundFilter(chunk, out byte[] reply);
                if (reply.Length > 0)
                    await SendBytesAsync(reply, CancellationToken.None).ConfigureAwait(false);
            }

            string text = _encoding.GetString(chunk);

            // The cursor model is fed every chunk; its replies are keystrokes, never sent into a change-password prompt.
            await _vt100.ProcessAndAnswerAsync(text, _passwordPrompt,
                reply => SendBytesAsync(_encoding.GetBytes(reply), CancellationToken.None)).ConfigureAwait(false);

            return text;
        }

        // ── Ending the session ────────────────────────────────────────────────

        /// <summary>
        /// Asks RouterOS to leave the console: <c>/quit</c> and one Enter, then <c>y</c> when the session holds Safe Mode
        /// (<see cref="RouterOsCliLogin.QuitKeys"/>). When answering, reads, discarding, until the router ends the session
        /// (a 0 read, or <see cref="ICliByteStream.ClosedAndDrained"/>) or <see cref="RouterOsCliLogin.SafeModeQuitWaitMs"/>
        /// passes. Never throws: a carrier that is already gone has nothing left to end.
        /// </summary>
        /// <param name="answerSafeModeQuestion">The session holds Safe Mode. RouterOS then answers <c>/quit</c> with "You
        /// are in Safe Mode. Quitting will unroll changes. Quit? [y/N]" instead of quitting, and the <c>y</c> answers it:
        /// the unroll a session ending without a release is owed anyway, done now.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// The Enter is this session's own, one keystroke: over a PTY a CR LF is two Enters, and the second would answer the
        /// question with its default, N. The <c>y</c> must then be read by the console before the carrier goes: a close that
        /// races it leaves the session ending inside the question, and on RouterOS 6.49.13 that stops the console from
        /// serving any new Telnet, SSH or API login until a reboot (2 in 36 racing Telnet closes; 0 in 60 that waited for
        /// the router to close first, which it does in well under a second).
        /// </remarks>
        internal async Task EndSessionAsync(bool answerSafeModeQuestion, CancellationToken ct)
        {
            try
            {
                await SendBytesAsync(_encoding.GetBytes(RouterOsCliLogin.QuitKeys(answerSafeModeQuestion, _enter)), ct).ConfigureAwait(false);
                if (answerSafeModeQuestion)
                    await WaitForRouterToEndSessionAsync(ct).ConfigureAwait(false);
            }
            catch { /* the connection is gone or going: either way there is nothing left to end */ }
        }

        /// <summary>
        /// <see cref="EndSessionAsync"/> for a synchronous close (<see cref="ICliTerminal.End"/>), bounded by
        /// <see cref="RouterOsCliLogin.SafeModeQuitWaitMs"/> and a second for the write.
        /// </summary>
        internal void EndSession(bool answerSafeModeQuestion)
        {
            try
            {
                Task.Run(() => EndSessionAsync(answerSafeModeQuestion, CancellationToken.None))
                    .Wait(RouterOsCliLogin.SafeModeQuitWaitMs + 1000);
            }
            catch { /* EndSessionAsync does not throw; a faulted wait has nothing to report either */ }
        }

        // The quit wait's poll (CliSafeModeQuitWait): what is buffered, read and discarded; over when the carrier closes.
        private Task WaitForRouterToEndSessionAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            return CliSafeModeQuitWait.RunAsync(async () =>
            {
                while (_stream.HasData)
                {
                    int n = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0)
                        return true;
                    if (TikWireTrace.Enabled)
                        TikWireTrace.Emit(_traceChannel, TikWireDir.Recv, buffer, 0, n);
                }
                if (_stream.ClosedAndDrained)
                    return true;
                await Task.Delay(15, ct).ConfigureAwait(false);
                return false;
            });
        }

        // ── Writes ────────────────────────────────────────────────────────────

        /// <summary>Types <paramref name="text"/> and one Enter.</summary>
        internal Task SendLineAsync(string text, CancellationToken ct)
            => SendBytesAsync(_encoding.GetBytes(text + _enter), ct);

        /// <summary>Writes <paramref name="bytes"/> as they are.</summary>
        internal Task SendBytesAsync(byte[] bytes, CancellationToken ct)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Send, bytes, 0, bytes.Length);

            return _stream.WriteAsync(bytes, ct);
        }
    }
}
