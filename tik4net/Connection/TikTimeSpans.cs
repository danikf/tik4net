using System;
using System.Threading;

namespace tik4net.Connection
{
    /// <summary>
    /// The public surface takes time as <see cref="TimeSpan"/>; the waits underneath (<see cref="Thread.Join(int)"/>,
    /// socket timeouts) take milliseconds. The conversions live here so every member reads a span the same way.
    /// </summary>
    internal static class TikTimeSpans
    {
        /// <summary>
        /// A bounded wait's budget in milliseconds: <see cref="Timeout.InfiniteTimeSpan"/> is
        /// <see cref="Timeout.Infinite"/>, anything else must be positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Zero, or negative and not infinite.</exception>
        public static int ToWaitMilliseconds(TimeSpan timeout, string paramName)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
                return Timeout.Infinite;
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(paramName, timeout,
                    "A wait budget is positive, or Timeout.InfiniteTimeSpan to wait without a bound.");
            return (int)Math.Min(Math.Ceiling(timeout.TotalMilliseconds), int.MaxValue - 1);
        }

        /// <summary>A read duration: negative is zero, and the span is capped where a wait can still take it.</summary>
        public static TimeSpan ToDuration(TimeSpan duration)
            => duration < TimeSpan.Zero ? TimeSpan.Zero
             : duration.TotalMilliseconds >= int.MaxValue ? TimeSpan.FromMilliseconds(int.MaxValue - 1)
             : duration;
    }
}
