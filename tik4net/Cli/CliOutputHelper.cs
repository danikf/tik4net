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
        /// Removes the command echo (first line) and the trailing shell prompt (last non-empty line)
        /// from the ANSI-stripped router response.
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
            // A leading line is treated as noise when it is blank, shows the shell prompt
            // ("<prompt> …", contains "] >"), or is a fragment of the sent command. A real data line
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
                bool isEcho =
                    line.IndexOf(RouterOsCliLogin.PromptSuffix, StringComparison.Ordinal) >= 0
                    || line.IndexOf(RouterOsCliLogin.SafePromptSuffix, StringComparison.Ordinal) >= 0
                    || (cmdCore.Length > 0
                        && (cmdCore.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0
                            || cmdCore.StartsWith(line, StringComparison.OrdinalIgnoreCase)));
                if (!isEcho)
                    break;
                start++;
            }

            // Remove trailing empty lines
            while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
                end--;

            // Last non-empty line is the shell prompt — remove it
            if (end >= start)
            {
                string lastLine = lines[end].TrimEnd('\r', '\n', ' ');
                if (lastLine.EndsWith(RouterOsCliLogin.PromptSuffix, StringComparison.Ordinal)
                    || lastLine.EndsWith(RouterOsCliLogin.SafePromptSuffix, StringComparison.Ordinal))
                    end--;
            }

            if (start > end)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = start; i <= end; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return sb.ToString();
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
