using System;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// Utility class for stripping ANSI/VT100 escape sequences from terminal output.
    /// Transport-agnostic: does NOT remove command echo or shell prompts — that is a
    /// PTY-transport concern handled in the concrete transport implementation.
    /// </summary>
    // Internal: tik4net.ssh is the only caller outside this assembly and is a friend. The rest of the
    // CLI helper family (CliOutputHelper, Vt100State, CliOutputParser, CliCommandBuilder) is internal
    // already; this one was the odd one out rather than a deliberate public service.
    internal static class VtStripper
    {
        /// <summary>
        /// Removes all ANSI/VT100 escape sequences from <paramref name="input"/>.
        /// Returns an empty string if <paramref name="input"/> is null.
        /// </summary>
        /// <remarks>
        /// Covers CSI sequences (<c>ESC [ {params} {final}</c>, e.g. <c>ESC[32m</c>, <c>ESC[?25h</c>), OSC
        /// sequences (<c>ESC ] {text} BEL</c>) and any other <c>ESC</c> + one character.
        /// <para>
        /// Two of them are applied rather than dropped, within the current line only: cursor back
        /// (<c>ESC[nD</c>), after which text overwrites what is under the cursor, and erase to end of line
        /// (<c>ESC[K</c>) once the cursor has moved back. The router's line editor repaints a typed command in
        /// syntax colours by moving back over it and writing it again — over the WinBox terminal
        /// <c>/interface prnt ESC[15D … /interface prnt</c> — and with the move dropped both copies ran together
        /// into <c>/interface prnt/interface prnt</c>, which no echo check recognises and which then became the
        /// error message. Without a cursor-back the result is exactly the input minus its escape sequences.
        /// </para>
        /// </remarks>
        public static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            int lineStart = 0;   // index in sb where the current line begins
            int cursor = 0;      // index in sb the next character is written to; sb.Length unless moved back

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '\x1b' && i + 1 < input.Length && input[i + 1] != '\n')
                {
                    int last = SequenceEnd(input, i, out char final, out string parameters);
                    if (final == 'D' && last > i + 1)
                    {
                        int n = int.TryParse(parameters, out int parsed) && parsed > 0 ? parsed : 1;
                        cursor = Math.Max(lineStart, cursor - n);
                    }
                    else if (final == 'K' && (parameters.Length == 0 || parameters == "0") && cursor < sb.Length)
                    {
                        sb.Length = cursor;
                    }
                    i = last;
                    continue;
                }

                if (cursor < sb.Length && c != '\r' && c != '\n')
                {
                    sb[cursor++] = c;
                    continue;
                }

                // A line break, or a character at the end of the text: appended, as everything was before
                // cursor movement was applied.
                cursor = sb.Length;
                sb.Append(c);
                cursor++;
                if (c == '\r' || c == '\n')
                    lineStart = sb.Length;
            }
            return sb.ToString();
        }

        // Returns the index of the last character of the escape sequence starting at input[start] (an ESC
        // followed by something other than a line feed). For a complete CSI, final is its final letter and
        // parameters its parameter text; for anything else final is '\0'. An unterminated CSI or OSC is
        // ESC + one character, as the regular expression this replaces matched it.
        private static int SequenceEnd(string input, int start, out char final, out string parameters)
        {
            final = '\0';
            parameters = string.Empty;
            char kind = input[start + 1];
            if (kind == '[')
            {
                int i = start + 2;
                while (i < input.Length && (char.IsDigit(input[i]) && input[i] < 128 || input[i] == ';' || input[i] == '?'))
                    i++;
                if (i < input.Length && IsAsciiLetter(input[i]))
                {
                    final = input[i];
                    parameters = input.Substring(start + 2, i - start - 2);
                    return i;
                }
                return start + 1;
            }
            if (kind == ']')
            {
                int bel = input.IndexOf('\x07', start + 2);
                return bel < 0 ? start + 1 : bel;
            }
            return start + 1;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
