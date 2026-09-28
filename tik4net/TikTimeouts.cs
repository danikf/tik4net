using System;

namespace tik4net
{
    /// <summary>A public <see cref="TimeSpan"/> timeout in the milliseconds the sockets and read loops count in.</summary>
    internal static class TikTimeouts
    {
        /// <summary>
        /// <paramref name="value"/> in whole milliseconds, clamped to the <see cref="int"/> range: a longer span is
        /// the longest wait a socket takes, and <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> stays -1.
        /// </summary>
        internal static int ToMilliseconds(TimeSpan value)
        {
            double ms = value.TotalMilliseconds;
            if (ms >= int.MaxValue) return int.MaxValue;
            if (ms <= int.MinValue) return int.MinValue;
            return (int)ms;
        }
    }
}
