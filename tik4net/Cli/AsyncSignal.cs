using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>
    /// A one-shot "something arrived" signal that can be waited on <b>without holding a thread</b> —
    /// <see cref="ManualResetEventSlim"/>'s semantics with an awaitable wait.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists for the terminal transports, where a background pump owns the socket and the command read
    /// only waits for it to report progress. With a blocking wait that read had to run on a thread-pool
    /// thread — the <c>Task.Run</c> façade the <see cref="TikConnectionCapability.AsyncCommands"/> flag
    /// exists to rule out, since a command that spends 30 s waiting was spending it on a borrowed thread.
    /// </para>
    /// <para>
    /// Set-before-wait is preserved, which is the whole reason this is a semaphore and not a
    /// <see cref="TaskCompletionSource{TResult}"/> re-created per wait: the pump can signal in the window
    /// between the reader's <see cref="Reset"/> and its <see cref="WaitAsync"/>, and that signal must not be
    /// lost — losing it costs a full poll interval per command, every command. Written for <b>one</b>
    /// waiter; the terminal clients read one command at a time.
    /// </para>
    /// </remarks>
    internal sealed class AsyncSignal
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0, 1);

        /// <summary>Signals a waiter, or leaves the signal standing for the next one. Idempotent.</summary>
        public void Set()
        {
            // CurrentCount is checked rather than caught: Release past the maximum throws, and an exception
            // per inbound packet on the pump's own path is not a cost worth paying for tidiness.
            if (_semaphore.CurrentCount == 0)
            {
                try { _semaphore.Release(); }
                catch (SemaphoreFullException) { /* another Set won the race — already signalled */ }
            }
        }

        /// <summary>Clears a standing signal, so the next <see cref="WaitAsync"/> waits for a fresh one.</summary>
        public void Reset()
        {
            while (_semaphore.Wait(0)) { }
        }

        /// <summary>
        /// Waits for the signal, giving up after <paramref name="timeoutMs"/>. Returns true when signalled.
        /// Deliberately takes no <see cref="CancellationToken"/>: on the terminal transports a read that is
        /// abandoned mid-command leaves output the NEXT command would parse as its own, so cancellation is
        /// honoured between commands and never inside one (see <see cref="TikCancellationMode"/>).
        /// </summary>
        public Task<bool> WaitAsync(int timeoutMs) => _semaphore.WaitAsync(timeoutMs);
    }
}
