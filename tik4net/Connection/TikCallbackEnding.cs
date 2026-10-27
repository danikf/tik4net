using System;

namespace tik4net.Connection
{
    /// <summary>
    /// One <c>ExecuteWithCallback</c> run's ending, as the caller sees it (command-layer D21): <c>onDone</c> fires when
    /// the command ends without an error (it finished, or the caller's <c>Cancel()</c> or <c>Close()</c> stopped it), and not after an
    /// error, which reaches <c>errorCallback</c> alone.
    /// </summary>
    /// <remarks>
    /// The commands wrap the caller's two callbacks with this rather than changing the engines underneath: the API's
    /// bounded reads and the polling monitor engine keep their own <c>onDone</c>, which means "ended" there.
    /// </remarks>
    internal sealed class TikCallbackEnding
    {
        private volatile bool _errorReported;

        /// <summary>Records that an error was reported, then passes it to the caller's callback, if any.</summary>
        internal void Error(ITikTrapSentence trap, Action<ITikTrapSentence>? errorCallback)
        {
            _errorReported = true;
            errorCallback?.Invoke(trap);
        }

        /// <summary>Calls the caller's <paramref name="onDoneCallback"/> unless an error was reported in this run.</summary>
        internal void Done(Action? onDoneCallback)
        {
            if (!_errorReported)
                onDoneCallback?.Invoke();
        }
    }
}
