using System;
using System.Diagnostics;
using System.IO;
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
    internal sealed class WinboxCliClient : IDisposable
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

        // Very wide terminal — prevents line-wrapping of long ':put … as-value' records. RouterOS probes
        // width with 'ESC[9999C ESC[6n', so the cursor reply caps near 10000 columns; the width here must
        // exceed that so the full width is advertised (see findings-mactelnet.md / chapter E).
        private readonly Vt100State _vt100 = new Vt100State(65535, 25);
        private readonly IWinboxM2Channel _session;
        private readonly Encoding _encoding;
        private readonly int _receiveTimeoutMs;
        private readonly int _loginTimeoutMs;

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

        // How many times we answer the change-password nag with Ctrl-C before giving up. Bounded so a
        // terminal stuck on that prompt fails loudly instead of being fed input indefinitely.
        private const int MaxNagRounds = 3;

        internal WinboxCliClient(IWinboxM2Channel channel, Encoding encoding, int receiveTimeoutMs, int loginTimeoutMs)
        {
            _session          = channel ?? throw new ArgumentNullException(nameof(channel));
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
            _loginTimeoutMs   = loginTimeoutMs > 0 ? loginTimeoutMs : receiveTimeoutMs;
        }

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
        /// </remarks>
        internal Task LoginAsync(string host, int port, string user, string pass, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                _session.Open(host, port, user, pass, _loginTimeoutMs, _receiveTimeoutMs);

                // Open one mepty terminal and keep it for the whole connection. The password is supplied
                // here (not via a Login:/Password: prompt) — auth already happened at the M2 layer, so the
                // session starts directly at the RouterOS shell (or the change-password nag). The login
                // size hint stays at the PoC-proven 80x25 (RouterOS rejects oversized values with an
                // error response carrying no SESSION_ID); the real width comes from the VT100 cursor-probe
                // answered by the wide _vt100 below.
                _sessionId = OpenTerminalSession(pass, "vt102", 80, 25);
                SendTerminalReady(_sessionId);

                WaitForPromptSync();
                await DrainAsync(250).ConfigureAwait(false);

                // From here the terminal is idle between commands, and a RouterOS terminal is written to
                // unprompted. On a carrier that expects each write to be acknowledged, leaving it unread is
                // what kills the session (P2.55) — so hand the channel the job of servicing itself. TCP has
                // nothing to do here.
                _session.StartIdleServicing();
            }, ct);
        }

        // ── Command execution ─────────────────────────────────────────────────

        internal Task<string> SendCommandAndReadAsync(string command, CancellationToken ct)
            => SendCommandAndReadAsync(command, null, ct);

        /// <summary>
        /// As <see cref="SendCommandAndReadAsync(string,CancellationToken)"/>, but also reports each
        /// completed output line to <paramref name="onLine"/> while the command is still running — the
        /// streaming driver registered by the WinBox-CLI connections.
        /// </summary>
        internal async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Discard any residual frames still buffered from the PREVIOUS command before issuing
            // this one. A late prompt-repaint or VT100 probe-answer that arrives after the prior
            // read returned would otherwise be consumed first here and either mistaken for this
            // command's completion (an early, empty result) or desync a frame boundary (a full
            // receive-timeout hang). Gated on DataAvailable so the normal, clean path pays nothing;
            // the login drains for the same reason after the initial prompt.
            if (_session.DataAvailable)
                await DrainAsync(SettleMs).ConfigureAwait(false);

            string cmd = CliOutputHelper.InjectWithoutPaging(command);
            SendInput(_encoding.GetBytes(cmd + "\r"));
            string raw = await ReadCommandResponseAsync(cmd, onLine).ConfigureAwait(false);
            return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no carriage return, no paging injection) and
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode.
        /// </summary>
        internal async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SendInput(raw);
            // null → tolerant; a control key need not be answered with a prompt.
            return VtStripper.StripAnsi(await ReadCommandResponseAsync(null).ConfigureAwait(false));
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for Tab-completion) and reads the reaction until
        /// the terminal goes quiet for <paramref name="quietMs"/> — the completion listing does not end in a
        /// shell prompt (RouterOS redraws the prompt with the echoed stem), so it must be read on a settle
        /// window rather than a prompt match. ANSI-stripped.
        /// </summary>
        internal async Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SendInput(raw);
            return await ReadUntilQuietAsync(quietMs).ConfigureAwait(false);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <summary>Asks RouterOS to leave the console (<c>/quit</c>); errors are ignored.</summary>
        internal void TryCloseSession()
        {
            try { if (_sessionId >= 0) SendInput(_encoding.GetBytes("/quit\r")); } catch { /* ignore */ }
        }

        public void Dispose() => _session.Dispose();

        // ── mepty message building (handler [76]) ─────────────────────────────

        private int OpenTerminalSession(string password, string terminalType, int cols, int rows)
        {
            if (!_session.IsEncrypted)
                throw new NotSupportedException(
                    "WinBox terminal (mepty) is only supported over the encrypted EC-SRP5 channel " +
                    "(RouterOS 6.43+). The legacy MD5 path does not carry an encrypted terminal session.");

            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                _session.NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Login),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Password, password),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Input, terminalType),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Cols, cols),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Rows, rows));
            byte[] resp = _session.SendReceive(msg, FrameTimeoutMs);
            return M2Message.ParseSessionId(resp);
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
                TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Send, "PULL ack=" + _ackBytes);
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
                TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Send, keystrokes, 0, keystrokes.Length,
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
                    TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Note,
                        "dropped frame from retired session " + frameSession + " (current " + _sessionId + ")");
                return null;
            }

            byte[]? payload = M2Message.ParseUserBytes(resp, WinboxM2Protocol.Mepty.Key.Input);

            // Acknowledge what we consumed — every subsequent Data frame reports this total and that is what
            // lets RouterOS release the next window of output. See _ackBytes.
            if (payload != null) _ackBytes += payload.Length;

            if (payload != null && TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Recv, payload, 0, payload.Length);

            return payload;
        }

        // ── Synchronous terminal loops ────────────────────────────────────────

        /// <summary>
        /// Waits until the RouterOS shell prompt appears, answering VT100 cursor-probe negotiation and
        /// dismissing the change-password nag with Ctrl-C.
        /// </summary>
        private void WaitForPromptSync()
        {
            var sb = new StringBuilder();
            int nagRounds = 0;
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < _loginTimeoutMs)
            {
                if (!_session.DataAvailable) { Thread.Sleep(PollSleepMs); continue; }

                byte[]? chunk;
                try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                catch (IOException) { break; }
                if (chunk == null) continue;

                string text = _encoding.GetString(chunk);
                sb.Append(text);
                string stripped = VtStripper.StripAnsi(sb.ToString());

                // The cursor model is fed unconditionally (Process is eager; only sending the replies is
                // I/O), but the replies are held back while the change-password prompt is on screen. A VT100
                // probe answer is a keystroke like any other, and typed into 'new password>' /
                // 'repeat new password>' it becomes password input — two matching entries (the same cursor
                // report answered twice, say) would silently change the router's admin password. While that
                // prompt is up the ONLY byte we ever send is the Ctrl-C that skips it. Self-clearing: the
                // buffer is reset with the nag, so replies resume as soon as the prompt is gone.
                bool atPasswordPrompt = RouterOsCliLogin.IsChangePasswordNag(stripped);
                var replies = _vt100.Process(text);
                if (!atPasswordPrompt)
                {
                    foreach (string reply in replies)
                        SendInput(_encoding.GetBytes(reply));
                }
                else
                {
                    if (nagRounds++ >= MaxNagRounds)
                        throw new TimeoutException(
                            "WinBox: RouterOS keeps prompting for a new password; refusing to send anything " +
                            "further to the password prompt.");
                    SendInput(new byte[] { 0x03 });  // Ctrl-C — skip the change
                    sb.Clear();
                    continue;
                }

                if (RouterOsCliLogin.IsShellPrompt(stripped))
                    return;
            }

            throw new TimeoutException("WinBox: timed out waiting for shell prompt.");
        }

        private static TikConnectionSessionClosedException SessionClosed(string? sentCommand)
            => new TikConnectionSessionClosedException(
                "WinBox CLI: the router stopped acknowledging this session — it did not take the bytes of "
                // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
                + (string.IsNullOrEmpty(sentCommand) ? "the last request" : "'" + sentCommand!.Trim() + "'")
                + ", so the command did not run. The MAC-layer carrier keeps the UDP socket open and RouterOS "
                + "sends no error when it drops a console session, so the session goes silent rather than "
                + "reporting anything.");

        /// <summary>
        /// Reads a command response, requiring the prompt to be stable for <see cref="SettleMs"/>
        /// before returning (the line-editor repaints the prompt, so a single prompt sighting is not
        /// proof the output is complete) — and to have been preceded by the command's own echo
        /// (<see cref="Cli.CliOutputHelper.ContainsEcho"/>), so that a prompt left behind by the PREVIOUS
        /// response cannot end this read before the router has said anything.
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
        private async Task<string> ReadCommandResponseAsync(string? sentCommand, Action<string>? onLine = null)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            DateTime? settleUntil = null;
            bool prompted = false;
            bool echoSeen = false;   // latched: see CliOutputHelper.ContainsEcho
            var streamer = new Cli.CliLineStreamer(onLine);
            long lastPullMs = -1;   // -1 = a pull is due now (fire immediately)

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                bool gotData = false;
                if (_session.DataAvailable)
                {
                    byte[]? chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { break; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                        gotData = true;
                    }
                }
                else
                {
                    // mepty delivers output one batch per Data frame and will not push the remainder of a
                    // large response on its own — so, until the completion prompt has appeared, keep pulling
                    // (empty Data) whenever nothing is buffered. RouterOS does NOT answer every pull (a pull
                    // sent while nothing is pending simply yields no frame), so we cannot block waiting for a
                    // pull to be acknowledged before sending the next — we must keep firing on idle. The pull
                    // also carries the byte acknowledgement that reopens RouterOS's send window (see
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
                    // once. False on carriers that cannot tell (P2.56).
                    if (!prompted && !_session.SendStalled
                        && (lastPullMs < 0 || sw.ElapsedMilliseconds - lastPullMs >= PullIntervalMs))
                    {
                        SendPull();
                        lastPullMs = sw.ElapsedMilliseconds;
                    }
                    await Task.Delay(PollSleepMs).ConfigureAwait(false);
                }

                if (gotData)
                {
                    settleUntil = null;
                    lastPullMs  = -1;   // output is flowing — allow the next batch-pull immediately
                }

                // Nothing has come back AND the carrier says the router never took the bytes, even after the
                // retransmits ran out. Spending the rest of _receiveTimeoutMs would only turn an answer we
                // already have into "nothing was received within 30000 ms" — a message that blames the read
                // for what the session did (P2.54). Safe to call the command un-run: see
                // TikConnectionSessionClosedException. On carriers that cannot tell, SendAbandoned is false
                // and this costs a field read per poll.
                if (sb.Length == 0 && _session.SendAbandoned)
                    throw SessionClosed(sentCommand);

                string stripped = VtStripper.StripAnsi(sb.ToString());
                streamer.Feed(stripped);
                if (!echoSeen)
                    echoSeen = Cli.CliOutputHelper.ContainsEcho(stripped, sentCommand);

                if (!prompted && echoSeen && RouterOsCliLogin.IsShellPrompt(stripped))
                {
                    if (TikWireTrace.Enabled)
                        TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Note,
                            "prompt seen @" + sw.ElapsedMilliseconds + "ms (bytes=" + sb.Length + ")");
                    prompted = true;
                }

                // Once the completion prompt has been seen, the command is done and we return as soon as the
                // terminal has been quiet for SettleMs (any arriving batch resets the window above). We
                // deliberately do NOT re-require the buffer to still *end* at a prompt: RouterOS may append
                // something afterwards — a repaint, or the asynchronous output of an action verb such as
                // /system/script/run that surfaces after the command returned — and requiring the prompt to
                // remain the last thing on screen turns any such trailer into a full receive-timeout hang
                // (P2.13c).
                if (prompted)
                {
                    if (settleUntil == null)
                        settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                    else if (DateTime.UtcNow >= settleUntil.Value)
                    {
                        if (TikWireTrace.Enabled)
                            TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Note,
                                "settled -> return @" + sw.ElapsedMilliseconds + "ms");
                        return stripped;
                    }
                }
            }

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxcli.mepty", TikWireDir.Note,
                    "TIMEOUT @" + sw.ElapsedMilliseconds + "ms (prompted=" + prompted + ", bytes=" + sb.Length + ")");

            string strippedSoFar = VtStripper.StripAnsi(sb.ToString());
            if (sentCommand != null)
                throw Cli.CliReadTimeout.Create("WinBox CLI", _receiveTimeoutMs, sentCommand, strippedSoFar);
            return strippedSoFar;
        }

        /// <summary>
        /// Accumulates the terminal reaction until the channel stays quiet for <paramref name="quietMs"/>
        /// after at least some data (or the receive deadline expires), answering VT100 probes. Returns the
        /// ANSI-stripped text. Used for Tab-completion (see <see cref="SendRawAndReadUntilQuietAsync"/>).
        /// </summary>
        private async Task<string> ReadUntilQuietAsync(int quietMs)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            DateTime lastData = DateTime.UtcNow;
            bool any = false;

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                bool gotData = false;
                if (_session.DataAvailable)
                {
                    byte[]? chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { break; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                        gotData = true;
                        any = true;
                    }
                }
                else
                {
                    await Task.Delay(PollSleepMs).ConfigureAwait(false);
                }

                if (gotData)
                    lastData = DateTime.UtcNow;
                else if (any && (DateTime.UtcNow - lastData).TotalMilliseconds >= quietMs)
                    break;
            }

            return VtStripper.StripAnsi(sb.ToString());
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
                    {
                        string text = _encoding.GetString(chunk);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                    }
                }
                catch (IOException) { break; }
                deadline = DateTime.UtcNow.AddMilliseconds(quietMs);  // keep draining while data flows
            }
        }
    }
}
