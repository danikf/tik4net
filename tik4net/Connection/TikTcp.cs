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

        /// <summary>The idle time before the first keepalive probe, the gap between probes, and how many go unanswered.</summary>
        /// <remarks>A half-open connection is then found dead about 110 s after its last byte.</remarks>
        internal const int KeepAliveIdleSeconds = 60;
        internal const int KeepAliveIntervalSeconds = 10;
        internal const int KeepAliveProbes = 5;

        /// <summary>
        /// Turns on TCP keepalive, so a connection whose peer has gone without a FIN or a reset (power cut, cable, a
        /// NAT that forgot it) fails its blocked read rather than waiting on it for ever.
        /// </summary>
        /// <remarks>
        /// The binary API reads its socket with no timeout (the deadline lives with each caller), and a callback monitor
        /// waits with none at all, so on a half-open connection nothing else would ever end them. The timings are set
        /// where the runtime has the options (net8.0); on netstandard2.0 keepalive is switched on with the operating
        /// system's own timings. A platform that refuses the option is left as it was: keepalive is a safety net here,
        /// not something a connection needs in order to work.
        /// </remarks>
        internal static void EnableKeepAlive(Socket socket)
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
#if NET8_0_OR_GREATER
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, KeepAliveIdleSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, KeepAliveIntervalSeconds);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveProbes);
#endif
            }
            catch (SocketException) { /* not supported here: the OS default (off, or its own timings) stands */ }
            catch (PlatformNotSupportedException) { }
        }

        // An abandoned connect that later fails must not surface as an unobserved task exception elsewhere.
        private static void Observe(Task t) => _ = t.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
}
