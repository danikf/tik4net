using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// What a command line may contain when it is typed into RouterOS's line editor: inside a double-quoted string, only
    /// printable ASCII, everything else written as the hex escape <c>\XX</c> of its bytes.
    /// </summary>
    /// <remarks>
    /// <para>A CLI transport does not send a command, it types it, and the line editor acts on keys whether or not they
    /// stand in a quoted string. Measured over Telnet on RouterOS 7.25.1 and 6.49.21: a Ctrl-C (0x03) inside a quoted
    /// value drops the line typed so far, so the rest of the value runs as a new command; the byte 0x9B is a CSI and eats
    /// the characters after it — and it is the second byte of the UTF-8 form of <c>ě</c> (C4 9B), as of every character
    /// whose UTF-8 form holds a byte 0x80–0x9F.</para>
    /// <para>The hex escape stores the byte itself on both versions (read back over the API: <c>\03</c>, <c>\1B</c>,
    /// <c>\7F</c>, <c>\00</c>, and <c>\C4\9B</c> for <c>ě</c>). Only uppercase digits parse.</para>
    /// <para>Control characters are escaped by <see cref="CliCommandBuilder"/> already, since they need no encoding. The
    /// console applies <see cref="ForTerminal"/> to every line it types, translated or raw: it changes how a string's
    /// bytes are delivered, not what the router reads them as, the way doubling a Telnet IAC does.</para>
    /// </remarks>
    internal static class CliTerminalLine
    {
        /// <summary>A C0 control character or DEL.</summary>
        internal static bool IsControl(char c) => c < 0x20 || c == 0x7F;

        /// <summary>Appends <c>\XX</c>, two uppercase hex digits: RouterOS does not parse lowercase ones.</summary>
        internal static void AppendHexEscape(StringBuilder sb, byte b)
        {
            const string digits = "0123456789ABCDEF";
            sb.Append('\\').Append(digits[b >> 4]).Append(digits[b & 0xF]);
        }

        /// <summary>
        /// <paramref name="line"/> with every character inside a double-quoted string that is not printable ASCII
        /// replaced by the hex escapes of its bytes in <paramref name="encoding"/>. Outside quotes the line is unchanged:
        /// that is command syntax, and a builder never puts a value there unquoted.
        /// </summary>
        /// <remarks>Inside quotes a backslash and the character after it are copied as a pair, so an escaped quote does
        /// not end the string and an escape already written stays as it is. TAB, CR and LF are left alone here: the
        /// builder writes them as escapes itself, and in a raw line a backslash before a line break is RouterOS's line
        /// continuation.</remarks>
        internal static string ForTerminal(string line, Encoding encoding)
        {
            if (!NeedsEscape(line))
                return line;

            var sb = new StringBuilder(line.Length + 16);
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!quoted)
                {
                    quoted = c == '"';
                    sb.Append(c);
                }
                else if (c == '\\' && i + 1 < line.Length && IsPassedThrough(line[i + 1]))
                {
                    sb.Append(c).Append(line[++i]);
                }
                else if (IsPassedThrough(c))
                {
                    quoted = c != '"';
                    sb.Append(c);
                }
                else
                {
                    i = AppendEscapedChar(sb, line, i, encoding);
                }
            }
            return sb.ToString();
        }

        // Printable ASCII, and the three controls left to the builder (see ForTerminal's remarks).
        private static bool IsPassedThrough(char c)
            => (c >= 0x20 && c < 0x7F) || c == '\t' || c == '\r' || c == '\n';

        private static bool NeedsEscape(string line)
        {
            foreach (char c in line)
                if (!IsPassedThrough(c))
                    return true;
            return false;
        }

        // One character, or a surrogate pair, as the escapes of its bytes; returns the index of its last char.
        private static int AppendEscapedChar(StringBuilder sb, string line, int i, Encoding encoding)
        {
            int length = char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]) ? 2 : 1;
            foreach (byte b in encoding.GetBytes(line.Substring(i, length)))
                AppendHexEscape(sb, b);
            return i + length - 1;
        }
    }
}
