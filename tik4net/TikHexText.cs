namespace tik4net
{
    /// <summary>A byte run as unseparated hexadecimal digits, two to a byte: <c>0A0B</c>.</summary>
    /// <remarks>
    /// Written in upper case, read in either. <c>Convert.ToHexString</c> exists only on net8.0, so the netstandard2.0 build
    /// needs this either way.
    /// </remarks>
    internal static class TikHexText
    {
        internal static string Format(byte[] bytes) => Format(bytes, 0, bytes.Length);

        internal static string Format(byte[] bytes, int offset, int count)
        {
            var chars = new char[count * 2];
            for (int i = 0; i < count; i++)
            {
                byte b = bytes[offset + i];
                chars[i * 2] = Digit(b >> 4);
                chars[i * 2 + 1] = Digit(b & 0xF);
            }
            return new string(chars);
        }

        /// <summary>Reads an even, non-empty run of hex digits; anything else, whitespace included, is not hex.</summary>
        internal static bool TryParse(string? text, out byte[]? bytes)
        {
            bytes = null;
            if (text == null || text.Length == 0 || text.Length % 2 != 0 || !IsHex(text)) return false;

            var result = new byte[text.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = (byte)(ValueOf(text[i * 2]) << 4 | ValueOf(text[i * 2 + 1]));
            bytes = result;
            return true;
        }

        /// <summary>Whether every character is a hex digit; an empty string has none that is not.</summary>
        internal static bool IsHex(string text)
        {
            foreach (char c in text)
                if (!IsHexDigit(c)) return false;
            return true;
        }

        internal static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        internal static int ValueOf(char digit)
            => digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10;

        private static char Digit(int n) => (char)(n < 10 ? '0' + n : 'A' + n - 10);
    }
}
