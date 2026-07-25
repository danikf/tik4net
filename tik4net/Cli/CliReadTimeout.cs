using System;

namespace tik4net.Cli
{
    /// <summary>
    /// Builds the exception raised when a terminal transport's request/reply read reaches the receive
    /// deadline without ever seeing the closing shell prompt.
    /// </summary>
    /// <remarks>
    /// <para>Every CLI transport used to <c>return</c> the partial text at this point, and the caller had no
    /// way to tell it apart from a complete, short answer — so a half-read table silently became "the table"
    /// and a half-read record became a parse error blamed on the parser. That is the same class of defect as
    /// a swallowed exception: the failure existed, but nothing could observe it.</para>
    /// <para>It is not hypothetical. It is how RouterOS's Safe Mode prompt could stop matching ours without a
    /// single test going red — every command inside safe mode simply ran to the full 30 s deadline and
    /// returned the (complete, as it happened) text anyway, so the suite passed while taking 4½ minutes
    /// (P2.31).</para>
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

            return new TikConnectionReceiveTimeoutException(timeoutMs, message,
                received.Length == 0 ? null : received);
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
