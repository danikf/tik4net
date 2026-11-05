using System;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// What of a terminal read is the command's answer: the read without the command's echo, the repainted prompts and
    /// the console log lines the router writes into the session.
    /// </summary>
    internal static class CliOutputCleaner
    {
        /// <summary>
        /// Removes the command echo (leading lines) and every trailing shell prompt from the ANSI-stripped
        /// router response — the prompt can be repainted more than once, see below.
        /// Returns the data lines joined with '\n'.
        /// </summary>
        internal static string CleanOutput(string stripped, string? sentCommand)
        {
            string[] lines = stripped.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int start = 0;
            int end   = lines.Length - 1;

            // Anchor the response on OUR OWN echo, and throw away anything that precedes it.
            //
            // Every read here is terminated by a prompt that has been silent for a settle window, and the
            // pre-send drain that clears the leftovers is gated on DataAvailable at one instant — so a tail
            // that arrives just after both can be carried into the NEXT command's read, where it lands
            // ahead of that command's echo. The head-trim loop below cannot see this: foreign output is
            // not blank, not a prompt and not a fragment of the sent command, so it looks exactly like the
            // first genuine data line and the loop stops on it. The result is another command's answer
            // returned as this one's, which is silent — and worth stating plainly, because a spliced
            // 'add' response hands back an '.id' that was never created here and the read-back after it
            // fails with "no such item" a layer away from the cause.
            //
            // Only a line before the echo that is real foreign content triggers this; blank lines,
            // repainted prompts, asynchronous log lines and partial echoes are the ordinary head of a
            // response and are left to the loop below.
            start = SkipForeignResidue(lines, sentCommand, start, end);

            // Skip ALL leading empty lines and command-echo lines. A PTY transport may echo the
            // command more than once: RouterOS character-echoes the typed command and then repaints
            // the line-editor as "<prompt> <command>" (prompt-prefixed echo). For a command whose text
            // contains newlines (e.g. /system/script/add with a multi-line source), the echo is split
            // across several output lines. Telnet (CR-LF) typically produces a single echo; MAC-Telnet
            // (raw VT100, CR only) produces both forms. Removing only the first echo line leaves the
            // residual prompt-prefixed echo, which then merges into the first as-value record and
            // corrupts it (record without .id) or is mistaken for an add's returned id.
            //
            // A leading line is treated as noise when it is blank, opens with the shell prompt
            // ("[user@identity] > …"), or is a fragment of the sent command. A real data line
            // (as-value record starting with ".id=", a bare ".id" value, an error line) matches none
            // of these, so the loop stops at the first genuine output line — safe across transports.
            string cmdCore = (sentCommand ?? string.Empty).TrimStart('/');
            while (start <= end)
            {
                string line = lines[start].Trim();
                if (line.Length == 0)
                {
                    start++;
                    continue;
                }
                // Compare with the leading '/' trimmed on BOTH sides: the terminal echoes the command exactly as sent —
                // "/interface set [find …]" — and an echo left in the output is a false error on a silent-on-success
                // verb, which treats residue as a failure.
                string lineCore = line.TrimStart('/');
                bool isEcho =
                    CliPromptText.IsPromptPrefixed(line)
                    // An asynchronous log line (see IsRouterLogLine) can land at the HEAD of a response,
                    // ahead of the echo — the router writes it whenever it is emitted, not where it fits. It is no data line: the
                    // echo behind it must still be found, and the join below discards log lines anyway.
                    || IsRouterLogLine(line)
                    || (cmdCore.Length > 0
                        && cmdCore.IndexOf(lineCore, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!isEcho)
                    break;
                start++;
            }

            // Strip the trailing shell prompt(s) — plural, deliberately. RouterOS repaints the prompt line
            // after a command completes, so the tail can carry it MORE THAN ONCE with blank lines between:
            // measured over SSH (channel ssh.pty), a single read returns
            //   ".id=…;list=X<CR><LF><CR><CR><CR><ESC>[9999B[admin@CHR] > <CR><LF><CR><CR><CR><CR><ESC>[9999B[admin@CHR] > "
            // A prompt left inside the data reads as a value: CliOutputParser turns newlines into ';' field separators,
            // "[admin@CHR] >" carries no '=', and it is appended to the preceding field as a multi-value continuation.
            while (end >= start)
            {
                while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
                    end--;
                if (end < start)
                    break;

                // A console log entry after the answer is followed by one more repaint, so the tail can be
                // "prompt, log line, prompt": the log line is skipped like a prompt, or it ends this loop with the
                // prompts above it still inside the data — after the count line of a counted read, which then
                // reads as missing (measured over SSH under load, a WinBox login failure from another session).
                if (CliPromptText.IsPromptLine(lines[end]) || IsRouterLogLine(lines[end]))
                    end--;
                else
                    break;
            }

            if (start > end)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = start; i <= end; i++)
            {
                // RouterOS writes log entries straight into the terminal — the shipped logging rules send
                // `critical` topics to the console, so this happens on a stock router, not just a lab one.
                // Such a line is unrelated to the command that happens to be running, and leaving it in
                // corrupts whatever consumes the output: it shredded an as-value parse into
                // "Missing field '.id'", and the positional rule would read it as the router rejecting a write.
                // Recognised by the leading wall-clock stamp, which no as-value record and no RouterOS
                // diagnostic ever starts with.
                if (IsRouterLogLine(lines[i])) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// True once <paramref name="strippedSoFar"/> carries the echo of <paramref name="sentCommand"/> —
        /// i.e. the router has started answering the command we actually sent. <see cref="CliPromptSettle"/>, which
        /// ends every terminal client's command read, requires this before it lets a settled prompt terminate the read.
        /// </summary>
        /// <remarks>
        /// The prompt on its own is not proof: a prompt left over from the PREVIOUS command's response —
        /// one that arrived after that read had already returned and after this command's pre-send drain
        /// had looked at the socket — settles just as convincingly, and terminates this read before the
        /// router has said anything. What comes back is then the previous command's tail, which is silent
        /// and, for an <c>add</c>, hands the caller an <c>.id</c> that was never created here.
        /// <para>True (i.e. no gate) when there is nothing to anchor on: a control-key read, which need not
        /// be answered at all, or a command too short to be distinctive. Waiting for the echo costs nothing
        /// in the ordinary case — it is on screen long before the prompt that ends the response — and where
        /// it is genuinely late this turns a wrong answer into a correct one rather than into a failure;
        /// only a response that never arrives reaches the receive deadline, where the read already
        /// throws.</para>
        /// </remarks>
        internal static bool ContainsEcho(string strippedSoFar, string? sentCommand)
        {
            string? key = EchoKey(sentCommand);
            if (key == null)
                return true;
            return Squash(strippedSoFar).IndexOf(key, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Returns the index of the first line that belongs to this command's own response: the line
        /// carrying its echo, when there is genuine foreign content in front of it; otherwise
        /// <paramref name="start"/> unchanged. Reports what it dropped, and reports an echo it could not
        /// find at all, through the wire trace (channel <c>cli.align</c>).
        /// </summary>
        /// <remarks>
        /// The echo is matched on whitespace-squashed text because the router's line editor repaints the
        /// line rather than replaying it byte for byte, and only on a leading slice of the command (the
        /// tail of a long <c>add</c> may be wrapped or repainted separately) — long enough that no data
        /// line matches it by accident. The FIRST match wins, which is what makes a false match later in
        /// the output harmless: the real echo always precedes the output it introduces.
        /// </remarks>
        private static int SkipForeignResidue(string[] lines, string? sentCommand, int start, int end)
        {
            string? key = EchoKey(sentCommand);
            if (key == null)
                return start;   // nothing distinctive enough to anchor on (control keys, empty command)

            string cmdSquashed = Squash(sentCommand!);   // key != null (checked above) implies EchoKey saw a non-null sentCommand

            int echoAt = -1;
            for (int i = start; i <= end; i++)
            {
                if (Squash(lines[i]).IndexOf(key, StringComparison.Ordinal) >= 0)
                {
                    echoAt = i;
                    break;
                }
            }

            if (echoAt < 0)
            {
                // The response does not contain the command that asked for it. Either the read returned on
                // a prompt belonging to the previous command (so this text is entirely someone else's), or
                // the echo never arrived. Not turned into a failure here: CleanOutput is also fed reads
                // that legitimately carry no echo, and the loop below still does something sane.
                if (Diagnostics.TikWireTrace.Enabled)
                    Diagnostics.TikWireTrace.Emit("cli.align", Diagnostics.TikWireDir.Note,
                        "echo-missing cmd=" + Preview(sentCommand) + " head=" + Preview(FirstContentLine(lines, start, end)));
                return start;
            }

            int residue = -1;
            for (int i = start; i < echoAt; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || CliPromptText.IsPromptPrefixed(line) || IsRouterLogLine(line))
                    continue;
                // A partial echo: the character-echo of the same command, split by a repaint.
                string squashed = Squash(line);
                if (squashed.Length > 0 && cmdSquashed.IndexOf(squashed, StringComparison.Ordinal) >= 0)
                    continue;
                residue = i;
                break;
            }

            if (residue < 0)
                return start;   // ordinary head — leave it to the caller's loop, unchanged behaviour

            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit("cli.align", Diagnostics.TikWireDir.Note,
                    "residue-dropped cmd=" + Preview(sentCommand) + " dropped=" + Preview(lines[residue]));

            return echoAt;
        }

        // A leading slice of the command, squashed, long enough to be unique among a response's data lines.
        // null when there is nothing to anchor on.
        private static string? EchoKey(string? sentCommand)
        {
            if (string.IsNullOrEmpty(sentCommand))
                return null;

            // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
            int nl = sentCommand!.IndexOfAny(new[] { '\r', '\n' });
            string firstLine = nl >= 0 ? sentCommand.Substring(0, nl) : sentCommand;

            string key = Squash(firstLine);
            if (key.Length < 8)
                return null;
            return key.Length > 40 ? key.Substring(0, 40) : key;
        }

        // Whitespace removed and lower-cased: the router repaints the echo, it does not replay the bytes.
        private static string Squash(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (!char.IsWhiteSpace(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static string FirstContentLine(string[] lines, int start, int end)
        {
            for (int i = start; i <= end; i++)
                if (lines[i].Trim().Length > 0)
                    return lines[i];
            return string.Empty;
        }

        private static string Preview(string? s)
        {
            s = (s ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n").Trim();
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }

        /// <summary>
        /// True for an asynchronous router log line echoed into the terminal — <c>"19:54:32 system,error,critical
        /// login failure …"</c>, or the <c>"jul/25 19:54:32 …"</c> form used once the entry is not from today.
        /// </summary>
        internal static bool IsRouterLogLine(string line)
        {
            string s = (line ?? string.Empty).TrimStart();

            // Optional "mmm/dd " date prefix on entries that are not from today.
            if (s.Length > 7 && s[3] == '/' && char.IsLetter(s[0]) && char.IsDigit(s[4]))
            {
                int sp = s.IndexOf(' ');
                if (sp > 0) s = s.Substring(sp + 1);
            }

            // hh:mm:ss followed by a space — the console log stamp.
            return s.Length > 9
                && char.IsDigit(s[0]) && char.IsDigit(s[1]) && s[2] == ':'
                && char.IsDigit(s[3]) && char.IsDigit(s[4]) && s[5] == ':'
                && char.IsDigit(s[6]) && char.IsDigit(s[7]) && s[8] == ' ';
        }
    }
}
