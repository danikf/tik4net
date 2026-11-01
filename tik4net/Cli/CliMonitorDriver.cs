using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// The CLI's monitor loops: a terminal has no server push, so a monitor is a snapshot taken again on a timer, a
    /// self-terminating one (ping, traceroute) is streamed once, and torch is read frame by frame. The table shapes
    /// (<c>print</c>, <c>listen</c>) are <see cref="PollingMonitorEngine"/>'s; the connection stays the engine's
    /// <see cref="IPollingMonitorHost"/>. One per connection.
    /// </summary>
    internal sealed class CliMonitorDriver
    {
        private readonly ICliCommandHost _host;

        internal CliMonitorDriver(ICliCommandHost host)
        {
            _host = host;
        }

        /// <summary>Interval between monitor-snapshot polls (ms). Sub-second so callers see a fresh reading
        /// promptly; RouterOS GUI/webfig refresh ~1 s but a terminal snapshot is cheap.</summary>
        private const int MonitorPollIntervalMs = 500;
        /// <summary>torch's <c>freeze-frame-interval</c> (seconds). Each poll blocks for
        /// <c>2×</c> this (see <see cref="CliCommandBuilder.BuildTorchSnapshot"/>) — confirmed live as the
        /// minimum that reliably flushes one frame — so no separate inter-poll sleep is needed.</summary>
        private const int TorchFreezeFrameSeconds = 2;

        /// <summary>
        /// Starts the worker for a monitor verb: once for a self-terminating one (ping, traceroute), the torch
        /// freeze-frame loop, or the snapshot poll loop for every other monitor.
        /// </summary>
        internal TikMonitorHandle Start(TikCommandDescriptor descriptor, string verb,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
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

        // Monitor poll loop: re-issue the one-shot snapshot, emit each record, sleep, honour cancel.
        private void MonitorPollLoop(TikCommandDescriptor descriptor, string snapshotModifier, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => PollingMonitorEngine.Run(_host, handle, onError, onDone, () =>
            {
                while (!handle.CancelRequested)
                {
                    // The same snapshot a one-shot read takes, so RouterOS 6 — which has no as-value on its
                    // monitors — is polled through the plain table too. A rejected monitor (bad interface name)
                    // throws out of it rather than being shrugged off, so the loop reports the error once
                    // instead of spinning with nothing to deliver.
                    var rows = _host.RunMonitorSnapshotAsync(descriptor, snapshotModifier, includeFilters: false,
                        CancellationToken.None).GetAwaiter().GetResult();
                    PollingMonitorEngine.Emit(rows, handle, onRow);
                    PollingMonitorEngine.SleepInterruptible(MonitorPollIntervalMs, handle);
                }
            });

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
            => PollingMonitorEngine.Run(_host, handle, onError, onDone, () =>
            {
                string output;
                if (_host.SupportsStreamingRead)
                {
                    var table = new CliTableParser();
                    output = _host.ExecuteStreaming(
                        CliCommandBuilder.BuildInteractiveMonitor(
                            descriptor.CommandText, descriptor.Parameters, snapshotModifier),
                        line =>
                        {
                            if (handle.CancelRequested) return;
                            TikRecordSentence? row = table.Feed(line);
                            if (row != null) onRow?.Invoke(row);
                        });
                    // The table's own header is the acceptance signal: RouterOS prints it before the first
                    // reading, so text that never produced one is the refusal instead. Keyed on the
                    // header rather than on the row count, because a monitor can legitimately be accepted and
                    // then print no reading. Checked after the fact rather than per line: an error ends the
                    // command, and a half-arrived line must never be classified as one.
                    if (!table.HasHeader && !string.IsNullOrWhiteSpace(output))
                    {
                        CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));
                        throw new TikCommandTrapException(_host.CommandFor(descriptor),
                            new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
                    }
                    return;
                }

                output = _host.Execute(CliCommandBuilder.BuildMonitorSnapshot(
                    descriptor.CommandText, descriptor.Parameters, snapshotModifier));
                PollingMonitorEngine.Emit(_host.ParseRecords(output, descriptor), handle, onRow);
            });

        // torch: driven by freeze-frame-interval + an explicit proplist (see CliMonitorVerbs/
        // CliCommandBuilder.BuildTorchSnapshot) instead of once/as-value. Each execution blocks for
        // ~2×TorchFreezeFrameSeconds and flushes one complete frame, which IS the poll interval — no
        // separate SleepInterruptible is needed between iterations.
        private void TorchFreezeFrameLoop(TikCommandDescriptor descriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => PollingMonitorEngine.Run(_host, handle, onError, onDone, () =>
            {
                int incompleteInARow = 0;
                while (!handle.CancelRequested)
                {
                    IList<TikRecordSentence> frame;
                    // A frame the transport refused as incomplete is skipped: the next one is a whole new frame
                    // (PollingMonitorEngine.MaxIncompletePollsInARow).
                    try
                    {
                        frame = ReadTorchFrameAsync(descriptor, TorchFreezeFrameSeconds, includeFilters: false,
                            CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (TikConnectionResponseIncompleteException)
                        when (++incompleteInARow < PollingMonitorEngine.MaxIncompletePollsInARow)
                    {
                        continue;
                    }
                    incompleteInARow = 0;
                    PollingMonitorEngine.Emit(frame, handle, onRow);
                }
            });

        /// <summary>
        /// One frame of torch through a read method. The arguments arrive in Filter format there unless the caller named
        /// the format, and torch has no query, so they are its inputs.
        /// </summary>
        internal Task<IList<TikRecordSentence>> ReadTorchFrameAsync(TikCommandDescriptor descriptor, CancellationToken ct)
            => ReadTorchFrameAsync(descriptor, TorchFreezeFrameSeconds, includeFilters: true, ct);

        /// <summary>
        /// Torch through <c>ExecuteListUntilDone(timeout)</c>: frames until the deadline, as the binary API reads it until
        /// the timeout ends the read. Under <see cref="TikCancellationMode.Cooperative"/> the token is seen between frames,
        /// so a frame takes the shortest time that flushes one (a 1 s interval, 2 s in all), and the deadline is overrun by
        /// at most that.
        /// </summary>
        internal async Task<IList<TikRecordSentence>> ReadTorchUntilCancelledAsync(TikCommandDescriptor descriptor, CancellationToken ct)
        {
            var rows = new List<TikRecordSentence>();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                rows.AddRange(await ReadTorchFrameAsync(descriptor, 1, includeFilters: true, ct).ConfigureAwait(false));
            }
        }

        // One frame: the proplist form, or on a router whose torch has none (RouterOS 6.49.13: "expected end of command
        // (line 1 column 71)", the column proplist starts on) its plain table, which carries the columns the caller's
        // arguments ask for. Remembered per command once that answer has a table, so another refusal is not.
        private async Task<IList<TikRecordSentence>> ReadTorchFrameAsync(TikCommandDescriptor descriptor, int freezeFrameSeconds,
            bool includeFilters, CancellationToken ct)
        {
            await _host.Knowledge.EnsureSchemaStoreAsync(ct).ConfigureAwait(false);
            bool plain = _host.Knowledge.MenuFacts.TorchWithoutProplist.Contains(descriptor.CommandText);
            string output = await _host.ExecuteAsync(CliCommandBuilder.BuildTorchSnapshot(descriptor.CommandText, descriptor.Parameters,
                freezeFrameSeconds, withProplist: !plain, includeFilters), ct).ConfigureAwait(false);
            if (!plain && CliErrorParser.IsSyntaxErrorOnly(output))
            {
                output = await _host.ExecuteAsync(CliCommandBuilder.BuildTorchSnapshot(descriptor.CommandText, descriptor.Parameters,
                    freezeFrameSeconds, withProplist: false, includeFilters), ct).ConfigureAwait(false);
                if (IsTorchFrame(output))
                {
                    _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.TorchWithoutProplist, CliRouterKnowledge.FactTorchWithoutProplist, descriptor.CommandText);
                    plain = true;
                }
            }

            // Text with no frame in it is the refusal: "input does not match any value of interface" matches no phrase,
            // and read as no rows it would make a monitor repeat the command at once for as long as it runs. No text at
            // all is a run that flushed no frame.
            if (!string.IsNullOrWhiteSpace(output) && !IsTorchFrame(output))
            {
                CliErrorParser.ThrowIfError(output, _host.CommandFor(descriptor));
                throw new TikCommandTrapException(_host.CommandFor(descriptor),
                    new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
            }
            return plain ? CliTorchParser.ParseTorchTable(output) : CliTorchParser.ParseTorchFrame(output);
        }

        private static bool IsTorchFrame(string output)
            => output.IndexOf("-- [Q quit", StringComparison.Ordinal) >= 0
               || output.IndexOf("Columns:", StringComparison.Ordinal) >= 0;
    }
}
