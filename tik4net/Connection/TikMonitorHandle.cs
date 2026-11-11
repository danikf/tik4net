using System;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// Optional capability interface for transports that support streaming-monitor commands
    /// (<c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>). Kept off the neutral <see cref="TikCommandConnectionBase"/> so the
    /// base stays CRUD-only — mirrors how <c>ITikConnectionCapabilities</c> is an opt-in interface rather than a
    /// throwing base member. <see cref="TikGenericCommand.ExecuteWithCallback"/> checks for this interface and throws
    /// <see cref="NotSupportedException"/> only when the active transport does not implement it. Implemented by
    /// the CLI transports (<c>CliConnectionBase</c>, via polling) and the native WinBox M2 connection.
    /// </summary>
    internal interface ITikMonitorTransport
    {
        /// <summary>
        /// Starts a streaming monitor for <paramref name="descriptor"/> on a background worker, invoking
        /// <paramref name="onRow"/> per polled record, <paramref name="onError"/> on failure, and
        /// <paramref name="onDone"/> exactly once when the monitor ends. Returns a handle used to cancel/await it.
        /// </summary>
        TikMonitorHandle RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone);
    }

    /// <summary>
    /// Cancellation handle for a streaming-monitor command running on a background worker thread
    /// (see <see cref="ITikMonitorTransport.RunMonitorAsync"/>). The worker polls the router on a
    /// timer and invokes the row callback; <see cref="Cancel"/> asks it to stop after the current poll,
    /// and <see cref="Join"/> waits for it to finish. Created by the transport, surfaced to callers
    /// indirectly via <see cref="ITikCommand.Cancel"/>/<see cref="ITikCommand.CancelAndJoin()"/>.
    /// </summary>
    internal sealed class TikMonitorHandle
    {
        private volatile bool _cancelRequested;
        // Cancelled with the flag, for a worker blocked in one long read that has to be interrupted rather than polled
        // (the CLI's streaming ping). Never disposed: it has no timer, and a callback registered late still runs.
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private Thread _thread = null!; // assigned by AttachThread() right after the transport starts the worker

        /// <summary>True once the caller has asked the worker loop to stop; the loop checks it each pass.</summary>
        internal bool CancelRequested => _cancelRequested;

        /// <summary>Attaches the worker thread (called by the transport right after it is started).</summary>
        internal void AttachThread(Thread thread) => _thread = thread;

        /// <summary>True while the worker thread is still running.</summary>
        internal bool IsRunning => _thread != null && _thread.IsAlive;

        /// <summary>
        /// Cancelled when the caller asks the worker to stop: for a worker that is inside one long read rather than
        /// between polls, so the read can be interrupted (<see cref="Cli.CliMonitorDriver"/>'s streaming ping).
        /// </summary>
        internal CancellationToken CancellationToken => _cancel.Token;

        /// <summary>Requests cancellation; the worker stops after its current poll pass and sends the cancel command.</summary>
        internal void Cancel()
        {
            _cancelRequested = true;
            RequestStop();
        }

        private void RequestStop()
        {
            try { _cancel.Cancel(); }
            catch (AggregateException) { /* a registered interrupt failed: the flag above still stops the worker */ }
        }

        /// <summary>Requests cancellation and waits up to <paramref name="millisecondsTimeout"/> for the worker
        /// to finish (negative = wait indefinitely). Returns true when the worker has stopped.</summary>
        /// <remarks>
        /// Called on the worker itself — from one of the command's own callbacks, which all run there — it only requests
        /// the cancel and returns <c>true</c>: a thread cannot wait for itself to end, and the worker ends as soon as the
        /// callback returns, which is the join the caller asked for.
        /// </remarks>
        internal bool Join(int millisecondsTimeout)
        {
            _cancelRequested = true;
            RequestStop();
            var t = _thread;
            if (t == null || t == Thread.CurrentThread) return true;
            return millisecondsTimeout < 0 ? JoinForever(t) : t.Join(millisecondsTimeout);
        }

        private static bool JoinForever(Thread t) { t.Join(); return true; }
    }
}
