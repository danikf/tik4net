using System.Text;

namespace tik4net.Cli
{
    /// <summary>Small text helpers the terminal clients share.</summary>
    internal static class CliTerminalText
    {
        /// <summary>Ctrl-C: aborts a running command or the input line (Tab completion, the nag, the resync).</summary>
        internal const byte CtrlC = 0x03;

        /// <summary>
        /// One Enter on a RouterOS console over a PTY (SSH), MAC-Telnet or the WinBox mepty terminal: CR. A CR LF there is
        /// two Enters, and the second one answers a question with its default (Safe Mode's <c>/quit</c>: N) or makes
        /// RouterOS 6.49 repaint the prompt after the real end of a response.
        /// </summary>
        internal const string Enter = "\r";

        /// <summary>One Enter on Telnet: CR LF, which the NVT reads as one (RFC 854).</summary>
        internal const string TelnetEnter = "\r\n";

        /// <summary>How raw bytes are named in a message: printable text as itself, a control key as <c>^X</c>.</summary>
        internal static string KeyName(byte[] raw, Encoding encoding)
            => raw.Length == 1 && raw[0] < 0x20 ? "^" + (char)(raw[0] + 0x40) : encoding.GetString(raw);
    }
}
