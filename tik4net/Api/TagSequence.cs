using System;
using System.Globalization;
using System.Threading;

namespace tik4net.Api
{
    /// <summary>
    /// Hands out the <c>.tag</c> that ties a reply to the caller that asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tag reads <c><see cref="Prefix">prefix</see>-pid-stamp-counter</c>, and each field rules out one way
    /// two commands could end up sharing one:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>counter</b> — one sequence for every connection in the process. Within a connection a shared tag
    /// would let one caller dequeue another's sentences, which is wrong data rather than an error.
    /// </description></item>
    /// <item><description>
    /// <b>pid</b> — two processes running <i>at the same time</i> on one host always have different process
    /// ids, so this is what makes "the MCP server and a program cannot collide" a guarantee rather than a
    /// probability. A bare counter gives both of them <c>.tag=1</c>.
    /// </description></item>
    /// <item><description>
    /// <b>stamp</b> — the clock at startup, which separates two runs that were handed the same recycled pid.
    /// Not enough on its own: <see cref="DateTime.UtcNow"/> is granular to roughly a millisecond on Windows,
    /// so two processes started by one script can genuinely read the same value.
    /// </description></item>
    /// <item><description>
    /// <b>prefix</b> — names the program. Its own field rather than a concatenation, because a stamp is
    /// base36 and could itself begin with the letters <c>mcp</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// None of this is needed for correctness: RouterOS echoes a tag on the session that sent it and each
    /// connection reads only its own socket, so values in two processes could not be confused even when they
    /// were identical. What it buys is diagnosis — a tag in a router log or a wire trace naming exactly one
    /// command of one run of one program, which is what this area was short of.
    /// </para>
    /// </remarks>
    internal static class TagSequence
    {
        /// <summary>Used when <see cref="Prefix"/> has not been set, so the field is never empty.</summary>
        /// <remarks>
        /// An always-present field is what keeps the shape fixed: an empty one would make
        /// <c>-1234-abc-1</c> a legal tag, and a reader (or a test) could no longer tell which field is which.
        /// </remarks>
        internal const string DefaultPrefix = "app";

        private static int _tagCounter;
        private static string _prefix = DefaultPrefix;

        // Both fixed for the life of the process, so they are computed once. Base36 keeps them short: a pid is
        // 4 characters or so, and 41 bits of 100-ns ticks about 9. Only the low 41 bits of the clock are used —
        // the high bits are the same for every process for centuries, so they would cost width and say nothing.
        private static readonly string _processField = ToBase36(CurrentProcessId());
        private static readonly string _stampField = ToBase36(DateTime.UtcNow.Ticks & 0x1FFFFFFFFFFL);

        /// <summary>
        /// Short text identifying this program in every tag it sends, for reading a router log or a wire trace
        /// that has more than one client in it. Defaults to <see cref="DefaultPrefix"/>; the MCP server sets
        /// <c>mcp</c>.
        /// </summary>
        /// <remarks>
        /// Set it once at startup, before opening a connection — a tag already in flight keeps the prefix it
        /// was issued with, which is the whole point of a tag. Keep it to a few characters: it is sent with
        /// every command. Only letters and digits are kept, and an empty result falls back to
        /// <see cref="DefaultPrefix"/>, because a tag has to survive the round trip as an opaque router word
        /// and stay legible in the log line the prefix exists to disambiguate.
        /// </remarks>
        internal static string Prefix
        {
            get { return _prefix; }
            set
            {
                string clean = Sanitize(value);
                _prefix = clean.Length > 0 ? clean : DefaultPrefix;
            }
        }

        /// <summary>
        /// The next tag: unique among every connection in this process, and among every process talking to the
        /// router at the same time.
        /// </summary>
        internal static string NextTag()
        {
            int n = Interlocked.Increment(ref _tagCounter);
            return _prefix + "-" + _processField + "-" + _stampField + "-"
                + n.ToString(CultureInfo.InvariantCulture);
        }

        // Restricted environments can refuse to report the process id. Falling back to a random value keeps
        // the guarantee probabilistic instead of losing the field altogether — and losing it would silently
        // put two processes back on identical tags, which is the one thing this class exists to prevent.
        private static long CurrentProcessId()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    return process.Id;
            }
            catch (Exception)
            {
                return new Random().Next(1, int.MaxValue);
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var chars = new char[value.Length];
            int len = 0;
            foreach (char c in value)
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    chars[len++] = c;
            return new string(chars, 0, len);
        }

        private static string ToBase36(long value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (value <= 0)
                return "0";

            var buffer = new char[13];
            int at = buffer.Length;
            while (value > 0)
            {
                buffer[--at] = digits[(int)(value % 36)];
                value /= 36;
            }
            return new string(buffer, at, buffer.Length - at);
        }
    }
}
