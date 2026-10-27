using System;

namespace tik4net.Cli
{
    /// <summary>
    /// Builds the exception raised when a terminal transport's request/reply read reaches the receive
    /// deadline without ever seeing the closing shell prompt.
    /// </summary>
    /// <remarks>
    /// <para>Returning the partial text instead would leave the caller no way to tell it apart from a
    /// complete, short answer: a half-read table silently becomes "the table", and a half-read record a
    /// parse error blamed on the parser. That is the same class of defect as a swallowed exception — the
    /// failure exists, but nothing can observe it.</para>
    /// <para>It is not hypothetical. Should RouterOS's Safe Mode prompt stop matching ours, no test goes
    /// red on its own: every command inside safe mode runs to the full 30 s deadline and then returns text
    /// that happens to be complete, so the suite passes while taking 4½ minutes. Failing loudly here is
    /// what makes that visible.</para>
    /// </remarks>
    internal static class CliReadTimeout
    {
        /// <summary>Characters of the received tail quoted in the message.</summary>
        private const int TailChars = 120;

        /// <summary>
        /// Creates the exception for a command whose response never completed.
        /// </summary>
        /// <param name="transport">Short transport name, e.g. <c>"Telnet"</c>.</param>
        /// <param name="timeoutMs">The receive timeout that elapsed.</param>
        /// <param name="sentCommand">The command that was sent, if known.</param>
        /// <param name="received">Everything received so far (ANSI-stripped).</param>
        internal static TikConnectionReceiveTimeoutException Create(
            string transport, int timeoutMs, string sentCommand, string received)
        {
            received = received ?? string.Empty;

            string what = received.Length == 0
                ? "nothing was received"
                : received.Length + " characters were received but the response never ended at a shell prompt, "
                  + "so it is incomplete — the tail is '" + Tail(received) + "'";

            string message =
                transport + ": the router did not finish answering within " + timeoutMs + " ms — " + what + ". "
                + (string.IsNullOrEmpty(sentCommand) ? string.Empty : "Command: '" + sentCommand.Trim() + "'. ")
                + "Raise ReceiveTimeout if the command is genuinely slow; a response that never ends at a "
                + "prompt otherwise means the terminal is out of step with the router.";

            return new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs), message,
                received.Length == 0 ? null : received);
        }

        /// <summary>
        /// Creates the exception for a response whose frame stopped arriving part-way through — the channel
        /// failed, or its deadline fired, while a frame was being read.
        /// </summary>
        /// <remarks>
        /// Still a <see cref="TikConnectionReceiveTimeoutException"/>, because what the caller has to do is the
        /// same — the response is incomplete and the connection is closed, since a stream left mid-frame cannot
        /// be read in step again — but it says how long the read actually ran and carries the channel's own
        /// exception, instead of claiming the whole deadline elapsed.
        /// </remarks>
        /// <param name="transport">Short transport name, e.g. <c>"WinBox CLI"</c>.</param>
        /// <param name="timeoutMs">The configured receive timeout.</param>
        /// <param name="elapsedMs">How long the read had run when the frame failed.</param>
        /// <param name="sentCommand">The command that was sent.</param>
        /// <param name="received">Everything received so far (ANSI-stripped).</param>
        /// <param name="cause">The channel's exception.</param>
        internal static TikConnectionReceiveTimeoutException CreateMidFrame(
            string transport, int timeoutMs, long elapsedMs, string sentCommand, string received, Exception cause)
        {
            received = received ?? string.Empty;

            string message =
                transport + ": a frame of the answer stopped arriving part-way through, " + elapsedMs + " ms into "
                + "the " + timeoutMs + " ms receive deadline — " + cause.GetType().Name + ": " + cause.Message + ". "
                + (received.Length == 0
                    ? string.Empty
                    : received.Length + " characters had been received; the tail is '" + Tail(received) + "'. ")
                + "Command: '" + sentCommand.Trim() + "'. A stream left mid-frame cannot be read in step again, "
                + "so the connection is closed.";

            return new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs), message,
                received.Length == 0 ? null : received, cause);
        }

        /// <summary>
        /// Creates the exception for a response the carrier stopped delivering because it closed: the router (or the
        /// network) ended the connection before the answer reached a prompt.
        /// </summary>
        /// <remarks>
        /// Still a <see cref="TikConnectionReceiveTimeoutException"/>, as <see cref="CreateMidFrame"/> is, because the
        /// caller's position is the same — the response is incomplete and the command may have run — but it says the
        /// connection closed and how long the read ran, instead of claiming the whole deadline passed.
        /// </remarks>
        /// <param name="transport">Short transport name, e.g. <c>"Telnet"</c>.</param>
        /// <param name="timeoutMs">The configured receive timeout.</param>
        /// <param name="elapsedMs">How long the read had run when the carrier closed.</param>
        /// <param name="sentCommand">The command that was sent.</param>
        /// <param name="received">Everything received so far (ANSI-stripped).</param>
        internal static TikConnectionReceiveTimeoutException CreateClosed(
            string transport, int timeoutMs, long elapsedMs, string sentCommand, string received)
        {
            received = received ?? string.Empty;

            string message =
                transport + ": the router closed the connection " + elapsedMs + " ms into the " + timeoutMs
                + " ms receive deadline, before the answer reached a shell prompt — "
                + (received.Length == 0
                    ? "nothing had been received. "
                    : received.Length + " characters had been received; the tail is '" + Tail(received) + "'. ")
                + "Command: '" + sentCommand.Trim() + "'. The command may have run.";

            return new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs), message,
                received.Length == 0 ? null : received);
        }

        /// <summary>
        /// Creates the exception for a command the carrier refused to write because the router had already closed the
        /// connection — the same report as <see cref="CreateClosed"/>, except that the command did not run.
        /// </summary>
        /// <param name="transport">Short transport name, e.g. <c>"SSH"</c>.</param>
        /// <param name="timeoutMs">The configured receive timeout.</param>
        /// <param name="sentCommand">The command that was to be sent.</param>
        /// <param name="cause">The carrier's own exception.</param>
        internal static TikConnectionReceiveTimeoutException CreateClosedBeforeWrite(
            string transport, int timeoutMs, string sentCommand, Exception cause)
        {
            string message =
                transport + ": the router closed the connection before the command could be written — "
                + cause.GetType().Name + ": " + cause.Message + ". Command: '" + sentCommand.Trim() + "'. "
                + "It was not sent, so it did not run.";

            return new TikConnectionReceiveTimeoutException(TimeSpan.FromMilliseconds(timeoutMs), message, null, cause);
        }

        // Last TailChars characters with line breaks made visible, so the quoted tail stays on one line.
        private static string Tail(string received)
        {
            string tail = received.Length <= TailChars
                ? received
                : "…" + received.Substring(received.Length - TailChars);
            return tail.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
