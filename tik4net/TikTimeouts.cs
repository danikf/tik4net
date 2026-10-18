using System;
using System.Threading;

namespace tik4net
{
    /// <summary>A public <see cref="TimeSpan"/> timeout in the milliseconds the sockets and read loops count in.</summary>
    internal static class TikTimeouts
    {
        /// <summary>
        /// <paramref name="value"/> in whole milliseconds, clamped to the <see cref="int"/> range: a longer span is
        /// the longest wait a socket takes, and <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> stays -1.
        /// Saturating rather than overflowing keeps "effectively no bound" (<see cref="TimeSpan.MaxValue"/>) from
        /// arriving as a negative count, which several transports would read as "no wait at all".
        /// </summary>
        internal static int ToMilliseconds(TimeSpan value)
        {
            double ms = value.TotalMilliseconds;
            if (ms >= int.MaxValue) return int.MaxValue;
            if (ms <= int.MinValue) return int.MinValue;
            return (int)ms;
        }

        /// <summary>
        /// The bound for a TCP connect: <paramref name="connectMs"/>, or none of our own when it is ≤ 0, which leaves the
        /// connect to the OS (Windows gives up after about 21 s).
        /// </summary>
        internal static int ConnectBoundMs(int connectMs) => connectMs > 0 ? connectMs : Timeout.Infinite;

        /// <summary>
        /// The bound for a step of the open that nothing else bounds (a TLS handshake, a login exchange, the MAC-layer login,
        /// REST's probe): <paramref name="connectMs"/>, or the receive timeout when it is ≤ 0, so an open never waits
        /// forever unless both are.
        /// </summary>
        /// <remarks>"No bound" is <see cref="int.MaxValue"/>, not <see cref="Timeout.Infinite"/>: the login loops compare
        /// elapsed time against the bound, and a -1 there would end them before they start.</remarks>
        internal static int HandshakeBoundMs(int connectMs, int receiveMs)
            => connectMs > 0 ? connectMs : receiveMs > 0 ? receiveMs : int.MaxValue;
    }
}
