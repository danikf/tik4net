using System;

namespace tik4net.Cli
{
    /// <summary>
    /// Decides when a terminal command's read is over, for every terminal client: the command's own echo is on screen,
    /// the text ends at a completion prompt (<see cref="CliPromptText.EndsWithCompletionPrompt"/>), and then either the
    /// counted read's last line says the answer is complete (<see cref="CliPromptText.JudgePrompt"/>) or nothing new
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
        private bool _interrupted;
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

        /// <summary>
        /// A Ctrl-C went out: the answer is cut wherever it had got to. The echo may never complete (RouterOS 6 repaints the
        /// typed line per character, and the key can land inside it), and a counted read's last line will not come, so
        /// the prompt ends the read on its own.
        /// </summary>
        internal void Interrupted()
        {
            _interrupted = true;
            _settleUntilMs = null;
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
            if (!_echoSeen) _echoSeen = _interrupted || CliOutputCleaner.ContainsEcho(stripped, _sentCommand);
            // After the key the line break no longer matters: RouterOS 6 repaints its prompt on the same line.
            if (!_echoSeen || !CliPromptText.EndsWithCompletionPrompt(stripped, _interrupted ? null : _sentCommand))
            {
                _settleUntilMs = null;
                return Outcome.Reading;
            }

            switch (_interrupted ? CliPromptText.PromptVerdict.Settle : CliPromptText.JudgePrompt(stripped, _sentCommand))
            {
                case CliPromptText.PromptVerdict.Complete:
                    _note?.Invoke("count line + prompt -> return @" + elapsedMs + "ms (bytes=" + rawLength + ")");
                    return Outcome.Complete;
                case CliPromptText.PromptVerdict.NotYet:
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

        /// <summary>
        /// The read is over without a settled prompt: the carrier ended during it (<paramref name="closed"/>) or the
        /// receive deadline passed. Notes the end, then throws for a command — the relay's end when
        /// <paramref name="romonTarget"/> is set and the carrier closed, the close, or the deadline
        /// (<see cref="CliReadTimeout"/>) — and returns the text so far for a control key, which need not end at a prompt.
        /// </summary>
        /// <param name="transport">Named in the exception texts.</param>
        /// <param name="romonTarget">The RoMON target a relayed session runs on; null for a direct session.</param>
        /// <param name="closed">The carrier ended during this read; <c>false</c> for the deadline.</param>
        /// <param name="elapsedMs">Time since the read started.</param>
        /// <param name="receiveTimeoutMs">The receive deadline, for the messages.</param>
        /// <param name="stripped">Everything read so far, ANSI-stripped.</param>
        /// <param name="rawLength">Raw characters so far, for the trace note.</param>
        /// <returns><paramref name="stripped"/>, for a control key.</returns>
        internal string EndWithoutPrompt(string transport, string? romonTarget, bool closed, long elapsedMs,
            int receiveTimeoutMs, string stripped, int rawLength)
        {
            // Silent truncation here is how a partial read reaches the parser looking like data, so it is always traced.
            NoteEnd(closed ? "CLOSED" : "TIMEOUT", elapsedMs, rawLength);
            if (_sentCommand == null)
                return stripped;
            if (closed && romonTarget != null)
                throw RouterOsCliLogin.RomonRelayEndedWithTheSession(transport, romonTarget, _sentCommand, stripped);
            throw closed
                ? CliReadTimeout.CreateClosed(transport, receiveTimeoutMs, elapsedMs, _sentCommand, stripped)
                : CliReadTimeout.Create(transport, receiveTimeoutMs, _sentCommand, stripped);
        }
    }
}
