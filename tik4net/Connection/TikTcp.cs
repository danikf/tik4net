using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Connection
{
    /// <summary>
    /// The bounded TCP connect shared by the binary API, Telnet and the WinBox TCP carrier. Built on
    /// <c>ConnectAsync</c> and a manual deadline because netstandard2.0 has no <c>ConnectAsync(CancellationToken)</c>.
    /// </summary>
    internal static class TikTcp
    {
        /// <summary>
        /// Connects within <paramref name="timeoutMs"/>, or within the OS's own bound when it is ≤ 0
        /// (<see cref="TikTimeouts.ConnectBoundMs"/>). A timeout is a SocketException with SocketError.TimedOut; a
        /// refusal is the SocketException the connect itself raised, unwrapped.
        /// </summary>
        /// <remarks>
        /// <c>Task.Wait(timeout)</c> throws an AggregateException, not the original exception, when the connect faults
        /// within the window (an immediate "connection refused"); it is unwrapped so callers see the same
        /// SocketException a direct await would give them.
        /// </remarks>
        internal static void Connect(TcpClient tcp, string host, int port, int timeoutMs)
        {
            var connect = tcp.ConnectAsync(host, port);
            try
            {
                if (!connect.Wait(TikTimeouts.ConnectBoundMs(timeoutMs)))
                {
                    Observe(connect);
                    throw new SocketException((int)SocketError.TimedOut);
                }
            }
            catch (AggregateException aex)
            {
                throw aex.InnerException ?? aex;
            }
        }

        /// <summary>As <see cref="Connect"/>, awaiting; the caller's cancel is OperationCanceledException, never a timeout.</summary>
        /// <remarks>
        /// The delay carries the token, so one WhenAny races both the deadline and the caller's cancel; which of the two
        /// ended the wait is read off the token, because a cancelled open must not be reported as a router that timed out.
        /// </remarks>
        internal static async Task ConnectAsync(TcpClient tcp, string host, int port, int timeoutMs, CancellationToken ct)
        {
            var connect = tcp.ConnectAsync(host, port);
            var timeout = Task.Delay(TikTimeouts.ConnectBoundMs(timeoutMs), ct);
            if (await Task.WhenAny(connect, timeout).ConfigureAwait(false) == timeout)
            {
                Observe(connect);
                ct.ThrowIfCancellationRequested();
                throw new SocketException((int)SocketError.TimedOut);
            }
            await connect.ConfigureAwait(false);
        }

        // An abandoned connect that later fails must not surface as an unobserved task exception elsewhere.
        private static void Observe(Task t) => _ = t.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
}
