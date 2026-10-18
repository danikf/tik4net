using System;

namespace tik4net.Cli
{
    /// <summary>
    /// Decides when a terminal command's read is over, for every terminal client: the command's own echo is on screen,
    /// the text ends at a completion prompt (<see cref="CliOutputHelper.EndsWithCompletionPrompt"/>), and then either the
    /// counted read's last line says the answer is complete (<see cref="CliOutputHelper.JudgePrompt"/>) or nothing new
    /// arrives for the settle window. The prompt must still be the last thing on screen when the window ends.
    /// </summary>
    /// <remarks>
    /// One instance per read. The caller keeps its own carrier loop and passes what it has read so far, whether anything
    /// arrived since the last call, and the time since the read started, from its own clock — which is what makes the
    /// rule testable without sleeping.
    /// <para>The trace notes are the ones the tooling reads: <c>prompt seen</c> each time the settle window starts,
    /// <c>settled -> return</c> and <c>count line + prompt -> return</c> when the read ends at a prompt, and
    /// <see cref="NoteEnd"/>'s <c>TIMEOUT</c> / <c>CLOSED</c> when it ends without one.</para>
    /// </remarks>
    internal sealed class CliPromptSettle
    {
        /// <summary>What <see cref="Observe"/> found.</summary>
        internal enum Outcome
        {
            /// <summary>The answer is not over: keep reading.</summary>
            Reading,

            /// <summary>A counted read's own last line and the prompt: the answer is complete, no window waited out.</summary>
            Complete,

            /// <summary>The prompt has stayed last for the settle window: the answer is complete.</summary>
            Settled,
        }

        private readonly string? _sentCommand;
        private readonly int _settleMs;
        private readonly Action<string>? _note;
        private bool _echoSeen;
        private long? _settleUntilMs;

        /// <param name="sentCommand">The command being answered; <c>null</c> for a control key.</param>
        /// <param name="settleMs">How long the prompt has to stay last.</param>
        /// <param name="note">Trace sink; null when tracing is off.</param>
        internal CliPromptSettle(string? sentCommand, int settleMs, Action<string>? note = null)
        {
            _sentCommand = sentCommand;
            _settleMs = settleMs;
            _note = note;
        }

        /// <summary>The text ends at a completion prompt and the settle window is running. WinBox CLI does not pull then.</summary>
        internal bool AtPrompt => _settleUntilMs != null;

        /// <summary>A completion prompt was seen at least once during this read (for the end-of-read note).</summary>
        internal bool Prompted { get; private set; }

        /// <summary>Takes one look at the read so far.</summary>
        /// <param name="stripped">Everything read so far, ANSI-stripped.</param>
        /// <param name="newData">Something arrived since the last call.</param>
        /// <param name="elapsedMs">Time since the read started.</param>
        /// <param name="rawLength">Raw bytes so far, for the trace notes only.</param>
        internal Outcome Observe(string stripped, bool newData, long elapsedMs, int rawLength)
        {
            if (newData) _settleUntilMs = null;
            if (!_echoSeen) _echoSeen = CliOutputHelper.ContainsEcho(stripped, _sentCommand);
            if (!_echoSeen || !CliOutputHelper.EndsWithCompletionPrompt(stripped, _sentCommand))
            {
                _settleUntilMs = null;
                return Outcome.Reading;
            }

            switch (CliOutputHelper.JudgePrompt(stripped, _sentCommand))
            {
                case CliOutputHelper.PromptVerdict.Complete:
                    _note?.Invoke("count line + prompt -> return @" + elapsedMs + "ms (bytes=" + rawLength + ")");
                    return Outcome.Complete;
                case CliOutputHelper.PromptVerdict.NotYet:
                    _settleUntilMs = null;   // a repaint before the answer, not its end
                    return Outcome.Reading;
            }

            if (_settleUntilMs == null)
            {
                _settleUntilMs = elapsedMs + _settleMs;
                Prompted = true;
                _note?.Invoke("prompt seen @" + elapsedMs + "ms (bytes=" + rawLength + ")");
                return Outcome.Reading;
            }
            if (elapsedMs < _settleUntilMs.Value)
                return Outcome.Reading;
            _note?.Invoke("settled -> return @" + elapsedMs + "ms");
            return Outcome.Settled;
        }

        /// <summary>The read ends without a settled prompt: <paramref name="why"/> is "TIMEOUT" or "CLOSED".</summary>
        internal void NoteEnd(string why, long elapsedMs, int rawLength)
            => _note?.Invoke(why + " @" + elapsedMs + "ms (prompted=" + Prompted + ", bytes=" + rawLength + ")");
    }
}
