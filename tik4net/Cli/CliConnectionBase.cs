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
    /// Concrete transport subclasses must implement:
    /// <list type="bullet">
    ///   <item><see cref="TikCommandConnectionBase.Open(string, string, string)"/> / <see cref="TikCommandConnectionBase.Open(string, int, string, string)"/></item>
    ///   <item><see cref="TikCommandConnectionBase.OpenAsync(string, string, string)"/> / <see cref="TikCommandConnectionBase.OpenAsync(string, int, string, string)"/></item>
    ///   <item><see cref="TikCommandConnectionBase.Close"/></item>
    ///   <item><see cref="ExecuteCliCommandCoreAsync"/> — send one CLI string, return the response text.</item>
    /// </list>
    ///
    /// The response text passed to <see cref="ExecuteCliCommandCoreAsync"/> must already have ANSI
    /// escape sequences stripped (<see cref="VtStripper.StripAnsi"/>) and any terminal echo /
    /// prompt trimmed by the transport before being returned — the core layer only sees data lines.
    /// </summary>
    public abstract class CliConnectionBase : TikCommandConnectionBase, ITikMonitorTransport
    {
        /// <summary>Interval between monitor-snapshot polls (ms). Sub-second so callers see a fresh reading
        /// promptly; RouterOS GUI/webfig refresh ~1 s but a terminal snapshot is cheap.</summary>
        private const int MonitorPollIntervalMs = 500;
        /// <summary>Interval between /listen config-table polls (ms).</summary>
        private const int ListenPollIntervalMs = 1000;

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <summary>
        /// CLI transports support CRUD and (via polling) Listen/async: <c>ExecuteAsync</c>/<c>LoadAsync</c>/
        /// <c>LoadListenAsync</c> are emulated by re-issuing a one-shot snapshot/print on a background timer
        /// (see <see cref="ITikMonitorTransport"/> below). Streaming (<c>ExecuteListWithDuration</c>) is NOT
        /// reported — use the binary API for that.
        /// </summary>
        public override TikConnectionCapability Capabilities
            => TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode;

        // ── Transport — subclass contract ─────────────────────────────────────

        /// <summary>
        /// Sends <paramref name="cliText"/> to the router and returns the cleaned response text
        /// (ANSI stripped, echo and prompt removed). The implementation is responsible for all
        /// transport-specific concerns: framing, echo removal, paging (without-paging or equivalent).
        /// </summary>
        protected abstract Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct);

        // ── Semaphore-serialised execution ────────────────────────────────────

        /// <summary>
        /// Serialises access, fires diagnostics events, then delegates to
        /// <see cref="ExecuteCliCommandCoreAsync"/>.
        /// </summary>
        protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
        {
            await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                FireWriteRow(cliText);
                string result = await ExecuteCliCommandCoreAsync(cliText, ct).ConfigureAwait(false);
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
        /// the ANSI-stripped response read up to the next stable shell prompt. Unlike
        /// <see cref="ExecuteCliCommandCoreAsync"/> the bytes are sent verbatim and the output is not
        /// echo/prompt-stripped — the caller inspects the raw terminal reaction (e.g. <c>[Safe Mode taken]</c>).
        /// </summary>
        protected abstract Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct);

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

            // Action verbs invoked via ExecuteList (e.g. /system/script/run) are not reads — over a
            // terminal they execute fire-and-forget and yield no per-record !re output (unlike the
            // binary API). Run as an action and return an empty result set.
            if (GetVerb(descriptor.CommandText) == "run")
            {
                string runCli = CliCommandBuilder.BuildSimpleVerb(descriptor.CommandText, "run", descriptor.Parameters);
                string runOut = ExecuteCliCommand(runCli);
                CliErrorParser.ThrowIfError(runOut, CreateDummyCommand(descriptor));
                return new List<TikRecordSentence>();
            }

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
            string verb = GetVerb(descriptor.CommandText);

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
        /// Executes a scalar <c>get</c> command (already fully built by the command) and returns the raw value.
        /// </summary>
        internal override string RunScalarGet(string cliText)
        {
            EnsureOpened();
            string output = ExecuteCliCommand(cliText);
            // Error-check so RouterOS error text (e.g. "input does not match any value of value-name",
            // "syntax error …") is surfaced as the correct exception instead of leaking as a value.
            CliErrorParser.ThrowIfError(output, new TikGenericCommand(this, cliText));
            if (string.IsNullOrWhiteSpace(output))
                return null;
            return output.Trim();
        }

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
        ///         emit each polled record. Interactive-only verbs (<c>torch</c>) are rejected with guidance.</item>
        /// </list>
        /// The worker owns the (single request/reply) channel while polling; issuing concurrent CRUD on the
        /// same connection from another thread while a monitor is active is not supported.
        /// </summary>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureOpened();
            string verb = GetVerb(descriptor.CommandText);

            if (verb == "listen")
            {
                string listPath = StripLastSegment(descriptor.CommandText);
                var printDescriptor = new TikCommandDescriptor(listPath + "/print", descriptor.Parameters);
                return StartWorker("cli-listen", h => ListenLoop(printDescriptor, h, onRow, onError, onDone));
            }

            if (verb == "print" || verb == "getall")
                return StartWorker("cli-asynclist", h => AsyncListOnce(descriptor, h, onRow, onError, onDone));

            string modifier = CliMonitorVerbs.SnapshotModifier(verb);
            switch (CliMonitorVerbs.Classify(verb))
            {
                case CliMonitorVerbs.Kind.Once:
                    // Self-terminating (ping/traceroute): run the snapshot once, emit rows, complete.
                    return StartWorker("cli-monitor-once",
                        h => SnapshotOnce(descriptor, modifier, h, onRow, onError, onDone));

                case CliMonitorVerbs.Kind.InteractiveOnly:
                    // Cannot be polled over a terminal — report a guiding error (not a throw, so onDone still
                    // fires and the async contract is honoured) and finish.
                    return StartWorker("cli-monitor-interactive",
                        h => InteractiveOnlyEnd(descriptor, onError, onDone));

                default:
                    // Continuous monitor: re-issue the snapshot on a timer until cancelled.
                    return StartWorker("cli-monitor",
                        h => MonitorPollLoop(descriptor, modifier, h, onRow, onError, onDone));
            }
        }

        // Spins up a background worker bound to a fresh TikMonitorHandle and returns the handle.
        private TikMonitorHandle StartWorker(string name, Action<TikMonitorHandle> body)
        {
            var handle = new TikMonitorHandle();
            var worker = new Thread(() => body(handle)) { IsBackground = true, Name = name };
            handle.AttachThread(worker);
            worker.Start();
            return handle;
        }

        // A worker is "stopping" (so a transport error is expected, not reported) when the caller cancelled or
        // the connection was closed out from under the poll — both are graceful, not failures.
        private bool MonitorStopping(TikMonitorHandle handle) => handle.CancelRequested || !IsOpened;

        private static TikTrapSentenceResult ToTrap(Exception ex) => new TikTrapSentenceResult(ex.Message);

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
                    SleepInterruptible(MonitorPollIntervalMs, handle);
                }
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(ToTrap(ex));
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
                if (!MonitorStopping(handle)) onError?.Invoke(ToTrap(ex));
            }
            finally { onDone?.Invoke(); }
        }

        // Interactive-only verb (torch): surface a guiding error through the async error callback, then end.
        private void InteractiveOnlyEnd(TikCommandDescriptor descriptor,
            Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                onError?.Invoke(new TikTrapSentenceResult(
                    $"CLI transport: '{descriptor.CommandText}' is interactive-only — it repaints a VT100 screen " +
                    "and produces no as-value snapshot, so it cannot be polled over a terminal. Use the binary API " +
                    "transport (Streaming capability) for this command."));
            }
            finally { onDone?.Invoke(); }
        }

        // Async one-shot list (LoadAsync on a /print path): one read off-thread, emit rows, complete.
        // Filter (?...) words are stripped from the printed command and evaluated CLIENT-SIDE via the shared
        // query-stack — the CLI 'where' builder cannot express the RouterOS postfix stack (?#| / ?#& / ?#!),
        // so OR/AND/NOT queries would otherwise return nothing. Mirrors the WinBox native getall path.
        private void AsyncListOnce(TikCommandDescriptor descriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                var filters = descriptor.Parameters
                    .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter).ToList();
                var nonFilter = descriptor.Parameters
                    .Where(p => p.ParameterFormat != TikCommandParameterFormat.Filter).ToList();
                var printDescriptor = new TikCommandDescriptor(descriptor.CommandText, nonFilter);

                foreach (var row in RunPrint(printDescriptor))
                {
                    if (handle.CancelRequested) break;
                    if (filters.Count == 0 || TikQueryStack.Matches(row, filters))
                        onRow?.Invoke(row);
                }
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(ToTrap(ex));
            }
            finally { onDone?.Invoke(); }
        }

        // /listen emulation: poll the table and diff snapshots by .id. The first pass seeds silently (RouterOS
        // listen only pushes future deltas, never replays the table); afterwards an added/changed row is emitted
        // as itself, and a vanished .id as a synthetic ".dead=true" record. onDone fires once when cancelled.
        private void ListenLoop(TikCommandDescriptor printDescriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                var lastSig = new Dictionary<string, string>(StringComparer.Ordinal); // .id → row signature
                bool seeded = false;
                while (!handle.CancelRequested)
                {
                    IList<TikRecordSentence> rows = RunPrint(printDescriptor);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in rows)
                    {
                        string rid = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                        if (rid == null) continue;
                        seen.Add(rid);
                        string sig = RowSignature(row);
                        bool changed = !lastSig.TryGetValue(rid, out var prev) || prev != sig;
                        lastSig[rid] = sig;
                        if (seeded && changed) onRow?.Invoke(row);
                    }

                    if (seeded)
                        foreach (var goneId in lastSig.Keys.Where(k => !seen.Contains(k)).ToList())
                        {
                            lastSig.Remove(goneId);
                            onRow?.Invoke(new TikRecordSentence(new Dictionary<string, string>
                            {
                                { TikSpecialProperties.Id, goneId },
                                { ".dead", "true" },
                            }));
                        }
                    seeded = true;

                    SleepInterruptible(ListenPollIntervalMs, handle);
                }
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(ToTrap(ex));
            }
            finally { onDone?.Invoke(); }
        }

        // Canonical signature of a record (sorted key=value), used to detect changes between listen polls.
        // Unlike the native transport, CLI has no per-field read-only metadata, so all fields are compared;
        // listen is intended for config tables, where runtime counters are not present in 'print detail'.
        private static string RowSignature(TikRecordSentence row)
        {
            return string.Join("|", row.Words
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value));
        }

        // Sleep in short slices so Cancel/close is responsive.
        private static void SleepInterruptible(int totalMs, TikMonitorHandle handle)
        {
            int slept = 0;
            while (slept < totalMs && !handle.CancelRequested) { Thread.Sleep(50); slept += 50; }
        }

        // Returns the command path with its last (verb) segment removed (e.g. "/interface/listen" → "/interface").
        private static string StripLastSegment(string commandText)
        {
            string t = (commandText ?? string.Empty).TrimEnd('/');
            int idx = t.LastIndexOf('/');
            return idx > 0 ? t.Substring(0, idx) : t;
        }
    }
}
