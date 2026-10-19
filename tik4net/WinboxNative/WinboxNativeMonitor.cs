using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.WinboxNative
{
    /// <summary>
    /// The native transport's monitors: the streaming windows (<c>.jg</c> <c>type:'query'</c> and poll-action windows,
    /// started, polled and cancelled over M2), the synchronous read of a monitor window, the snapshot monitors that
    /// read the parent record (<c>/interface/monitor-traffic</c>), and the table shapes the polling engine runs.
    /// <see cref="WinboxNativeConnection"/> implements <see cref="ITikMonitorTransport"/> by forwarding here and stays
    /// the engine's <see cref="IPollingMonitorHost"/>. One per connection.
    /// </summary>
    internal sealed class WinboxNativeMonitor
    {
        private readonly IWinboxNativeHost _host;

        internal WinboxNativeMonitor(IWinboxNativeHost host)
        {
            _host = host;
        }

        // ── Streaming monitor (ExecuteWithCallback / LoadWithCallback) ──────────

        /// <summary>
        /// Runs a streaming-monitor command (e.g. <c>/tool/torch</c>, <c>/tool/profile</c>) on a background
        /// worker that polls the router every <c>autorefresh</c> ms over the normal M2 channel — start → poll →
        /// cancel (webfig <c>ObjectQuery</c>; see <c>Docs/winbox-native-m2-protocol.md</c> §20). Each polled record
        /// is decoded to API field names and pushed to <paramref name="onRow"/>; <paramref name="onDone"/> fires
        /// when the worker stops (cancelled, the router's "finished" flag, or an error — reported via
        /// <paramref name="onError"/>). Request parameters (NameValue) are encoded as the monitor's request fields.
        /// </summary>
        /// <remarks>The worker owns the M2 channel while polling; issuing concurrent CRUD on the same connection
        /// from another thread while a native monitor is active is not supported (the transport is request/reply).</remarks>
        internal TikMonitorHandle RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            _host.EnsureNativeOpen();
            descriptor = _host.Canonical(descriptor);
            string verb = TikPath.Verb(descriptor.CommandText);

            // /path/listen — RouterOS pushes add/change/delete deltas over the API. WinBox M2 has no server
            // push, so webfig (and we) emulate it the way it polls live config tables: getall on a timer and
            // diff snapshots by .id (see Docs/winbox-native-m2-protocol.md §20). Deleted rows are surfaced as a
            // synthetic ".dead=true" record so the O/R LoadListenAsync handler routes them to onDeleted. The diff
            // compares config fields only — runtime counters (ro:1: rx-byte, link-downs, …) tick every poll and
            // would otherwise make every row look "changed", whereas RouterOS listen emits on real changes.
            // /path/print (LoadAsync) — a one-shot async list, not a streaming window: run the print off the
            // calling thread, emit each row, then complete. No monitor cycle is involved.
            var table = PollingMonitorEngine.TryStartTableShape("winbox-native", _host, descriptor, verb, ReadOnlyFieldNames,
                onRow, onError, onDone);
            if (table != null) return table;

            // A snapshot monitor with no window of its own (/interface/monitor-traffic,
            // /interface/ethernet/monitor): its values are read-only fields of the PARENT record, which the
            // synchronous path reads with a getall + name filter (TryRunMonitor). Async re-reads that same
            // snapshot on a timer instead of resolving a monitor window, because the parent-handler fallback
            // below lands on the generic [20,0] interface window — a real monitor window, but the wrong one:
            // it started a monitor cycle whose records decoded to EMPTY rows, and the caller got two blank
            // records per second that its row count read as success (P2.52).
            if (IsSnapshotMonitorPath(descriptor.CommandText))
                return PollingMonitorEngine.StartWorker("winbox-native-monitor-snapshot",
                    handle => PollingMonitorEngine.SnapshotLoop(_host, descriptor, SnapshotMonitorIntervalMs, handle, onRow, onError, onDone));

            // Otherwise a streaming-monitor window (/tool/torch, /tool/profile, …).
            WinboxMonitorSpec? spec = ResolveMonitorWindow(descriptor.CommandText, out string apiPath, out int[]? handler);
            if (spec == null)
            {
                var cmd = _host.CommandFor(descriptor.CommandText);
                throw new TikPathNotMappedException(cmd, apiPath,
                    $"WinBox native: '{descriptor.CommandText}' is not a streaming-monitor window in the .jg catalog. " +
                    $"Add a PathOverride(\"{apiPath}\", new[]{{maj,min}}) to a monitor handler, or use a CLI transport.");
            }

            // handler is non-null here: ResolveMonitorWindow assigns it whenever it returns a spec,
            // and the no-spec case threw above.
            var resolver = _host.MakeResolver(apiPath, handler!);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            return PollingMonitorEngine.StartWorker("winbox-native-monitor",
                handle => MonitorLoop(spec, descriptor, resolver, keyToName, keyToField, handle, onRow, onError, onDone));
        }

        /// <summary>
        /// Finds the streaming-monitor window a command path names, or <c>null</c> when it names none. The
        /// monitor path is either the command path itself (<c>/ping</c>, <c>/interface/monitor-traffic</c>) or
        /// the parent of a trailing verb, so both are tried — in that order.
        /// </summary>
        /// <remarks>
        /// Shared by the async and the synchronous monitor paths deliberately: they must agree on WHICH window
        /// a command means, or the same command answers differently depending on the method used to call it —
        /// a defect nothing in the result can reveal.
        /// </remarks>
        private WinboxMonitorSpec? ResolveMonitorWindow(string commandText, out string apiPath, out int[]? handler)
        {
            apiPath = WinboxNativeConnection.ApiPathOf(commandText);
            handler = _host.HandlerMap.Resolve(apiPath);
            WinboxMonitorSpec? spec = _host.Catalog.GetMonitorByHandler(handler);
            if (spec != null) return spec;

            string parent = TikPath.Parent(commandText);
            int[]? ph = _host.HandlerMap.Resolve(parent);
            var pspec = _host.Catalog.GetMonitorByHandler(ph);
            if (pspec == null) return null;

            apiPath = parent;
            handler = ph;
            return pspec;
        }

        /// <summary>
        /// Runs a monitor window to completion on the CALLING thread and returns everything it produced — the
        /// read-method (<c>ExecuteList</c>/<c>LoadList</c>) counterpart of <see cref="MonitorLoop"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Before this existed, a synchronous <c>/ping</c> over native fell through to the generic getall on the
        /// monitor handler, which the router answers with no records at all — so <c>ToolPing.Execute</c>
        /// returned an empty list and the caller was told the ping had succeeded with nothing to report
        ///. A monitor window is not a table: its rows only exist while a monitor cycle is running.
        /// </para>
        /// <para>
        /// "To completion" means: until the router sets Finished (a self-terminating command — <c>ping
        /// count=N</c>, <c>traceroute</c>), or until the first pass ends without it (a continuous window —
        /// <c>monitor-traffic</c>, <c>torch</c> — whose pass IS one snapshot). That is the same rule the CLI
        /// transports get from the <c>once</c>/<c>count=1</c> snapshot modifier, and it matches what the binary
        /// API returns for the same command. A monitor the caller never bounded (a <c>/ping</c> with no
        /// <c>count</c>) blocks until the connection is closed — exactly as it does on the binary API, which
        /// waits for a <c>!done</c> that never comes.
        /// </para>
        /// <para>
        /// This is also the one place on the native transport where cancelling a running command is a
        /// router-side stop rather than a local abandon: the <c>finally</c> sends the window's
        /// <c>.jg</c>-declared <c>cancelcmd</c>, which is what WinBox itself sends when its torch/ping/scan
        /// window is closed, and every streaming window in the 7.23.2 catalog declares one (68 of them,
        /// exactly one per <c>startcmd</c>). That is what <see cref="TikConnectionCapability.CancelInFlight"/>
        /// means on this transport — see the remarks on <see cref="WinboxNativeConnection.Capabilities"/>.
        /// </para>
        /// </remarks>
        internal async Task<IList<TikRecordSentence>> RunMonitorWindowAsync(
            WinboxMonitorSpec spec, string apiPath, int[] handler, TikCommandDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            var resolver = _host.MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            var numFlags = resolver.BuildNumFlags();

            var rows = new List<TikRecordSentence>();
            uint? id = null;
            bool started = false;
            try
            {
                // allowReadOnly / includeFilters: a window's input fields are .jg-marked read-only (they are
                // display widgets in the GUI) yet are the monitor's request inputs, and the read path has
                // already rewritten the caller's parameters to Filter format — the same pair of exceptions
                // RunActionWindow makes, for the same reasons.
                var requestFields = await _host.EncodeNameValueFieldsAsync(
                    spec.Handler, descriptor, resolver, skipId: true, cancellationToken, allowReadOnly: true,
                    includeFilters: true, skipSnapshotModifier: true).ConfigureAwait(false);
                id = await _host.Ops.StartMonitorAsync(spec.Handler, spec.StartCmd, requestFields, cancellationToken)
                    .ConfigureAwait(false);
                started = true;

                // A self-terminating command's answer is everything it produces up to its own end, so a pass
                // that ends without Finished means "still working", not "that was the snapshot": a traceroute
                // republishes a longer table every autorefresh until the last hop is probed. A continuous
                // window has no end to wait for, so for those the first pass IS the answer.
                bool waitForDone = TikMonitorVerbs.SelfTerminating(TikPath.Verb(descriptor.CommandText));
                var deadline = DateTime.UtcNow.AddMilliseconds(_host.ReceiveTimeoutMs);

                // A caller's duration= keeps a continuous window open for that long and returns every pass, the
                // way the API streams a report per refresh until the duration ends (/tool/romon/discover
                // duration=3 → three reports). The window has no input for it — see IsMonitorSnapshotModifier.
                TimeSpan? duration = waitForDone ? null : MonitorDuration(descriptor);
                DateTime? runUntil = duration.HasValue ? DateTime.UtcNow + duration.Value : (DateTime?)null;
                if (runUntil.HasValue && runUntil.Value.AddMilliseconds(_host.ReceiveTimeoutMs) > deadline)
                    deadline = runUntil.Value.AddMilliseconds(_host.ReceiveTimeoutMs);

                WinboxM2Continuation? continuation = null;
                while (true)
                {
                    // contToken's parameter type isn't annotated nullable — PollMonitorRoundAsync lives in
                    // Winbox/, out of scope here — but null is its documented first-round value.
                    var (records, done, next) = await _host.Ops
                        .PollMonitorRoundAsync(spec.Handler, spec.PollCmd, id, spec.IsQuery, continuation!, cancellationToken)
                        .ConfigureAwait(false);
                    continuation = next;

                    await _host.Codec.PrimeReferencesAsync(records, keyToName, keyToField, cancellationToken).ConfigureAwait(false);
                    foreach (var rec in records)
                        rows.Add(new TikRecordSentence(_host.Codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields, numFlags,
                            resolver.ExtraSpellings)));

                    if (done) break;                    // router set Finished: the command is over
                    if (continuation != null) continue; // mid-pass: keep reading this one
                    if (runUntil.HasValue)
                    {
                        if (DateTime.UtcNow >= runUntil.Value) break;   // the caller's duration is over
                    }
                    else if (!waitForDone) break;       // continuous window: one pass is the snapshot

                    // Bounded so a command that never finishes fails like any other unfinished read instead of
                    // hanging on this thread forever.
                    if (DateTime.UtcNow >= deadline)
                        throw new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(_host.ReceiveTimeoutMs),
                            $"WinBox native: '{descriptor.CommandText}' produced {rows.Count} row(s) but never " +
                            $"reported itself finished within {_host.ReceiveTimeoutMs} ms.");
                    await Task.Delay(Math.Max(100, spec.AutorefreshMs), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (WinboxM2OperationException ex) { throw _host.TranslateM2Error(ex, descriptor.CommandText); }
            finally
            {
                if (started && _host.IsOpened)
                {
                    // Deliberately NOT passed the caller's token: this is the request that tells the router to
                    // stop, so cancelling it would leave the window running on the router — the exact failure
                    // the capability claims not to have. Best-effort otherwise (the monitor may have ended).
                    try { _host.Ops.CancelMonitor(spec.Handler, spec.CancelCmd, id); }
                    catch { /* best-effort — the rows above are the result */ }
                }
            }
            return rows;
        }

        // How often a snapshot monitor with no window of its own (/interface/monitor-traffic) re-reads its parent record.
        private const int SnapshotMonitorIntervalMs = 1000;

        // The monitor worker: encode request fields → start → poll loop (emit decoded rows, sleep autorefresh,
        // honour cancel/finished) → cancel. Request-field encoding runs here (not on the caller) so a runtime
        // resolution failure (e.g. interface not found) surfaces async via onError, matching the API transport,
        // instead of throwing synchronously out of ExecuteWithCallback. An M2 error reaches onError through
        // IPollingMonitorHost.ToTrap. The window is cancelled before onDone, which fires exactly once.
        private void MonitorLoop(WinboxMonitorSpec spec, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            IReadOnlyDictionary<int, string> keyToName, IReadOnlyDictionary<int, WinboxJgField> keyToField,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => PollingMonitorEngine.Run(_host, handle, onError, onDone, () =>
            {
                uint? id = null;
                bool started = false;
                var numFlags = resolver.BuildNumFlags();
                try
                {
                    using (_host.EnterCommand())
                    {
                        // Encode the caller's NameValue parameters as the monitor's request fields (interface, cpu, …).
                        // allowReadOnly: a window's input fields are often .jg-marked ro (display) yet are the
                        // monitor's legitimate request inputs and must be sent (e.g. ping 'address').
                        var requestFields = _host.EncodeNameValueFields(
                            spec.Handler, descriptor, resolver, skipId: true, _host.IdResolver.ResolveReference,
                            allowReadOnly: true);
                        id = _host.Ops.StartMonitor(spec.Handler, spec.StartCmd, requestFields);
                        started = true;
                    }

                    // The pass is driven here rather than inside the operations layer, because this is where the
                    // cancel handle and the row callback live — and because a pass is unbounded in time (see
                    // PollMonitorRound). `continuation != null` means the router is still mid-pass, so the next
                    // round goes out immediately; the autorefresh sleep applies only BETWEEN passes.
                    WinboxM2Continuation? continuation = null;
                    while (!handle.CancelRequested)
                    {
                        bool done;
                        List<Dictionary<int, Tuple<string, object>>> records;
                        // Gated per ROUND, not per pass: on a multiplexed connection this is a no-op, but a
                        // 30-second ping must not hold the lockstep gate for its whole duration (design §2).
                        // contToken's parameter type isn't annotated nullable (PollMonitorRound is out of scope
                        // here), but null is its documented first-round value.
                        using (_host.EnterCommand())
                            (records, done, continuation) =
                                _host.Ops.PollMonitorRound(spec.Handler, spec.PollCmd, id, spec.IsQuery, continuation!);

                        // Emitted per round, so a streaming window (ping, traceroute, torch) reaches the caller
                        // as the router produces it instead of in a lump when the pass ends.
                        foreach (var rec in records)
                            onRow?.Invoke(new TikRecordSentence(_host.Codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields, numFlags,
                                resolver.ExtraSpellings)));

                        if (done) break;              // router set Finished: the operation is over for good
                        if (continuation != null) continue;   // same pass, next record — no sleep

                        // The pass ended without Finished: an autorefresh snapshot window (Torch, Scan, …) whose
                        // getall lists what exists right now. Wait the interval, then start a fresh pass.
                        // Sleep in short slices so Cancel stays responsive.
                        PollingMonitorEngine.SleepInterruptible(Math.Max(100, spec.AutorefreshMs), handle);
                    }
                }
                finally
                {
                    if (started && _host.IsOpened)
                    {
                        // (The gate is entered inside the try: the previous shape released a semaphore it had
                        // not necessarily acquired when the Wait itself threw during teardown.)
                        try
                        {
                            using (_host.EnterCommand())
                                _host.Ops.CancelMonitor(spec.Handler, spec.CancelCmd, id);
                        }
                        catch { /* best-effort */ }
                    }
                }
            });

        // The set of read-only (ro:1) field names for a table's handler — the volatile runtime fields a listen
        // diff must ignore. Empty when the path has no handler/catalog entry (then all fields are compared).
        private HashSet<string> ReadOnlyFieldNames(string apiPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[]? handler = _host.HandlerMap.Resolve(apiPath);
            if (handler == null) return set;
            var resolver = _host.MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var kv in keyToField)
                if (kv.Value != null && kv.Value.ReadOnly && keyToName.TryGetValue(kv.Key, out var n))
                    set.Add(n);
            return set;
        }

        // Attempts a "monitor [once]" snapshot (e.g. /interface/ethernet/monitor numbers=ether1). The monitored
        // values (rate, link status, auto-negotiation, full-duplex) are read-only fields on the parent
        // interface record — webfig surfaces them as a Status tab of the [20,0] window, not a separate handler.
        // A getall on the parent handler filtered to the named interface yields the same single snapshot the
        // RouterOS "monitor once" returns. Returns false (fall through) when this is not a monitor path.
        /// <summary>
        /// True when the path is a "monitor once"-style snapshot served from the parent record rather than
        /// from a monitor window of its own — the async counterpart of the <see cref="TryRunMonitorAsync"/>
        /// test, kept next to it so the two stay in step.
        /// </summary>
        private bool IsSnapshotMonitorPath(string commandText)
            => IsSnapshotMonitorVerb(TikPath.Verb(commandText))
               && _host.HandlerMap.Resolve(WinboxNativeConnection.ApiPathOf(commandText)) == null
               && _host.HandlerMap.Resolve(TikPath.Parent(commandText)) != null;

        // The monitor verbs whose readings are fields of the parent record rather than a window of their own.
        private static bool IsSnapshotMonitorVerb(string verb)
            => string.Equals(verb, "monitor", StringComparison.OrdinalIgnoreCase)
               || string.Equals(verb, "monitor-traffic", StringComparison.OrdinalIgnoreCase);

        /// <returns>
        /// The snapshot rows, or <c>null</c> when this is not a snapshot-monitor path and the caller should
        /// fall through. A null return rather than a <c>bool</c> + <c>out</c> because an <c>out</c> parameter
        /// cannot be written across an <c>await</c>.
        /// </returns>
        internal async Task<IList<TikRecordSentence>?> TryRunMonitorAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (!IsSnapshotMonitorVerb(TikPath.Verb(descriptor.CommandText)))
                return null;

            string parentPath = TikPath.Parent(descriptor.CommandText);
            int[]? handler = _host.HandlerMap.Resolve(parentPath);
            if (handler == null) return null;

            // The interface is named via 'numbers' (RouterOS monitor convention), or 'interface'/'.id'.
            string? target = WinboxNativeConnection.FindParam(descriptor, "numbers")
                ?? WinboxNativeConnection.FindParam(descriptor, "interface")
                ?? WinboxNativeConnection.FindParam(descriptor, TikSpecialProperties.Id);
            if (string.IsNullOrEmpty(target)) return null;

            // Keys and overrides come from the PARENT window (that is where the values live), but the field
            // NAMES come from the monitor path's own alias set — the same record is called 'rx' in the
            // interface list and 'rx-bits-per-second' by /interface/monitor-traffic, and a caller asking for
            // the monitor must get the monitor's names.
            var resolver = new WinboxFieldResolver(WinboxNativeConnection.ApiPathOf(descriptor.CommandText), handler, _host.Catalog,
                _host.OverridesFor(parentPath), _host.UseGuiNames, _host.HandlerMap.ResolveDerivedKey(parentPath),
                labelHints: _host.LabelHints);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            var numFlags = resolver.BuildNumFlags();
            int flags = _host.Catalog.GetReadFlags(handler);

            List<Dictionary<int, Tuple<string, object>>> records;
            try { records = await _host.Ops.GetAllAsync(handler, cancellationToken, flags).ConfigureAwait(false); }
            catch (WinboxM2OperationException ex) { throw _host.TranslateM2Error(ex, descriptor.CommandText); }

            await _host.Codec.PrimeReferencesAsync(records, keyToName, keyToField, cancellationToken).ConfigureAwait(false);

            var result = new List<TikRecordSentence>();
            foreach (var rec in records)
            {
                var decoded = _host.Codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields, numFlags,
                            resolver.ExtraSpellings);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, target, StringComparison.Ordinal))
                {
                    result.Add(new TikRecordSentence(decoded));
                    break;
                }
            }
            return result;
        }

        // The caller's 'duration=' on a monitor, or null. A value the router would refuse is refused here too,
        // rather than silently turning a bounded scan into a single pass.
        private TimeSpan? MonitorDuration(TikCommandDescriptor descriptor)
        {
            foreach (var p in descriptor.Parameters)
            {
                if (!string.Equals(p.Name.TrimStart('?', '='), WinboxNativeWriter.MonitorDurationParam, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TikDuration.TryParseTimeSpan(p.Value, out TimeSpan span)) return span;
                if (int.TryParse(p.Value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int seconds))
                    return TimeSpan.FromSeconds(seconds);
                throw new TikCommandTrapException(_host.CommandFor(descriptor),
                    new TikTrapSentenceResult($"invalid value for argument duration: {p.Value}"));
            }
            return null;
        }
    }
}
