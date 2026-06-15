using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Shared background-worker scaffolding for the polling monitor emulation (<c>ExecuteAsync</c>/<c>LoadAsync</c>/
    /// <c>LoadListenAsync</c>) used by <c>CliConnectionBase</c> and <c>WinboxNativeConnection</c>. A terminal / M2
    /// channel has no server push, so async-list, <c>/listen</c> and continuous monitors are emulated by polling
    /// the table on a background thread. The transport supplies an <see cref="IPollingMonitorHost"/>; the
    /// per-transport continuous-monitor body (CLI snapshot re-issue, native start→poll→cancel window) stays in
    /// the transport, since those differ fundamentally.
    /// </summary>
    internal static class PollingMonitorEngine
    {
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
        /// One-shot async list (LoadAsync on a <c>/path/print</c>): filter (?...) words are stripped and evaluated
        /// CLIENT-SIDE via the shared postfix query stack — the CLI <c>where</c> builder and the native getall
        /// cannot express the RouterOS stack (<c>?#|</c> / <c>?#&amp;</c> / <c>?#!</c>) — then the table is snapshotted
        /// once off-thread, matching rows are emitted and the worker completes.
        /// </summary>
        public static void AsyncListOnce(IPollingMonitorHost host, TikCommandDescriptor descriptor,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                var filters = descriptor.Parameters
                    .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter).ToList();
                var nonFilter = descriptor.Parameters
                    .Where(p => p.ParameterFormat != TikCommandParameterFormat.Filter).ToList();
                var printDescriptor = new TikCommandDescriptor(descriptor.CommandText, nonFilter);

                foreach (var row in host.PollSnapshot(printDescriptor))
                {
                    if (handle.CancelRequested) break;
                    if (filters.Count == 0 || TikQueryStack.Matches(row, filters))
                        onRow?.Invoke(row);
                }
            }
            catch (Exception ex)
            {
                if (!Stopping(host, handle)) onError?.Invoke(host.ToTrap(ex));
            }
            finally { onDone?.Invoke(); }
        }

        /// <summary>
        /// <c>/listen</c> emulation: poll the table and diff snapshots by <c>.id</c>. The first pass seeds silently
        /// (RouterOS listen only pushes future deltas, never replays the table); afterwards an added/changed row is
        /// emitted as itself, and a vanished <c>.id</c> as a synthetic <c>.dead=true</c> record. <paramref name="onDone"/>
        /// fires once when cancelled. <paramref name="volatileFields"/> (e.g. native <c>ro:1</c> runtime counters)
        /// are excluded from the change signature so a counter tick is not mistaken for a config change;
        /// <c>null</c> compares all fields.
        /// </summary>
        public static void ListenLoop(IPollingMonitorHost host, TikCommandDescriptor printDescriptor,
            ICollection<string> volatileFields, int pollIntervalMs, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                var lastSig = new Dictionary<string, string>(StringComparer.Ordinal); // .id → row signature
                bool seeded = false;
                while (!handle.CancelRequested)
                {
                    IList<TikRecordSentence> rows = host.PollSnapshot(printDescriptor);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in rows)
                    {
                        string rid = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                        if (rid == null) continue;
                        seen.Add(rid);
                        string sig = RowSignature(row, volatileFields);
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

                    SleepInterruptible(pollIntervalMs, handle);
                }
            }
            catch (Exception ex)
            {
                if (!Stopping(host, handle)) onError?.Invoke(host.ToTrap(ex));
            }
            finally { onDone?.Invoke(); }
        }

        // Canonical signature of a record (sorted key=value), used to detect changes between listen polls.
        // Volatile fields (per-field runtime counters/status) are excluded when supplied.
        private static string RowSignature(TikRecordSentence row, ICollection<string> volatileFields)
        {
            return string.Join("|", row.Words
                .Where(kv => volatileFields == null || !volatileFields.Contains(kv.Key))
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value));
        }
    }
}
