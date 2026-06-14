using System;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// Optional capability interface for transports that support streaming-monitor commands
    /// (<c>ExecuteAsync</c>/<c>LoadAsync</c>). Kept off the neutral <see cref="TikCommandConnectionBase"/> so the
    /// base stays CRUD-only — mirrors how <c>ITikConnectionCapabilities</c> is an opt-in interface rather than a
    /// throwing base member. <see cref="TikGenericCommand.ExecuteAsync"/> checks for this interface and throws
    /// <see cref="NotSupportedException"/> only when the active transport does not implement it. Currently
    /// implemented by the native WinBox M2 connection.
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
    /// (see <see cref="TikCommandConnectionBase.RunMonitorAsync"/>). The worker polls the router on a
    /// timer and invokes the row callback; <see cref="Cancel"/> asks it to stop after the current poll,
    /// and <see cref="Join"/> waits for it to finish. Created by the transport, surfaced to callers
    /// indirectly via <see cref="ITikCommand.Cancel"/>/<see cref="ITikCommand.CancelAndJoin()"/>.
    /// </summary>
    internal sealed class TikMonitorHandle
    {
        private volatile bool _cancelRequested;
        private Thread _thread;

        /// <summary>True once the caller has asked the worker loop to stop; the loop checks it each pass.</summary>
        internal bool CancelRequested => _cancelRequested;

        /// <summary>Attaches the worker thread (called by the transport right after it is started).</summary>
        internal void AttachThread(Thread thread) => _thread = thread;

        /// <summary>True while the worker thread is still running.</summary>
        internal bool IsRunning => _thread != null && _thread.IsAlive;

        /// <summary>Requests cancellation; the worker stops after its current poll pass and sends the cancel command.</summary>
        internal void Cancel() => _cancelRequested = true;

        /// <summary>Requests cancellation and waits up to <paramref name="millisecondsTimeout"/> for the worker
        /// to finish (negative = wait indefinitely). Returns true when the worker has stopped.</summary>
        internal bool Join(int millisecondsTimeout)
        {
            _cancelRequested = true;
            var t = _thread;
            if (t == null) return true;
            return millisecondsTimeout < 0 ? JoinForever(t) : t.Join(millisecondsTimeout);
        }

        private static bool JoinForever(Thread t) { t.Join(); return true; }
    }
}
