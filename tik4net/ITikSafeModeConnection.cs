using System;

namespace tik4net
{
    /// <summary>
    /// RouterOS <b>Safe Mode</b> bound to this connection: while it is held every configuration change is
    /// recorded, and if the connection drops without a release, RouterOS <b>rolls them all back</b>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ITikConnection"/> because safe mode needs a persistent, session-bound
    /// channel and stateless REST has none — the rollback would have nothing to be bound to. A transport that
    /// implements this interface also declares <see cref="TikConnectionCapability.SafeMode"/>, so
    /// <c>connection.Supports(TikConnectionCapability.SafeMode)</c> and
    /// <c>connection is ITikSafeModeConnection</c> answer the same question.
    /// <para>
    /// Safe mode is connection-wide state, not a per-command option: any command issued while it is held
    /// takes part in it, and it must not be driven from two threads.
    /// </para>
    /// <para>The binary-API / native-WinBox paths use the scriptable <c>/safe-mode</c> mechanism
    /// (RouterOS 7.18+); the CLI terminals use the <c>Ctrl+X</c>/<c>Ctrl+D</c> control keys, which also work
    /// on older RouterOS.</para>
    /// </remarks>
    public interface ITikSafeModeConnection
    {
        /// <summary>
        /// Enters Safe Mode on this connection (mirrors <c>/safe-mode/take</c>, the WebFig "Safe Mode"
        /// button, or the terminal <c>Ctrl+X</c>). Call <see cref="SafeModeRelease"/> to keep the changes,
        /// or <see cref="SafeModeUnroll"/> to discard them immediately while staying connected. Losing the
        /// connection in between rolls them back — that is what safe mode is for.
        /// </summary>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS refused to take safe mode (e.g. already held by another session).</exception>
        void SafeModeTake();

        /// <summary>
        /// Commits the changes made since <see cref="SafeModeTake"/> and leaves Safe Mode (mirrors
        /// <c>/safe-mode/release</c> or a second terminal <c>Ctrl+X</c>). After this the changes are
        /// permanent and a later disconnect no longer reverts them. No-op when safe mode is not held.
        /// </summary>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS reported an error releasing safe mode.</exception>
        void SafeModeRelease();

        /// <summary>
        /// Discards every change made since <see cref="SafeModeTake"/> <b>now</b>, without disconnecting,
        /// and leaves Safe Mode (mirrors <c>/safe-mode/unroll</c> or the terminal <c>Ctrl+D</c>). No-op when
        /// safe mode is not held.
        /// <para>Not available on the native WinBox transport (WebFig exposes only take/release); there,
        /// drop the connection without releasing to roll back.</para>
        /// </summary>
        /// <exception cref="NotSupportedException">The transport cannot roll back safe mode in place.</exception>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS reported an error rolling back safe mode.</exception>
        void SafeModeUnroll();

        /// <summary>
        /// Whether this connection currently holds Safe Mode. Tracked client-side per connection: <c>true</c>
        /// after <see cref="SafeModeTake"/>, <c>false</c> after <see cref="SafeModeRelease"/> /
        /// <see cref="SafeModeUnroll"/>. Useful in a <c>finally</c> block to decide whether to commit or let
        /// the disconnect roll back.
        /// </summary>
        bool SafeModeGet();
    }
}
