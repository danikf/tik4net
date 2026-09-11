using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// MAC-Telnet terminal client (UDP 20561, client_type=0x0015).
    /// After EC-SRP5 auth the DATA packets carry raw VT100 bytes (unencrypted).
    /// </summary>
    /// <remarks>
    /// A MAC-Telnet session is a <em>terminal</em>, not a request/reply channel: RouterOS writes to it on
    /// its own initiative (the shipped logging rules echo <c>critical</c> topics to the console, so an
    /// unrelated event anywhere on the router lands here), and it drops the session when that output is
    /// left unacknowledged. So the socket is serviced continuously by a background pump from login until
    /// close — see <see cref="StartPump"/> — and the command reads consume what the pump has accumulated
    /// rather than receiving from the socket themselves.
    /// </remarks>
    internal sealed class MacTelnetUdpClient : MacLayerTransport
    {
        private const ushort CLIENT_TYPE = 0x0015;
        private const int    SettleMs    = 150;
        // Poll interval for UdpClient.Receive timeout (ms). Matches PoC.
        private const int    PollMs      = 500;
        // How long the pump blocks on one receive. Short so that stopping it is prompt and so that an idle
        // session still gets its RetransmitIfUnacked tick; the cost when nothing arrives is one syscall.
        private const int    PumpPollMs  = 200;
        // How long a waiting reader blocks for the pump to signal new terminal text.
        private const int    ReadWaitMs  = 25;

        /// <inheritdoc/>
        protected override string WireTraceChannel => "mactelnet.udp";

        // Answers RouterOS VT100 cursor-probe negotiation (shared PTY logic). Without truthful
        // cursor replies RouterOS assumes a 1x1 terminal and emits no command output; the width the
        // shared state advertises is what keeps RouterOS from wrapping long ':put' as-value records
        // into the data (Vt100State.RouterOsWidth explains why it is a reachable column, not a huge one).
        private readonly Vt100State     _vt100 = Vt100State.ForRouterOs();
        private readonly Encoding       _encoding;
        private readonly int            _receiveTimeoutMs;
        private readonly int            _loginTimeoutMs;

        // ── Receive pump state ───────────────────────────────────────────────
        private readonly object                _rxLock   = new object();
        private readonly StringBuilder         _rx       = new StringBuilder();
        // Awaitable rather than a ManualResetEventSlim: this is what a command read spends its whole
        // duration waiting on, and blocking on it meant every read occupied a thread-pool thread for as long
        // as the router took to answer (P2.5 / A6). See AsyncSignal.
        private readonly AsyncSignal           _rxSignal = new AsyncSignal();
        private Thread?           _pump;
        private volatile bool     _pumpStop;
        private volatile Exception? _pumpFault;

        // ── Why there is no keepalive here ────────────────────────────────────
        //
        // RouterOS logs a MAC-Telnet console out after roughly 30 s of idle - the router says so itself
        // ("user admin logged out from <mac> via mac-telnet, outcome=success", timestamped to the exact
        // moment the session stops answering). Four ways of holding it open were measured on 7.23.2 and
        // ALL FOUR made things worse, so do not re-derive them:
        //
        //   * an unsolicited ACK of our own                 - no effect, session still lost at 30 s
        //   * a client-initiated PING                       - never answered; the router pings us, not
        //                                                     the reverse, and the session still dies
        //   * an empty DATA packet                          - no effect
        //   * answering the router's own 10 s ACK probe,    - ACTIVELY HARMFUL: with the probe answered,
        //     and/or typing a periodic CR or NUL              a session that was serving a real command
        //                                                     every 10 s died after ~50 s (probe answer)
        //                                                     or inside the first 10 s (CR). Without
        //                                                     either, the same poll runs 12/12 clean over
        //                                                     120 s.
        //
        // So the pump deliberately only ACKs what the router SENDS and never speaks unprompted. A session
        // left idle past the router's window is gone, and that is the router's contract, not a defect we
        // can paper over from here - it has to be handled by reconnecting, not by inventing traffic.

        internal MacTelnetUdpClient(Encoding encoding, int receiveTimeoutMs, int loginTimeoutMs, string? routerMac)
        {
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
            _loginTimeoutMs   = loginTimeoutMs > 0 ? loginTimeoutMs : receiveTimeoutMs;
            // Opening the session is part of opening the connection, so it gets the connect budget.
            SessionStartBudgetMs = _loginTimeoutMs;
            RouterMacOverride = routerMac;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the session: MAC-layer connect, EC-SRP5 authentication, the VT100 startup exchange, and
        /// then the pump that owns the socket from here on.
        /// </summary>
        /// <remarks>
        /// The one place on this transport that is a <c>Task.Run</c> façade, and deliberately so.
        /// Everything inside runs before the pump exists, so it is the only reader of the socket and
        /// reads it with <c>SO_RCVTIMEO</c> — mixing that with <c>ReceiveAsync</c> is what made the timeout
        /// be ignored on .NET Framework 4.8, and the EC-SRP5 handshake underneath is synchronous down
        /// through the crypto. Open is once per connection and is not the operation the
        /// <see cref="TikConnectionCapability.AsyncCommands"/> flag is about; the commands after it, which
        /// are, hold no thread at all.
        /// </remarks>
        internal Task LoginAsync(string host, string user, string pass, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                BaseConnect(host, CLIENT_TYPE);
                Authenticate(user, pass);       // EC-SRP5 auth (sync, in base class)
                WaitForPromptSync();
                DrainSync(250);
                StartPump();                    // from here on nothing else may receive on this socket
            }, ct);
        }

        // ── Command execution ─────────────────────────────────────────────────

        internal Task<string> SendCommandAndReadAsync(string command, CancellationToken ct)
            => SendCommandAndReadAsync(command, null, ct);

        /// <summary>
        /// As <see cref="SendCommandAndReadAsync(string,CancellationToken)"/>, but also reports each
        /// completed output line to <paramref name="onLine"/> while the command is still running — the
        /// streaming driver registered by <see cref="MacTelnetConnection"/>.
        /// </summary>
        internal async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            // No Task.Run: the pump owns the socket, so nothing here blocks. The send is a UDP datagram plus
            // its retransmit bookkeeping (MacLayerTransport.Send) and the read only waits for the pump to
            // report progress — awaiting that holds no thread, which is the whole of A6 on this transport.
            ct.ThrowIfCancellationRequested();
            string cmd = CliOutputHelper.InjectWithoutPaging(command);
            ResetReadBuffer();
            ResetDataHoles();
            SendTerminalBytes(_encoding.GetBytes(cmd + "\r"));
            string raw = await ReadCommandResponseAsync(cmd, ct, onLine).ConfigureAwait(false);
            return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd);
        }

        /// <summary>
        /// Refuses a response that arrived across a retransmission episode. The response itself looks
        /// perfect — it ends at a real prompt and its byte counter is unbroken — so the packet loss is the
        /// only evidence there is that RouterOS may have dropped part of its own output while recovering.
        /// See <see cref="TikConnectionResponseIncompleteException"/> for the measurement behind that.
        /// <para>Called by <see cref="MacTelnetConnection"/> for every command <b>except a print</b>. A print
        /// carries the router's own count of its records and is checked against it exactly
        /// (<see cref="CliConnectionBase"/>), and this heuristic condemns complete answers now and then —
        /// measured, a 10-row slice tripped it while the paged read it belonged to returned every row.</para>
        /// </summary>
        internal void ThrowIfResponseLostDatagrams(string sentCommand, string response)
        {
            // One wide gap is not enough to condemn an answer. The router refills a single gap with one
            // retransmit and carries on; what precedes the output-dropping is a SUSTAINED episode — we stay
            // behind, the router keeps streaming, and its queue is what it eventually discards. The measured
            // populations do not overlap: the reads that came back short had 4, 29, 30 and 31 wide gaps,
            // while every intact read on the same suite leg had at most one.
            int lost = DataHoles;
            if (lost <= 1)
                return;

            throw new TikConnectionResponseIncompleteException(
                "MAC-Telnet: the router ran a backlog past this client " + lost + " times while answering '"
                + sentCommand.Trim() + "' — each a gap of more than one datagram, and a sustained run of them is "
                + "what RouterOS answers by "
                + "discarding part of its own output and carrying on with an unbroken byte counter. The "
                + response.Length + " characters received therefore end at a prompt and still cannot be "
                + "trusted to be the whole answer. Retry the command, or use a transport that is not on the "
                + "MAC layer for a response this large.",
                lost, response);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no carriage return, no paging injection) and
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode.
        /// </summary>
        internal async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ResetReadBuffer();
            SendTerminalBytes(raw);
            // null -> tolerant; a control key need not be answered with a prompt.
            return VtStripper.StripAnsi(await ReadCommandResponseAsync(null, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for Tab-completion) and reads the reaction until
        /// the socket goes quiet for <paramref name="quietMs"/> — the completion listing does not end in a
        /// shell prompt (RouterOS redraws the prompt with the echoed stem), so it must be read on a settle
        /// window rather than a prompt match. ANSI-stripped.
        /// </summary>
        internal async Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ResetReadBuffer();
            SendTerminalBytes(raw);
            return await ReadUntilQuietAsync(quietMs).ConfigureAwait(false);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a graceful session-end sequence: first asks RouterOS to exit the console
        /// (<c>/quit</c>), then sends the MAC-layer <c>PKT_END</c>. Errors are ignored.
        /// </summary>
        internal void TryCloseSession()
        {
            StopPump();
            try { Send(PKT_DATA, _encoding.GetBytes("/quit\r")); } catch { /* ignore */ }
            try { Send(PKT_END, null); } catch { /* ignore */ }
        }

        /// <inheritdoc/>
        protected override void OnDisposing() => StopPump();

        // ── Receive pump ──────────────────────────────────────────────────────

        /// <summary>
        /// Starts the background receive pump. From here until <see cref="StopPump"/> the pump is the only
        /// reader of the socket.
        /// <para>
        /// It exists because leaving the socket unread is not merely untidy on this transport — it kills
        /// the session. RouterOS pushes console output whenever it feels like it and expects the byte ACK
        /// back; measured on 7.23.2, one console-echoed log line left unacknowledged during a 15 s idle is
        /// enough for the router to stop answering the session permanently, and a plain 30 s idle does the
        /// same on its own (25 s still survives). Both are unremarkable gaps inside a test run, which is
        /// why the wedge looked like it needed "accumulated state" to reproduce.
        /// </para>
        /// </summary>
        private void StartPump()
        {
            _pumpStop = false;
            _pump = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "tik4net MAC-Telnet pump",
            };
            _pump.Start();
        }

        private void StopPump()
        {
            var pump = _pump;
            if (pump == null)
                return;
            _pump = null;
            _pumpStop = true;
            try { pump.Join(2000); } catch { /* ignore */ }
        }

        private void PumpLoop()
        {
            try
            {
                while (!_pumpStop)
                {
                    _udp.Client.ReceiveTimeout = PumpPollMs;
                    try
                    {
                        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] pkt = _udp.Receive(ref ep);
                        if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                            continue;

                        if (type == PKT_ACK)  { NoteAck(counter);  continue; }
                        if (type == PKT_PING) { SendPong(counter); continue; }
                        if (type != PKT_DATA) continue;

                        if (!AckData(counter, payload.Length)) continue;   // duplicate or out-of-order
                        if (IsControlPacket(payload))          continue;

                        string text = _encoding.GetString(payload);
                        lock (_rxLock)
                            _rx.Append(text);
                        _rxSignal.Set();

                        // Answer RouterOS cursor-probe negotiation immediately. This path is timing
                        // sensitive: if the cursor-position reply is delayed (e.g. by per-packet logging),
                        // RouterOS falls back to a tiny terminal width and wraps its output, breaking
                        // prompt detection. Keep this loop free of expensive work.
                        foreach (string reply in _vt100.Process(text))
                            SendTerminalBytes(_encoding.GetBytes(reply));
                    }
                    catch (SocketException)
                    {
                        // Poll timeout: nothing arrived. If the router has not acknowledged what we sent,
                        // this silence is our own lost datagram — resend it rather than wait out the full
                        // receive deadline for an answer to a command that never landed (P2.19).
                        RetransmitIfUnacked();
                    }
                }
            }
            catch (ObjectDisposedException) { /* socket closed underneath us — normal shutdown */ }
            catch (Exception ex)
            {
                // Never swallow: a dead pump means every later read times out with no data, which is
                // exactly the symptom that has to point at its own cause rather than at the parser.
                _pumpFault = ex;
                _rxSignal.Set();
            }
        }

        // ── Reading what the pump accumulated ─────────────────────────────────

        /// <summary>
        /// Discards terminal text the pump collected before this command was issued. The pump ACKs router
        /// output as it arrives (which is what keeps the session alive), but that text is not an answer to
        /// anything we asked: it typically ends in a repainted prompt, and a read that accepted it would
        /// return it as the next command's result. Every terminal transport has to guard this.
        /// </summary>
        private void ResetReadBuffer()
        {
            lock (_rxLock)
                _rx.Clear();
            _rxSignal.Reset();
        }

        private static TikConnectionSessionClosedException SessionClosed(string? sentCommand)
            => new TikConnectionSessionClosedException(
                "MAC-Telnet: the router stopped acknowledging this session — it did not take the bytes of "
                // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
                + (string.IsNullOrEmpty(sentCommand) ? "the last request" : "'" + sentCommand!.Trim() + "'")
                + ", so the command did not run. RouterOS logs an idle MAC-Telnet console out after about "
                + "30 s (its own log records it as a normal logout), and the UDP socket stays open, so the "
                + "session goes silent rather than reporting an error.");

        // The pump's failure is rethrown as-is, with its original stack. Inventing a wrapper type here
        // would trade the one thing that identifies the fault (a SocketException, say, with its error
        // code) for a generic "transport failed", and a read that then reports "nothing was received"
        // would be blaming the router for our own dead thread.
        private void ThrowIfPumpFaulted()
        {
            var fault = _pumpFault;
            if (fault != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
        }

        /// <summary>
        /// Reads a command response, requiring the prompt to be stable for
        /// <see cref="SettleMs"/> before returning — and to have been preceded by the command's own echo
        /// (<see cref="CliOutputHelper.ContainsEcho"/>), so that a prompt left behind by the PREVIOUS
        /// response cannot end this read before the router has said anything.
        /// </summary>
        /// <param name="sentCommand">
        /// The command being answered. When non-null, reaching the deadline without a prompt throws
        /// <see cref="TikConnectionReceiveTimeoutException"/> instead of returning the partial text — see
        /// <see cref="CliReadTimeout"/>. <c>null</c> for control keys, which need not end at a prompt.
        /// </param>
        /// <param name="onLine">
        /// Optional: called with each completed line as it arrives, so a long-running command can be
        /// consumed while it runs (see <see cref="CliLineStreamer"/>). Does not affect when the read
        /// returns — the stable prompt is still the only terminator.
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DateTime? settleUntil = null;
            int lastLength = -1;
            var streamer = new CliLineStreamer(onLine);
            bool echoSeen = false;   // latched: see CliOutputHelper.ContainsEcho

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                ThrowIfPumpFaulted();
                _rxSignal.Reset();

                string stripped = Snapshot(out int length);
                streamer.Feed(stripped);
                if (length != lastLength)
                {
                    lastLength  = length;
                    settleUntil = null;
                }

                // Nothing has come back AND the router never took the bytes, even after the pump ran the
                // retransmits out. Waiting the rest of the deadline would only turn a known answer into a
                // vague one — see TikConnectionSessionClosedException for why this is safe to call.
                if (length == 0 && LastSendAbandoned)
                    throw SessionClosed(sentCommand);

                if (!echoSeen)
                    echoSeen = CliOutputHelper.ContainsEcho(stripped, sentCommand);

                if (echoSeen && RouterOsCliLogin.IsShellPrompt(stripped))
                {
                    if (settleUntil == null)
                        settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                    else if (DateTime.UtcNow >= settleUntil.Value)
                        return stripped;
                }

                await _rxSignal.WaitAsync(ReadWaitMs).ConfigureAwait(false);
            }

            string strippedSoFar = Snapshot(out _);
            if (sentCommand != null)
                throw CliReadTimeout.Create("MAC-Telnet", _receiveTimeoutMs, sentCommand, strippedSoFar);
            return strippedSoFar;
        }

        /// <summary>
        /// Accumulates the terminal reaction until it stays quiet for <paramref name="quietMs"/> after at
        /// least some data (or the receive deadline expires). Returns the ANSI-stripped text.
        /// Used for Tab-completion (see <see cref="SendRawAndReadUntilQuietAsync"/>).
        /// </summary>
        private async Task<string> ReadUntilQuietAsync(int quietMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DateTime lastData = DateTime.UtcNow;
            int lastLength = 0;

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                ThrowIfPumpFaulted();
                _rxSignal.Reset();

                Snapshot(out int length);
                if (length != lastLength)
                {
                    lastLength = length;
                    lastData   = DateTime.UtcNow;
                }
                else if (lastLength > 0 && (DateTime.UtcNow - lastData).TotalMilliseconds >= quietMs)
                    break;

                await _rxSignal.WaitAsync(ReadWaitMs).ConfigureAwait(false);
            }

            return Snapshot(out _);
        }

        // Current buffer, ANSI-stripped, plus its raw length (the length is what "did anything new
        // arrive" is decided on — the stripped text can stay identical while escapes accumulate).
        private string Snapshot(out int rawLength)
        {
            string raw;
            lock (_rxLock)
            {
                rawLength = _rx.Length;
                raw       = _rx.ToString();
            }
            return VtStripper.StripAnsi(raw);
        }

        // ── Synchronous terminal I/O used during login (before the pump starts) ─

        private void SendTerminalBytes(byte[] data) => Send(PKT_DATA, data);

        /// <summary>
        /// Waits until the RouterOS shell prompt appears (post-auth VT100 startup).
        /// Handles the change-password nag and VT100 cursor-probe replies.
        /// Pure synchronous socket receive with UdpClient.ReceiveTimeout — no ReceiveAsync.
        /// </summary>
        private void WaitForPromptSync()
        {
            var sb = new StringBuilder();
            bool nagSent = false;
            int  dataPackets = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < _loginTimeoutMs)
            {
                _udp.Client.ReceiveTimeout = PollMs;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                        continue;

                    if (type == PKT_ACK) { NoteAck(counter); continue; }
                    if (type == PKT_PING) { SendPong(counter); continue; }

                    // The router closing the session is an answer, and waiting out the rest of the login
                    // timeout after it cannot produce a better one. It sends PKT_END repeatedly, and on a
                    // refused login it sends the refusal text first — so whatever is already on screen is
                    // the reason, and it is worth more than the fact that the session is gone (P2.49).
                    if (type == PKT_END)
                        throw RefusalOrClosure(VtStripper.StripAnsi(sb.ToString()));

                    if (type != PKT_DATA) continue;

                    if (!AckData(counter, payload.Length)) continue;   // duplicate retransmit
                    if (IsControlPacket(payload))
                        continue;

                    dataPackets++;
                    string text = _encoding.GetString(payload);
                    sb.Append(text);

                    // See the pump loop for why the cursor-probe reply must not be delayed.
                    foreach (string reply in _vt100.Process(text))
                        SendTerminalBytes(_encoding.GetBytes(reply));

                    string stripped = VtStripper.StripAnsi(sb.ToString());

                    if (!nagSent && RouterOsCliLogin.IsChangePasswordNag(stripped))
                    {
                        SendTerminalBytes(new byte[] { 0x03 });  // Ctrl-C
                        nagSent = true;
                        sb.Clear();
                        continue;
                    }

                    // Checked before the prompt, and on every packet: the refusal is the last thing the
                    // router says before it stops talking, so nothing later will arrive to be examined.
                    if (IsLoginRefusal(stripped))
                        throw new TikConnectionLoginRefusedException("MAC-Telnet", FirstLine(stripped));

                    if (RouterOsCliLogin.IsShellPrompt(stripped))
                        return;
                }
                catch (SocketException) { RetransmitIfUnacked(); /* poll timeout — continue loop */ }
            }

            // What it was waiting for is only half the story; the other half is what it had already been
            // given, and without that the failure is indistinguishable between "the router said nothing at all"
            // (a lost SESSIONSTART or a dead MAC path) and "the router is mid-negotiation and one packet
            // went missing" (a stalled VT100 probe exchange). P2.49 spent nine reproduction runs on a
            // message that carried neither.
            string seen = VtStripper.StripAnsi(sb.ToString());
            if (seen.Length > 200)
                seen = seen.Substring(seen.Length - 200);
            throw new TimeoutException(
                "MAC-Telnet: timed out waiting for shell prompt after " + sw.ElapsedMilliseconds + " ms; "
                + dataPackets + " terminal packet(s) received, " + sb.Length + " char(s)"
                + (nagSent ? ", change-password nag dismissed" : "")
                + (seen.Length == 0 ? ", nothing on screen." : ", screen tail: " + seen.Replace("\r", "\\r").Replace("\n", "\\n")));
        }

        // RouterOS refuses a MAC-Telnet login with this exact line, on its own, AFTER the EC-SRP5 exchange
        // has ended in CTRL_END_AUTH. Matched narrowly rather than through RouterOsCliLogin.IsLoginFailure,
        // which also matches "login failure" — the wording of the router's own log lines, which the shipped
        // logging rules echo onto any console at any moment, including this one (see findings-cli.md §11).
        private static bool IsLoginRefusal(string stripped)
            => !string.IsNullOrEmpty(stripped)
            && stripped.IndexOf("Login failed", StringComparison.OrdinalIgnoreCase) >= 0;

        // Whatever the router said before it hung up: a refusal if it said one, otherwise the plain fact
        // that the session was closed during login.
        private Exception RefusalOrClosure(string stripped)
            => IsLoginRefusal(stripped)
             ? (Exception)new TikConnectionLoginRefusedException("MAC-Telnet", FirstLine(stripped))
             : new TikConnectionSessionClosedException(
                   "MAC-Telnet: the router closed the session during login"
                   + (string.IsNullOrEmpty(stripped.Trim()) ? "." : ": " + FirstLine(stripped)));

        private static string FirstLine(string s)
        {
            foreach (string line in s.Split('\r', '\n'))
                if (line.Trim().Length > 0) return line.Trim();
            return s.Trim();
        }

        /// <summary>
        /// Discards residual bytes until the socket stays quiet for <paramref name="quietMs"/>.
        /// Login only — once the pump is running it is the sole reader of the socket.
        /// </summary>
        private void DrainSync(int quietMs)
        {
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            _udp.Client.ReceiveTimeout = 50;
            while (DateTime.UtcNow < quietDeadline)
            {
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                        continue;
                    if (type == PKT_DATA)
                    {
                        bool fresh = AckData(counter, payload.Length);
                        if (fresh && !IsControlPacket(payload))
                        {
                            string text = _encoding.GetString(payload);
                            foreach (string reply in _vt100.Process(text))
                                SendTerminalBytes(_encoding.GetBytes(reply));
                        }
                    }
                    else if (type == PKT_PING) { SendPong(counter); }
                    // Reset quiet window — keep draining while data arrives.
                    quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
                }
                catch (SocketException) { /* quiet timeout — exit */ break; }
            }
        }

        // Parses raw datagram. Skips own-echo. Returns false if invalid or own echo.
        private bool TryParsePacket(byte[] pkt, out byte type, out uint counter, out byte[] payload)
        {
            type = 0; counter = 0; payload = new byte[0];
            if (pkt == null || pkt.Length < 22) return false;

            // Skip own echo (srcMac == _localMac)
            bool ownEcho = true;
            for (int i = 0; i < 6; i++)
                if (pkt[2 + i] != _localMac[i]) { ownEcho = false; break; }
            if (ownEcho) return false;

            type    = pkt[1];
            counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                      ((uint)pkt[20] <<  8) |  pkt[21];
            payload = pkt.Length > 22 ? new byte[pkt.Length - 22] : new byte[0];
            if (payload.Length > 0) Buffer.BlockCopy(pkt, 22, payload, 0, payload.Length);

            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Recv,
                    payload, 0, payload.Length,
                    "type=0x" + type.ToString("x2") + " counter=" + counter + TraceTag);

            return true;
        }
    }
}
