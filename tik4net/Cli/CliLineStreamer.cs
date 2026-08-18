using System;

namespace tik4net.Cli
{
    /// <summary>
    /// Turns the ever-growing, ANSI-stripped read buffer of a PTY transport into a callback per
    /// <b>completed line</b>, so a long-running command can be consumed while it is still running instead of
    /// only when it returns to the prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every CLI client (<c>TelnetClient</c>, <c>WinboxCliClient</c>, <c>MacTelnetUdpClient</c>, the SSH
    /// shell) reads the same way: append the chunk to an accumulator, re-run
    /// <see cref="VtStripper.StripAnsi"/> over the whole thing, then test the result for a shell prompt.
    /// A streaming read only adds one call to <see cref="Feed"/> at that same point — see P2.50.
    /// </para>
    /// <para>
    /// <b>Why lines are counted rather than indexed.</b> The obvious implementation remembers how many
    /// characters of the stripped text were already delivered and continues from there. That is unsound
    /// here: the accumulator is re-stripped on every pass, and a chunk boundary can fall in the middle of
    /// an escape sequence. On the pass where the sequence is still incomplete <see cref="VtStripper"/>
    /// leaves it in place; on the next pass, now complete, it removes it — and the prefix the offset was
    /// pointing into gets SHORTER. The offset then addresses the wrong characters and the delivered text
    /// silently desynchronises. Counting delivered lines instead cannot drift: a line already handed out is
    /// never revisited, and the worst a late strip can do is alter a line that was already emitted.
    /// </para>
    /// <para>
    /// <b>Leading whitespace is preserved.</b> The consumer of a streamed monitor line is a column parser
    /// (<see cref="CliTableParser"/>) that reads RouterOS's fixed-width table by character offset, so
    /// indentation is data. Only the carriage returns RouterOS wraps its lines in are removed.
    /// </para>
    /// </remarks>
    internal sealed class CliLineStreamer
    {
        private readonly Action<string>? _onLine;
        private int _emitted;   // number of COMPLETE lines already handed to the callback

        /// <summary>
        /// Creates a streamer that reports each completed line to <paramref name="onLine"/>.
        /// A <c>null</c> callback makes <see cref="Feed"/> a no-op, so a non-streaming read can share the
        /// same code path without a branch at every call site.
        /// </summary>
        internal CliLineStreamer(Action<string>? onLine)
        {
            _onLine = onLine;
        }

        /// <summary>True when this streamer actually delivers anything (i.e. a callback was supplied).</summary>
        internal bool IsActive => _onLine != null;

        /// <summary>
        /// Reports every line of <paramref name="strippedSoFar"/> that has been completed by a newline and
        /// not yet delivered. Text after the last newline is a partial line and is deliberately withheld —
        /// a half-arrived row would parse into a truncated record.
        /// </summary>
        /// <param name="strippedSoFar">The full ANSI-stripped read buffer as it stands right now.</param>
        internal void Feed(string strippedSoFar)
        {
            if (_onLine == null || string.IsNullOrEmpty(strippedSoFar))
                return;

            int lineNo = 0;
            int start = 0;
            while (true)
            {
                int nl = strippedSoFar.IndexOf('\n', start);
                if (nl < 0)
                    return;         // the remainder is an incomplete line — wait for more bytes

                if (lineNo >= _emitted)
                {
                    _emitted = lineNo + 1;
                    _onLine(strippedSoFar.Substring(start, nl - start).Trim('\r'));
                }

                lineNo++;
                start = nl + 1;
            }
        }
    }
}
