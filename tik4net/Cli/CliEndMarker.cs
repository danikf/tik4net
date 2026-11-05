using System;

namespace tik4net.Cli
{
    /// <summary>How a CLI command is framed so that the end of its answer can be found.</summary>
    /// <remarks>
    /// A <c>print</c> is sent <c>without-paging</c>, so the answer is not held at a <c>-- more --</c> pause, and a write
    /// is followed by a statement that prints a marker line once it is over (<see cref="WithEndMarker"/>).
    /// </remarks>
    internal static class CliEndMarker
    {
        private const string PrintToken    = "print";
        private const string WithoutPaging = "without-paging";

        /// <summary>
        /// Injects <c>without-paging</c> immediately after the <c>print</c> token when
        /// the command contains <c>print</c> but does not already contain <c>without-paging</c>.
        /// <para>
        /// Commands wrapped in <c>:put [ … ]</c> are left untouched: script context does not page (so the
        /// modifier is unnecessary), and the <c>print</c> token they match is often INSIDE a quoted value
        /// (e.g. a <c>/system/script/add source="… /system identity print …"</c>), where injecting
        /// <c>without-paging</c> would corrupt the stored value. Print modifiers for <c>:put</c> reads are
        /// already added by <see cref="CliCommandBuilder.BuildPrint"/>.
        /// </para>
        /// </summary>
        internal static string InjectWithoutPaging(string command)
        {
            if (command.TrimStart().StartsWith(":put", StringComparison.OrdinalIgnoreCase))
                return command;
            if (command.IndexOf(WithoutPaging, StringComparison.OrdinalIgnoreCase) >= 0)
                return command;

            int idx = IndexOfToken(command, PrintToken);
            if (idx < 0) return command;

            int insertAt = idx + PrintToken.Length;
            return command.Substring(0, insertAt) + " " + WithoutPaging + command.Substring(insertAt);
        }

        private const string EndMarkerHead = "#e";

        /// <summary>
        /// <paramref name="command"/> with a statement after it that prints <c>#e=&lt;nonce&gt;</c> — the line that says
        /// the command is over, so its read need not wait for the prompt to stay silent. The marker is computed
        /// (<c>("#e" . "=&lt;nonce&gt;")</c>), so the echoed command never contains the line it prints; the nonce ties
        /// the line to this command. A command the router refuses stops the line before the marker, and its read
        /// settles as before.
        /// </summary>
        internal static string WithEndMarker(string command, string nonce)
            => command + "; :put (\"" + EndMarkerHead + "\" . \"=" + nonce + "\")";

        /// <summary>The line <see cref="WithEndMarker"/> makes the router print, or <c>null</c> when the command has none.</summary>
        internal static string? EndMarkerLineOf(string? sentCommand)
        {
            if (sentCommand == null)
                return null;
            const string tail = "; :put (\"" + EndMarkerHead + "\" . \"=";
            int at = sentCommand.LastIndexOf(tail, StringComparison.Ordinal);
            if (at < 0 || !sentCommand.EndsWith("\")", StringComparison.Ordinal))
                return null;
            string nonce = sentCommand.Substring(at + tail.Length, sentCommand.Length - at - tail.Length - 2);
            return nonce.Length == 0 || nonce.IndexOf('"') >= 0 ? null : EndMarkerHead + "=" + nonce;
        }

        /// <summary>
        /// The cleaned output of a command sent <see cref="WithEndMarker"/>, without its marker line — what the
        /// command alone would have printed. Output without the marker (a refused command) is returned as it is.
        /// </summary>
        internal static string WithoutEndMarker(string output, string markerLine)
        {
            string text = output ?? string.Empty;
            string trimmed = text.TrimEnd('\r', '\n', ' ');
            if (!trimmed.EndsWith(markerLine, StringComparison.Ordinal))
                return text;
            string head = trimmed.Substring(0, trimmed.Length - markerLine.Length);
            if (head.Length > 0 && head[head.Length - 1] != '\n')
                return text;   // the marker text inside a longer line is not the marker line
            return head.TrimEnd('\r', '\n');
        }

        // Finds the start of token as a whole word (surrounded by non-alpha or at string bounds).
        private static int IndexOfToken(string source, string token)
        {
            int start = 0;
            while (start <= source.Length - token.Length)
            {
                int idx = source.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return -1;

                bool leftOk  = idx == 0 || !char.IsLetterOrDigit(source[idx - 1]);
                bool rightOk = idx + token.Length >= source.Length || !char.IsLetterOrDigit(source[idx + token.Length]);

                if (leftOk && rightOk) return idx;
                start = idx + 1;
            }
            return -1;
        }
    }
}
