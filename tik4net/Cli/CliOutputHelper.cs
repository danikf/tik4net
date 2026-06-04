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
        /// </summary>
        internal static string InjectWithoutPaging(string command)
        {
            if (command == null) return command;
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
            // command more than once: RouterOS first character-echoes the typed command on its own
            // line and then repaints the line-editor as "<prompt> <command>" (prompt-prefixed echo).
            // Telnet (CR-LF line ending) typically produces a single echo; MAC-Telnet (raw VT100, CR
            // only) produces both. Removing only the first echo leaves the prompt-prefixed echo line,
            // which then merges into the first as-value record and corrupts it (record without .id).
            // A data line never contains the sent command, so this loop is safe across transports.
            string cmdCore = sentCommand.TrimStart('/');
            while (start <= end)
            {
                string line = lines[start].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    start++;
                    continue;
                }
                bool isEcho = !string.IsNullOrEmpty(cmdCore)
                              && (line.IndexOf(cmdCore, StringComparison.OrdinalIgnoreCase) >= 0
                                  || cmdCore.StartsWith(line, StringComparison.OrdinalIgnoreCase));
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
                if (lastLine.EndsWith(RouterOsCliLogin.PromptSuffix, StringComparison.Ordinal))
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
