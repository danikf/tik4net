using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// A connection that serves the polling-based monitor/listen/async-list emulation shared by the CLI and
    /// native-WinBox transports. Both poll a request/reply channel (neither has RouterOS server push), so the
    /// transport only has to supply the snapshot primitive, a liveness flag and an error mapping; the loop
    /// scaffolding lives in <see cref="PollingMonitorEngine"/>.
    /// </summary>
    internal interface IPollingMonitorHost
    {
        /// <summary>True while the connection is open. A poll failure after close is graceful, not an error.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// Reads the current table for a <c>/path/print</c> descriptor, applying whatever serialization the
        /// transport requires (e.g. the native M2 command lock). Returns the rows with NO client-side query
        /// filtering — the engine evaluates <see cref="TikQueryStack"/> filters itself.
        /// </summary>
        IList<TikRecordSentence> PollSnapshot(TikCommandDescriptor printDescriptor);

        /// <summary>Maps a poll exception to a trap sentence (transports may add protocol-specific detail).</summary>
        TikTrapSentenceResult ToTrap(Exception ex);
    }

    /// <summary>
    /// Shared background-worker scaffolding for the polling monitor emulation (<c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/
    /// <c>LoadListenWithCallback</c>) used by <c>CliConnectionBase</c> and <c>WinboxNativeConnection</c>. A terminal / M2
    /// channel has no server push, so async-list, <c>/listen</c> and continuous monitors are emulated by polling
    /// the table on a background thread. The transport supplies an <see cref="IPollingMonitorHost"/>; the
    /// per-transport continuous-monitor body (CLI snapshot re-issue, native start→poll→cancel window) stays in
    /// the transport, since those differ fundamentally.
    /// </summary>
    internal static class PollingMonitorEngine
    {
        /// <summary>How often a polled listen re-reads its table (ms) — the same on every polling transport.</summary>
        internal const int ListenPollIntervalMs = 1000;

        /// <summary>listen, print and getall: served the same way by every polled transport; null for a monitor verb.</summary>
        /// <param name="namePrefix">The worker thread's name prefix: <c>cli</c>, <c>rest</c>, <c>winbox-native</c>.</param>
        /// <param name="host">The polling transport.</param>
        /// <param name="descriptor">The caller's command.</param>
        /// <param name="verb">The command's verb.</param>
        /// <param name="volatileFieldsOf">For a listen, the fields of the listed path its diff ignores; null compares all.</param>
        /// <param name="onRow">Receives each row.</param>
        /// <param name="onError">Receives the one error, if any.</param>
        /// <param name="onDone">Fires once when the worker ends.</param>
        public static TikMonitorHandle? TryStartTableShape(string namePrefix, IPollingMonitorHost host,
            TikCommandDescriptor descriptor, string verb, Func<string, ICollection<string>?>? volatileFieldsOf,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            if (verb == "listen")
            {
                string listPath = TikPath.Parent(descriptor.CommandText);
                return StartListen(namePrefix + "-listen", host, new TikCommandDescriptor(listPath + "/print", descriptor.Parameters),
                    volatileFieldsOf?.Invoke(listPath), ListenPollIntervalMs, onRow, onError, onDone);
            }
            if (verb == "print" || verb == "getall")
                return StartWorker(namePrefix + "-asynclist", h => AsyncListOnce(host, descriptor, h, onRow, onError, onDone));
            return null;
        }

        /// <summary>Runs a worker body: an error is reported once, unless the caller stopped it; onDone fires exactly once.</summary>
        /// <remarks>The error goes through <see cref="IPollingMonitorHost.ToTrap"/>, so every polled transport words it the same way.</remarks>
        public static void Run(IPollingMonitorHost host, TikMonitorHandle handle,
            Action<TikTrapSentenceResult> onError, Action onDone, Action body)
        {
            try { body(); }
            catch (Exception ex) { if (!Stopping(host, handle)) onError?.Invoke(host.ToTrap(ex)); }
            finally { onDone?.Invoke(); }
        }

        /// <summary>Hands rows to the caller until it cancels.</summary>
        public static void Emit(IEnumerable<TikRecordSentence> rows, TikMonitorHandle handle, Action<TikRecordSentence> onRow)
        {
            foreach (var row in rows)
            {
                if (handle.CancelRequested) break;
                onRow?.Invoke(row);
            }
        }

        /// <summary>Spins up a background worker bound to a fresh <see cref="TikMonitorHandle"/> and returns the handle.</summary>
        public static TikMonitorHandle StartWorker(string name, Action<TikMonitorHandle> body)
        {
            var handle = new TikMonitorHandle();
            var worker = new Thread(() => body(handle)) { IsBackground = true, Name = name };
            handle.AttachThread(worker);
            worker.Start();
            return handle;
        }

        /// <summary>A worker is "stopping" (so a transport error is expected, not reported) when the caller
        /// cancelled or the connection was closed out from under the poll — both are graceful, not failures.</summary>
        public static bool Stopping(IPollingMonitorHost host, TikMonitorHandle handle)
            => handle.CancelRequested || !host.IsOpen;

        /// <summary>Sleeps in short slices so cancel/close is responsive.</summary>
        public static void SleepInterruptible(int totalMs, TikMonitorHandle handle)
        {
            int slept = 0;
            while (slept < totalMs && !handle.CancelRequested) { Thread.Sleep(50); slept += 50; }
        }

        /// <summary>
        /// One-shot async list (LoadWithCallback on a <c>/path/print</c>): filter (?...) words are stripped and evaluated
        /// CLIENT-SIDE via the shared postfix query stack — the CLI <c>where</c> builder and the native getall
        /// cannot express the RouterOS stack (<c>?#|</c> / <c>?#&amp;</c> / <c>?#!</c>) — then the table is snapshotted
        /// once off-thread, matching rows are emitted and the worker completes.
        /// </summary>
        public static void AsyncListOnce(IPollingMonitorHost host, TikCommandDescriptor descriptor,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => Run(host, handle, onError, onDone, () =>
            {
                var filters = descriptor.Parameters
                    .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter).ToList();
                var nonFilter = descriptor.Parameters
                    .Where(p => p.ParameterFormat != TikCommandParameterFormat.Filter).ToList();
                var printDescriptor = new TikCommandDescriptor(descriptor.CommandText, nonFilter);

                Emit(host.PollSnapshot(printDescriptor).Where(row => filters.Count == 0 || TikQueryStack.Matches(row, filters)),
                    handle, onRow);
            });

        /// <summary>
        /// How many polls in a row a polled monitor or listen skips when the transport refuses their answer as
        /// incomplete (<see cref="TikConnectionResponseIncompleteException"/>) before it reports the refusal. Over
        /// MAC-Telnet a loaded router now and then drops part of its own output; the answer still ends at its prompt,
        /// so the terminal is in step, and the next poll is a whole new snapshot. What one skipped poll loses is one
        /// frame, not a row the caller will never see: a later poll reads the same state again.
        /// </summary>
        internal const int MaxIncompletePollsInARow = 3;

        /// <summary>
        /// Repeating-snapshot monitor: re-read the same one-shot snapshot every <paramref name="intervalMs"/>
        /// and emit its rows, until the caller cancels. This is the async face of a monitor whose values are
        /// ordinary read-only fields of a record rather than a streaming window — RouterOS answers
        /// <c>/interface/monitor-traffic</c> without <c>once</c> exactly this way, one row per interval.
        /// </summary>
        /// <remarks>
        /// The point of routing such a path here is that the synchronous and the asynchronous read then take
        /// the SAME snapshot, so they cannot disagree about what the command means.
        /// </remarks>
        public static void SnapshotLoop(IPollingMonitorHost host, TikCommandDescriptor descriptor, int intervalMs,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => Run(host, handle, onError, onDone, () =>
            {
                int incompleteInARow = 0;
                while (!handle.CancelRequested && host.IsOpen)
                {
                    IList<TikRecordSentence> rows;
                    try { rows = host.PollSnapshot(descriptor); }
                    catch (TikConnectionResponseIncompleteException) when (++incompleteInARow < MaxIncompletePollsInARow)
                    {
                        SleepInterruptible(intervalMs, handle);
                        continue;
                    }
                    incompleteInARow = 0;
                    Emit(rows, handle, onRow);
                    SleepInterruptible(intervalMs, handle);
                }
            });

        /// <summary>
        /// <c>/listen</c> emulation: poll the table and diff snapshots by <c>.id</c>. The baseline snapshot is read
        /// HERE, on the caller's thread, before the handle is returned — so a change made after the listen call
        /// returns is diffed against a table that predates it and reported, as the binary API's <c>=listen</c>
        /// reports it. The baseline itself is silent (RouterOS listen only pushes future deltas, never replays the
        /// table). A background worker then polls every <paramref name="pollIntervalMs"/>: an added/changed row is
        /// emitted as itself, and a vanished <c>.id</c> as a synthetic <c>.dead=true</c> record.
        /// <paramref name="onDone"/> fires once when cancelled. <paramref name="volatileFields"/> (e.g. native
        /// <c>ro:1</c> runtime counters) are excluded from the change signature so a counter tick is not mistaken
        /// for a config change; <c>null</c> compares all fields.
        /// </summary>
        /// <remarks>
        /// The baseline read costs the caller one table read (a second or more through a RoMON relay or over
        /// MAC-Telnet). A failure of it is not thrown: like every later poll failure it arrives as
        /// <paramref name="onError"/> followed by <paramref name="onDone"/>, from the worker thread, so the
        /// callbacks keep one shape whichever read failed.
        /// </remarks>
        public static TikMonitorHandle StartListen(string name, IPollingMonitorHost host, TikCommandDescriptor printDescriptor,
            ICollection<string>? volatileFields, int pollIntervalMs,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            Dictionary<string, string>? baseline = null;
            Exception? baselineError = null;
            try
            {
                baseline = new Dictionary<string, string>(StringComparer.Ordinal);
                Diff(host.PollSnapshot(printDescriptor), volatileFields, baseline, null);
            }
            catch (Exception ex)
            {
                baselineError = ex;
            }

            return StartWorker(name, handle =>
            {
                if (baselineError != null)
                {
                    Run(host, handle, onError, onDone, () => ExceptionDispatchInfo.Capture(baselineError).Throw());
                    return;
                }
                ListenLoop(host, printDescriptor, volatileFields, pollIntervalMs, baseline!, handle, onRow, onError, onDone);
            });
        }

        // The polls after the baseline: sleep, read, diff against the previous read, report what differs.
        private static void ListenLoop(IPollingMonitorHost host, TikCommandDescriptor printDescriptor,
            ICollection<string>? volatileFields, int pollIntervalMs, Dictionary<string, string> lastSig,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => Run(host, handle, onError, onDone, () =>
            {
                int incompleteInARow = 0;
                while (true)
                {
                    SleepInterruptible(pollIntervalMs, handle);
                    if (handle.CancelRequested) break;
                    IList<TikRecordSentence> rows;
                    // A skipped poll leaves lastSig as it was, so the next one is diffed against the last whole read.
                    try { rows = host.PollSnapshot(printDescriptor); }
                    catch (TikConnectionResponseIncompleteException) when (++incompleteInARow < MaxIncompletePollsInARow)
                    {
                        continue;
                    }
                    incompleteInARow = 0;
                    Diff(rows, volatileFields, lastSig, onRow);
                }
            });

        // Updates lastSig (.id → row signature) to the rows read, reporting each added/changed row and each
        // vanished .id to onRow. With onRow null it only records the baseline.
        private static void Diff(IList<TikRecordSentence> rows, ICollection<string>? volatileFields,
            Dictionary<string, string> lastSig, Action<TikRecordSentence>? onRow)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string? rid = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (rid == null) continue;
                seen.Add(rid);
                string sig = RowSignature(row, volatileFields);
                bool changed = !lastSig.TryGetValue(rid, out var prev) || prev != sig;
                lastSig[rid] = sig;
                if (changed) onRow?.Invoke(row);
            }

            foreach (var goneId in lastSig.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                lastSig.Remove(goneId);
                onRow?.Invoke(new TikRecordSentence(new Dictionary<string, string>
                {
                    { TikSpecialProperties.Id, goneId },
                    { ".dead", "true" },
                }));
            }
        }

        // Canonical signature of a record (sorted key=value), used to detect changes between listen polls.
        // Volatile fields (per-field runtime counters/status) are excluded when supplied.
        private static string RowSignature(TikRecordSentence row, ICollection<string>? volatileFields)
        {
            return string.Join("|", row.Words
                .Where(kv => volatileFields == null || !volatileFields.Contains(kv.Key))
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value));
        }
    }
}
