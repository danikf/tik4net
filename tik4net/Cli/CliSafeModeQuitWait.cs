using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>
    /// The wait every terminal's close runs after <c>/quit</c> and the Safe Mode <c>y</c>
    /// (<see cref="RouterOsCliLogin.QuitKeys"/>): the carrier is polled until the router ends the session, at most
    /// <see cref="RouterOsCliLogin.SafeModeQuitWaitMs"/>.
    /// </summary>
    /// <remarks>
    /// The <c>y</c> must be read by the console before the session goes: a close that races it leaves the session ending
    /// inside the question, and on RouterOS 6.49.13 that stopped the console from serving any new Telnet, SSH or API login
    /// until a reboot. Each carrier says what "ended" is — the PTY carrier closing, MAC-Telnet's <c>PKT_END</c>, the
    /// WinBox terminal's <c>Mepty.Close</c> (with a quiet fallback) — and its poll may wait one poll interval, never longer.
    /// The bound is this loop's, so once the wait returns nothing reads the carrier any more: the close that follows tears
    /// it down without a read still running on it.
    /// </remarks>
    internal static class CliSafeModeQuitWait
    {
        /// <param name="pollEnded">One look at the carrier: <c>true</c> when the router has ended the session (or the wait
        /// is otherwise over for the carrier), <c>false</c> to look again. Waits at most one poll interval.</param>
        /// <param name="maxMs">The bound.</param>
        internal static async Task RunAsync(Func<Task<bool>> pollEnded, int maxMs = RouterOsCliLogin.SafeModeQuitWaitMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < maxMs)
            {
                if (await pollEnded().ConfigureAwait(false))
                    return;
            }
        }
    }
}
