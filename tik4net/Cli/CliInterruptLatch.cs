using System.Threading;

namespace tik4net.Cli
{
    /// <summary>
    /// The pending Ctrl-C of one terminal (<see cref="ICliTerminal.RequestInterrupt"/>): set from any thread, taken by the
    /// read loop at its next poll, which then sends the key itself (the loop owns the writes).
    /// </summary>
    internal sealed class CliInterruptLatch
    {
        private int _requested;

        /// <summary>Asks the read in progress to interrupt. Any thread.</summary>
        internal void Request() => Interlocked.Exchange(ref _requested, 1);

        /// <summary>A new command starts with no interrupt pending: a request that came after the last read ended is void.</summary>
        internal void Clear() => Interlocked.Exchange(ref _requested, 0);

        /// <summary>
        /// Takes the pending request for a read answering <paramref name="sentCommand"/>: <c>true</c> once per request, and
        /// never for a control key's read (<paramref name="sentCommand"/> null), which leaves the request pending.
        /// </summary>
        internal bool TakeFor(string? sentCommand)
            => sentCommand != null && Interlocked.Exchange(ref _requested, 0) != 0;
    }
}
