using System;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// A dedicated background thread running one loop until asked to stop: the start / stop-flag / join plumbing the
    /// MAC-layer pumps share. The loop body stays with its owner.
    /// </summary>
    /// <remarks>
    /// A dedicated thread, not a pool thread: the loops block on a socket or sleep for the life of the session.
    /// </remarks>
    internal sealed class TikBackgroundLoop
    {
        private Thread? _thread;
        private volatile bool _stop;

        /// <summary>Whether a loop has been started and not stopped.</summary>
        internal bool IsRunning => _thread != null;

        /// <summary>
        /// Starts <paramref name="body"/> on a new background thread named <paramref name="name"/>. The body is given
        /// a "stop requested" check and returns when it answers true.
        /// </summary>
        internal void Start(string name, Action<Func<bool>> body)
        {
            _stop = false;
            _thread = new Thread(() => body(() => _stop)) { IsBackground = true, Name = name };
            _thread.Start();
        }

        /// <summary>
        /// Asks the loop to stop and waits up to <paramref name="joinMs"/> for it. A loop never started, or already
        /// stopped, is left alone.
        /// </summary>
        internal void Stop(int joinMs)
        {
            var thread = _thread;
            if (thread == null) return;
            _thread = null;
            _stop = true;
            try { thread.Join(joinMs); } catch { /* best effort, as every caller's own join was */ }
        }
    }
}
