using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The RouterOS console the five CLI transports hold, over whichever carrier brings its text
    /// (<see cref="ICliConsoleCarrier"/>): the command prologue, the interrupt latch, the command read, the quiet read and
    /// the drain, written once. The carrier is polled; what differs between carriers is below the poll.
    /// </summary>
    /// <remarks>
    /// The read ends by the one rule (<see cref="CliPromptSettle"/>), asked before the carrier's end is: a counted answer
    /// whose count line and prompt arrived with the end is whole, and the end is the next command's to report (D2 of the
    /// terminal clients review). The Safe Mode quit wait stays each carrier's (<see cref="CliSafeModeQuitWait"/> over its own
    /// poll): what "ended" means there differs by carrier — the PTY reads without answering, MAC-Telnet counts an end from
    /// before the wait, the WinBox terminal falls back on quiet.
    /// </remarks>
    internal sealed class CliConsole : ICliTerminal
    {
        private readonly ICliConsoleCarrier _carrier;
        private readonly CliInterruptLatch _interrupt = new CliInterruptLatch();

        internal CliConsole(ICliConsoleCarrier carrier)
        {
            _carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
        }

        /// <summary>The carrier under this console (MAC-Telnet's connection vets an answer against its pump's count).</summary>
        internal ICliConsoleCarrier Carrier => _carrier;

        private string Transport => _carrier.TransportName;

        private int ReceiveTimeoutMs => _carrier.ReceiveTimeoutMs;

        // ── ICliTerminal ──────────────────────────────────────────────────────

        public bool SupportsQuietRead => true;

        public bool SupportsLineStreaming => true;

        /// <inheritdoc cref="ICliTerminal.RequestInterrupt"/>
        public void RequestInterrupt() => _interrupt.Request();

        /// <inheritdoc cref="ICliTerminal.End"/>
        public void End(bool answerSafeModeQuestion) => _carrier.End(answerSafeModeQuestion);

        /// <summary>Releases the carrier without answering the Safe Mode question (<see cref="ICliTerminal.End"/>).</summary>
        public void Dispose() => _carrier.Release();

        /// <summary>
        /// Sends a CLI command and returns the cleaned output: ANSI stripped, echo line removed, trailing prompt
        /// removed, <c>without-paging</c> injected after <c>print</c> (<see cref="CliEndMarker.InjectWithoutPaging"/>).
        /// <paramref name="onLine"/>, when given, gets each completed output line while the command is still running;
        /// the streamed lines are the raw ANSI-stripped ones (echo and prompt included).
        /// </summary>
        /// <remarks>The prologue: the line with <c>without-paging</c> injected, the interrupt latch cleared (a request from
        /// before the call is void; one made from here on, the residue's drain included, is this command's), the residue of
        /// the previous read dealt with, a carrier that has ended refused, the line and one Enter written.</remarks>
        public async Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string cmd = CliEndMarker.InjectWithoutPaging(command);
            _interrupt.Clear();
            await DealWithResidueAsync(cmd, beforeCommand: true, ct).ConfigureAwait(false);
            _carrier.ThrowIfEnded(cmd);

            // A carrier the router has closed refuses the write in its own words (SSH.NET: ObjectDisposedException);
            // reported as the read a close ends is, and the command was never written.
            try
            {
                await _carrier.SendAsync(_carrier.Encoding.GetBytes(cmd + _carrier.Enter), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && _carrier.WriteRefusedByClose)
            {
                throw CliReadTimeout.CreateClosedBeforeWrite(Transport, ReceiveTimeoutMs, cmd, ex);
            }

            string stripped = await ReadCommandResponseAsync(cmd, ct, onLine).ConfigureAwait(false);
            return CliOutputCleaner.CleanOutput(stripped, cmd);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no line terminator, no paging injection) and returns the
        /// ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode toggling, where RouterOS
        /// reacts to the control key rather than a typed command.
        /// </summary>
        public async Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            await SendKeysAsync(raw, ct).ConfigureAwait(false);
            // sentCommand: null → tolerant. A control key is not a command and RouterOS does not always answer one with a
            // prompt (measured: Ctrl+D replies with a bare cursor save/restore and nothing else), so reaching the deadline
            // here is normal rather than a truncated response.
            return await ReadCommandResponseAsync(null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for completion) and reads the reaction until the console
        /// goes <b>quiet</b> for <paramref name="quietMs"/> — used for Tab-completion, whose listing does not end in a
        /// shell prompt (RouterOS redraws the prompt with the echoed stem), so a prompt-terminated read would block until
        /// the receive deadline. Returned with its escape sequences: an inline completion is written in cursor moves,
        /// which the completion parser replays (<see cref="CliCompletionParser"/>). Bounded by the receive deadline.
        /// </summary>
        public async Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            await SendKeysAsync(raw, ct).ConfigureAwait(false);
            return await ReadUntilQuietAsync(quietMs, requireData: true, ct).ConfigureAwait(false);
        }

        // A key's prologue: the residue dealt with, as before a command, an ended carrier refused, the keys written.
        private async Task SendKeysAsync(byte[] raw, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string what = CliTerminalText.KeyName(raw, _carrier.Encoding);
            await DealWithResidueAsync(what, beforeCommand: false, ct).ConfigureAwait(false);
            _carrier.ThrowIfEnded(what);
            await _carrier.SendAsync(raw, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deals with what is still on screen from the previous read before <paramref name="what"/> is typed, a command or a
        /// key alike: drained when something is buffered (the clean path pays nothing), or dropped by a carrier whose pump
        /// has already read it (<see cref="ICliConsoleCarrier.DiscardsResidue"/>).
        /// </summary>
        /// <remarks>
        /// RouterOS's line editor repaints the echo as "&lt;prompt&gt; &lt;command&gt;", and the read that returns on the
        /// prompt inside that repaint leaves the echoed command text behind: the next command would read it as its own
        /// answer, and because the stale echo carries <c>name=value</c> text it parses as a record with no <c>.id</c>.
        /// Before a key the same residue ends the read early: its prompt settles a Safe Mode key's read before the router's
        /// question to it arrives, and it starts a Tab's quiet window before the listing does (X4 of the terminal clients
        /// review). Residue that lands as the key goes out cannot be told from its answer, on any carrier. A relay that
        /// ended while the connection was idle shows in the residue: the agent's session is gone, and nothing may be sent
        /// into it.
        /// </remarks>
        private async Task DealWithResidueAsync(string what, bool beforeCommand, CancellationToken ct)
        {
            string residue;
            if (_carrier.DiscardsResidue)
            {
                residue = Stripped(_carrier.PeekResidue());
                RouterOsCliLogin.ThrowIfRomonRelayEnded(Transport, _carrier.RomonTarget, what, residue, commandSent: false);
                _carrier.DiscardResidue(beforeCommand);
                return;
            }
            if (!_carrier.HasData)
                return;
            residue = await DrainAsync(_carrier.SettleMs, ct).ConfigureAwait(false);
            RouterOsCliLogin.ThrowIfRomonRelayEnded(Transport, _carrier.RomonTarget, what, residue, commandSent: false);
        }

        // ── Reads ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a command's response until <see cref="CliPromptSettle"/> says it is over (with the carrier's
        /// <see cref="ICliConsoleCarrier.SettleMs"/>), polling the carrier between waits. Bounded by the receive deadline; a
        /// carrier that ends first ends the read too, as the relay ending when a RoMON target is set.
        /// </summary>
        /// <param name="sentCommand">
        /// The command being answered. When non-null, reaching the deadline without a prompt throws
        /// <see cref="TikConnectionReceiveTimeoutException"/> instead of returning the partial text — see
        /// <see cref="CliReadTimeout"/> for why returning it is worse than failing. <c>null</c> for reads that
        /// legitimately need not end at a prompt (control keys).
        /// </param>
        /// <param name="ct">
        /// Cancels the wait between polls. Only ever the caller's own token, and only in
        /// <see cref="TikCancellationMode.AbandonAndClose"/> — <c>CliConnectionBase</c> passes
        /// <see cref="CancellationToken.None"/> otherwise, because a read abandoned mid-command leaves output the next
        /// command would parse as its own. In that mode the connection is closed straight afterwards, so there is no next
        /// command for it to confuse.
        /// </param>
        /// <param name="onLine">
        /// Optional: called with each completed line as it arrives, for a caller that must consume a long-running command
        /// while it runs (see <see cref="CliLineStreamer"/>). Does not affect when the read returns — the stable prompt is
        /// still the only terminator.
        /// </param>
        /// <returns>The text read, ANSI-stripped.</returns>
        private async Task<string> ReadCommandResponseAsync(string? sentCommand, CancellationToken ct,
            Action<string>? onLine = null)
        {
            var sw = Stopwatch.StartNew();
            var settle = new CliPromptSettle(sentCommand, _carrier.SettleMs, TikWireTrace.Enabled ? TraceNote : (Action<string>?)null);
            var streamer = new CliLineStreamer(onLine);
            ICliCarrierRead read = _carrier.BeginRead(CliReadKind.Command);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (_interrupt.TakeFor(sentCommand))
                {
                    await _carrier.SendAsync(new[] { CliTerminalText.CtrlC }, CancellationToken.None).ConfigureAwait(false);
                    settle.Interrupted();
                }

                CliCarrierPoll poll = await read.PollAsync(settle.AtPrompt, sw.ElapsedMilliseconds, ct).ConfigureAwait(false);
                string raw = read.Text;

                if (poll.MidFrame != null)
                {
                    // Not the generic timeout: that one says the whole deadline elapsed, which is not what happened, and it
                    // would drop the one exception that says what did.
                    string soFar = VtStripper.StripAnsi(raw);
                    if (sentCommand == null)
                        return soFar;
                    throw CliReadTimeout.CreateMidFrame(Transport, ReceiveTimeoutMs, sw.ElapsedMilliseconds, sentCommand, soFar,
                        poll.MidFrame);
                }

                // Nothing has come back AND the router never took the bytes, even after the retransmits ran out. Waiting the
                // rest of the deadline would only turn a known answer into a vague one — see
                // TikConnectionSessionClosedException for why this is safe to call.
                if (raw.Length == 0 && poll.NeverAcknowledged)
                    throw _carrier.NotAcknowledged(sentCommand);

                string stripped = Stripped(raw);
                streamer.Feed(stripped);

                // The relay ended mid-command: no prompt will follow, the agent's session is over.
                RouterOsCliLogin.ThrowIfRomonRelayEnded(Transport, _carrier.RomonTarget, sentCommand, stripped, commandSent: true);

                // Asked before the end is (D2): a counted answer whose count line and prompt came with the end is whole.
                switch (settle.Observe(stripped, poll.Received, sw.ElapsedMilliseconds, raw.Length))
                {
                    case CliPromptSettle.Outcome.Complete:
                    case CliPromptSettle.Outcome.Settled:
                        return stripped;
                }

                // Reached WITHOUT a settled prompt: the response is whatever arrived so far and may end mid-record.
                if (poll.Ended || sw.ElapsedMilliseconds >= ReceiveTimeoutMs)
                    return settle.EndWithoutPrompt(Transport, _carrier.RomonTarget, poll.Ended, sw.ElapsedMilliseconds,
                        ReceiveTimeoutMs, stripped, raw.Length);

                await read.WaitAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads (the carrier answering its probes) until <see cref="CliQuietWindow"/> says the reaction is over — quiet for
        /// <paramref name="quietMs"/> after some data, or nothing at all for
        /// <see cref="CliCompletionParser.NoReactionQuietFactor"/> windows (an empty answer) — or the carrier ends, or the
        /// receive deadline expires. Returns the accumulated text, escape sequences included.
        /// </summary>
        /// <param name="quietMs">How long nothing new must arrive.</param>
        /// <param name="requireData">When <c>true</c> (Tab-completion) the quiet only counts once something has arrived;
        /// when <c>false</c> a silent console is quiet too — for draining a repaint that may not come.</param>
        /// <param name="ct">Checked on every poll, so a caller's cancel is not held up by the quiet window. A frame already
        /// arriving is still read whole.</param>
        internal async Task<string> ReadUntilQuietAsync(int quietMs, bool requireData, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var quiet = new CliQuietWindow(quietMs, requireData);
            ICliCarrierRead read = _carrier.BeginRead(CliReadKind.Quiet);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                CliCarrierPoll poll = await read.PollAsync(false, sw.ElapsedMilliseconds, ct).ConfigureAwait(false);
                if (poll.MidFrame != null)
                    break;

                if (quiet.Observe(read.Text.Length) != CliQuietWindow.Outcome.Continue)
                    break;

                // The window cannot see the carrier: an ended one has nothing more to say.
                if (poll.Ended || sw.ElapsedMilliseconds >= ReceiveTimeoutMs)
                    break;

                await read.WaitAsync(ct).ConfigureAwait(false);
            }

            return read.Text;
        }

        /// <summary>
        /// Reads and drops what the carrier still has (a post-login VT100 redraw, a second prompt, a late repaint) until
        /// nothing has arrived for <paramref name="quietMs"/>; the carrier answers its probes meanwhile, so the link stays
        /// sane. Ends early when the carrier ends.
        /// </summary>
        /// <returns>The drained text, ANSI-stripped.</returns>
        internal async Task<string> DrainAsync(int quietMs, CancellationToken ct)
        {
            ICliCarrierRead read = _carrier.BeginRead(CliReadKind.Drain);
            var sw = Stopwatch.StartNew();
            long quietUntil = quietMs;

            while (sw.ElapsedMilliseconds < quietUntil)
            {
                ct.ThrowIfCancellationRequested();

                CliCarrierPoll poll = await read.PollAsync(false, sw.ElapsedMilliseconds, ct).ConfigureAwait(false);
                if (poll.Ended || poll.MidFrame != null)
                    break;

                if (poll.Received)
                    quietUntil = sw.ElapsedMilliseconds + quietMs;   // keep draining while data keeps arriving
                else
                    await read.WaitAsync(ct).ConfigureAwait(false);
            }

            return VtStripper.StripAnsi(read.Text);
        }

        private string Stripped(string raw)
            => VtStripper.StripAnsi(RouterOsCliLogin.WithoutRomonRelayNoise(_carrier.RomonTarget, raw));

        private void TraceNote(string note)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit(_carrier.TraceChannel, TikWireDir.Note, note);
        }
    }
}
