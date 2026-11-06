using System;

namespace tik4net.Cli
{
    /// <summary>Whether the terminal text read so far ends at the prompt that ends a command's answer.</summary>
    internal static class CliPromptText
    {
        /// <summary>What a settled-looking prompt means for a read: see <see cref="JudgePrompt"/>.</summary>
        internal enum PromptVerdict
        {
            /// <summary>Not a counted read: wait for the prompt to stay silent, as every read did before.</summary>
            Settle,

            /// <summary>A counted read whose own last line has arrived: the answer is complete now.</summary>
            Complete,

            /// <summary>
            /// A counted read with nothing of its own after the echo yet: this prompt is a repaint (after a console
            /// log line, say), not the end of the answer — keep reading.
            /// </summary>
            NotYet,
        }

        /// <summary>
        /// Decides, for a read that has seen its echo and now ends at a prompt, whether the prompt ends the answer —
        /// by the answer's expected last line rather than by silence.
        /// </summary>
        /// <remarks>
        /// <para>A counted read (<see cref="CliCommandBuilder.CountMarker"/>, <see cref="CliCommandBuilder.WindowMarker"/>)
        /// always writes its count last, so the count line followed by the prompt IS the end: no settle window to wait
        /// out (120–150 ms, which was 59 % of an average CLI command — measured over SSH, 1874 commands of a full leg,
        /// 2026-10-01). The marker on the command line itself is never taken for it: the echo carries
        /// <c>("#n=" . …)</c>, never a line that is only the marker and a count.</para>
        /// <para>Nor does a prompt end a counted read before anything of its own has come: RouterOS repaints the prompt
        /// after every console log line it writes into the session. It holds such a line while a command runs
        /// (Docs/findings-cli.md §7, measured over Telnet on 7.x and 6.49.21), so this is a guard: a prompt with
        /// nothing behind the echo is no proof of the end. A counted read that is refused has text of its own (the error), so it
        /// still settles as before — only a prompt with nothing behind the echo is waited past, up to the deadline.</para>
        /// </remarks>
        internal static PromptVerdict JudgePrompt(string strippedSoFar, string? sentCommand)
        {
            string? endLine = CliEndMarker.EndMarkerLineOf(sentCommand);
            if (endLine == null && !IsCountedCommand(sentCommand))
                return PromptVerdict.Settle;
            if (endLine != null ? LastLineIs(strippedSoFar, endLine) : EndsWithCountLine(strippedSoFar))
                return PromptVerdict.Complete;
            return CliOutputCleaner.CleanOutput(strippedSoFar, sentCommand).Length == 0 ? PromptVerdict.NotYet : PromptVerdict.Settle;
        }

        // The last line that is not blank, a prompt or a console log line.
        private static string? LastContentLine(string stripped)
        {
            string[] lines = stripped.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.Length > 0 && !IsPromptLine(line) && !CliOutputCleaner.IsRouterLogLine(line))
                    return line;
            }
            return null;
        }

        private static bool LastLineIs(string stripped, string expected) => LastContentLine(stripped) == expected;

        // A command whose last statement writes the count line — the literal marker in quotes, as CliCommandBuilder
        // writes it (":put (\"#n=\" . …)").
        private static bool IsCountedCommand(string? sentCommand)
            => sentCommand != null
               && (sentCommand.IndexOf("\"" + CliCommandBuilder.CountMarker + "\"", StringComparison.Ordinal) >= 0
                   || sentCommand.IndexOf("\"" + CliCommandBuilder.WindowMarker + "\"", StringComparison.Ordinal) >= 0);

        // The last line that is not blank, a prompt or a console log line is the count line: "#n=<len>/<kind>" or "#w=<len>".
        private static bool EndsWithCountLine(string stripped)
        {
            string? line = LastContentLine(stripped);
            if (line == null)
                return false;
            if (line.StartsWith(CliCommandBuilder.CountMarker, StringComparison.Ordinal))
                return CliCommandBuilder.ExpectedRecordCount(line.Substring(CliCommandBuilder.CountMarker.Length)) >= 0;
            return line.StartsWith(CliCommandBuilder.WindowMarker, StringComparison.Ordinal)
                   && IsDigits(line.Substring(CliCommandBuilder.WindowMarker.Length));
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>
        /// True when <paramref name="strippedSoFar"/> ends with the shell prompt that ends a command's response.
        /// </summary>
        /// <remarks>
        /// For a typed command the prompt has to start a line of its own. RouterOS 6 echoes typed input by
        /// repainting the whole line — prompt included — after every character (<c>c&#13;[admin@X] &gt; &lt;line
        /// so far&gt;</c>), so a read can stop on a repainted prompt in the middle of the echo; measured on 6.49.13
        /// over the WinBox terminal, where the read then stopped pulling and returned nothing. The prompt that
        /// ends the response follows the output's line break (only carriage returns between). A control key
        /// (<paramref name="sentCommand"/> null) is not ended by a line break, so it keeps the plain prompt test.
        /// <para>A control key is ended by a question as well (<see cref="EndsWithKeyQuestion"/>).</para>
        /// </remarks>
        internal static bool EndsWithCompletionPrompt(string strippedSoFar, string? sentCommand)
        {
            if (EndsWithKeyQuestion(strippedSoFar, sentCommand))
                return true;
            if (!RouterOsCliLogin.IsShellPrompt(strippedSoFar))
                return false;
            if (sentCommand == null)
                return true;
            string t = strippedSoFar.TrimEnd('\r', '\n', ' ');
            string lastLine = t.Substring(t.LastIndexOf('\n') + 1).TrimStart('\r');
            return lastLine.IndexOf('\r') < 0;
        }

        /// <summary>
        /// True when a control key (<paramref name="sentCommand"/> null) was answered with a question that waits for
        /// its answer — the text ends in a choice list such as <c>[u/r/d]:</c>, <c>[u/r]?</c> or <c>[y/N]</c>. No
        /// prompt follows until it is answered, so the read ends there rather than at the receive deadline.
        /// </summary>
        /// <remarks>
        /// The Safe Mode key meets one when another session holds Safe Mode (6.49.13 <c>Hijacking Safe Mode from
        /// someone - unroll/release/don't take it [u/r/d]:</c>, 7.24.4 <c>Unroll, release or abort [u/r]?</c>).
        /// Waiting out the deadline there is not only slow: RouterOS logs an idle MAC-Telnet console out after about
        /// 30 s, so the answer sent after it never arrived (6.49.13, 10 takes in 34).
        /// </remarks>
        private static bool EndsWithKeyQuestion(string strippedSoFar, string? sentCommand)
        {
            if (sentCommand != null)
                return false;
            string t = strippedSoFar.TrimEnd(' ', '\r', '\n');
            if (t.EndsWith(":", StringComparison.Ordinal) || t.EndsWith("?", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);
            if (!t.EndsWith("]", StringComparison.Ordinal))
                return false;
            int open = t.LastIndexOf('[');
            if (open < 0)
                return false;
            string[] choices = t.Substring(open + 1, t.Length - open - 2).Split('/');
            if (choices.Length < 2)
                return false;
            foreach (string c in choices)
                if (c.Length != 1 || !char.IsLetter(c[0]))
                    return false;
            return true;
        }

        /// <summary>
        /// True for a line that is nothing but a repainted shell prompt — <c>"[admin@CHR] &gt; "</c>, or the
        /// <c>"[admin@CHR] &lt;SAFE&gt; "</c> form while Safe Mode is active.
        /// </summary>
        /// <remarks>
        /// The leading <c>'['</c> is what makes this safe to apply repeatedly: a *data* line can also end in
        /// <c>"] &gt;"</c> — a stored script source such as <c>source=:put [$x] &gt;</c> does — and an
        /// EndsWith-only test would delete it as if it were the prompt, silently dropping a record. A prompt
        /// line always starts with the <c>[user@identity]</c> bracket.
        /// </remarks>
        internal static bool IsPromptLine(string line)
        {
            string s = (line ?? string.Empty).Trim();
            if (s.Length == 0 || s[0] != '[')
                return false;
            return RouterOsCliLogin.EndsWithPromptSuffix(s);
        }

        /// <summary>
        /// True for a line that <i>opens</i> with the shell prompt: either the bare repainted prompt or the
        /// prompt-prefixed echo RouterOS's line editor paints, <c>"[admin@CHR] &gt; :put […]"</c>.
        /// </summary>
        /// <remarks>
        /// Anchored on the leading <c>'['</c> for the same reason as <see cref="IsPromptLine"/>: a bare
        /// "contains <c>] &gt;</c>" test would discard any leading data line that happens to contain the
        /// sequence — a stored script source, a comment — as if it were an echo.
        /// </remarks>
        internal static bool IsPromptPrefixed(string line)
        {
            string s = (line ?? string.Empty).TrimStart();
            if (s.Length == 0 || s[0] != '[')
                return false;
            return RouterOsCliLogin.ContainsPromptSuffix(s);
        }
    }
}
