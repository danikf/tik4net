using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The RouterOS console over a PTY byte stream, for the Telnet and SSH clients: every read and write after the
    /// carrier is up. The client keeps its connect, login and close, and hands this its <see cref="ICliByteStream"/>.
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
    internal sealed class CliPtySession
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
        private readonly Vt100State _vt100 = Vt100State.ForRouterOs();

        /// <param name="stream">The carrier.</param>
        /// <param name="encoding">The console's text encoding.</param>
        /// <param name="receiveTimeoutMs">Read at each use rather than taken once: <see cref="ITikConnection.ReceiveTimeout"/>
        /// may change on an open connection, and the API and REST honour that per command.</param>
        /// <param name="transportName">"Telnet" / "SSH": named in exception texts.</param>
        /// <param name="traceChannel">"telnet.sock" / "ssh.pty": the wire-trace channel.</param>
        /// <param name="enter">One Enter: <c>"\r\n"</c> on Telnet, whose NVT reads it as one; <c>"\r"</c> over a PTY, where
        /// CR LF is two Enters and the second, on an empty line, makes RouterOS 6.49 repaint the prompt in place after the
        /// real end of every response.</param>
        /// <param name="inboundFilter">Strips carrier negotiation from each received chunk and gives its answer (Telnet
        /// IAC); null when the carrier has none.</param>
        internal CliPtySession(ICliByteStream stream, Encoding encoding, Func<int> receiveTimeoutMs,
            string transportName, string traceChannel, string enter, CliInboundFilter? inboundFilter = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _encoding = encoding ?? Encoding.UTF8;
            _receiveTimeout = receiveTimeoutMs;
            _transportName = transportName;
            _traceChannel = traceChannel;
            _enter = enter;
            _inboundFilter = inboundFilter;
        }

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

            // The target repaints its prompt after the login just as a direct login does (see DrainAsync).
            await DrainAsync(RouterOsCliLogin.PostLoginDrainMs, ct).ConfigureAwait(false);
            RomonTarget = target.RomonId;
            return agentRomonId;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a CLI command and returns the cleaned output: ANSI stripped, echo line removed, trailing prompt
        /// removed, <c>without-paging</c> injected after <c>print</c> (<see cref="CliOutputHelper.InjectWithoutPaging"/>).
        /// <paramref name="onLine"/>, when given, gets each completed output line while the command is still running;
        /// the streamed lines are the raw ANSI-stripped ones (echo and prompt included).
        /// </summary>
        internal async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            // Discard anything still buffered from the PREVIOUS command before issuing this one.
            // RouterOS's line editor repaints the echo as "<prompt> <command>", and the read that returns on
            // the prompt inside that repaint leaves the echoed command text behind: the accumulated buffer
            // ends at "…[admin@CHR] > ", the settle window expires, and the trailing ":put […]" arrives
            // afterwards. The next command then reads that leftover as its own answer — and because the
            // stale echo contains 'name=value' text it parses as a record with no '.id' (P2.28).
            // Gated on HasData so the normal, clean path pays nothing. WinboxCliClient does the same.
            string cmd = CliOutputHelper.InjectWithoutPaging(command);

            if (_stream.HasData)
            {
                string residue = await DrainAsync(SettleMs, ct).ConfigureAwait(false);
                // A relay that ended while the connection was idle: the agent's session is gone, and this
                // command must not be sent into what is left of it.
                RouterOsCliLogin.ThrowIfRomonRelayEnded(_transportName, RomonTarget, cmd, residue, commandSent: false);
            }

            await SendLineAsync(cmd, ct).ConfigureAwait(false);

            string raw = await ReadCommandResponseAsync(ct, cmd, onLine).ConfigureAwait(false);

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
            // sentCommand: null → tolerant. A control key is not a command and RouterOS does not always
            // answer one with a prompt (measured: Ctrl+D replies with a bare cursor save/restore and nothing
            // else), so reaching the deadline here is normal rather than a truncated response.
            return await ReadCommandResponseAsync(ct, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for completion) and reads the reaction until the
        /// stream goes <b>quiet</b> for <paramref name="quietMs"/> — used for Tab-completion, whose listing
        /// does not end in a shell prompt (RouterOS redraws the prompt with the echoed stem), so a
        /// prompt-terminated read would block until the receive deadline. Returned with its escape sequences:
        /// an inline completion is written in cursor moves, which the completion parser replays
        /// (<see cref="CliCompletionParser"/>). Bounded by the receive deadline.
        /// </summary>
        internal async Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            await SendBytesAsync(raw, ct).ConfigureAwait(false);
            return await ReadUntilQuietAsync(quietMs, ct).ConfigureAwait(false);
        }

        // ── Reads ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads (answering negotiation and VT100 probes) until no new bytes arrive for <paramref name="quietMs"/> after
        /// at least some data, the carrier closes, or the receive deadline expires. Returns the accumulated text, escape
        /// sequences included. Nothing at all for <see cref="CliCompletionParser.NoReactionQuietFactor"/> quiet windows is
        /// an empty answer.
        /// </summary>
        private async Task<string> ReadUntilQuietAsync(int quietMs, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeoutMs);
            DateTime lastData = DateTime.UtcNow;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool gotData = false;
                bool closed = false;
                while (_stream.HasData)
                {
                    int n = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0) { closed = true; break; }
                    accumulated.Append(await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false));
                    gotData = true;
                }

                closed |= _stream.ClosedAndDrained;
                if (gotData)
                    lastData = DateTime.UtcNow;

                // Settled: some data arrived and the stream has been silent for the quiet window.
                if (!gotData && accumulated.Length > 0
                    && (DateTime.UtcNow - lastData).TotalMilliseconds >= quietMs)
                    return accumulated.ToString();
                // A Tab the router leaves unanswered (nothing more to list): not the receive deadline.
                if (accumulated.Length == 0
                    && (DateTime.UtcNow - lastData).TotalMilliseconds >= quietMs * CliCompletionParser.NoReactionQuietFactor)
                    return string.Empty;

                if (closed || DateTime.UtcNow >= deadline)
                    return accumulated.ToString();

                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }

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

                    accumulated.Append(await ProcessChunkAsync(buffer, available, ct).ConfigureAwait(false));
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

        /// <summary>
        /// Reads a command's response until <see cref="CliPromptSettle"/> says it is over (with <see cref="SettleMs"/>),
        /// reading whatever the stream has buffered between polls. Bounded by the receive deadline; a carrier that
        /// closes first ends the read too, as the relay ending when a RoMON target is set.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="sentCommand">
        /// The command being answered. When non-null, reaching the deadline without a prompt throws
        /// <see cref="TikConnectionReceiveTimeoutException"/> instead of returning the partial text — see
        /// <see cref="CliReadTimeout"/> for why returning it is worse than failing. Pass <c>null</c> for
        /// reads that legitimately need not end at a prompt (control keys).
        /// </param>
        /// <param name="onLine">
        /// Optional: called with each completed line as it arrives, for a caller that must consume a
        /// long-running command while it runs (see <see cref="CliLineStreamer"/>). Does not affect when the
        /// read returns — the stable prompt is still the only terminator.
        /// </param>
        private async Task<string> ReadCommandResponseAsync(CancellationToken ct, string? sentCommand,
            Action<string>? onLine = null)
        {
            var buffer = new byte[4096];
            var accumulated = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var settle = new CliPromptSettle(sentCommand, SettleMs, TikWireTrace.Enabled ? TraceNote : (Action<string>?)null);
            var streamer = new CliLineStreamer(onLine);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool gotData = false;
                bool closed = false;
                while (_stream.HasData)
                {
                    int n = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0) { closed = true; break; }

                    accumulated.Append(await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false));
                    gotData = true;
                }

                closed |= _stream.ClosedAndDrained;

                string stripped = VtStripper.StripAnsi(RouterOsCliLogin.WithoutRomonRelayNoise(RomonTarget, accumulated.ToString()));
                streamer.Feed(stripped);

                // The relay ended mid-command: no prompt will follow, the agent's session is over.
                RouterOsCliLogin.ThrowIfRomonRelayEnded(_transportName, RomonTarget, sentCommand, stripped, commandSent: true);

                switch (settle.Observe(stripped, gotData, sw.ElapsedMilliseconds, accumulated.Length))
                {
                    case CliPromptSettle.Outcome.Complete:
                    case CliPromptSettle.Outcome.Settled:
                        return stripped;
                }

                if (closed || sw.ElapsedMilliseconds >= ReceiveTimeoutMs)
                {
                    // Reached WITHOUT a settled prompt: the response is whatever arrived so far and may end
                    // mid-record. Silent truncation here is how a partial read reaches the parser looking
                    // like data (P2.25's failure class), so it is always traced.
                    settle.NoteEnd(closed ? "CLOSED" : "TIMEOUT", sw.ElapsedMilliseconds, accumulated.Length);
                    if (closed && sentCommand != null && RomonTarget != null)
                        throw RouterOsCliLogin.RomonRelayEndedWithTheSession(_transportName, RomonTarget, sentCommand, stripped);
                    if (sentCommand != null)
                        throw CliReadTimeout.Create(_transportName, ReceiveTimeoutMs, sentCommand, stripped);
                    return stripped;
                }

                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Consumes and discards any residual bytes on the stream (post-login VT100 redraw, a second
        /// prompt, stray push frames) until the stream stays silent for <paramref name="quietMs"/>.
        /// Negotiation and VT100 probes are still answered so the link stays in a sane state.
        /// </summary>
        /// <returns>The drained text, ANSI-stripped.</returns>
        internal async Task<string> DrainAsync(int quietMs, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var drained = new StringBuilder();
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);

            while (DateTime.UtcNow < quietDeadline)
            {
                ct.ThrowIfCancellationRequested();

                if (_stream.HasData)
                {
                    int n = await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0)
                        break; // connection closed

                    // Discard the text but still answer negotiation + VT100 probes so the link stays sane.
                    drained.Append(await ProcessChunkAsync(buffer, n, ct).ConfigureAwait(false));

                    // Reset the quiet window — keep draining as long as data keeps arriving.
                    quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
                }
                else
                {
                    await Task.Delay(15, ct).ConfigureAwait(false);
                }
            }

            return VtStripper.StripAnsi(drained.ToString());
        }

        /// <summary>
        /// Decodes a freshly-read chunk: strips carrier negotiation (answering it) when there is a filter, feeds the
        /// remaining bytes to the VT100 state machine (answering cursor probes), and returns the decoded text to
        /// append to the caller's accumulator.
        /// </summary>
        private async Task<string> ProcessChunkAsync(byte[] buffer, int count, CancellationToken ct)
        {
            byte[] chunk = new byte[count];
            Array.Copy(buffer, 0, chunk, 0, count);

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Recv, chunk, 0, count);

            if (_inboundFilter != null)
            {
                chunk = _inboundFilter(chunk, out byte[] reply);
                if (reply.Length > 0)
                    await SendBytesAsync(reply, ct).ConfigureAwait(false);
            }

            string text = _encoding.GetString(chunk);

            foreach (string vtReply in _vt100.Process(text))
                await SendBytesAsync(_encoding.GetBytes(vtReply), ct).ConfigureAwait(false);

            return text;
        }

        private void TraceNote(string note)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Note, note);
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
