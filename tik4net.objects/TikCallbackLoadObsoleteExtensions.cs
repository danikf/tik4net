using System;
using System.Diagnostics.CodeAnalysis;

namespace tik4net.Objects
{
    /// <summary>
    /// The previous names of the callback-based loads, kept only so that 3.x code fails to compile with an
    /// explanation instead of silently binding to something else.
    /// <para>
    /// These are <c>[Obsolete(..., error: true)]</c> rather than warnings, deliberately: the old name now
    /// denotes a DIFFERENT KIND of method. A warning is the right tool when the call still does what the
    /// caller expects; here somebody writing <c>LoadAsync</c> in 4.0 is reaching for the awaitable one, and
    /// letting that compile with a warning hands them a background callback they will not await.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The names were the problem, not the behaviour. <c>LoadAsync</c> and <c>LoadListenAsync</c> end in
    /// <c>Async</c> but return <see cref="ITikCommand"/> and <c>void</c> — nothing awaitable — while
    /// <see cref="TikConnectionAsyncExtensions"/> offers <c>LoadListAsync</c>, <c>LoadAllAsync</c>,
    /// <c>SaveAsync</c> and friends, which return a <c>Task</c> and mean what
    /// the suffix says. Both sets are extension methods on <see cref="ITikConnection"/> in this namespace,
    /// so they arrive in one completion list: a caller typing <c>connection.Load</c> saw <c>LoadAsync</c>
    /// next to <c>LoadListAsync</c> with no way to tell which one <c>await</c> works on.
    /// </para>
    /// <para>
    /// The new names say what the method actually does — it starts the command and calls you back on a
    /// background thread — and they still sort under <c>Load…</c>, so nothing becomes harder to find.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikCallbackLoadObsoleteExtensions
    {
        private const string LoadMessage =
            "Renamed to LoadWithCallback. It is not awaitable — it returns a running ITikCommand and calls "
            + "you back on a background thread. For the Task-based API use LoadListAsync/LoadAllAsync "
            + "(TikConnectionAsyncExtensions).";

        private const string ListenMessage =
            "Renamed to LoadListenWithCallback. It is not awaitable — it returns a running ITikCommand and "
            + "calls you back on a background thread.";

        /// <inheritdoc cref="TikConnectionExtensions.LoadWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, ITikCommandParameter[])"/>
        [Obsolete(LoadMessage, true)]
        public static ITikCommand LoadAsync<TEntity>(this ITikConnection connection,
            Action<TEntity> onLoadItemCallback, Action<Exception>? onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
            => connection.LoadWithCallback(onLoadItemCallback, onExceptionCallback, parameters);

        /// <inheritdoc cref="TikConnectionExtensions.LoadListenWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{string}, Action{Exception}, ITikCommandParameter[])"/>
        [Obsolete(ListenMessage, true)]
        public static ITikCommand LoadListenAsync<TEntity>(this ITikConnection connection,
            Action<TEntity> onChangeCallback,
            Action<string>? onDeletedCallback = null,
            Action<Exception>? onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
            => connection.LoadListenWithCallback(onChangeCallback, onDeletedCallback, onExceptionCallback, parameters);

        /// <inheritdoc cref="TikCommandExtensions.LoadWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{Exception}, Action)"/>
        [Obsolete(LoadMessage, true)]
        public static void LoadAsync<TEntity>(this ITikCommand command,
            Action<TEntity> onLoadItemCallback,
            Action<Exception>? onExceptionCallback = null,
            Action? onDoneCallback = null)
            where TEntity : new()
            => command.LoadWithCallback(onLoadItemCallback, onExceptionCallback, onDoneCallback);

        /// <inheritdoc cref="TikCommandExtensions.LoadListenWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{string}, Action{Exception})"/>
        [Obsolete(ListenMessage, true)]
        public static void LoadListenAsync<TEntity>(this ITikCommand command,
            Action<TEntity> onChangeCallback,
            Action<string>? onDeletedCallback = null,
            Action<Exception>? onExceptionCallback = null)
            where TEntity : new()
            => command.LoadListenWithCallback(onChangeCallback, onDeletedCallback, onExceptionCallback);
    }
}
