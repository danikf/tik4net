using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Diagnostics;
using tik4net.Winbox;

namespace tik4net.WinboxCli
{
    /// <summary>
    /// WinBox CLI terminal client. On top of any <see cref="IWinboxM2Channel"/> (TCP 8291 or MAC-layer
    /// UDP 20561) it opens the mepty (terminal PTY) handler and drives a persistent RouterOS CLI
    /// session — the encrypted-transport equivalent of <c>MacTelnetUdpClient</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transport-agnostic: the injected channel decides whether M2 messages travel over TCP or the MAC
    /// layer, so this same engine backs both <c>WinboxCliConnection</c> and <c>WinboxCliMacConnection</c>.
    /// </para>
    /// <para>
    /// <b>How the command path is asynchronous.</b> The read loops below never wait on the network:
    /// they receive a frame only once <see cref="IWinboxM2Channel.DataAvailable"/> says one is arriving,
    /// and otherwise wait out a short poll interval. That gate is not a convenience — a receive deadline
    /// firing part-way through an encrypted frame leaves the stream unrecoverably desynchronized, which is
    /// why this transport polls a readiness flag rather than awaiting a socket read with a deadline on it.
    /// The interval is a <see cref="Task.Delay(int)"/> and not a <see cref="Thread.Sleep(int)"/>, so a
    /// command waiting thirty seconds for the router occupies a thread for the frame decodes alone and not
    /// for the waiting — which is the property <see cref="TikConnectionCapability.AsyncCommands"/> makes a
    /// claim about.
    /// </para>
    /// </remarks>
    internal sealed class WinboxCliClient : ICliTerminal
    {
        // mepty (terminal PTY) handler + commands — see WinboxM2Protocol.Mepty.
        private const int SettleMs       = 150;
        // Receive timeout for one encrypted frame. Must be generous: a timeout that fires mid-frame
        // leaves the TCP stream misaligned and every subsequent read fails (see winbox findings §2).
        // We gate every read behind DataAvailable so this timeout only bounds a frame already arriving.
        private const int FrameTimeoutMs = 5000;
        private const int PollSleepMs    = 20;
        // Minimum gap between two fire-on-idle pulls while waiting on a response that has not started
        // arriving. Purely to avoid pointless frames: an empty pull sent while nothing is pending is never
        // answered. A batch arrival resets the cadence to "fire immediately", so this throttles only a
        // genuine stall, never multi-batch streaming.
        private const int PullIntervalMs = 120;

        // Answers RouterOS VT100 cursor-probe negotiation (shared PTY logic). Without truthful
        // cursor replies RouterOS assumes a 1x1 terminal and emits no command output; the width the
        // shared state advertises is what keeps RouterOS from wrapping long ':put' as-value records
        // into the data (Vt100State.RouterOsWidth explains why it is a reachable column, not a huge one).
        private readonly Vt100State _vt100 = Vt100State.ForRouterOs();
        private readonly CliPasswordPromptGuard _passwordPrompt = new CliPasswordPromptGuard();
        private readonly IWinboxM2Channel _session;
        private readonly Encoding _encoding;
        private readonly Func<int> _receiveTimeout;
        // Read at each use rather than taken once: ITikConnection.ReceiveTimeout may change on an open connection,
        // and the API and REST honour that per command.
        private int ReceiveTimeoutMs => _receiveTimeout();
        private readonly int _loginTimeoutMs;
        private readonly string _transportName;
        private readonly string _traceChannel;

        private int _sessionId = -1;

        /// <summary>
        /// Running total of terminal-output bytes received on this session — the value RouterOS expects in the
        /// mepty <see cref="WinboxM2Protocol.Mepty.Key.Counter"/> field, which is a cumulative
        /// <b>byte acknowledgement</b>, not a message counter. mepty will not let unacknowledged
        /// output exceed a ~8 KB window: send a value that does not track the bytes actually consumed and the
        /// terminal delivers roughly that much and then goes permanently silent, mid-command. That is the whole
        /// of the "large output hangs" / "the session degrades after N commands" family of symptoms — the
        /// apparent per-session command limit was just this window divided by the average command's output.
        /// Deliberately <see cref="int"/>: the wire field is a u32 and <see cref="M2Message.U32User"/> casts,
        /// so unchecked wraparound past <see cref="int.MaxValue"/> still encodes the correct modulo-2^32 value.
        /// </summary>
        private int _ackBytes;

        /// <param name="channel">The M2 carrier: TCP, the MAC layer, or a RoMON relay over either.</param>
        /// <param name="encoding">The console's text encoding.</param>
        /// <param name="timeouts">The connection's timeouts.</param>
        /// <param name="transportName">"WinBox CLI" / "WinBox CLI MAC": named in exception texts.</param>
        /// <param name="traceChannel">The wire-trace channel (<see cref="WinboxCliSession.TraceChannel"/>).</param>
        internal WinboxCliClient(IWinboxM2Channel channel, Encoding encoding, CliClientTimeouts timeouts,
            string transportName, string traceChannel)
        {
            _session          = channel ?? throw new ArgumentNullException(nameof(channel));
            _transportName    = transportName;
            _traceChannel     = traceChannel;
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeout = timeouts.ReceiveMs;
            _loginTimeoutMs   = TikTimeouts.HandshakeBoundMs(timeouts.ConnectMs, timeouts.ReceiveMs());
            _sendTimeoutMs    = timeouts.SendMs;
        }

        // Zero means "the channel decides", which for the TCP carrier is the receive timeout it always
        // used. Kept separate so ITikConnection.SendTimeout reaches the socket it is about.
        private readonly int _sendTimeoutMs;

        // ── Login ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the channel, authenticates, takes a mepty terminal and waits out the RouterOS startup
        /// exchange.
        /// </summary>
        /// <remarks>
        /// The one <see cref="Task.Run(Action)"/> on this transport, deliberately: the channel's
        /// <c>Open</c> is a synchronous EC-SRP5 handshake down through the crypto, and the prompt wait that
        /// follows it runs before there is a command path to speak of. Open happens once per connection and
        /// is not what <see cref="TikConnectionCapability.AsyncCommands"/> is a claim about.
        /// <para><paramref name="terminalPassword"/> is the password the terminal is opened with when it differs
        /// from the channel's: through a RoMON agent the channel logs in to the agent, and the terminal lives on the
        /// target (<see cref="Winbox.WinboxRomonChannel"/>).</para>
        /// </remarks>
        internal Task LoginAsync(string host, int port, string user, string pass, CancellationToken ct,
            string? terminalPassword = null)
        {
            return Task.Run(async () =>
            {
                _session.Open(host, port, user, pass, _loginTimeoutMs, ReceiveTimeoutMs, _sendTimeoutMs);

                // Open one mepty terminal and keep it for the whole connection. The password is supplied
                // here (not via a Login:/Password: prompt) — auth already happened at the M2 layer, so the
                // session starts directly at the RouterOS shell (or the change-password nag). The login
                // size hint stays at 80x25, the size measured to work (RouterOS rejects oversized values with an
                // error response carrying no SESSION_ID); the real width comes from the VT100 cursor-probe
                // answered by the wide _vt100 below.
                _sessionId = OpenTerminalSession(terminalPassword ?? pass, "vt102", 80, 25);
                SendTerminalReady(_sessionId);

                WaitForPromptSync();
                await DrainAsync(RouterOsCliLogin.PostLoginDrainMs).ConfigureAwait(false);

                // From here the terminal is idle between commands, and a RouterOS terminal is written to
                // unprompted. On a carrier that expects each write to be acknowledged, leaving it unread is
                // what kills the session — so hand the channel the job of servicing itself. TCP has
                // nothing to do here.
                _session.StartIdleServicing();
            }, ct);
        }

        // ── Command execution ─────────────────────────────────────────────────

        /// <inheritdoc cref="Cli.ICliTerminal.RequestInterrupt"/>
        public void RequestInterrupt() => _interrupt.Request();

        private readonly Cli.CliInterruptLatch _interrupt = new Cli.CliInterruptLatch();

        /// <summary>
        /// Types a command line and reads its cleaned answer, reporting each completed output line to
        /// <paramref name="onLine"/> (when given) while the command is still running.
        /// </summary>
        /// <remarks>The prologue is the one every terminal runs (<see cref="Cli.CliConsole.SendCommandAndReadAsync"/>):
        /// the line, the latch cleared, the residue, the ended check, the write.</remarks>
        public async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string cmd = CliEndMarker.InjectWithoutPaging(command);
            _interrupt.Clear();
            await DiscardResidueAsync().ConfigureAwait(false);
            ThrowIfTerminalClosed(cmd);
            SendInput(_encoding.GetBytes(cmd + Cli.CliTerminalText.Enter));
            string raw = await ReadCommandResponseAsync(cmd, ct, onLine).ConfigureAwait(false);
            return CliOutputCleaner.CleanOutput(VtStripper.StripAnsi(raw), cmd);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no carriage return, no paging injection) and
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode.
        /// </summary>
        public async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await DiscardResidueAsync().ConfigureAwait(false);
            ThrowIfTerminalClosed(KeyName(raw));
            SendInput(raw);
            // null → tolerant; a control key need not be answered with a prompt.
            return VtStripper.StripAnsi(await ReadCommandResponseAsync(null, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for Tab-completion) and reads the reaction until
        /// the terminal goes quiet for <paramref name="quietMs"/> — the completion listing does not end in a
        /// shell prompt (RouterOS redraws the prompt with the echoed stem), so it must be read on a settle
        /// window rather than a prompt match. Returned with its escape sequences: an inline completion is
        /// written in cursor moves, which the completion parser replays.
        /// </summary>
        public async Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await DiscardResidueAsync().ConfigureAwait(false);
            ThrowIfTerminalClosed(KeyName(raw));
            SendInput(raw);
            return await ReadUntilQuietAsync(quietMs, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drains residual frames still buffered from the previous read before a command or a key is typed; gated on
        /// <see cref="IWinboxM2Channel.DataAvailable"/> so the clean path pays nothing. The login drains for the same
        /// reason after the initial prompt.
        /// </summary>
        /// <remarks>A late prompt repaint or VT100 probe answer that arrived after the previous read returned would
        /// otherwise be read first. Before a command it is mistaken for the command's completion (an early, empty result)
        /// or desynchronises a frame boundary (a full receive-timeout hang); before a key its prompt settles a Safe Mode
        /// key's read before the router's question to it arrives, or starts a Tab's quiet window before the listing does
        /// (X4 of the terminal clients review). The PTY drains the same way.</remarks>
        private async Task DiscardResidueAsync()
        {
            if (_session.DataAvailable)
                await DrainAsync(SettleMs).ConfigureAwait(false);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <summary>Asks RouterOS to leave the console (<c>/quit</c>); errors are ignored.</summary>
        /// <param name="answerSafeModeQuestion">The session holds Safe Mode. RouterOS then answers <c>/quit</c> with
        /// "You are in Safe Mode. Quitting will unroll changes. Quit? [y/N]" (7.24.4) instead of quitting, so <c>y</c> is
        /// typed after it: the unroll a session ending without a release is owed anyway, done now. Left unanswered, the
        /// console waits on a terminal that is gone - over WinBox CLI that wedged RouterOS 6.49.13's console until a
        /// reboot.</param>
        /// <remarks>In Safe Mode the router is given time to take the <c>y</c> before the session goes: a close that
        /// races it ends the session inside the question, which wedges RouterOS 6.49.13's console (measured over WinBox
        /// CLI and Telnet). The reaction — the question, the unroll, the end of the session — is read until the router
        /// ends the session itself with <see cref="WinboxM2Protocol.Mepty.Close"/>, as the PTY and MAC-Telnet waits end on
        /// their carrier's end; at most <see cref="RouterOsCliLogin.SafeModeQuitWaitMs"/>. Measured, RouterOS closes the
        /// terminal after every answered question: ~220 ms after the keys on 7.25.1 (~160 ms after its
        /// <c>interrupted</c> line), ~65 ms on 6.49.21 (~30 ms after it), and writes nothing after the close.
        /// <see cref="SafeModeQuitQuietMs"/> of quiet ends the wait too, for a router that never closes: the close lands
        /// well inside it in every measurement, so the quiet never cuts the wait short of the router's own
        /// end.</remarks>
        internal void TryCloseSession(bool answerSafeModeQuestion = false)
        {
            if (_sessionId < 0) return;
            try { SendInput(_encoding.GetBytes(RouterOsCliLogin.QuitKeys(answerSafeModeQuestion, Cli.CliTerminalText.Enter))); } catch { return; }
            if (!answerSafeModeQuestion) return;
            try { WaitForRouterToEndSessionAsync().GetAwaiter().GetResult(); }
            catch { /* the session is gone: the wait is over */ }
        }

        // The quit wait's poll (CliSafeModeQuitWait): one frame when one is arriving, its probes answered; over on the
        // router's Mepty.Close, on a carrier that has failed, or on SafeModeQuitQuietMs of quiet.
        private Task WaitForRouterToEndSessionAsync()
        {
            var quiet = new Cli.CliQuietWindow(SafeModeQuitQuietMs);
            int received = 0;
            return Cli.CliSafeModeQuitWait.RunAsync(async () =>
            {
                if (_session.DataAvailable)
                {
                    byte[]? chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { return true; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        received += text.Length;
                        _vt100.ProcessAndAnswer(text, _passwordPrompt, SendText);
                    }
                }
                else
                {
                    await Task.Delay(PollSleepMs).ConfigureAwait(false);
                }
                return _terminalClosed || quiet.Observe(received) != Cli.CliQuietWindow.Outcome.Continue;
            });
        }

        /// <summary>
        /// Quiet that ends the wait for the router's reaction to a Safe Mode <c>/quit</c> when no
        /// <see cref="WinboxM2Protocol.Mepty.Close"/> comes (ms): a fallback, longer than every close delay measured.
        /// </summary>
        private const int SafeModeQuitQuietMs = 500;

        public void Dispose() => _session.Dispose();

        // ── ICliTerminal ──────────────────────────────────────────────────────

        public bool SupportsQuietRead => true;

        public bool SupportsLineStreaming => true;

        public void End(bool answerSafeModeQuestion)
        {
            TryCloseSession(answerSafeModeQuestion);
            Dispose();
        }

        // ── mepty message building (handler [76]) ─────────────────────────────

        internal int OpenTerminalSession(string password, string terminalType, int cols, int rows)
        {
            if (!_session.IsEncrypted)
                throw new NotSupportedException(
                    "WinBox terminal (mepty) is only supported over the encrypted EC-SRP5 channel " +
                    "(RouterOS 6.43+). The legacy MD5 path does not carry an encrypted terminal session.");

            byte[] msg = M2Message.Request(
                WinboxM2Protocol.Mepty.Handler, WinboxM2Protocol.Mepty.Login, _session.NextReqIdField(),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Password, password),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Input, terminalType),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Cols, cols),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Rows, rows));
            int requestId = M2Message.ParseSysReqId(msg) ?? -1;
            _session.Send(msg);
            return M2Message.ParseSessionId(ReceiveTerminalOpenReply(requestId));
        }

        /// <summary>
        /// Reads the reply to the terminal open, skipping a frame that is not one.
        /// </summary>
        /// <remarks>
        /// The reply echoes our request id. A frame with a different one, or with none and nothing a reply carries
        /// (a session id, an error code), is not the answer. The one seen there is a
        /// <see cref="WinboxM2Protocol.Command.Logout"/> push with no request id: RouterOS sends it to every WinBox
        /// session when another session or a RoMON relay link ends — three per relay link — so several links ending
        /// together put a burst of them ahead of the reply. Taking one for the reply read "no SESSION_ID". Skipped
        /// frames are not counted, only timed by <see cref="FrameTimeoutMs"/>; each is traced, and named in the
        /// exception when no reply follows.
        /// </remarks>
        private byte[] ReceiveTerminalOpenReply(int requestId)
        {
            const int NamedInFailure = 8;
            var skipped = new List<string>();
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                int left = FrameTimeoutMs - (int)deadline.ElapsedMilliseconds;
                byte[]? frame;
                // No command has run yet, so an open the router ended can be retried (seen once, cause unknown).
                try { frame = left > 0 ? _session.Receive(left) : null; }
                catch (Winbox.WinboxMacSessionEndedException) { throw new MacTelnet.MacLayerLoginClosedSilentlyException(_transportName); }
                if (frame == null)
                {
                    if (skipped.Count == 0)
                        throw new InvalidOperationException($"The router did not answer the WinBox terminal open within {FrameTimeoutMs} ms.");
                    throw new InvalidOperationException($"The router did not answer the WinBox terminal open within {FrameTimeoutMs} ms; it sent "
                        + string.Join(", ", skipped.Take(NamedInFailure))
                        + (skipped.Count > NamedInFailure ? $" and {skipped.Count - NamedInFailure} more frame(s)" : "") + " instead"
                        + (skipped.Any(s => s.StartsWith("a logout", StringComparison.Ordinal))
                            ? " (RouterOS pushes a logout to every WinBox session when any session or RoMON relay link ends)" : "")
                        + ".");
                }
                if (IsTerminalOpenReply(frame, requestId))
                {
                    if (TikWireTrace.Enabled)
                        TikWireTrace.Emit(_traceChannel, TikWireDir.Note, "terminal open: reply " + M2Message.Describe(frame));
                    return frame;
                }

                bool logout = M2Message.ParseAllFields(frame).TryGetValue(WinboxM2Protocol.SysKey.Command, out var command)
                    && command.Value != null && Convert.ToInt64(command.Value, CultureInfo.InvariantCulture) == WinboxM2Protocol.Command.Logout;
                string what = (logout ? "a logout push " : "a frame ") + M2Message.Describe(frame);
                skipped.Add(what);
                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit(_traceChannel, TikWireDir.Note, "terminal open: skipped " + what);
            }
        }

        private static bool IsTerminalOpenReply(byte[] frame, int requestId)
        {
            int? echoed = M2Message.ParseSysReqId(frame);
            if (echoed.HasValue)
                return echoed.Value == requestId;
            // No request id: a reply only if it carries what a reply carries.
            return M2Message.TryParseSessionId(frame, out _) || M2Message.ParseSysStatus(frame) != 0;
        }

        private void SendTerminalReady(int sessionId)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, 0));
            _session.Send(msg);
        }

        /// <summary>
        /// Sends an empty mepty <c>Data</c> frame — a request to flush more terminal output without any
        /// keystrokes. The mepty <c>Data</c> command doubles as "send input" and "pull output"
        /// (<see cref="WinboxM2Protocol.Mepty.Data"/>): RouterOS answers a single <c>Data</c> with one batch
        /// of pending output, so a response larger than that batch is only delivered if the client keeps
        /// pulling. Without this, any command whose output exceeds one batch (verified live at a few hundred
        /// bytes — e.g. <c>print detail as-value</c> over several records) hangs: RouterOS waits for the next
        /// pull, the client waits for push that never comes, and the terminal wedges for the rest of the
        /// session (subsequent commands stop even echoing). Same shape as <see cref="SendTerminalReady"/>
        /// (Data, no Input key), and it carries <see cref="_ackBytes"/> — pulling is only half the contract,
        /// the acknowledgement is what reopens RouterOS's send window.
        /// </summary>
        private void SendPull()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.SessionIdField(_sessionId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, _ackBytes));
            _session.Send(msg);

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Send, "PULL ack=" + _ackBytes);
        }

        private void SendInput(byte[] keystrokes)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.SessionIdField(_sessionId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, keystrokes),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, _ackBytes));
            _session.Send(msg);

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Send, keystrokes, 0, keystrokes.Length,
                    "ack=" + _ackBytes);
        }

        /// <summary>Receives one frame and returns the terminal payload (user key 2), or null.</summary>
        private byte[]? ReceiveTerminalChunk(int timeoutMs)
        {
            byte[]? resp = _session.Receive(timeoutMs);
            if (resp == null) return null;

            // Only accept output from the terminal we are actually driving. A frame from any other mepty
            // session must not be folded into this read: its bytes would corrupt the response text, and —
            // arriving after the completion prompt — would leave the buffer no longer ending at a prompt.
            // It must also not be acknowledged below, since the ack is per-session. Only drop when the frame
            // actually names a session: one without SESSION_ID cannot be attributed, so it is accepted.
            if (M2Message.TryParseSessionId(resp, out int frameSession) && frameSession != _sessionId)
            {
                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit(_traceChannel, TikWireDir.Note,
                        "dropped frame from retired session " + frameSession + " (current " + _sessionId + ")");
                return null;
            }

            if (M2Message.ParseSysU32(resp, WinboxM2Protocol.SysKey.Command) == WinboxM2Protocol.Mepty.Close)
                _terminalClosed = true;

            byte[]? payload = M2Message.ParseUserBytes(resp, WinboxM2Protocol.Mepty.Key.Input);

            // Acknowledge what we consumed — every subsequent Data frame reports this total and that is what
            // lets RouterOS release the next window of output. See _ackBytes.
            if (payload != null) _ackBytes += payload.Length;

            if (payload != null && TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Recv, payload, 0, payload.Length);
            else if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_traceChannel, TikWireDir.Note, "frame without terminal output: " + M2Message.Describe(resp));

            return payload;
        }

        // ── Synchronous terminal loops ────────────────────────────────────────

        /// <summary>
        /// Waits until the RouterOS shell prompt appears, answering VT100 cursor-probe negotiation and settling the
        /// screen by the one login rule (<see cref="CliLoginScreen"/>: the change-password nag, the refusal).
        /// </summary>
        private void WaitForPromptSync()
        {
            var sb = new StringBuilder();
            var screen = new CliLoginScreen();
            int textFrames = 0;
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < _loginTimeoutMs)
            {
                if (!_session.DataAvailable) { Thread.Sleep(PollSleepMs); continue; }

                byte[]? chunk;
                try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                catch (IOException) { break; }
                if (chunk == null) continue;

                textFrames++;
                string text = _encoding.GetString(chunk);
                sb.Append(text);

                // The cursor model is fed unconditionally (Process is eager; only sending the replies is
                // I/O), but the replies are held back while the change-password prompt is on screen
                // (CliPasswordPromptGuard): while that prompt is up the ONLY byte we ever send is the Ctrl-C that
                // skips it. Self-clearing: the buffer is reset with the nag, so the check below resumes as soon as
                // the prompt is gone.
                _vt100.ProcessAndAnswer(text, _passwordPrompt, SendText);

                switch (screen.Feed(VtStripper.StripAnsi(sb.ToString())))
                {
                    case CliLoginScreen.Step.SendCtrlC:
                        SendInput(CliLoginScreen.CtrlC);   // skip the change
                        sb.Clear();
                        continue;
                    case CliLoginScreen.Step.Refused:     throw screen.Refusal(_transportName);
                    case CliLoginScreen.Step.NagPersists: throw CliLoginScreen.NagPersisted(_transportName);
                    case CliLoginScreen.Step.Prompt:      return;
                }
            }

            throw screen.TimedOut(_transportName, sw.ElapsedMilliseconds, textFrames, sb.ToString());
        }

        private void TraceNote(string note) => TikWireTrace.Emit(_traceChannel, TikWireDir.Note, note);

        // The router closed this terminal (Mepty.Close): the console has ended, and keystrokes sent into it are never
        // answered, so a read after them would only run the deadline out.
        private volatile bool _terminalClosed;

        private void ThrowIfTerminalClosed(string what)
        {
            if (_terminalClosed)
                throw Cli.CliReadTimeout.CreateClosedBeforeWrite(_transportName, ReceiveTimeoutMs, what, null);
        }

        private string KeyName(byte[] raw) => Cli.CliTerminalText.KeyName(raw, _encoding);

        private void SendText(string text) => SendInput(_encoding.GetBytes(text));

        private TikConnectionSessionClosedException SessionClosed(string? sentCommand)
            => TikConnectionSessionClosedException.NotAcknowledged(_transportName, sentCommand,
                "The MAC-layer carrier keeps the UDP socket open and RouterOS sends no error when it drops a console "
                + "session, so the session goes silent rather than reporting anything.");

        /// <summary>
        /// Reads a command response until <see cref="Cli.CliPromptSettle"/> says it is over (with <see cref="SettleMs"/>),
        /// pulling mepty output while the text does not end at a completion prompt and receiving a frame only once
        /// one is arriving.
        /// </summary>
        /// <param name="sentCommand">
        /// The command being answered. When non-null, reaching the deadline without a prompt throws
        /// <see cref="TikConnectionReceiveTimeoutException"/> instead of returning the partial text — see
        /// <see cref="Cli.CliReadTimeout"/>. <c>null</c> for control keys, which need not end at a prompt.
        /// </param>
        /// <param name="onLine">
        /// Optional: called with each completed line as it arrives, so a long-running command can be
        /// consumed while it runs (see <see cref="Cli.CliLineStreamer"/>). Does not affect when the read
        /// returns — the stable prompt plus its settle window is still the only terminator.
        /// </param>
        /// <param name="ct">
        /// Cancels the wait between polls. Only ever the caller's own token, and only in
        /// <see cref="TikCancellationMode.AbandonAndClose"/> — <c>CliConnectionBase</c> passes
        /// <see cref="CancellationToken.None"/> otherwise, because a read abandoned mid-command leaves
        /// output the next command would parse as its own. In that mode the connection is closed straight
        /// afterwards, so there is no next command for it to confuse.
        /// </param>
        private async Task<string> ReadCommandResponseAsync(string? sentCommand, CancellationToken ct,
            Action<string>? onLine = null)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var settle = new Cli.CliPromptSettle(sentCommand, SettleMs, TikWireTrace.Enabled ? TraceNote : (Action<string>?)null);
            var streamer = new Cli.CliLineStreamer(onLine);
            long lastPullMs = -1;   // -1 = a pull is due now (fire immediately)
            bool closedBefore = _terminalClosed;   // only a close that arrives during this read is this read's

            while (sw.ElapsedMilliseconds < ReceiveTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                if (_interrupt.TakeFor(sentCommand))
                {
                    SendInput(new[] { Cli.CliTerminalText.CtrlC });
                    settle.Interrupted();
                }
                bool gotData = false;
                if (_session.DataAvailable)
                {
                    byte[]? chunk;
                    // A frame has started arriving. It gets what is left of the receive deadline, not a fixed
                    // FrameTimeoutMs: the router pauses its terminal output for 10-12 s at a time
                    // (findings-winbox.md §20), a pause BETWEEN frames already gets the whole deadline, and one
                    // that happens to land mid-frame deserves no less. A socket deadline that fires mid-frame
                    // also leaves the stream unreadable, so it should fire only when the read is over anyway.
                    try { chunk = ReceiveTerminalChunk(RemainingFrameBudget(sw)); }
                    catch (IOException ex)
                    {
                        // Not the generic timeout below: that one says the whole deadline elapsed, which is not
                        // what happened, and it would drop the one exception that says what did.
                        string soFar = VtStripper.StripAnsi(sb.ToString());
                        if (sentCommand == null)
                            return soFar;
                        throw Cli.CliReadTimeout.CreateMidFrame(
                            _transportName, ReceiveTimeoutMs, sw.ElapsedMilliseconds, sentCommand, soFar, ex);
                    }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        // Guarded on every read: a caller's /password puts the change-password prompt on screen.
                        _vt100.ProcessAndAnswer(text, _passwordPrompt, SendText);
                        gotData = true;
                    }
                }
                else
                {
                    // mepty delivers output one batch per Data frame and will not push the remainder of a
                    // large response on its own — so, while the text does not end at a completion prompt, keep
                    // pulling (empty Data) whenever nothing is buffered. While it does (the settle window is
                    // running), nothing is pulled; if the text moves on, pulling resumes. RouterOS does NOT
                    // answer every pull (a pull sent while nothing is pending simply yields no frame), so we
                    // cannot block waiting for a pull to be acknowledged before sending the next — we must keep
                    // firing on idle. The pull also carries the byte acknowledgement that reopens RouterOS's send window (see
                    // _ackBytes), which is what makes arbitrarily large output flow rather than stopping dead
                    // after ~8 KB. Receiving any batch clears lastPullMs so the next pull fires immediately —
                    // multi-batch output streams at full speed (one pull per delivered batch) and only a true
                    // stall is rate-limited, purely to avoid emitting frames nobody will answer.
                    // ...unless the carrier is still waiting for the router to take what we already sent.
                    // A pull is speculative traffic, and on a cumulatively acknowledged carrier nothing sent
                    // past an unacknowledged packet can be processed until that packet lands — so pulling
                    // through a stall only piles bytes up behind the hole (~2.4 KB in 24 packets, measured)
                    // and buries the one packet that has to get through. Suppression ends the moment the ACK
                    // arrives, and no pull is lost: lastPullMs is not advanced, so the next one fires at
                    // once. False on carriers that cannot tell.
                    if (!settle.AtPrompt && !_session.SendStalled
                        && (lastPullMs < 0 || sw.ElapsedMilliseconds - lastPullMs >= PullIntervalMs))
                    {
                        SendPull();
                        lastPullMs = sw.ElapsedMilliseconds;
                    }
                    await Task.Delay(PollSleepMs).ConfigureAwait(false);
                }

                if (gotData)
                    lastPullMs = -1;   // output is flowing — allow the next batch-pull immediately

                // Nothing has come back AND the carrier says the router never took the bytes, even after the
                // retransmits ran out. Spending the rest of ReceiveTimeoutMs would only turn an answer we
                // already have into "nothing was received within 30000 ms" — a message that blames the read
                // for what the session did. Safe to call the command un-run: see
                // TikConnectionSessionClosedException. On carriers that cannot tell, SendAbandoned is false
                // and this costs a field read per poll.
                if (sb.Length == 0 && _session.SendAbandoned)
                    throw SessionClosed(sentCommand);

                string stripped = VtStripper.StripAnsi(sb.ToString());
                streamer.Feed(stripped);

                // The read ends when the prompt is still last after the settle window (CliPromptSettle). RouterOS
                // writes nothing after a command's completion prompt over mepty (Docs/findings-cli.md §4); a Safe
                // Mode key's pushed status line ends at a prompt again, so it only restarts the window.
                // Asked before the close is: a counted answer whose count line and prompt came in the frame that closed
                // the terminal is whole, and the close is the next command's to report (D2 of the terminal clients
                // review, as on the PTY).
                switch (settle.Observe(stripped, gotData, sw.ElapsedMilliseconds, sb.Length))
                {
                    case Cli.CliPromptSettle.Outcome.Complete:
                    case Cli.CliPromptSettle.Outcome.Settled:
                        return stripped;
                }

                // The router closed the terminal: no prompt will follow. The WinBox relay is M2, not a CLI relay, so
                // there is no RoMON target to report.
                if (_terminalClosed && !closedBefore)
                    return settle.EndWithoutPrompt(_transportName, null, closed: true, sw.ElapsedMilliseconds,
                        ReceiveTimeoutMs, stripped, sb.Length);
            }

            return settle.EndWithoutPrompt(_transportName, null, closed: false, sw.ElapsedMilliseconds,
                ReceiveTimeoutMs, VtStripper.StripAnsi(sb.ToString()), sb.Length);
        }

        /// <summary>
        /// The deadline for completing a frame that has started arriving: what remains of the command's receive
        /// deadline, never less than <see cref="MinFrameBudgetMs"/> so a frame begun just before the deadline
        /// can still finish.
        /// </summary>
        private int RemainingFrameBudget(Stopwatch sw)
            => (int)Math.Max(MinFrameBudgetMs, ReceiveTimeoutMs - sw.ElapsedMilliseconds);

        private const int MinFrameBudgetMs = 1000;

        /// <summary>
        /// Accumulates the terminal reaction until the channel stays quiet for <paramref name="quietMs"/>
        /// after at least some data (or the receive deadline expires), answering VT100 probes. Returns the
        /// text, escape sequences included. Used for Tab-completion (see <see cref="SendRawAndReadUntilQuietAsync"/>).
        /// </summary>
        /// <param name="quietMs">How long nothing new must arrive.</param>
        /// <param name="ct">Checked on every poll and cancels the wait between polls, so a caller's cancel is not held
        /// up by the quiet window (as on the other two engines). A frame already arriving is still read whole: a receive
        /// abandoned mid-frame leaves the encrypted stream unreadable.</param>
        private async Task<string> ReadUntilQuietAsync(int quietMs, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var quiet = new Cli.CliQuietWindow(quietMs);

            while (sw.ElapsedMilliseconds < ReceiveTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                if (_session.DataAvailable)
                {
                    byte[]? chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { break; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        _vt100.ProcessAndAnswer(text, _passwordPrompt, SendText);
                    }
                }
                else
                {
                    await Task.Delay(PollSleepMs, ct).ConfigureAwait(false);
                }

                if (quiet.Observe(sb.Length) != Cli.CliQuietWindow.Outcome.Continue)
                    break;
            }

            return sb.ToString();
        }

        /// <summary>Consumes residual frames until the channel stays quiet for <paramref name="quietMs"/>.</summary>
        private async Task DrainAsync(int quietMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!_session.DataAvailable) { await Task.Delay(PollSleepMs).ConfigureAwait(false); continue; }
                try
                {
                    byte[]? chunk = ReceiveTerminalChunk(FrameTimeoutMs);
                    if (chunk != null)
                        _vt100.ProcessAndAnswer(_encoding.GetString(chunk), _passwordPrompt, SendText);
                }
                catch (IOException) { break; }
                deadline = DateTime.UtcNow.AddMilliseconds(quietMs);  // keep draining while data flows
            }
        }
    }
}
