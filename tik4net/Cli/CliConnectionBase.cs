using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// Transport-agnostic base class for RouterOS CLI-based connections (Telnet, SSH, MACTelnet, WinBox CLI).
    /// Builds the CLI command text, sends it through the transport, and parses the textual response back
    /// into the shared command/sentence model. All the transport-neutral plumbing
    /// (<see cref="ITikConnection"/> surface, command factory, low-level dispatch, diagnostics) lives in
    /// <see cref="TikCommandConnectionBase"/>.
    ///
    /// Concrete transport subclasses must:
    /// <list type="bullet">
    ///   <item>implement <see cref="TikCommandConnectionBase.Open(string, string, string)"/> /
    ///     <see cref="TikCommandConnectionBase.Open(string, int, string, string)"/> and their async
    ///     counterparts by building the concrete transport client and calling <see cref="OpenWith"/> /
    ///     <see cref="OpenWithAsync"/> with delegates bound to it;</item>
    ///   <item>implement <see cref="TransportName"/> (shown in the "not open" diagnostic).</item>
    /// </list>
    /// <see cref="Close"/> and the not-open guards are provided by this base (R6).
    ///
    /// The text returned by the send delegate must already have ANSI escape sequences stripped
    /// (<see cref="VtStripper.StripAnsi"/>) and any terminal echo / prompt trimmed by the transport — the
    /// core layer only sees data lines.
    /// </summary>
    /// <remarks>
    /// <b>Thread safety.</b> A CLI connection is safe to use from several threads, but commands do not
    /// overlap: a terminal carries one conversation, so every command is taken through
    /// <see cref="TikCommandConnectionBase"/>'s command semaphore and extra callers queue behind the one
    /// holding it. That is by design and not a limitation waiting to be lifted — a gate that failed to
    /// hold here would not raise an error, it would interleave two commands' bytes and hand each caller a
    /// plausible answer built from the other's. Open a second connection, or use a transport that
    /// multiplexes (the binary API, REST, native WinBox), when commands have to run at the same time.
    /// <para>
    /// <b>A monitor is the one case worth planning around.</b> Its poll worker takes the same turn a
    /// command does, so nothing corrupts and CRUD from another thread still runs — it queues. What is not
    /// dependable is the other direction: the worker holds the terminal on its own cadence, so a change
    /// made over the <i>same</i> connection can fall between two polls and never be reported. Measured on
    /// Telnet and WinBox CLI, a listen missed such a change 3 times in 4, and polling harder made it worse
    /// rather than better. Drive the change from a second connection when a monitor has to observe it, or
    /// use the binary API, where the tag makes the two independent.
    /// </para>
    /// <para>The rest of the contract — <c>Close</c> not waiting for a running command, Safe Mode as
    /// connection-wide state, and the router's shared throughput ceiling — is on
    /// <see cref="ITikConnection"/>.</para>
    /// </remarks>
    public abstract class CliConnectionBase : TikCommandConnectionBase, ITikCliConnection,
        ITikMonitorTransport, IPollingMonitorHost, ITikCliPagedReadConnection
    {
        /// <inheritdoc/>
        public int CliReadPageSize
        {
            get => _cliReadPageSize;
            set
            {
                if (value < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(value),
                        "CliReadPageSize is a row count; use 0 to read each table in a single command.");
                _cliReadPageSize = value;
            }
        }

        // Set by the MAC-layer terminals in their constructors (see ITikCliPagedReadConnection), and by
        // TikConnectionSetup only when the caller asked for a value.
        private int _cliReadPageSize;

        /// <summary>Interval between monitor-snapshot polls (ms). Sub-second so callers see a fresh reading
        /// promptly; RouterOS GUI/webfig refresh ~1 s but a terminal snapshot is cheap.</summary>
        private const int MonitorPollIntervalMs = 500;
        /// <summary>Interval between /listen config-table polls (ms).</summary>
        private const int ListenPollIntervalMs = 1000;
        /// <summary>torch's <c>freeze-frame-interval</c> (seconds). Each poll blocks for
        /// <c>2×</c> this (see <see cref="CliCommandBuilder.BuildTorchSnapshot"/>) — confirmed live as the
        /// minimum that reliably flushes one frame — so no separate inter-poll sleep is needed.</summary>
        private const int TorchFreezeFrameSeconds = 2;

        // ── Raw sentences — native CLI text (ITikRawSentenceConnection) ───────

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        /// <summary>
        /// Runs a command written in this transport's own language — <b>RouterOS CLI text</b> — and returns
        /// the response as sentences. Nothing is translated on the way out.
        /// </summary>
        /// <param name="commandRows">
        /// The command, in CLI syntax. Several rows are joined with a single space, so the command may be
        /// written in one piece or split the way the API's sentence rows are:
        /// <c>CallCommandSync("/interface print as-value")</c> and
        /// <c>CallCommandSync("/interface print", "as-value", "where name=ether1")</c> send the same line.
        /// </param>
        /// <returns>
        /// The parsed response. A command whose output is in <c>as-value</c> form yields one
        /// <see cref="ITikReSentence"/> per record followed by an <see cref="ITikDoneSentence"/>; any other
        /// output is returned whole as the <c>ret</c> word of a single <see cref="ITikDoneSentence"/>, because
        /// arbitrary terminal text has no record structure to parse.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This is the low-level counterpart of <c>CreateRawCommand</c> and behaves the same way: the line is
        /// sent as typed, with no path rewriting, no <c>where</c> building and no <c>proplist</c>. That is the
        /// point of it — everything the O/R mapper cannot express is reachable here, including
        /// <c>:put</c>/<c>:foreach</c> scripting, <c>/export</c>, and menus tik4net has no entity for.
        /// </para>
        /// <para>
        /// <b>To get records back you must ask the router for them.</b> RouterOS prints nothing for a bare
        /// <c>print as-value</c> typed at a terminal — as-value output is materialised only in script context
        /// — so wrap a read yourself: <c>:put [/interface print as-value]</c>. Unwrapped output is still
        /// returned, just as text rather than as rows.
        /// </para>
        /// <para>
        /// Errors the router reports in its output are raised as
        /// <see cref="TikCommandTrapException"/>, the same as on any other CLI command.
        /// </para>
        /// </remarks>
        /// <exception cref="TikConnectionNotOpenException">The connection is not open.</exception>
        /// <exception cref="TikCommandTrapException">The router reported an error.</exception>
        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
            => CallCommandAsync(commandRows, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public Task<IList<ITikSentence>> CallCommandAsync(string[] commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
            => CallCommandAsync((IEnumerable<string>)commandRows, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>
        /// This is the implementation and <see cref="CallCommandSync(IEnumerable{string})"/> blocks on it,
        /// the same way round as the rest of this class: one code path, so the two cannot drift apart in
        /// what they send or how they read the answer.
        /// </remarks>
        public async Task<IList<ITikSentence>> CallCommandAsync(IEnumerable<string> commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(commandRows, nameof(commandRows));
            EnsureOpened();

            string cliText = string.Join(" ",
                commandRows.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray());
            if (cliText.Length == 0)
                throw new ArgumentException("commandRows must contain a CLI command.", nameof(commandRows));

            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false)
                            ?? string.Empty;

            // Raw mode is handed a finished line rather than building one, but the verb is IN that line —
            // so a plain '/path set …' still gets the positional check, and only a line the reader cannot
            // vouch for (scripting, ':put [ … ]', chaining) falls back to phrase matching alone. "No
            // output" remains a perfectly good answer either way and must not be read as failure.
            CliErrorParser.ThrowIfError(output,
                CreateDummyCommand(new TikCommandDescriptor(cliText, new List<ITikCommandParameter>())),
                silentOnSuccess: CliErrorParser.TryGetRawSilentVerb(cliText, out _));

            var result = new List<ITikSentence>();
            var records = CliOutputParser.ParseAsValue(output);
            if (records.Count > 0)
            {
                foreach (var record in records)
                    result.Add(record);
                result.Add(new TikDoneSentenceResult());
            }
            else
            {
                // Not as-value output — hand back the text rather than inventing rows from it.
                string text = output.Trim();
                result.Add(new TikDoneSentenceResult(text.Length > 0 ? text : null));
            }
            return result;
        }

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <summary>
        /// CLI transports support CRUD and (via polling) Listen/async: <c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/
        /// <c>LoadListenWithCallback</c> are emulated by re-issuing a one-shot snapshot/print on a background timer
        /// (see <see cref="ITikMonitorTransport"/> below). Streaming (<c>ExecuteListWithDuration</c>) is NOT
        /// reported — use the binary API for that.
        /// <para>
        /// <see cref="TikConnectionCapability.AsyncCommands"/> is reported because the terminal clients are
        /// async to the socket and the <c>Execute*Async</c> surface awaits them — nothing is pushed onto a
        /// thread-pool thread to look asynchronous. <see cref="TikConnectionCapability.CancelInFlight"/> is
        /// <b>not</b>, and never will be: a terminal answers with an unframed byte stream, so a read that is
        /// abandoned mid-command leaves output for the next command to misread. See
        /// <see cref="TikCancellationMode"/> for what a token does here instead.
        /// </para>
        /// <para>
        /// <see cref="TikConnectionCapability.RawCommand"/> is reported because a terminal <i>has</i> a
        /// native dialect to be raw in: <see cref="CallCommandSync(string[])"/> sends RouterOS CLI text
        /// verbatim. It is not reported by REST or native WinBox, which have no command language of their own
        /// for a caller to write.
        /// </para>
        /// </summary>
        public override TikConnectionCapability Capabilities
            => TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode
             | TikConnectionCapability.RawCommand | TikConnectionCapability.AsyncCommands;

        /// <inheritdoc/>
        /// <remarks>
        /// Set it to <see cref="TikCancellationMode.AbandonAndClose"/> (usually via
        /// <see cref="TikConnectionSetup.CancellationMode"/>) to trade the connection for a prompt return.
        /// A token cancelled <i>before</i> dispatch always throws without writing anything, in either mode.
        /// </remarks>
        public TikCancellationMode CancellationMode { get; set; } = TikCancellationMode.Cooperative;

        // ── Transport driver — subclass contract ──────────────────────────────

        // The active transport client is held as three delegates wired up by the leaf transport in its Open
        // (R6): send a CLI command, send raw bytes, and close. Holding delegates rather than a typed client
        // keeps the leaf's concrete client type (and any internal transport interface) out of this public
        // base's signatures — no public-API leak (a protected member cannot expose an internal type, CS0057)
        // — while still centralising the open/close/guard boilerplate the five CLI transports used to repeat.
        private Func<string, CancellationToken, Task<string>>? _send;
        private Func<byte[], CancellationToken, Task<string>>? _sendRaw;
        private Action? _close;

        /// <summary>Short transport name shown in the "connection is not open" diagnostic (e.g. "Telnet").</summary>
        protected abstract string TransportName { get; }

        /// <summary>
        /// When set before <c>Open</c>, the host/user/password given to <c>Open</c> are the AGENT's, and the
        /// transport continues from the agent's shell into this target over RoMON SSH before the connection
        /// counts as open. Set by <see cref="TikConnectionSetup.ApplyTo"/> through <see cref="ITikRomonConnection"/>,
        /// which only the transports that relay implement (Telnet, SSH, MAC-Telnet); every other CLI transport refuses a
        /// target at open — ignoring one would open the AGENT and run every command there.
        /// </summary>
        internal RomonSshTarget? RomonTarget { get; set; }

        /// <summary>Set once the relay has reached the target — see <see cref="ITikRomonConnection"/>.</summary>
        internal TikRomonConnectionInfo? RomonConnectionInfo { get; private set; }

        /// <summary>
        /// Records that the relay reached <see cref="RomonTarget"/>. Called by a relaying transport's login, after
        /// <see cref="RouterOsCliLogin.RomonSshLoginAsync"/> returned the agent's own RoMON id.
        /// </summary>
        internal void RomonEntered(TikConnectionType agentConnectionType, string host, string user, string agentRomonId)
        {
            var target = RomonTarget!;
            TikRouterAddress agentAddress = target.Agent?.Address ?? TikRouterAddress.FromHost(host);
            RomonConnectionInfo = new TikRomonConnectionInfo(TikRomonRelay.Ssh,
                new TikRomonAgentInfo(agentAddress, agentConnectionType, user, agentRomonId),
                new TikRomonTargetInfo(target.RomonId, target.User));
        }

        /// <summary>
        /// A failed login to the RoMON agent, said to be the agent's: otherwise the message reads as if the
        /// target had refused, and the target's credentials are the first thing a reader would doubt. Only for
        /// an agent that answered and refused — an agent port that refuses the connection is a
        /// <see cref="SocketException"/>, as on a direct connection.
        /// </summary>
        internal static TikConnectionLoginException AgentLoginFailed(string host, Exception ex)
            => new TikConnectionLoginException(new Exception(
                "RoMON agent " + host + " refused the login — " +
                (ex is TikConnectionLoginException ? ex.InnerException?.Message ?? ex.Message : ex.Message), ex));

        /// <summary>
        /// Shared open: runs <paramref name="login"/> under the standard guard (a
        /// <see cref="TikConnectionLoginException"/>, a <see cref="SocketException"/> and a cancellation are
        /// rethrown as-is; any other exception is wrapped in a login exception; the half-open client is closed
        /// either way), then registers the driver delegates and marks the connection opened. Leaf transports
        /// build their concrete client and call this with delegates bound to it.
        /// </summary>
        protected void OpenWith(Func<CancellationToken, Task> login,
            Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close)
            => OpenWithAsync(login, send, sendRaw, close).GetAwaiter().GetResult();

        /// <summary>Async counterpart of <see cref="OpenWith"/>.</summary>
        protected async Task OpenWithAsync(Func<CancellationToken, Task> login,
            Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close,
            CancellationToken cancellationToken = default)
        {
            // Before the delegate, not inside it: the delegate opens the socket synchronously and only then
            // awaits the login, so an already-cancelled token would otherwise still cost a TCP connect.
            cancellationToken.ThrowIfCancellationRequested();
            if (RomonTarget != null && !(this is ITikRomonConnection))
                throw new NotSupportedException(TransportName + " cannot relay to a RoMON target (" + RomonTarget +
                    "); use Telnet, SSH or MAC-Telnet to the agent.");
            RomonConnectionInfo = null;
            try
            {
                // The login delegate has always taken a token; it used to be handed CancellationToken.None,
                // so a caller's token stopped at the door. It reaches the router-facing waits now — the
                // prompt reads of RouterOsCliLogin — which is the part of an open that actually blocks.
                // The socket connect inside the delegate is synchronous and stays bounded by ConnectTimeout.
                await login(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Not a login failure: the caller asked to stop. Wrapping it would break every
                // catch (OperationCanceledException) written against the async surface.
                close();
                throw;
            }
            catch (TikConnectionLoginException)
            {
                close();
                throw;
            }
            catch (SocketException)
            {
                // Not a login failure: nothing answered (refused, unreachable, the connect timed out). It
                // leaves as itself, which is what the binary API throws for the same port — a caller told to
                // check credentials when the service is simply off is sent the wrong way.
                close();
                throw;
            }
            catch (Exception ex)
            {
                close();
                throw new TikConnectionLoginException(ex);
            }
            _send = send;
            _sendRaw = sendRaw;
            _close = close;
            SetOpened();
        }

        /// <inheritdoc/>
        public override void Close()
        {
            // SetClosed FIRST, as WinboxNativeConnection.Close does and for the same reason: a command
            // running on another thread is about to lose its socket, and IsOpened is how it tells "the user
            // closed this" from "the transport broke". Tearing down first left a window in which the failure
            // arrived while the connection still looked open, and it was reported as a transport error.
            SetClosed();
            _close?.Invoke();
            _send = null;
            _sendRaw = null;
            _close = null;
        }

        private TikConnectionNotOpenException NotOpen()
            => new TikConnectionNotOpenException($"{TransportName} connection is not open.");

        // ── Semaphore-serialised execution ────────────────────────────────────

        /// <summary>
        /// Serialises access, fires diagnostics events, then sends the command through the transport.
        /// </summary>
        /// <remarks>
        /// <paramref name="ct"/> is observed at the points where observing it is safe, which on a terminal is
        /// not the same as "wherever it fires" — see <see cref="TransportToken"/> and
        /// <see cref="TikCancellationMode"/>. Cancelling while queued for <c>_cmdLock</c> is free: the command
        /// has not been written, so nothing needs resynchronizing.
        /// </remarks>
        protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
        {
            var send = _send ?? throw NotOpen();
            await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();   // level 0 — nothing written yet
                FireWriteRow(cliText);
                string result;
                try
                {
                    result = await send(cliText, TransportToken(ct)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Only reachable in AbandonAndClose: the read was cut mid-response, so whatever the router
                    // is still writing would be read by the next command. Close instead of pretending we are
                    // in step again.
                    CloseAfterAbandonedRead();
                    throw;
                }
                catch (TikConnectionReceiveTimeoutException)
                {
                    // Same reasoning as the abandoned cancel above, arrived at from the other direction: the
                    // response never reached a prompt, so its tail is still on its way and the NEXT command
                    // would read it as its own answer. A terminal has no framing to resynchronize on, so the
                    // only honest thing left is to stop pretending this session is usable.
                    CloseAfterAbandonedRead();
                    throw;
                }
                catch (TikRomonRelayEndedException)
                {
                    // The agent's session ended with the relay; there is nothing left to send commands into.
                    CloseAfterAbandonedRead();
                    throw;
                }
                catch (Exception ex) when (!IsOpened && !(ex is TikConnectionException))
                {
                    // Close() ran on another thread while this command held the socket. IsOpened is already
                    // false because Close sets it first, which is what makes this distinguishable from a
                    // transport that simply broke.
                    throw ClosedWhileRunning(ex);
                }
                FireReadRow(result);
                // The safe point. The response has been drained, so the channel is consistent and the caller's
                // cancel can finally be honoured without costing the next command its answer.
                ct.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        /// <summary>
        /// The token the <b>transport read</b> is given, which is deliberately not always the caller's.
        /// In <see cref="TikCancellationMode.Cooperative"/> it is <see cref="CancellationToken.None"/>: the
        /// response must be read to its end before the cancel is reported, because a terminal has no framing
        /// to resynchronize on. In <see cref="TikCancellationMode.AbandonAndClose"/> the caller's token is
        /// passed down, and the connection is closed if it fires.
        /// </summary>
        private CancellationToken TransportToken(CancellationToken ct)
            => CancellationMode == TikCancellationMode.AbandonAndClose ? ct : CancellationToken.None;

        // Closes the session after a read was abandoned. Best-effort: the point is that the connection is
        // marked unusable, and a transport that throws while closing an already-broken channel must not
        // replace the OperationCanceledException the caller is waiting for.
        private void CloseAfterAbandonedRead()
        {
            TikWireTrace.Emit("cli.cancel", TikWireDir.Note,
                "read abandoned mid-response (in-flight cancel with TikCancellationMode.AbandonAndClose, "
                    + "a receive timeout, or the end of a RoMON relay) — closing the connection, the unread "
                    + "response cannot be resynchronized");
            try { Close(); }
            catch (Exception ex)
            {
                TikWireTrace.Emit("cli.cancel", TikWireDir.Note,
                    "close after abandoned read failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Synchronous wrapper around <see cref="ExecuteCliCommandAsync"/>.</summary>
        protected string ExecuteCliCommand(string cliText)
            => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

        // ── Streaming read (incremental line delivery) ─────────────────────────

        // Optional driver: "send a command and report each COMPLETED output line while the command is still
        // running", returning the same cleaned output the normal driver returns when it finally ends. A leaf
        // transport registers this in Open if its read loop can hand out lines mid-command; a transport that
        // does not is not broken, it just falls back to the one-shot as-value monitor (see SnapshotOnce).
        private Func<string, Action<string>, CancellationToken, Task<string>>? _sendStreaming;

        /// <summary>
        /// Registers the driver that enables incremental (line-at-a-time) reads on this transport. Leaf
        /// transports whose read loop can report completed lines before the command returns to the prompt
        /// call this from their Open, right after <see cref="OpenWith"/>.
        /// </summary>
        /// <remarks>
        /// Registered separately from <see cref="OpenWith"/> — rather than by widening its delegate set —
        /// so that adding the capability neither breaks a transport built outside this assembly nor claims
        /// the capability on its behalf. Same pattern as <see cref="RegisterCompletionDriver"/>.
        /// </remarks>
        protected void RegisterStreamingDriver(Func<string, Action<string>, CancellationToken, Task<string>> sendStreaming)
            => _sendStreaming = sendStreaming;

        /// <summary>True when this transport can deliver a command's output line by line as it arrives.</summary>
        protected bool SupportsStreamingRead => _sendStreaming != null;

        /// <summary>
        /// Runs a command, reporting each completed output line to <paramref name="onLine"/> as the router
        /// produces it, and returns the full cleaned output once the command ends. The lines are raw — echo,
        /// header and prompt included — because the caller parses them positionally.
        /// </summary>
        protected string ExecuteCliCommandStreaming(string cliText, Action<string> onLine)
        {
            var send = _sendStreaming ?? throw new NotSupportedException(
                $"{TransportName} does not support incremental reads.");
            _cmdLock.Wait();
            try
            {
                FireWriteRow(cliText);
                string result;
                try
                {
                    result = send(cliText, onLine, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (TikConnectionReceiveTimeoutException)
                {
                    CloseAfterAbandonedRead();   // see ExecuteCliCommandAsync — the tail is still coming
                    throw;
                }
                catch (TikRomonRelayEndedException)
                {
                    CloseAfterAbandonedRead();   // see ExecuteCliCommandAsync — the agent's session is gone
                    throw;
                }
                FireReadRow(result);
                return result;
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        // ── Safe Mode (Ctrl+X / Ctrl+D control keys) ───────────────────────────

        /// <summary>Ctrl+X — toggles Safe Mode in the RouterOS terminal (take, then commit). Byte 0x18.</summary>
        private const byte CtrlX = 0x18;
        /// <summary>Ctrl+D — quits Safe Mode discarding the changes (rollback now). Byte 0x04.</summary>
        private const byte CtrlD = 0x04;

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X, with no line terminator) to the terminal and returns
        /// the ANSI-stripped response read up to the next stable shell prompt. The bytes are sent verbatim and
        /// the output is not echo/prompt-stripped — the caller inspects the raw terminal reaction
        /// (e.g. <c>[Safe Mode taken]</c>). Drives the leaf transport's send-raw delegate registered in Open.
        /// </summary>
        protected Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
            => (_sendRaw ?? throw NotOpen())(raw, ct);

        private string SendControlKey(byte key)
        {
            _cmdLock.Wait();
            try
            {
                FireWriteRow($"<ctrl-0x{key:X2}>");
                string result = SendRawAndReadAsync(new[] { key }, CancellationToken.None).GetAwaiter().GetResult();
                FireReadRow(result);
                return result;
            }
            finally { _cmdLock.Release(); }
        }

        /// <summary>
        /// Enters Safe Mode by sending <c>Ctrl+X</c> in the live terminal. RouterOS prints
        /// <c>[Safe Mode taken]</c> and shows the <c>&lt;SAFE&gt;</c> token in the prompt (handled transparently
        /// by <see cref="RouterOsCliLogin.IsShellPrompt"/>); the rollback is tied to THIS terminal session, so
        /// dropping the connection without a <see cref="SafeModeRelease"/> reverts every change made since. Works
        /// on any RouterOS version (no scriptable <c>/safe-mode</c> needed). No-op when already held.
        /// </summary>
        public virtual void SafeModeTake()
        {
            EnsureOpened();
            if (SafeModeHeld) return;
            string output = SendControlKey(CtrlX);
            CliSafeModeParser.ThrowIfTakeFailed(output, new TikGenericCommand(this, "/safe-mode/take"));
            SafeModeHeld = true;
        }

        /// <summary>
        /// Commits the safe-mode changes and leaves Safe Mode by sending a second <c>Ctrl+X</c>; the prompt
        /// reverts to its normal form afterwards. No-op when safe mode is not held.
        /// </summary>
        public virtual void SafeModeRelease()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
            SendControlKey(CtrlX);
            SafeModeHeld = false;
        }

        /// <summary>
        /// Discards the safe-mode changes immediately and leaves Safe Mode without dropping the connection.
        /// Uses the scriptable <c>/safe-mode/unroll</c> command (RouterOS 7.18+), falling back to the
        /// terminal <c>Ctrl+D</c> on older versions. No-op when safe mode is not held.
        /// </summary>
        /// <remarks>
        /// The control key is the slow path: measured on 7.23.2 over Telnet,
        /// <c>Ctrl+D</c> is answered with nothing but a cursor save/restore (<c>&lt;CR&gt;&lt;ESC&gt;7&lt;ESC&gt;8</c>)
        /// — no confirmation line and no repainted prompt — so the read has nothing to terminate on and runs
        /// to the full receive deadline. The rollback itself does happen, which is why that path costs 30 s
        /// per call instead of failing. The scriptable command answers normally and ends on a prompt.
        /// </remarks>
        public virtual void SafeModeUnroll()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;

            try
            {
                string output = ExecuteCliCommand("/safe-mode/unroll");
                CliErrorParser.ThrowIfError(output, new TikGenericCommand(this, "/safe-mode/unroll"),
                    silentOnSuccess: false); // prints "Unrolling Safe Mode... Success!"
                SafeModeHeld = false;
                return;
            }
            catch (TikNoSuchCommandException)
            {
                // RouterOS predates scriptable /safe-mode → fall back to the control key.
            }

            SafeModeUnrollByControlKey();
        }

        /// <inheritdoc/>
        public bool SafeModeGet() => SafeModeHeld;

        /// <summary>
        /// Leaves Safe Mode with the terminal <c>Ctrl+D</c> key — the pre-7.18 path, and the only one on a
        /// router without the scriptable <c>/safe-mode</c> menu. Overridden where the key has a second
        /// meaning at the transport layer (on SSH it is VEOF and tears the channel down).
        /// </summary>
        protected virtual void SafeModeUnrollByControlKey()
        {
            SendControlKey(CtrlD);
            SafeModeHeld = false;
        }

        // ── Tab-completion probe (ITikCliCompletion) ───────────────────────────

        /// <summary>Tab — triggers the RouterOS terminal completion listing. Byte 0x09.</summary>
        private const byte Tab = 0x09;
        /// <summary>Ctrl-C — aborts the current input line (leaving a fresh prompt). Byte 0x03.</summary>
        private const byte CtrlC = 0x03;
        /// <summary>Silence (no new bytes) that marks the completion listing as fully arrived (ms).</summary>
        private const int CompletionSettleQuietMs = 300;

        // Optional driver for the completion probe: "send raw bytes, then read until the output goes quiet
        // for N ms" (settle), returning the reaction with its escape sequences — an inline completion is written
        // in cursor moves, which CliCompletionParser replays. Unlike the command/control-key drivers
        // (which read up to the next shell prompt), the Tab listing does NOT end in a bare prompt — RouterOS
        // redraws the prompt with the echoed stem — so it must be read on a settle window, not a prompt match.
        // A leaf transport registers this in Open only if it supports completion (all CLI transports do);
        // when it is null, CompleteCli reports the transport does not support completion (fail-closed).
        private Func<byte[], int, CancellationToken, Task<string>>? _sendRawSettle;

        /// <summary>
        /// Registers the settle-read driver that enables <see cref="ITikCliCompletion"/> on this transport.
        /// Leaf transports that can drive interactive Tab-completion call this from their Open after
        /// <see cref="OpenWith"/>; transports that don't leave it unregistered (completion then throws).
        /// </summary>
        protected void RegisterCompletionDriver(Func<byte[], int, CancellationToken, Task<string>> sendRawSettle)
            => _sendRawSettle = sendRawSettle;

        /// <inheritdoc/>
        public IReadOnlyList<string> CompleteCli(string partialInput)
            => CliCompletionParser.Tokens(CompleteCliReaction(partialInput), partialInput);

        /// <inheritdoc/>
        public string CompleteCliRaw(string partialInput)
            => CliCompletionParser.Clean(CompleteCliReaction(partialInput), partialInput);

        /// <summary>
        /// Drives one Tab-completion probe and returns the terminal reaction, escape sequences included.
        /// Sequence (verified live): send <c>&lt;partialInput&gt;&lt;Tab&gt;</c> and read until the listing
        /// settles (RouterOS prints the completions then redraws <c>] &gt; &lt;stem&gt;</c> — never a bare
        /// prompt, so a prompt-based read would hang); then send <c>Ctrl-C</c> to abort the half-typed line
        /// so the session is left at a clean prompt for the next call. <c>?</c> is deliberately not used — it
        /// emits no listing over a RouterOS PTY.
        /// </summary>
        private string CompleteCliReaction(string partialInput)
        {
            EnsureOpened();
            if (partialInput == null)
                throw new ArgumentNullException(nameof(partialInput));
            var settle = _sendRawSettle
                ?? throw new NotSupportedException(
                    $"The {TransportName} transport does not support interactive Tab-completion. "
                    + "Use the Telnet transport for CompleteCli / the mikrotik_cli_complete MCP tool.");

            byte[] stem = Encoding.GetBytes(partialInput);
            byte[] tab = new byte[stem.Length + 1];
            Array.Copy(stem, tab, stem.Length);
            tab[stem.Length] = Tab;

            _cmdLock.Wait();
            try
            {
                FireWriteRow("<tab-complete> " + partialInput);
                string reaction = settle(tab, CompletionSettleQuietMs, CancellationToken.None).GetAwaiter().GetResult();
                FireReadRow(VtStripper.StripAnsi(reaction));

                // Abort the half-typed line (Ctrl-C → fresh prompt). Prompt-based read returns promptly here.
                try { SendRawAndReadAsync(new[] { CtrlC }, CancellationToken.None).GetAwaiter().GetResult(); }
                catch { /* best-effort cleanup — the listing is already captured */ }

                return reaction;
            }
            finally { _cmdLock.Release(); }
        }

        // ── CRUD hooks — CLI text build + parse ────────────────────────────────
        //
        // The terminal clients are async to the socket, so the Task-based hooks are the real implementation
        // here and the synchronous ones block on them (D5: async is the primitive; nothing is pushed onto a
        // thread-pool thread to look asynchronous). Every await below carries ConfigureAwait(false), which is
        // also what keeps the blocking wrappers safe to call from a UI / ASP.NET-classic SynchronizationContext.

        /// <inheritdoc/>
        protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override string RunRawText(TikCommandDescriptor descriptor)
            => RunRawTextAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        protected override async Task<IList<TikRecordSentence>> RunPrintAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();

            // Raw pass-through (CreateRawCommand): send the command line verbatim — no CliCommandBuilder,
            // no path→CLI rewrite, no where-clause. The caller is responsible for as-value materialisation;
            // wrapAsValue:true wraps it in ':put [ … as-value]' as a convenience so the output parses.
            if (descriptor.IsRaw)
            {
                string rawCli = descriptor.WrapAsValue
                    ? WrapRawAsValue(descriptor.CommandText)
                    : descriptor.CommandText;
                string rawOutput = await ExecuteCliCommandAsync(rawCli, cancellationToken).ConfigureAwait(false);
                // A command that did not run must not come back as an empty table: 0 rows is
                // indistinguishable from a table with nothing in it. Same check the low-level
                // CallCommandSync applies, so the two halves of the raw promise agree about failure.
                CliErrorParser.ThrowIfError(rawOutput, CreateDummyCommand(descriptor));
                return CliOutputParser.ParseAsValue(rawOutput);
            }

            // Action verbs (e.g. /system/script/run) perform an action and produce no result set over a
            // terminal (no per-record !re output, unlike the binary API) — they belong on the non-query
            // path. Reject them on the read path so the misuse is explicit instead of silently returning an
            // empty list (R7); invoke them via ExecuteNonQuery (RunNonQuery handles the 'run' verb).
            string printVerb = TikPath.Verb(descriptor.CommandText);
            if (IsActionVerb(printVerb))
                throw ActionVerbOnReadPath(descriptor.CommandText);

            // Actions that the binary API answers with an empty '!re' row (verified live for /tool/wol:
            // "!re" with no words, then "!done"). Consumers therefore legitimately reach them through a
            // read method — ToolWol.ExecuteWol uses ExecuteSingleRowOrDefault — so throwing here would
            // punish correct code. Route them to the non-query builder instead and return no rows;
            // 'at most one row' is what the callers are written against. This is the opposite treatment
            // from IsActionVerb ('run'), where a read call really is consumer error.
            // Without this, BuildPrint silently DROPS the name=value inputs (mac= is not a print
            // modifier) and appends 'as-value', yielding ':put [/tool wol as-value]' — the router then
            // rejects it for a missing mac.
            if (IsEmptyRowAction(printVerb))
            {
                // includeFilters: the read path rewrote the action's own inputs (mac=, interface=) to
                // Filter format — see CliCommandBuilder.BuildNonQuery.
                string actionText = CliCommandBuilder.BuildNonQuery(
                    descriptor.CommandText, descriptor.Parameters, includeFilters: true);
                string actionOutput = await ExecuteCliCommandAsync(actionText, cancellationToken).ConfigureAwait(false);
                // An empty-row action (wol) is silent when it succeeds and prints its complaint when it does
                // not — e.g. "input does not match any value of interface", which no phrase list caught, so
                // WolWithInvalidInterfaceWillFail saw a failed send reported as success (P2.12).
                CliErrorParser.ThrowIfError(actionOutput, CreateDummyCommand(descriptor), silentOnSuccess: true);
                return new List<TikRecordSentence>();
            }

            // Monitor commands (/ping, /tool/traceroute, /interface/monitor-traffic, …) reached through a READ
            // method. Their parameters are the command's own INPUTS, but BuildPrint only understands print
            // modifiers and a 'where' clause, so it dropped every one of them: '/ping =address=127.0.0.1
            // =count=2' went out as ':put [/ping as-value]' → "failure: resolve failed" (P2.51). The async path
            // never had the bug — it has always used BuildMonitorSnapshot, which is what is used here too.
            if (CliMonitorVerbs.IsSyncMonitorVerb(printVerb))
                return await RunMonitorSnapshotAsync(descriptor, printVerb, cancellationToken).ConfigureAwait(false);

            // The `get` verb. BuildPrint cannot express it: it drops the .id and value-name inputs (neither
            // is a print modifier) and appends as-value, so `/interface/get =.id=*2 =value-name=name` went
            // out as `:put [/interface get as-value]` — a singleton read of a list menu, answered "no such
            // item". The singleton form was broken the same way, and worse: `as-value` landed where the
            // value name goes, so `/system/identity/get =value-name=name` was refused with "input does not
            // match any value of value-name". The binary API honours both, and ITikCommand is the level
            // whose whole promise is that one command runs everywhere, so this is ours to translate.
            if (printVerb == "get")
                return await RunGetAsync(descriptor, cancellationToken).ConfigureAwait(false);

            // .proplist: the fields the caller asked for, and only those — the binary API's contract. It never
            // reaches the wire: the CLI's own proplist= refuses the whole read when one name is unknown
            // ("input does not match any value of value-name", 7.24) where the API ignores the name. So the
            // read asks for the full field set and the rows are trimmed here. A plain print is the SUMMARY
            // columns only (/interface/ethernet omits disable-running-check), which is why 'detail' is added;
            // a singleton menu refuses it ("bad parameter detail") and prints every field without it.
            var proplist = descriptor.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);
            if (proplist != null)
            {
                var rest = descriptor.Parameters.Where(p => p.Name != TikSpecialProperties.Proplist).ToList();
                var plain = new TikCommandDescriptor(descriptor.CommandText, rest);
                IList<TikRecordSentence> rows;
                if (rest.Any(p => p.ParameterFormat != TikCommandParameterFormat.Filter
                                  && string.Equals(p.Name, "detail", StringComparison.OrdinalIgnoreCase)))
                {
                    rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var withDetail = new List<ITikCommandParameter>(rest)
                    {
                        new TikCommandParameter("detail", "", TikCommandParameterFormat.NameValue),
                    };
                    try
                    {
                        rows = await RunPrintAsync(new TikCommandDescriptor(descriptor.CommandText, withDetail),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (TikConnectionResponseIncompleteException ex) when (IsDetailRefusal(ex.PartialResponse))
                    {
                        rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TikCommandTrapException ex) when (IsDetailRefusal(ex.Message))
                    {
                        // The refusal is a parse error ("… (line 1 column 27)"), which a counted read now reports
                        // as the router's error rather than as a lost count — see ThrowIfSyntaxError.
                        rows = await RunPrintAsync(plain, cancellationToken).ConfigureAwait(false);
                    }
                }
                return TrimToProplist(rows, proplist.Value);
            }

            bool needStats = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliStats);
            bool wantJson = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliJson);

            IList<TikRecordSentence> records = await RunPrintQueryAsync(descriptor, wantJson,
                (pars, from) => CliCommandBuilder.BuildPrintExpression(descriptor.CommandText, pars, from),
                cancellationToken).ConfigureAwait(false);

            if (needStats)
            {
                // Two-query path: detail (config) + stats (counters), merged by .id.
                IList<TikRecordSentence> statsRecords = await RunPrintQueryAsync(descriptor, wantJson,
                    (pars, from) => CliCommandBuilder.BuildPrintStatsExpression(descriptor.CommandText, pars, from),
                    cancellationToken).ConfigureAwait(false);
                records = MergeById(records, statsRecords);
            }

            return await SupplyFlagsAsync(descriptor, records, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds to each record of <paramref name="primary"/> the fields it lacks from the record of
        /// <paramref name="extra"/> with the same <c>.id</c>. A field <paramref name="primary"/> already has is
        /// kept; a record with no counterpart is kept as it is.
        /// </summary>
        internal static IList<TikRecordSentence> MergeById(IList<TikRecordSentence> primary,
            IList<TikRecordSentence> extra)
        {
            var extraById = new Dictionary<string, TikRecordSentence>(StringComparer.OrdinalIgnoreCase);
            foreach (var er in extra)
            {
                string? id = er.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id != null)
                    extraById[id] = er;
            }
            if (extraById.Count == 0)
                return primary;

            var merged = new List<TikRecordSentence>(primary.Count);
            foreach (var row in primary)
            {
                string? id = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null || !extraById.TryGetValue(id, out TikRecordSentence? er))
                {
                    merged.Add(row);
                    continue;
                }

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in row.Words)
                    fields[kv.Key] = kv.Value;
                foreach (var kv in er.Words)
                    if (!fields.ContainsKey(kv.Key))
                        fields[kv.Key] = kv.Value;
                merged.Add(new TikRecordSentence(fields));
            }
            return merged;
        }

        // ── Flag fields on RouterOS before 7.20 ────────────────────────────────

        /// <summary>
        /// Whether this router's <c>print as-value</c> leaves the flag fields out (RouterOS before 7.20);
        /// <c>null</c> = not established yet. See <see cref="AsValueOmitsFlagsAsync"/>.
        /// </summary>
        private bool? _asValueOmitsFlags;

        /// <summary>The flag names each menu knows, per requested list — see <see cref="KnownFlagFieldsAsync"/>.</summary>
        private readonly Dictionary<string, string[]> _knownFlagFields =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// On a router whose <c>print as-value</c> leaves the flag fields out, reads the ones the command names in
        /// <see cref="TikSpecialProperties.CliFlags"/> with a second print of the same rows and merges them in.
        /// </summary>
        /// <remarks>
        /// <para>RouterOS before 7.20 prints no flag in <c>as-value</c> — no <c>disabled</c>, <c>dynamic</c>,
        /// <c>running</c> — with or without <c>detail</c>, and a read then maps each of them to its CLR default:
        /// an interface that is up reads <c>Running = false</c>, and nothing says so. Asked for by name, the same
        /// router answers them explicitly <c>true</c> or <c>false</c> (<c>print as-value proplist=running,disabled</c>,
        /// 7.17 and 7.19.6); 7.20 made that the default ("include flags by default when printing to value").</para>
        /// <para>The second print is built like the first — same filter, same windows — so it costs one command per
        /// page, and only on such a router. The CLI's <c>proplist=</c> refuses the whole read when one name is
        /// unknown to the menu, so the names are checked first (<see cref="KnownFlagFieldsAsync"/>); a name the
        /// menu does not have is one the binary API does not send either.</para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>> SupplyFlagsAsync(
            TikCommandDescriptor descriptor, IList<TikRecordSentence> records, CancellationToken cancellationToken)
        {
            var hint = descriptor.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.CliFlags);
            if (hint == null || records.Count == 0)
                return records;

            string[] wanted = (hint.Value ?? string.Empty)
                .Split(',').Select(n => n.Trim()).Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (wanted.Length == 0)
                return records;

            if (!await AsValueOmitsFlagsAsync(records, wanted, cancellationToken).ConfigureAwait(false))
                return records;

            string[] known = await KnownFlagFieldsAsync(descriptor, wanted, cancellationToken).ConfigureAwait(false);
            if (known.Length == 0)
                return records;

            string proplist = string.Join(",", known);
            IList<TikRecordSentence> flagRecords = await RunPrintQueryAsync(descriptor, false,
                (pars, from) => CliCommandBuilder.BuildPrintExpression(descriptor.CommandText, pars, from, proplist),
                cancellationToken).ConfigureAwait(false);
            return MergeById(records, flagRecords);
        }

        /// <summary>
        /// Whether this router's <c>print as-value</c> leaves the flag fields out, asked once per connection with
        /// <see cref="CliCommandBuilder.FlagsProbe"/>.
        /// </summary>
        /// <remarks>
        /// When the probe cannot answer (no row, or a refusal) the read itself decides, uncached: no row carrying
        /// any of the requested flags is how a pre-7.20 answer looks, since 7.20+ prints <c>disabled</c> on every
        /// row of a menu that has it.
        /// </remarks>
        private async Task<bool> AsValueOmitsFlagsAsync(IList<TikRecordSentence> records, string[] wanted,
            CancellationToken cancellationToken)
        {
            if (_asValueOmitsFlags == null)
            {
                IList<TikRecordSentence> probe;
                try
                {
                    string output = await ExecuteCliCommandAsync(CliCommandBuilder.FlagsProbe, cancellationToken)
                        .ConfigureAwait(false);
                    probe = CliOutputParser.ParseAsValue(output);
                }
                catch (TikCommandException)
                {
                    probe = new List<TikRecordSentence>();
                }

                if (probe.Count > 0)
                {
                    _asValueOmitsFlags = !probe[0].Words.ContainsKey("disabled");
                    if (_asValueOmitsFlags == true)
                        TikWireTrace.Emit("cli.flags", TikWireDir.Note,
                            "this router's 'print as-value' carries no flag fields (RouterOS before 7.20) — the flags "
                                + "an entity maps are read by name with a second print for the rest of this connection");
                }
            }

            if (_asValueOmitsFlags != null)
                return _asValueOmitsFlags.Value;
            return !records.Any(r => wanted.Any(w => r.Words.ContainsKey(w)));
        }

        /// <summary>
        /// The names in <paramref name="wanted"/> that the menu knows, checked once per menu and list on this
        /// connection with <see cref="CliCommandBuilder.BuildProplistCheck"/>: the whole list first, and each
        /// name alone only when the router refuses the list.
        /// </summary>
        private async Task<string[]> KnownFlagFieldsAsync(TikCommandDescriptor descriptor, string[] wanted,
            CancellationToken cancellationToken)
        {
            string key = descriptor.CommandText + "|" + string.Join(",", wanted);
            if (_knownFlagFields.TryGetValue(key, out string[]? cached))
                return cached;

            string[] known;
            if (await ProplistKnownAsync(descriptor, string.Join(",", wanted), cancellationToken).ConfigureAwait(false))
            {
                known = wanted;
            }
            else
            {
                var list = new List<string>();
                foreach (string name in wanted)
                    if (await ProplistKnownAsync(descriptor, name, cancellationToken).ConfigureAwait(false))
                        list.Add(name);
                known = list.ToArray();
            }

            _knownFlagFields[key] = known;
            return known;
        }

        /// <summary>
        /// Whether the menu knows every name in <paramref name="proplist"/>: an empty answer means yes,
        /// <see cref="CliCommandBuilder.ProplistRefusal"/> means no. Anything else is the router refusing the
        /// command itself, and is thrown — read as "unknown name", it would drop every flag without a word.
        /// </summary>
        private async Task<bool> ProplistKnownAsync(TikCommandDescriptor descriptor, string proplist,
            CancellationToken cancellationToken)
        {
            string output = await ExecuteCliCommandAsync(
                CliCommandBuilder.BuildProplistCheck(descriptor.CommandText, proplist), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return true;
            if (output.IndexOf(CliCommandBuilder.ProplistRefusal, StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            throw new TikCommandTrapException(CreateDummyCommand(descriptor),
                new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
        }

        /// <summary>
        /// Whether a counted read failed because the menu has no <c>detail</c> modifier — a singleton answers
        /// <c>bad parameter detail (line 1 column 27)</c> and no count marker.
        /// </summary>
        private static bool IsDetailRefusal(string? response)
            => response != null && response.IndexOf("bad parameter detail", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>See <see cref="TikProplist.Trim"/>.</summary>
        private static IList<TikRecordSentence> TrimToProplist(IList<TikRecordSentence> rows, string? proplist)
            => TikProplist.Trim(rows, proplist);

        /// <summary>
        /// Runs one monitor snapshot synchronously and returns its records — the read-method counterpart of
        /// <c>MonitorPollLoop</c>'s single iteration.
        /// </summary>
        /// <remarks>
        /// <c>includeFilters</c> is what makes this correct on the read path: by the time a descriptor gets
        /// here, <c>TikGenericCommand.ResolveParamsForRead</c> has rewritten the caller's parameters to Filter
        /// format, and a monitor has no query semantics for them to mean anything else.
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RunMonitorSnapshotAsync(
            TikCommandDescriptor descriptor, string verb, CancellationToken cancellationToken)
        {
            string cliText = CliCommandBuilder.BuildMonitorSnapshot(
                descriptor.CommandText, descriptor.Parameters,
                CliMonitorVerbs.SnapshotModifier(verb), includeFilters: true);

            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            return ParseRecords(output, descriptor);
        }

        /// <summary>
        /// Parses <c>as-value</c> output, treating text that yields no record as the router's refusal
        /// rather than as an empty result.
        /// </summary>
        /// <remarks>
        /// The positional rule in its general form, and it needs no phrase list: as-value output is
        /// <c>key=value;…</c> or nothing at all, so text that parses to no record is not as-value output —
        /// it is the router saying why there is none. An EMPTY table is empty output, and stays a legitimate
        /// empty result.
        /// <para>This covers ordinary reads as well as monitors, which is not where it started. A print that
        /// the router refuses answers with its complaint and no records: asking <c>/ip/dns</c> for
        /// <c>detail</c> — a modifier a singleton menu does not have — answers
        /// <c>bad parameter detail (line 1 column 27)</c>, and with that swallowed the caller was told the
        /// singleton simply had no rows. Every menu the transport audit compared this way looked like a
        /// transport that could not read singletons at all.</para>
        /// <para>Measured on 7.23.2:
        /// <c>:put [/interface monitor-traffic interface=kjdshfkjdhf once as-value]</c> answers
        /// <c>input does not match any value of interface</c> — a phrase no classifier in
        /// <see cref="CliErrorParser"/> matches, so the call returned "no rows" and the O/R layer reported
        /// success for a command the router had rejected. Unlike
        /// <see cref="CliErrorParser.IsSilentOnSuccessVerb"/> this needs no phrase list and no verb whitelist:
        /// output-with-no-record is the signal.</para>
        /// <para>
        /// Empty output is deliberately NOT an error — <c>/tool torch … as-value</c> legitimately prints
        /// nothing at all (see <see cref="CliMonitorVerbs"/>), and "nothing happened" is not a refusal.
        /// </para>
        /// </remarks>
        private IList<TikRecordSentence> ParseRecords(string output, TikCommandDescriptor descriptor)
        {
            IList<TikRecordSentence> rows = CliOutputParser.ParseAsValue(output);
            if (rows.Count == 0 && !string.IsNullOrWhiteSpace(output))
                throw new TikCommandTrapException(CreateDummyCommand(descriptor),
                    new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
            return rows;
        }

        /// <summary>
        /// Tri-state record of whether this router understands <c>:serialize</c> (RouterOS 7.13+):
        /// <c>null</c> = not established yet, <c>true</c> = a JSON read has succeeded, <c>false</c> = the
        /// router refused one and the plain form worked. Detected from what the router actually answers
        /// rather than from a parsed version string — a prompt/version assumption we cannot see failing is
        /// exactly the kind that costs 30 s a command with nothing going red.
        /// </summary>
        private bool? _serializeSupported;

        /// <summary>
        /// Runs the <c>get</c> verb and returns its answer as a single record.
        /// </summary>
        /// <remarks>
        /// The record carries one field, named after the requested <c>value-name</c> (or <c>ret</c> for a
        /// whole-row get). That name is what makes both scalar spellings work: <c>ExecuteScalar()</c> takes
        /// the first non-<c>.id</c> field, and <c>ExecuteScalar("name")</c> asks for it by name.
        /// <para>
        /// An empty answer is left as zero records rather than an empty-valued one. <c>get</c> is a read
        /// verb, so the layer above turns "no rows" into <see cref="TikNoSuchItemException"/> — which is what
        /// a get for a row that is not there means, and what the binary API reports for the same command.
        /// </para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RunGetAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string? id = TikGetResult.FindInput(descriptor.Parameters, TikSpecialProperties.Id);
            string? valueName = TikGetResult.FindInput(descriptor.Parameters, "value-name");

            string cliText = CliCommandBuilder.BuildGet(descriptor.CommandText, id, valueName);
            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));

            string value = (output ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
                return new List<TikRecordSentence>();

            return TikGetResult.One(string.IsNullOrEmpty(valueName) ? TikSpecialProperties.Ret : valueName!, value);
        }

        /// <summary>
        /// Runs one print query, in JSON form when <paramref name="wantJson"/> is set and this router has
        /// not already refused <c>:serialize</c>.
        /// <para>
        /// The fallback is deliberately driven by evidence rather than by phrase-matching the refusal (the
        /// wording differs by RouterOS version, so a phrase list silently stops matching): if the wrapped form
        /// is rejected while support is still unknown, the plain form is run, and only its SUCCESS
        /// downgrades this connection. When both fail, the plain form's error is what the caller sees —
        /// i.e. exactly today's behaviour — and nothing is concluded about <c>:serialize</c>. Once support
        /// is known either way, no retry happens again on this connection.
        /// </para>
        /// <para>
        /// <b>Either way the answer is counted.</b> A window states how many ids it held, and a whole-table
        /// read states how many records it produced (<see cref="CliCommandBuilder.BuildCountedRead"/>); the
        /// records parsed must match, or the read is refused with
        /// <see cref="TikConnectionResponseIncompleteException"/>. That turns "the answer ended at a prompt, so
        /// it is probably whole" into a statement by the router of what "whole" is.
        /// </para>
        /// </summary>
        /// <param name="descriptor">The read being run.</param>
        /// <param name="wantJson">Whether the caller asked for the JSON form.</param>
        /// <param name="buildExpression">
        /// The bare print expression (no <c>:put</c>) for a given parameter list and <c>from=</c> selector
        /// (<c>null</c> for the whole table) — wrapped here, differently for a window and for a counted
        /// whole-table read. The parameters are passed in rather than closed over because a window's print
        /// is built without the caller's filters: they go into the window's own <c>find</c> instead
        /// (<see cref="RunOneWindowAsync"/>).
        /// </param>
        /// <param name="cancellationToken">Cancels the read.</param>
        private async Task<IList<TikRecordSentence>> RunPrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            CancellationToken cancellationToken)
        {
            if (CanPage(descriptor, wantJson))
            {
                var paged = await RunPagedPrintQueryAsync(descriptor, wantJson, buildExpression,
                    CliReadPageSize, cancellationToken).ConfigureAwait(false);
                if (paged != null)
                    return paged;
            }

            _countedReadDepth++;
            try
            {
                return await RunOnePrintQueryAsync(descriptor, wantJson,
                        buildExpression(descriptor.Parameters, null), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _countedReadDepth--;
            }
        }

        /// <summary>
        /// Whether this read may be windowed. Paging is an optimisation for reading a whole table, and
        /// several shapes of read are not that.
        /// </summary>
        /// <remarks>
        /// <para>A read that names a row (<c>.id</c>) must not be windowed: the window is taken over the
        /// menu's id list and the caller's selector is applied inside it, so the row is simply absent from
        /// every window that does not contain it and the read fails with <c>no such item</c> — measured, 18
        /// tests on the MAC-Telnet leg.</para>
        /// <para>The JSON path is windowed too. Its <c>:serialize</c> discovery happens on the FIRST window
        /// rather than before the loop: a connection starts not knowing, and leaving the discovery read
        /// unpaged means the one read that most needs windowing — <c>/file print detail</c> — never gets it.
        /// Nothing has been returned at that point, so the loop simply restarts as as-value.</para>
        /// <para>A menu with no <c>find</c> verb cannot be windowed at all; that one is not predictable from
        /// the descriptor, so it is handled by falling back when the router says so.</para>
        /// <para>A <b>filtered</b> read is windowed: the clause goes into the window's <c>find</c>, which
        /// selects the same rows in the same order as the <c>where</c> it replaces (see
        /// <see cref="CliCommandBuilder.BuildPagedWindow"/>), so the windows cover matching rows only.</para>
        /// </remarks>
        private bool CanPage(TikCommandDescriptor descriptor, bool wantJson)
        {
            if (CliReadPageSize <= 0)
                return false;

            if (TikGetResult.FindInput(descriptor.Parameters, TikSpecialProperties.Id) != null)
                return false;
            // A FILTERED read is windowed like any other: its clause goes into the window's own 'find', so
            // the windows cover matching rows only (RunOneWindowAsync). What is refused is applying the
            // filter INSIDE an unfiltered window — correct, but it multiplies the work instead of shrinking
            // it (49 of 1672 mangle rows: 1190 ms that way, 72 ms with the filter in the 'find', router-side
            // at page size 20) and it breaks what '#w=' means, since a filter legitimately prints fewer rows
            // than the window held.
            return !_pagingUnavailable.Contains(descriptor.CommandText);
        }

        /// <summary>
        /// True while the command on the wire is a print whose answer this class will count against the
        /// router's own statement of its size — a window, or a whole-table read. The MAC-layer datagram-loss
        /// heuristic stands down for these: the count is exact and the heuristic is not, and it condemns
        /// complete answers now and then. It still covers every other command.
        /// </summary>
        protected bool IsCountedReadInFlight => _windowedReadDepth > 0 || _countedReadDepth > 0;

        private int _windowedReadDepth;
        private int _countedReadDepth;

        // Menus this connection has already found cannot be windowed — asked once, then remembered, so the
        // fallback costs one wasted request per menu per connection rather than one per read.
        private readonly HashSet<string> _pagingUnavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a table one window of rows at a time and concatenates them.
        /// </summary>
        /// <remarks>
        /// <para>Each window is a single command that picks a slice of the menu's id list, prints the rows it
        /// names and reports how many ids it held — so a table smaller than one page is answered by the first
        /// request and costs nothing extra, which is the common case. The loop ends when a window holds fewer
        /// ids than a full page.</para>
        /// <para><b>It is not a snapshot.</b> The windows are separate commands and each re-evaluates the id
        /// list, so a row inserted or removed part-way through shifts the ones after it and can be seen twice
        /// or missed. A single-command read cannot do that, which is why paging is off unless a transport or
        /// a caller asks for it.</para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>?> RunPagedPrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            int pageSize, CancellationToken cancellationToken)
        {
            var all = new List<TikRecordSentence>();
            int interruptions = 0;

            for (int offset = 0; ; offset += pageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PagedWindow window;
                _windowedReadDepth++;
                try
                {
                    window = await RunOneWindowAsync(descriptor, wantJson, buildExpression, offset, pageSize,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (TikCommandException) when (offset == 0 && wantJson && _serializeSupported == null)
                {
                    // The router refused ':serialize' and we did not yet know whether it can. Nothing has
                    // been handed back, so settle the question and take the same table again as as-value.
                    _serializeSupported = false;
                    TikWireTrace.Emit("cli.json", TikWireDir.Note,
                        "':serialize to=json' refused inside a paged read — this router is pre-7.13; "
                            + "re-reading as-value for the rest of this connection");
                    offset = -pageSize;   // the loop's increment puts it back to 0
                    continue;
                }
                catch (TikNoSuchCommandException)
                {
                    // The menu has no 'find' (a singleton, or a command menu). Nothing to window over, so
                    // read it the way every read worked before paging — and remember, so the next read of
                    // this menu does not pay for the discovery again.
                    if (offset > 0)
                        throw;   // mid-table: this is a real failure, not a menu that cannot be paged
                    _pagingUnavailable.Add(descriptor.CommandText);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "'" + descriptor.CommandText + "' has no 'find' verb — reading it in a single command");
                    return null;
                }
                finally
                {
                    _windowedReadDepth--;
                }

                if (window.Interrupted)
                {
                    // A row this window named vanished before it was printed, and RouterOS aborted the line. Not
                    // a menu without 'find' (at offset 0 that mistake would switch paging off for the rest of
                    // the connection), and not a failure of the read: the same window taken again re-runs
                    // 'find' and names the rows that exist now.
                    if (++interruptions > MaxInterruptedWindowRetries)
                        throw new TikConnectionResponseIncompleteException(
                            TransportName + ": RouterOS interrupted the window at offset " + offset + " of '"
                                + descriptor.CommandText + "' " + interruptions + " times in a row — rows are "
                                + "vanishing between the window's 'find' and its 'print' faster than it can be "
                                + "taken. A table that turns over this quickly reads reliably as one command: set "
                                + "CliReadPageSize to 0 for it, or use a transport that is not on the MAC layer.",
                            0, window.Response);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "window at offset " + offset + " of '" + descriptor.CommandText
                            + "' interrupted (a row vanished under it) — taking it again");
                    offset -= pageSize;   // the loop's increment brings it back to the same window
                    continue;
                }
                interruptions = 0;

                if (window.WindowSize < 0)
                {
                    // No '#w=' came back, so there is no way to know when to stop. Same treatment.
                    if (offset > 0)
                        throw new TikSentenceException(
                            "A paged read must be able to tell how many rows the window held, and the window "
                            + "at offset " + offset + " of '" + descriptor.CommandText + "' did not say.", null);
                    _pagingUnavailable.Add(descriptor.CommandText);
                    TikWireTrace.Emit("cli.page", TikWireDir.Note,
                        "'" + descriptor.CommandText + "' did not report a window size — reading it in a single command");
                    return null;
                }

                // A window's print carries no filter (the filter is in its 'find'), so every id it held names a
                // row and the router has just said how many records this answer must carry. Anything else is an answer
                // that lost rows on the way — or a parser that split or merged them — and either way it is not
                // the table. This is exact, which the MAC layer's datagram heuristic is not, and it is the only
                // check a window gets: a vanished id is not a way to get here, since 'print from=' answers
                // 'no such item' for it rather than skipping it (measured on 7.24).
                if (window.Records.Count != window.WindowSize)
                    throw new TikConnectionResponseIncompleteException(
                        TransportName + ": the window at offset " + offset + " of '" + descriptor.CommandText
                            + "' held " + window.WindowSize + " row(s) by the router's own count, and "
                            + window.Records.Count + " record(s) were read from its answer. The answer is not the "
                            + "table, so it is refused rather than returned. Retry the read.",
                        0, window.Response);

                foreach (var record in window.Records)
                    all.Add(record);

                // The window's OWN size ends the loop — the two are equal by now, but the size is what the
                // router stated and the record count is only what we made of it.
                if (window.WindowSize < pageSize)
                    return all;
            }
        }

        /// <summary>
        /// How many times one window is taken again after RouterOS interrupted it. Measured under heavy
        /// conntrack churn (~5000 rows turning over every 30 s), 17 of 26 reads of about 50 windows each hit
        /// one without retry — 1.4–2.4 % of windows — so three retries leave well under one in a million per
        /// window; a window that still fails is a table no windowed read can keep up with.
        /// </summary>
        private const int MaxInterruptedWindowRetries = 3;

        private readonly struct PagedWindow
        {
            internal PagedWindow(IList<TikRecordSentence> records, int windowSize, string? response,
                                 bool interrupted = false)
            {
                Records = records;
                WindowSize = windowSize;
                Response = response;
                Interrupted = interrupted;
            }

            internal IList<TikRecordSentence> Records { get; }
            internal int WindowSize { get; }
            internal string? Response { get; }

            /// <summary>RouterOS aborted the window's line: it answered <c>interrupted</c> and nothing else.</summary>
            internal bool Interrupted { get; }
        }

        /// <summary>
        /// Throws the router's own complaint when a read's whole answer is one parse error — a line ending in
        /// <c>(line N column M)</c>, such as <c>expected yes or no (line 1 column 62)</c> for a boolean compared
        /// with <c>true</c>. None of the phrases <see cref="CliErrorParser"/> knows covers it, so without this the
        /// read reported "the answer is incomplete", and a window at offset 0 switched paging off for the path.
        /// </summary>
        /// <remarks>
        /// Called only where the answer is already known to lack its closing marker, so a record whose text
        /// happens to end the same way cannot be taken for an error: a real answer always ends with the marker.
        /// </remarks>
        private void ThrowIfSyntaxError(string? output, TikCommandDescriptor descriptor)
        {
            var lines = (output ?? string.Empty).Split((char)10)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            if (lines.Count == 1 && SyntaxErrorLine.IsMatch(lines[0]))
                throw new TikCommandTrapException(CreateDummyCommand(descriptor), new TikTrapSentenceResult(lines[0]));
        }

        private static readonly System.Text.RegularExpressions.Regex SyntaxErrorLine =
            new System.Text.RegularExpressions.Regex(@"\(line \d+ column \d+\)$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// True when the router refused a window because the menu's <c>print</c> takes no <c>from=</c> —
        /// <c>bad parameter from (line 1 column N)</c>, which is what <c>/log print</c> answers (7.24). Such a
        /// menu cannot be windowed and is read in one command, like one without <c>find</c>.
        /// </summary>
        private static bool IsPrintWithoutFrom(string? output)
            => (output ?? string.Empty).Trim().StartsWith("bad parameter from (line ", StringComparison.Ordinal);

        /// <summary>
        /// True when RouterOS aborted the command line — what it answers, with no rows, no marker and no error
        /// text, when a window's <c>print from=</c> names an id that vanished after its <c>find</c> (measured on
        /// 7.24 under conntrack churn; Docs/findings-cli.md §1). The word alone on a line, so a field value
        /// cannot be mistaken for it.
        /// </summary>
        private static bool IsInterruptedAnswer(string? output)
            => (output ?? string.Empty).Split((char)10)
                .Any(line => string.Equals(line.Trim(), "interrupted", StringComparison.Ordinal));

        /// <summary>
        /// Takes one window: the caller's filters become the window's <c>find</c> clause, and the print
        /// inside it is built WITHOUT them.
        /// </summary>
        /// <remarks>
        /// The filter belongs in exactly one of the two places. In the <c>find</c> it shrinks the window to
        /// matching rows, which is the point; left in the print as well it would be redundant on every row
        /// the window already holds, and a row that stopped matching between the <c>find</c> and the
        /// <c>print</c> would make the answer shorter than the window's own <c>#w=</c> count — refused as an
        /// incomplete response, with no retry path. So it is dropped from the print, and a windowed read
        /// stays what it already was: not a snapshot, the rows as the router had them when each window ran.
        /// </remarks>
        private async Task<PagedWindow> RunOneWindowAsync(
            TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression,
            int offset, int pageSize, CancellationToken cancellationToken)
        {
            bool asJson = wantJson && _serializeSupported != false;
            string findClause = CliCommandBuilder.BuildWhereClause(descriptor.Parameters);
            string inner = CliCommandBuilder.WrapPut(
                buildExpression(WithoutFilters(descriptor.Parameters), CliCommandBuilder.WindowVariable), asJson);
            string cliText = CliCommandBuilder.BuildPagedWindow(descriptor.CommandText, inner, offset, pageSize,
                findClause);

            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));

            string body = SplitOffWindowMarker(output, out int windowSize);
            // A bad filter is the caller's error, not a menu that cannot be windowed — unless what the router
            // refused is the window's own 'from=', which a print such as '/log print' does not take.
            if (windowSize < 0 && !IsPrintWithoutFrom(output))
                ThrowIfSyntaxError(output, descriptor);
            if (windowSize < 0)   // caller decides: take it again if interrupted, otherwise fall back
                return new PagedWindow(new List<TikRecordSentence>(), -1, output, IsInterruptedAnswer(output));

            IList<TikRecordSentence> parsed = asJson
                ? CliJsonParser.ParseJson(body)
                : ParseRecords(body, descriptor);
            if (asJson)
                _serializeSupported = true;
            return new PagedWindow(parsed, windowSize, body);
        }

        /// <summary>
        /// The parameters a window's print is built from: everything except the Filter-format ones, which
        /// the window's <c>find</c> has taken over. The rest are print modifiers and inputs
        /// (<c>detail</c>, <c>numbers</c>, <c>once</c>) and must stay.
        /// </summary>
        private static IList<ITikCommandParameter> WithoutFilters(IList<ITikCommandParameter> parameters)
        {
            var kept = new List<ITikCommandParameter>(parameters.Count);
            foreach (var p in parameters)
            {
                if (p.ParameterFormat != TikCommandParameterFormat.Filter)
                    kept.Add(p);
            }
            return kept;
        }

        /// <summary>
        /// Removes the trailing <c>#w=</c> line a paged window ends with and reports the number it carried
        /// (<c>-1</c> when the line is absent).
        /// </summary>
        // CR, LF, the field separator and a space: whatever the marker line was preceded by.
        private static readonly char[] WindowMarkerTrim = { (char)13, (char)10, ';', ' ' };

        private static string SplitOffWindowMarker(string output, out int windowSize)
        {
            windowSize = -1;
            if (string.IsNullOrEmpty(output))
                return output;

            int at = output.LastIndexOf(CliCommandBuilder.WindowMarker, StringComparison.Ordinal);
            if (at < 0)
                return output;

            string tail = output.Substring(at + CliCommandBuilder.WindowMarker.Length).Trim();
            if (!int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int size))
                return output;

            windowSize = size;
            return output.Substring(0, at).TrimEnd(WindowMarkerTrim);
        }

        private async Task<IList<TikRecordSentence>> RunOnePrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson, string expression, CancellationToken cancellationToken)
        {
            if (wantJson && _serializeSupported != false)
            {
                try
                {
                    string jsonOutput = await ExecuteCliCommandAsync(
                        CliCommandBuilder.BuildCountedRead(expression, asJson: true), cancellationToken).ConfigureAwait(false);
                    CliErrorParser.ThrowIfError(jsonOutput, CreateDummyCommand(descriptor));
                    string jsonBody = SplitOffCountMarker(jsonOutput, descriptor, out int expectedJson);
                    IList<TikRecordSentence> records = CliJsonParser.ParseJson(jsonBody);
                    _serializeSupported = true;
                    return EnsureCounted(records, expectedJson, descriptor, jsonBody);
                }
                catch (TikCommandException ex) when (_serializeSupported == null)
                {
                    // Router refused the wrapped command and we do not yet know whether it can serialise at
                    // all. Fall through to the plain form; if THAT works, the router is pre-7.13.
                    TikWireTrace.Emit("cli.json", TikWireDir.Note,
                        "':serialize to=json' refused (" + ex.GetType().Name
                            + ") — retrying as-value to establish whether this router supports it");
                }
            }

            string output = await ExecuteCliCommandAsync(
                CliCommandBuilder.BuildCountedRead(expression, asJson: false), cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            string body = SplitOffCountMarker(output, descriptor, out int expected);

            if (wantJson && _serializeSupported == null)
            {
                // The plain form worked where the wrapped one did not: RouterOS < 7.13. Say so — a
                // connection reading free-text fields through as-value can silently shred them (P2.17),
                // and a degradation nobody can observe is the P2.23/P2.25 failure mode.
                _serializeSupported = false;
                TikWireTrace.Emit("cli.json", TikWireDir.Note,
                    "router does not support ':serialize' (pre-7.13) — falling back to as-value for the "
                        + "rest of this connection; fields holding free-form text may parse incorrectly");
            }

            return EnsureCounted(ParseRecords(body, descriptor), expected, descriptor, body);
        }

        /// <summary>
        /// Removes the trailing <see cref="CliCommandBuilder.CountMarker"/> line of a counted read and returns
        /// what precedes it, with the record count the marker states.
        /// </summary>
        /// <remarks>
        /// A counted read that does not end with a well-formed marker is refused, not passed through. The
        /// answer has already ended at a prompt and carried no error, so the router ran the whole line; the
        /// marker is its last output, and an answer missing it has lost something — nothing legitimate reaches
        /// here without one. The marker must also start its own line, so a field value that happens to contain
        /// the same text cannot be taken for it.
        /// </remarks>
        private string SplitOffCountMarker(string output, TikCommandDescriptor descriptor, out int expected)
        {
            expected = -1;
            string text = output ?? string.Empty;
            int at = text.LastIndexOf(CliCommandBuilder.CountMarker, StringComparison.Ordinal);
            if (at >= 0 && (at == 0 || text[at - 1] == (char)10 || text[at - 1] == (char)13))
                expected = CliCommandBuilder.ExpectedRecordCount(text.Substring(at + CliCommandBuilder.CountMarker.Length));

            if (expected < 0)
                ThrowIfSyntaxError(text, descriptor);
            if (expected < 0)
                throw new TikConnectionResponseIncompleteException(
                    TransportName + ": the read of '" + descriptor.CommandText + "' ended without the router's "
                        + "count of the records it holds, which it always writes last"
                        + (IsInterruptedAnswer(text) ? " — RouterOS interrupted the command" : "")
                        + ". The answer is incomplete, so it is refused rather than returned. Retry the read.",
                    0, output);

            return text.Substring(0, at).TrimEnd(WindowMarkerTrim);
        }

        /// <summary>
        /// Returns <paramref name="records"/> when there are as many as the router said, and refuses them
        /// otherwise — a short answer lost rows on the way, a long one had a row split by the parser, and
        /// neither is the table.
        /// </summary>
        private IList<TikRecordSentence> EnsureCounted(IList<TikRecordSentence> records, int expected,
            TikCommandDescriptor descriptor, string response)
        {
            if (records.Count == expected)
                return records;

            throw new TikConnectionResponseIncompleteException(
                TransportName + ": the router counted " + expected + " record(s) in its answer to '"
                    + descriptor.CommandText + "', and " + records.Count + " were read from it. The answer is not "
                    + "the table, so it is refused rather than returned. Retry the read.",
                0, response);
        }

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        protected override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string cliText = CliCommandBuilder.BuildAdd(descriptor.CommandText, descriptor.Parameters);
            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));

            string? id = ExtractAddId(output);
            if (id != null)
                return id;

            // No id in the answer, and the router did not complain — so the row is very probably ON the
            // router with nothing referring to it. This is reported rather than returned because both ways
            // of returning it were silently wrong: `null!` handed the caller a null through a non-nullable
            // contract, and the text-shaped fallback handed back whatever the last line happened to be,
            // which then travelled into the `[find where .id=…]` of the next set or remove and matched
            // nothing there.
            //
            // Either way the caller's own cleanup had no id to delete with, so the row outlived the process
            // that made it — the orphan the integration suite keeps finding, and the reason a later run on a
            // different transport fails with a name collision instead of with the original error.
            //
            // The cause is a read that settled too early: a terminal answers with an unframed byte stream,
            // so a reply arriving after the prompt has gone quiet is lost rather than late. That is not
            // repairable from here, but it is reportable — and what the router said is carried with it,
            // because "nothing arrived" and "something arrived that was not an id" are different diagnoses.
            string said = (output ?? string.Empty).Trim();
            throw new TikAddIdNotReadException(
                "the add reached the router but no .id came back, so the new row cannot be tracked and has "
                + "very likely been created anyway — look it up by a field the add set rather than repeating "
                + "the add, which would make a second row. The router answered: "
                + (said.Length == 0 ? "(nothing)" : "'" + said + "'"),
                CreateDummyCommand(descriptor),
                new TikDoneSentenceResult(said.Length == 0 ? null : said));
        }

        /// <summary>
        /// Extracts the new record's <c>.id</c> from an <c>add</c> response. Normally the cleaned output is a
        /// single <c>*N</c> token. But when a parameter VALUE contains newlines (e.g. a script <c>source</c>
        /// with embedded line breaks), RouterOS's line editor enters bracket-continuation mode and echoes the
        /// continuation lines (<c>["... …</c>) BEFORE printing the result, so the cleaned output is multi-line
        /// with the real id on the LAST non-empty line. So the id is looked for on every line, from the last
        /// backwards.
        /// <para>
        /// <c>null</c> means <b>no line was an id</b>, and the caller reports that rather than substituting
        /// something for it. There used to be a fallback to the last non-empty line, which turned a read
        /// that had gone wrong into an id-shaped string: it was stored on the entity and then reached the
        /// <c>[find where .id=…]</c> of the next set or remove, where it matched nothing.
        /// </para>
        /// </summary>
        private static string? ExtractAddId(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (CliValueNormalizer.IsRecordId(t))
                    return CliValueNormalizer.NormalizeId(t);   // pre-7.20 answers in lowercase hex
            }
            return null;
        }

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, reboot, …).
        /// </summary>
        protected override async Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string verb = TikPath.Verb(descriptor.CommandText);

            string cliText;
            switch (verb)
            {
                case "set":
                    cliText = CliCommandBuilder.BuildSet(descriptor.CommandText, descriptor.Parameters);
                    break;
                case "remove":
                    cliText = CliCommandBuilder.BuildRemove(descriptor.CommandText, descriptor.Parameters);
                    break;
                case "enable":
                case "disable":
                case "move":
                case "unset":
                case "comment":
                    // 'comment' is a real RouterOS menu command (it appears in Tab-completion next to
                    // set/remove/move), not a synonym for 'set comment='. It needs the same [find …]
                    // record selector as the others: without this case it fell through to BuildNonQuery,
                    // which emitted '/ip firewall filter comment .id=*104 comment=x' and the router
                    // answered "syntax error (line 1 column 29)" — the column of the bare '.id'.
                case "run":
                    // 'run' (e.g. /system/script/run) is an action verb: fire-and-forget over the terminal,
                    // no result set. ExecuteNonQuery is the supported entry point (ExecuteList throws).
                    cliText = CliCommandBuilder.BuildSimpleVerb(descriptor.CommandText, verb, descriptor.Parameters);
                    break;
                default:
                    cliText = CliCommandBuilder.BuildNonQuery(descriptor.CommandText, descriptor.Parameters);
                    break;
            }

            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            // set/remove/enable/disable/move/unset print nothing when they succeed, so anything left after
            // echo/prompt trimming is the router rejecting the command — catch it by position rather than
            // by phrase, or it is reported to the caller as success (P2.12).
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor),
                silentOnSuccess: CliErrorParser.IsSilentOnSuccessVerb(verb));
        }

        /// <summary>
        /// Raw pass-through scalar/non-query (CreateRawCommand): sends the command line verbatim and returns the
        /// cleaned terminal output text (ANSI-stripped, echo/prompt-trimmed by the transport).
        /// Used by <c>ExecuteScalar</c> (e.g. <c>/export</c>) and <c>ExecuteNonQuery</c>.
        /// </summary>
        /// <remarks>
        /// The output IS checked for a router error, which raw mode used to leave alone on the argument that
        /// it cannot know what counts as one for an arbitrary command. The argument is real but the trade is
        /// the wrong way round: not checking meant <c>bad command name prnt (line 1 column 12)</c> was
        /// returned as a successful value, to be assigned and used, while the command had never run. The
        /// check is <see cref="CliErrorParser"/>'s text-only one — the same the rest of the CLI path relies
        /// on — and it recognises a router error line rather than the word "error" appearing in a value.
        /// </remarks>
        protected override async Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string rawCli = descriptor.WrapAsValue
                ? WrapRawAsValue(descriptor.CommandText)
                : descriptor.CommandText;
            string output = (await ExecuteCliCommandAsync(rawCli, cancellationToken).ConfigureAwait(false) ?? string.Empty).Trim();
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            return output;
        }

        // Wraps a verbatim CLI line so RouterOS materialises its as-value output (bare 'print as-value' prints
        // nothing to a terminal — only script context, i.e. inside ':put [ … ]', emits the as-value line).
        private static string WrapRawAsValue(string rawCli)
            => ":put [" + (rawCli ?? string.Empty).Trim() + " as-value]";

        // True for verbs that perform an action rather than read records (no result set over a terminal).
        private static bool IsActionVerb(string verb) => verb == "run";

        // Actions reached through a read method because the binary API returns an empty row for them.
        // Kept minimal and explicit (like CliCommandBuilder.CliPresenceFlagFields); extend as more surface.
        private static bool IsEmptyRowAction(string verb) => verb == "wol";

        // Misuse of a read method (ExecuteList/ExecuteScalar/…) on an action command — guide to ExecuteNonQuery.
        private NotSupportedException ActionVerbOnReadPath(string commandText)
            => new NotSupportedException(
                $"'{commandText}' is an action command and returns no result set over the {DiagnosticPrefix} " +
                "transport. Invoke it with ExecuteNonQuery() instead of ExecuteList()/ExecuteScalar().");

        // ── Streaming monitor / async / listen (ITikMonitorTransport) ──────────

        /// <summary>
        /// Dispatches a callback-based async command (<c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/<c>LoadListenWithCallback</c>)
        /// onto a background worker. A terminal has no server push, so all three shapes are emulated by polling:
        /// <list type="bullet">
        ///   <item><c>/path/print</c> (LoadWithCallback) — run the read once off-thread, emit rows, complete.</item>
        ///   <item><c>/path/listen</c> — poll the table and diff snapshots by <c>.id</c>; an added/changed row
        ///         fires <paramref name="onRow"/>, a vanished <c>.id</c> fires a synthetic <c>.dead=true</c>
        ///         record (routed to <c>onDeleted</c> by the O/R layer).</item>
        ///   <item>a monitor verb (<c>monitor-traffic</c>, <c>profile</c>, <c>ping</c>, …) — re-issue a one-shot
        ///         <c>:put [… &lt;snapshot-modifier&gt; as-value]</c> every <see cref="MonitorPollIntervalMs"/> ms and
        ///         emit each polled record. <c>torch</c> is driven differently — see <c>TorchFreezeFrameLoop</c>.</item>
        /// </list>
        /// The worker takes the same command turn everything else does, so CRUD from another thread while a
        /// monitor runs is safe — it queues behind the poll rather than interleaving with it. What it is not
        /// is dependable in the other direction: the worker holds the terminal on its own cadence, so a
        /// change made over this <i>same</i> connection can fall between two polls and never be reported.
        /// See the thread-safety remarks on <see cref="CliConnectionBase"/>.
        /// </summary>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureOpened();
            string verb = TikPath.Verb(descriptor.CommandText);

            if (verb == "listen")
            {
                string listPath = TikPath.Parent(descriptor.CommandText);
                var printDescriptor = new TikCommandDescriptor(listPath + "/print", descriptor.Parameters);
                return PollingMonitorEngine.StartListen("cli-listen", this, printDescriptor, null, ListenPollIntervalMs,
                    onRow, onError, onDone);
            }

            if (verb == "print" || verb == "getall")
                return PollingMonitorEngine.StartWorker("cli-asynclist",
                    h => PollingMonitorEngine.AsyncListOnce(this, descriptor, h, onRow, onError, onDone));

            string modifier = CliMonitorVerbs.SnapshotModifier(verb);
            switch (CliMonitorVerbs.Classify(verb))
            {
                case CliMonitorVerbs.Kind.Once:
                    // Self-terminating (ping/traceroute): run the snapshot once, emit rows, complete.
                    return PollingMonitorEngine.StartWorker("cli-monitor-once",
                        h => SnapshotOnce(descriptor, modifier, h, onRow, onError, onDone));

                case CliMonitorVerbs.Kind.FreezeFrame:
                    // torch: driven by the dedicated freeze-frame-interval + proplist builder/parser pair
                    // (see CliMonitorVerbs), not the once/as-value machinery the other monitors use.
                    return PollingMonitorEngine.StartWorker("cli-monitor-torch",
                        h => TorchFreezeFrameLoop(descriptor, h, onRow, onError, onDone));

                default:
                    // Continuous monitor: re-issue the snapshot on a timer until cancelled.
                    return PollingMonitorEngine.StartWorker("cli-monitor",
                        h => MonitorPollLoop(descriptor, modifier, h, onRow, onError, onDone));
            }
        }

        // ── IPollingMonitorHost (shared listen/async-list scaffolding lives in PollingMonitorEngine) ──

        /// <inheritdoc/>
        bool IPollingMonitorHost.IsOpen => IsOpened;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
            => RunPrint(printDescriptor);   // RunPrint serialises via ExecuteCliCommand's _cmdLock

        /// <inheritdoc/>
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);

        // Monitor poll loop: re-issue the one-shot snapshot, emit each record, sleep, honour cancel.
        private void MonitorPollLoop(TikCommandDescriptor descriptor, string snapshotModifier, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                string cliText = CliCommandBuilder.BuildMonitorSnapshot(
                    descriptor.CommandText, descriptor.Parameters, snapshotModifier);

                while (!handle.CancelRequested)
                {
                    string output = ExecuteCliCommand(cliText);
                    CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                    // ParseRecords, not ParseAsValue: a rejected monitor (bad interface name) prints a
                    // diagnostic no phrase list catches, and a poll loop that shrugs it off spins forever
                    // delivering nothing instead of reporting the error once (P2.51).
                    foreach (var row in ParseRecords(output, descriptor))
                    {
                        if (handle.CancelRequested) break;
                        onRow?.Invoke(row);
                    }
                    PollingMonitorEngine.SleepInterruptible(MonitorPollIntervalMs, handle);
                }
            }
            catch (Exception ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }

        /// <summary>
        /// Self-terminating monitor (ping/traceroute): one execution → N rows → done, matching the binary
        /// API's async ping/traceroute rather than a repeating poll (its built-in count/duration bounds it).
        /// </summary>
        /// <remarks>
        /// The rows are streamed as the router produces them where the transport can do that. The command
        /// shape is what decides this, not the read loop: <c>:put [/ping … as-value]</c> hands <c>:put</c> an
        /// already-complete array, so RouterOS prints nothing at all until the ping has finished — measured
        /// on 7.23.2, a <c>count=20</c> ping delivered 0 rows for 20 s and then all 20 at once. The
        /// bare interactive form of the same command emits its header at +58 ms and a row every ~1000 ms, so
        /// that is what a streaming transport sends, reading it back with <see cref="CliTableParser"/>.
        /// A transport with no streaming driver keeps the as-value one-shot: later, but not wrong.
        /// </remarks>
        private void SnapshotOnce(TikCommandDescriptor descriptor, string snapshotModifier, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                string output;
                if (SupportsStreamingRead)
                {
                    var table = new CliTableParser();
                    output = ExecuteCliCommandStreaming(
                        CliCommandBuilder.BuildInteractiveMonitor(
                            descriptor.CommandText, descriptor.Parameters, snapshotModifier),
                        line =>
                        {
                            if (handle.CancelRequested) return;
                            TikRecordSentence? row = table.Feed(line);
                            if (row != null) onRow?.Invoke(row);
                        });
                    // Checked after the fact rather than per line: an error ends the command, so it is in the
                    // final output too, and a half-arrived line must never be classified as one.
                    CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                    // The table's own header is the acceptance signal: RouterOS prints it before the first
                    // reading, so text that never produced one is the refusal instead (P2.51). Keyed on the
                    // header rather than on the row count, because a monitor can legitimately be accepted and
                    // then print no reading.
                    if (!table.HasHeader && !string.IsNullOrWhiteSpace(output))
                        throw new TikCommandTrapException(CreateDummyCommand(descriptor),
                            new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
                    return;
                }

                output = ExecuteCliCommand(CliCommandBuilder.BuildMonitorSnapshot(
                    descriptor.CommandText, descriptor.Parameters, snapshotModifier));
                CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                foreach (var row in ParseRecords(output, descriptor))
                {
                    if (handle.CancelRequested) break;
                    onRow?.Invoke(row);
                }
            }
            catch (Exception ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }

        // torch: driven by freeze-frame-interval + an explicit proplist (see CliMonitorVerbs/
        // CliCommandBuilder.BuildTorchSnapshot) instead of once/as-value. Each execution blocks for
        // ~2×TorchFreezeFrameSeconds and flushes one complete frame, which IS the poll interval — no
        // separate SleepInterruptible is needed between iterations.
        private void TorchFreezeFrameLoop(TikCommandDescriptor descriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                string cliText = CliCommandBuilder.BuildTorchSnapshot(
                    descriptor.CommandText, descriptor.Parameters, TorchFreezeFrameSeconds);

                while (!handle.CancelRequested)
                {
                    string output = ExecuteCliCommand(cliText);
                    CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                    foreach (var row in CliOutputParser.ParseTorchFrame(output))
                    {
                        if (handle.CancelRequested) break;
                        onRow?.Invoke(row);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }
    }
}
