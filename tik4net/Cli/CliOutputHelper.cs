using System;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// Shared CLI output post-processing helpers used by all PTY transports
    /// (Telnet, MAC-Telnet, SSH PTY).
    /// </summary>
    internal static class CliOutputHelper
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
            if (command == null) return command;
            if (command.TrimStart().StartsWith(":put", StringComparison.OrdinalIgnoreCase))
                return command;
            if (command.IndexOf(WithoutPaging, StringComparison.OrdinalIgnoreCase) >= 0)
                return command;

            int idx = IndexOfToken(command, PrintToken);
            if (idx < 0) return command;

            int insertAt = idx + PrintToken.Length;
            return command.Substring(0, insertAt) + " " + WithoutPaging + command.Substring(insertAt);
        }

        /// <summary>
        /// Removes the command echo (leading lines) and every trailing shell prompt from the ANSI-stripped
        /// router response — the prompt can be repainted more than once, see below.
        /// Returns the data lines joined with '\n'.
        /// </summary>
        internal static string CleanOutput(string stripped, string sentCommand)
        {
            string[] lines = stripped.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int start = 0;
            int end   = lines.Length - 1;

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
                // Compare with the leading '/' trimmed on BOTH sides. cmdCore already had it removed, but
                // the terminal echoes the command exactly as sent — "/interface set [find …]" — so for
                // every command that starts with a slash (i.e. all of set/remove/enable/disable/move) the
                // two never matched and the echo survived into the "output". That was invisible for as
                // long as nothing read meaning into leftover text; it becomes a false error the moment a
                // silent-on-success verb treats residue as a failure (P2.12).
                string lineCore = line.TrimStart('/');
                bool isEcho =
                    IsPromptPrefixed(line)
                    // An asynchronous log line (see IsRouterLogLine) can land at the HEAD of a response,
                    // ahead of the echo — the router writes it whenever it is emitted, not where it fits.
                    // Without this it is the first line that looks like data, so the loop stopped here and
                    // the command echo behind it survived into the output: a read got the echo prepended to
                    // its first record, and a silent-on-success write got a non-empty "output" that P2.12's
                    // positional rule reads as the router rejecting it. The join below already discards log
                    // lines, so skipping them here loses nothing it did not already lose.
                    || IsRouterLogLine(line)
                    || (cmdCore.Length > 0
                        && (cmdCore.IndexOf(lineCore, StringComparison.OrdinalIgnoreCase) >= 0
                            || cmdCore.StartsWith(lineCore, StringComparison.OrdinalIgnoreCase)));
                if (!isEcho)
                    break;
                start++;
            }

            // Strip the trailing shell prompt(s) — plural, deliberately. RouterOS repaints the prompt line
            // after a command completes, so the tail can carry it MORE THAN ONCE with blank lines between:
            // measured over SSH (channel ssh.pty), a single read returns
            //   ".id=…;list=X<CR><LF><CR><CR><CR><ESC>[9999B[admin@CHR] > <CR><LF><CR><CR><CR><CR><ESC>[9999B[admin@CHR] > "
            // Removing only the last one left the FIRST prompt inside the data: CliOutputParser turns
            // newlines into ';' field separators, "[admin@CHR] >" carries no '=', and it was appended to the
            // preceding field as a multi-value continuation — the read-back of an interface-list member came
            // out as "t4n-test-926b98b7,[admin@CHR] >" (P2.29). Silent-on-success verbs were never affected,
            // which is why this hid: with no data line the echo loop above (which treats any prompt-bearing
            // line as noise) consumes the whole response, so the residue only survives when there IS output.
            while (end >= start)
            {
                while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
                    end--;
                if (end < start)
                    break;

                if (IsPromptLine(lines[end]))
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
                // "Missing field '.id'", and under P2.12 it would be read as the router rejecting a write.
                // Recognised by the leading wall-clock stamp, which no as-value record and no RouterOS
                // diagnostic ever starts with.
                if (IsRouterLogLine(lines[i])) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return sb.ToString();
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
        /// Anchored on the leading <c>'['</c> for the same reason as <see cref="IsPromptLine"/>: this used to
        /// be a bare "contains <c>] &gt;</c>" test, which discarded any leading data line that happened to
        /// contain the sequence — a stored script source, a comment — as if it were an echo.
        /// </remarks>
        internal static bool IsPromptPrefixed(string line)
        {
            string s = (line ?? string.Empty).TrimStart();
            if (s.Length == 0 || s[0] != '[')
                return false;
            return RouterOsCliLogin.ContainsPromptSuffix(s);
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
