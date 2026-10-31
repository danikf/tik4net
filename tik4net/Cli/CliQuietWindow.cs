using System;
using System.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The quiet rule of a read that has no prompt to end at — a Tab-completion listing, a repaint that may not come:
    /// the read is over once nothing new has arrived for the quiet window after some data, and a terminal that has
    /// sent nothing at all for <see cref="CliCompletionParser.NoReactionQuietFactor"/> windows did not react.
    /// </summary>
    /// <remarks>
    /// Shared by the MAC-Telnet and WinBox CLI clients (review C25); <c>CliPtySession</c> keeps its own copy until the
    /// CLI review aligns it. Fed the length of the text read so far, so a caller that accumulates in a buffer it
    /// does not own (MAC-Telnet's pump) and one that reads frame by frame (the WinBox CLI) observe it the same way.
    /// </remarks>
    internal sealed class CliQuietWindow
    {
        /// <summary>What the read does next.</summary>
        internal enum Outcome
        {
            /// <summary>Keep reading.</summary>
            Continue,
            /// <summary>Quiet for the window after some data (or, when no data is required, at all): the read is over.</summary>
            Settled,
            /// <summary>Nothing arrived for <see cref="CliCompletionParser.NoReactionQuietFactor"/> windows: no reaction.</summary>
            NoReaction,
        }

        private readonly int _quietMs;
        private readonly bool _requireData;
        private readonly Stopwatch _sinceData = Stopwatch.StartNew();
        private int _lastLength;

        /// <param name="quietMs">How long nothing new must arrive.</param>
        /// <param name="requireData">When <c>true</c> (Tab-completion) the quiet only counts once something has arrived;
        /// when <c>false</c> a silent terminal is quiet too — for draining a repaint that may not come.</param>
        internal CliQuietWindow(int quietMs, bool requireData = true)
        {
            _quietMs = quietMs;
            _requireData = requireData;
        }

        /// <summary>Judges the read with <paramref name="length"/> characters received so far.</summary>
        internal Outcome Observe(int length)
        {
            if (length != _lastLength)
            {
                _lastLength = length;
                _sinceData.Restart();
                return Outcome.Continue;
            }

            long quiet = _sinceData.ElapsedMilliseconds;
            if ((_lastLength > 0 || !_requireData) && quiet >= _quietMs)
                return Outcome.Settled;
            if (_lastLength == 0 && quiet >= (long)_quietMs * CliCompletionParser.NoReactionQuietFactor)
                return Outcome.NoReaction;   // a Tab the router leaves unanswered: not the receive deadline
            return Outcome.Continue;
        }
    }
}
