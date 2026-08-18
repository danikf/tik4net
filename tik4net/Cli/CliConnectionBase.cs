using System;
using System.Collections.Generic;
using System.Linq;
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
    public abstract class CliConnectionBase : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost,
        ITikCliCompletion, ITikCancellationModeConnection, ITikSafeModeConnection
    {
        /// <summary>Interval between monitor-snapshot polls (ms). Sub-second so callers see a fresh reading
        /// promptly; RouterOS GUI/webfig refresh ~1 s but a terminal snapshot is cheap.</summary>
        private const int MonitorPollIntervalMs = 500;
        /// <summary>Interval between /listen config-table polls (ms).</summary>
        private const int ListenPollIntervalMs = 1000;
        /// <summary>torch's <c>freeze-frame-interval</c> (seconds). Each poll blocks for
        /// <c>2×</c> this (see <see cref="CliCommandBuilder.BuildTorchSnapshot"/>) — confirmed live as the
        /// minimum that reliably flushes one frame — so no separate inter-poll sleep is needed.</summary>
        private const int TorchFreezeFrameSeconds = 2;

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <summary>
        /// CLI transports support CRUD and (via polling) Listen/async: <c>ExecuteAsync</c>/<c>LoadAsync</c>/
        /// <c>LoadListenAsync</c> are emulated by re-issuing a one-shot snapshot/print on a background timer
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
        /// Shared open: runs <paramref name="login"/> under the standard guard (a
        /// <see cref="TikConnectionLoginException"/> is rethrown as-is; any other exception is wrapped in one
        /// and the half-open client closed), then registers the driver delegates and marks the connection
        /// opened. Leaf transports build their concrete client and call this with delegates bound to it.
        /// </summary>
        protected void OpenWith(Func<CancellationToken, Task> login,
            Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close)
            => OpenWithAsync(login, send, sendRaw, close).GetAwaiter().GetResult();

        /// <summary>Async counterpart of <see cref="OpenWith"/>.</summary>
        protected async Task OpenWithAsync(Func<CancellationToken, Task> login,
            Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close)
        {
            try
            {
                await login(CancellationToken.None).ConfigureAwait(false);
            }
            catch (TikConnectionLoginException)
            {
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
            _close?.Invoke();
            _send = null;
            _sendRaw = null;
            _close = null;
            SetClosed();
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
                "in-flight cancel with TikCancellationMode.AbandonAndClose — closing the connection, "
                    + "the unread response cannot be resynchronized");
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
                string result = send(cliText, onLine, CancellationToken.None).GetAwaiter().GetResult();
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
        /// The control key used to be the only path, and it is the slow one: measured on 7.23.2 over Telnet,
        /// <c>Ctrl+D</c> is answered with nothing but a cursor save/restore (<c>&lt;CR&gt;&lt;ESC&gt;7&lt;ESC&gt;8</c>)
        /// — no confirmation line and no repainted prompt — so the read has nothing to terminate on and runs
        /// to the full receive deadline. The rollback itself does happen, which is why this cost 30 s per
        /// call instead of failing. The scriptable command answers normally and ends on a prompt.
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
        // for N ms" (settle), returning the ANSI-stripped reaction. Unlike the command/control-key drivers
        // (which read up to the next shell prompt), the Tab listing does NOT end in a bare prompt — RouterOS
        // redraws the prompt with the echoed stem — so it must be read on a settle window, not a prompt match.
        // A leaf transport registers this in Open only if it supports completion (currently Telnet); when it
        // is null, CompleteCli reports the transport does not support completion (fail-closed).
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
        /// Drives one Tab-completion probe and returns the ANSI-stripped terminal reaction.
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
                FireReadRow(reaction);

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
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override string RunRawText(TikCommandDescriptor descriptor)
            => RunRawTextAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        internal override async Task<IList<TikRecordSentence>> RunPrintAsync(
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

            bool needStats = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliStats);
            bool wantJson = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliJson);

            if (!needStats)
            {
                // Normal single-query path.
                return await RunPrintQueryAsync(descriptor, wantJson,
                    json => CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters, json),
                    cancellationToken).ConfigureAwait(false);
            }

            // Two-query path: detail (config) + stats (counters), merged by .id.
            IList<TikRecordSentence> configRecords = await RunPrintQueryAsync(descriptor, wantJson,
                json => CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters, json),
                cancellationToken).ConfigureAwait(false);

            IList<TikRecordSentence> statsRecords = await RunPrintQueryAsync(descriptor, wantJson,
                json => CliCommandBuilder.BuildPrintStats(descriptor.CommandText, descriptor.Parameters, json),
                cancellationToken).ConfigureAwait(false);

            // Build index of stats records by .id for O(1) lookup.
            var statsById = new Dictionary<string, TikRecordSentence>(StringComparer.OrdinalIgnoreCase);
            foreach (var sr in statsRecords)
            {
                string id = sr.GetResponseFieldOrDefault(TikSpecialProperties.Id, null!); // defaultValue is meant to accept null (interface out of scope here)
                if (id != null)
                    statsById[id] = sr;
            }

            if (statsById.Count == 0)
            {
                // Stats returned nothing or no .id — fall back gracefully to config-only.
                return configRecords;
            }

            // Merge: overlay stats fields onto each config record.
            var merged = new List<TikRecordSentence>(configRecords.Count);
            foreach (var cfg in configRecords)
            {
                string id = cfg.GetResponseFieldOrDefault(TikSpecialProperties.Id, null!); // defaultValue is meant to accept null (interface out of scope here)
                if (id == null || !statsById.TryGetValue(id, out TikRecordSentence? sr))
                {
                    // No matching stats record — keep config as-is.
                    merged.Add(cfg);
                    continue;
                }

                // Clone config fields and add missing stats fields.
                var mergedFields = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                // Start with config (includes all config fields + .id).
                foreach (var kv in cfg.Words)
                    mergedFields[kv.Key] = kv.Value;
                // Overlay stats: add fields not already present in config.
                foreach (var kv in sr.Words)
                {
                    if (!mergedFields.ContainsKey(kv.Key))
                        mergedFields[kv.Key] = kv.Value;
                }
                merged.Add(new TikRecordSentence(mergedFields));
            }

            return merged;
        }

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
            return ParseMonitorSnapshot(output, descriptor);
        }

        /// <summary>
        /// Parses a monitor snapshot's <c>as-value</c> output, treating text that yields no record as the
        /// router's refusal rather than as an empty result.
        /// </summary>
        /// <remarks>
        /// The positional rule of P2.12, narrowed to monitors: a monitor exists to produce output, so a
        /// snapshot that SUCCEEDS always prints at least one <c>.id=…</c> record, and a snapshot that fails
        /// prints its complaint and nothing else. Measured on 7.23.2:
        /// <c>:put [/interface monitor-traffic interface=kjdshfkjdhf once as-value]</c> answers
        /// <c>input does not match any value of interface</c> — a phrase no classifier in
        /// <see cref="CliErrorParser"/> matches, so the call returned "no rows" and the O/R layer reported
        /// success for a command the router had rejected (P2.51). Unlike
        /// <see cref="CliErrorParser.IsSilentOnSuccessVerb"/> this needs no phrase list and no verb whitelist:
        /// output-with-no-record is the signal.
        /// <para>
        /// Empty output is deliberately NOT an error — <c>/tool torch … as-value</c> legitimately prints
        /// nothing at all (see <see cref="CliMonitorVerbs"/>), and "nothing happened" is not a refusal.
        /// </para>
        /// </remarks>
        private IList<TikRecordSentence> ParseMonitorSnapshot(string output, TikCommandDescriptor descriptor)
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
        /// exactly what cost 30 s a command in P2.31.
        /// </summary>
        private bool? _serializeSupported;

        /// <summary>
        /// Runs one print query, in JSON form when <paramref name="wantJson"/> is set and this router has
        /// not already refused <c>:serialize</c>.
        /// <para>
        /// The fallback is deliberately driven by evidence rather than by phrase-matching the refusal (the
        /// wording differs by RouterOS version, and guessing it is how P2.12 shipped): if the wrapped form
        /// is rejected while support is still unknown, the plain form is run, and only its SUCCESS
        /// downgrades this connection. When both fail, the plain form's error is what the caller sees —
        /// i.e. exactly today's behaviour — and nothing is concluded about <c>:serialize</c>. Once support
        /// is known either way, no retry happens again on this connection.
        /// </para>
        /// </summary>
        private async Task<IList<TikRecordSentence>> RunPrintQueryAsync(
            TikCommandDescriptor descriptor, bool wantJson, Func<bool, string> buildCommand,
            CancellationToken cancellationToken)
        {
            if (wantJson && _serializeSupported != false)
            {
                try
                {
                    string jsonOutput = await ExecuteCliCommandAsync(buildCommand(true), cancellationToken).ConfigureAwait(false);
                    CliErrorParser.ThrowIfError(jsonOutput, CreateDummyCommand(descriptor));
                    IList<TikRecordSentence> records = CliJsonParser.ParseJson(jsonOutput);
                    _serializeSupported = true;
                    return records;
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

            string output = await ExecuteCliCommandAsync(buildCommand(false), cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));

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

            return CliOutputParser.ParseAsValue(output);
        }

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        internal override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string cliText = CliCommandBuilder.BuildAdd(descriptor.CommandText, descriptor.Parameters);
            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            // ExtractAddId can return null when the cleaned output is entirely whitespace after a
            // successful add - RunAddAsync's declared Task<string> is non-nullable, so this `!` matches the
            // base class contract rather than a proven-non-null value. See report: a real latent null path.
            return ExtractAddId(output)!;
        }

        /// <summary>
        /// Extracts the new record's <c>.id</c> from an <c>add</c> response. Normally the cleaned output is a
        /// single <c>*N</c> token. But when a parameter VALUE contains newlines (e.g. a script <c>source</c>
        /// with embedded line breaks), RouterOS's line editor enters bracket-continuation mode and echoes the
        /// continuation lines (<c>["... …</c>) BEFORE printing the result, so the cleaned output is multi-line
        /// with the real id on the LAST non-empty line. Prefer the last line that looks like an id
        /// (<c>*</c> + hex); otherwise fall back to the last non-empty line.
        /// </summary>
        private static string? ExtractAddId(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string? lastNonEmpty = null;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (t.Length == 0)
                    continue;
                if (lastNonEmpty == null)
                    lastNonEmpty = t;
                if (IsRecordId(t))
                    return t;
            }
            return lastNonEmpty ?? output.Trim();
        }

        // True when <paramref name="s"/> is a RouterOS record id: '*' followed by one or more hex digits.
        private static bool IsRecordId(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '*' || s.Length < 2)
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, reboot, …).
        /// </summary>
        internal override async Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
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
        /// cleaned terminal output text (ANSI-stripped, echo/prompt-trimmed by the transport). No error parsing
        /// by output text — raw mode cannot know what counts as an error for an arbitrary command, so the text is
        /// returned as-is. Used by <c>ExecuteScalar</c> (e.g. <c>/export</c>) and <c>ExecuteNonQuery</c>.
        /// </summary>
        internal override async Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string rawCli = descriptor.WrapAsValue
                ? WrapRawAsValue(descriptor.CommandText)
                : descriptor.CommandText;
            return (await ExecuteCliCommandAsync(rawCli, cancellationToken).ConfigureAwait(false) ?? string.Empty).Trim();
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
        /// Dispatches a callback-based async command (<c>ExecuteAsync</c>/<c>LoadAsync</c>/<c>LoadListenAsync</c>)
        /// onto a background worker. A terminal has no server push, so all three shapes are emulated by polling:
        /// <list type="bullet">
        ///   <item><c>/path/print</c> (LoadAsync) — run the read once off-thread, emit rows, complete.</item>
        ///   <item><c>/path/listen</c> — poll the table and diff snapshots by <c>.id</c>; an added/changed row
        ///         fires <paramref name="onRow"/>, a vanished <c>.id</c> fires a synthetic <c>.dead=true</c>
        ///         record (routed to <c>onDeleted</c> by the O/R layer).</item>
        ///   <item>a monitor verb (<c>monitor-traffic</c>, <c>profile</c>, <c>ping</c>, …) — re-issue a one-shot
        ///         <c>:put [… &lt;snapshot-modifier&gt; as-value]</c> every <see cref="MonitorPollIntervalMs"/> ms and
        ///         emit each polled record. <c>torch</c> is driven differently — see <c>TorchFreezeFrameLoop</c>.</item>
        /// </list>
        /// The worker owns the (single request/reply) channel while polling; issuing concurrent CRUD on the
        /// same connection from another thread while a monitor is active is not supported.
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
                return PollingMonitorEngine.StartWorker("cli-listen",
                    h => PollingMonitorEngine.ListenLoop(this, printDescriptor, null!, ListenPollIntervalMs, h, onRow, onError, onDone)); // volatileFields: none for CLI listen (out-of-scope signature is non-nullable)
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
                    // ParseMonitorSnapshot, not ParseAsValue: a rejected monitor (bad interface name) prints a
                    // diagnostic no phrase list catches, and a poll loop that shrugs it off spins forever
                    // delivering nothing instead of reporting the error once (P2.51).
                    foreach (var row in ParseMonitorSnapshot(output, descriptor))
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
        /// on 7.23.2, a <c>count=20</c> ping delivered 0 rows for 20 s and then all 20 at once (P2.50). The
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
                foreach (var row in ParseMonitorSnapshot(output, descriptor))
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
