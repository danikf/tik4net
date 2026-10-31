using System.Text;

namespace tik4net.Cli
{
    /// <summary>Small text helpers the terminal clients share.</summary>
    internal static class CliTerminalText
    {
        /// <summary>How raw bytes are named in a message: printable text as itself, a control key as <c>^X</c>.</summary>
        internal static string KeyName(byte[] raw, Encoding encoding)
            => raw.Length == 1 && raw[0] < 0x20 ? "^" + (char)(raw[0] + 0x40) : encoding.GetString(raw);
    }
}
