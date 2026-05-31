using System.Text.RegularExpressions;

namespace tik4net.Cli
{
    /// <summary>
    /// Utility class for stripping ANSI/VT100 escape sequences from terminal output.
    /// Transport-agnostic: does NOT remove command echo or shell prompts — that is a
    /// PTY-transport concern handled in the concrete transport implementation (chapter C/E).
    /// </summary>
    public static class VtStripper
    {
        // Covers:
        //   CSI sequences:  ESC [ {params} {final}        e.g. ESC[32m, ESC[1;33m, ESC[?25h
        //   OSC sequences:  ESC ] {text} BEL               e.g. ESC]0;title BEL
        //   Other ESC+char: ESC {single char}              e.g. ESC= ESC7 ESC8
        private static readonly Regex AnsiRegex = new Regex(
            @"\x1B(\[[0-9;?]*[A-Za-z]|\][^\x07]*\x07|.)",
            RegexOptions.Compiled);

        /// <summary>
        /// Removes all ANSI/VT100 escape sequences from <paramref name="input"/>.
        /// Returns an empty string if <paramref name="input"/> is null.
        /// </summary>
        public static string StripAnsi(string input)
            => AnsiRegex.Replace(input ?? string.Empty, string.Empty);
    }
}
