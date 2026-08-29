using System;

namespace tik4net
{
    /// <summary>
    /// The previous name of the callback-based execute, kept only so that 3.x code fails to compile with an
    /// explanation instead of silently binding to something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ExecuteAsync</c> ended in <c>Async</c> and returned <c>void</c>. It started the command and called
    /// back on a background thread — there was nothing to <c>await</c> — while <see cref="ITikCommandAsync"/>
    /// on the same object offers <c>ExecuteListAsync</c>, <c>ExecuteScalarAsync</c> and friends, which return
    /// a <see cref="System.Threading.Tasks.Task"/> and mean what the suffix says. Both arrived in one
    /// completion list, so a caller typing <c>cmd.Execute</c> saw the two side by side with no way to tell
    /// which one <c>await</c> works on.
    /// </para>
    /// <para>
    /// This is <c>[Obsolete(..., error: true)]</c> rather than a warning for the same reason the mapper's
    /// <c>LoadAsync</c> is (see <c>TikCallbackLoadObsoleteExtensions</c>): the old name now denotes a
    /// <b>different kind of method</b>. A warning is right when the call still does what the caller expects.
    /// Here somebody writing <c>ExecuteAsync</c> in 4.0 is reaching for the awaitable one, and letting that
    /// compile would hand them a background callback they will not await.
    /// </para>
    /// <para>
    /// It is an extension method rather than a member, so that <see cref="ITikCommand"/> — an interface
    /// callers implement — carries only the name that stays.
    /// </para>
    /// </remarks>
    public static class TikCommandCallbackObsoleteExtensions
    {
        private const string Message =
            "Renamed to ExecuteWithCallback. It is not awaitable — it starts the command and calls you back "
            + "on a background thread. For the Task-based API use ExecuteListAsync/ExecuteScalarAsync and "
            + "friends (ITikCommandAsync).";

        /// <inheritdoc cref="ITikCommand.ExecuteWithCallback"/>
        [Obsolete(Message, true)]
        public static void ExecuteAsync(this ITikCommand command,
            Action<ITikReSentence> oneResponseCallback,
            Action<ITikTrapSentence>? errorCallback = null,
            Action? onDoneCallback = null)
            => command.ExecuteWithCallback(oneResponseCallback, errorCallback, onDoneCallback);
    }
}
