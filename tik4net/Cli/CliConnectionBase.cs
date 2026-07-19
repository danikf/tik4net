using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

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
    public abstract class CliConnectionBase : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost, ITikCliCompletion
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
        /// </summary>
        public override TikConnectionCapability Capabilities
            => TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode
             | TikConnectionCapability.RawCommand;

        // ── Transport driver — subclass contract ──────────────────────────────

        // The active transport client is held as three delegates wired up by the leaf transport in its Open
        // (R6): send a CLI command, send raw bytes, and close. Holding delegates rather than a typed client
        // keeps the leaf's concrete client type (and any internal transport interface) out of this public
        // base's signatures — no public-API leak (a protected member cannot expose an internal type, CS0057)
        // — while still centralising the open/close/guard boilerplate the five CLI transports used to repeat.
        private Func<string, CancellationToken, Task<string>> _send;
        private Func<byte[], CancellationToken, Task<string>> _sendRaw;
        private Action _close;

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
        protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
        {
            var send = _send ?? throw NotOpen();
            await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                FireWriteRow(cliText);
                string result = await send(cliText, ct).ConfigureAwait(false);
                FireReadRow(result);
                return result;
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        /// <summary>Synchronous wrapper around <see cref="ExecuteCliCommandAsync"/>.</summary>
        protected string ExecuteCliCommand(string cliText)
            => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

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
        public override void SafeModeTake()
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
        public override void SafeModeRelease()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
            SendControlKey(CtrlX);
            SafeModeHeld = false;
        }

        /// <summary>
        /// Discards the safe-mode changes immediately and leaves Safe Mode by sending <c>Ctrl+D</c>, without
        /// dropping the connection. No-op when safe mode is not held.
        /// </summary>
        public override void SafeModeUnroll()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
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
        private Func<byte[], int, CancellationToken, Task<string>> _sendRawSettle;

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

        /// <summary>
        /// Executes a <c>print as-value</c> command and returns parsed sentences.
        /// When the <c>.cli-stats</c> marker is present in the descriptor's parameters,
        /// performs two queries (detail + stats) and merges the results by <c>.id</c>
        /// so that config fields and live counter fields are combined in each record.
        /// </summary>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
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
                string rawOutput = ExecuteCliCommand(rawCli);
                return CliOutputParser.ParseAsValue(rawOutput);
            }

            // Action verbs (e.g. /system/script/run) perform an action and produce no result set over a
            // terminal (no per-record !re output, unlike the binary API) — they belong on the non-query
            // path. Reject them on the read path so the misuse is explicit instead of silently returning an
            // empty list (R7); invoke them via ExecuteNonQuery (RunNonQuery handles the 'run' verb).
            if (IsActionVerb(TikPath.Verb(descriptor.CommandText)))
                throw ActionVerbOnReadPath(descriptor.CommandText);

            bool needStats = descriptor.Parameters.Any(p => p.Name == TikSpecialProperties.CliStats);

            if (!needStats)
            {
                // Normal single-query path.
                string cliText = CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters);
                string output = ExecuteCliCommand(cliText);
                CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                return CliOutputParser.ParseAsValue(output);
            }

            // Two-query path: detail (config) + stats (counters), merged by .id.
            string detailText = CliCommandBuilder.BuildPrint(descriptor.CommandText, descriptor.Parameters);
            string detailOutput = ExecuteCliCommand(detailText);
            CliErrorParser.ThrowIfError(detailOutput, CreateDummyCommand(descriptor));
            IList<TikRecordSentence> configRecords = CliOutputParser.ParseAsValue(detailOutput);

            string statsText = CliCommandBuilder.BuildPrintStats(descriptor.CommandText, descriptor.Parameters);
            string statsOutput = ExecuteCliCommand(statsText);
            CliErrorParser.ThrowIfError(statsOutput, CreateDummyCommand(descriptor));
            IList<TikRecordSentence> statsRecords = CliOutputParser.ParseAsValue(statsOutput);

            // Build index of stats records by .id for O(1) lookup.
            var statsById = new Dictionary<string, TikRecordSentence>(StringComparer.OrdinalIgnoreCase);
            foreach (var sr in statsRecords)
            {
                string id = sr.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
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
                string id = cfg.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null || !statsById.TryGetValue(id, out TikRecordSentence sr))
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
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        internal override string RunAdd(TikCommandDescriptor descriptor)
        {
            EnsureOpened();
            string cliText = CliCommandBuilder.BuildAdd(descriptor.CommandText, descriptor.Parameters);
            string output = ExecuteCliCommand(cliText);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
            return ExtractAddId(output);
        }

        /// <summary>
        /// Extracts the new record's <c>.id</c> from an <c>add</c> response. Normally the cleaned output is a
        /// single <c>*N</c> token. But when a parameter VALUE contains newlines (e.g. a script <c>source</c>
        /// with embedded line breaks), RouterOS's line editor enters bracket-continuation mode and echoes the
        /// continuation lines (<c>["... …</c>) BEFORE printing the result, so the cleaned output is multi-line
        /// with the real id on the LAST non-empty line. Prefer the last line that looks like an id
        /// (<c>*</c> + hex); otherwise fall back to the last non-empty line.
        /// </summary>
        private static string ExtractAddId(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string lastNonEmpty = null;
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
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
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
                case "run":
                    // 'run' (e.g. /system/script/run) is an action verb: fire-and-forget over the terminal,
                    // no result set. ExecuteNonQuery is the supported entry point (ExecuteList throws).
                    cliText = CliCommandBuilder.BuildSimpleVerb(descriptor.CommandText, verb, descriptor.Parameters);
                    break;
                default:
                    cliText = CliCommandBuilder.BuildNonQuery(descriptor.CommandText, descriptor.Parameters);
                    break;
            }

            string output = ExecuteCliCommand(cliText);
            CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
        }

        /// <summary>
        /// Raw pass-through scalar/non-query (CreateRawCommand): sends the command line verbatim and returns the
        /// cleaned terminal output text (ANSI-stripped, echo/prompt-trimmed by the transport). No error parsing
        /// by output text — raw mode cannot know what counts as an error for an arbitrary command, so the text is
        /// returned as-is. Used by <c>ExecuteScalar</c> (e.g. <c>/export</c>) and <c>ExecuteNonQuery</c>.
        /// </summary>
        internal override string RunRawText(TikCommandDescriptor descriptor)
        {
            EnsureOpened();
            string rawCli = descriptor.WrapAsValue
                ? WrapRawAsValue(descriptor.CommandText)
                : descriptor.CommandText;
            return (ExecuteCliCommand(rawCli) ?? string.Empty).Trim();
        }

        // Wraps a verbatim CLI line so RouterOS materialises its as-value output (bare 'print as-value' prints
        // nothing to a terminal — only script context, i.e. inside ':put [ … ]', emits the as-value line).
        private static string WrapRawAsValue(string rawCli)
            => ":put [" + (rawCli ?? string.Empty).Trim() + " as-value]";

        // True for verbs that perform an action rather than read records (no result set over a terminal).
        private static bool IsActionVerb(string verb) => verb == "run";

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
                    h => PollingMonitorEngine.ListenLoop(this, printDescriptor, null, ListenPollIntervalMs, h, onRow, onError, onDone));
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
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => new TikTrapSentenceResult(ex.Message);

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
                    foreach (var row in CliOutputParser.ParseAsValue(output))
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

        // Self-terminating monitor (ping/traceroute): run the snapshot command once (its built-in count/
        // duration bounds it), emit each resulting record, then complete — matches the binary API's
        // async ping/traceroute (one execution → N rows → done), not a repeating poll.
        private void SnapshotOnce(TikCommandDescriptor descriptor, string snapshotModifier, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                string cliText = CliCommandBuilder.BuildMonitorSnapshot(
                    descriptor.CommandText, descriptor.Parameters, snapshotModifier);
                string output = ExecuteCliCommand(cliText);
                CliErrorParser.ThrowIfError(output, CreateDummyCommand(descriptor));
                foreach (var row in CliOutputParser.ParseAsValue(output))
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
