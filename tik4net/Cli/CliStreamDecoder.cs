using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// The text of one terminal's byte stream, decoded chunk by chunk with the state carried between chunks: a
    /// character whose bytes arrive in two chunks is decoded once, whole.
    /// </summary>
    /// <remarks>
    /// Decoding each chunk on its own (<c>Encoding.GetString</c>) turns both halves of a split multi-byte character
    /// into U+FFFD, so a value holding non-ASCII text changes wherever a frame boundary happens to fall in it —
    /// intermittently, and longer by a character. Seen on WinBox CLI on a file's contents: <c>C7 B2</c> (U+01F2)
    /// read as two U+FFFD once in 25 reads of the same file (7.25.1). One per receive stream; the terminal clients
    /// read their stream from one place at a time, and the lock keeps a second reader from interleaving the state.
    /// </remarks>
    internal sealed class CliStreamDecoder
    {
        private readonly Decoder _decoder;
        private readonly object _lock = new object();

        internal CliStreamDecoder(Encoding encoding)
        {
            _decoder = encoding.GetDecoder();
        }

        /// <summary>The characters <paramref name="bytes"/> completes; an unfinished last character waits for the next chunk.</summary>
        internal string Decode(byte[] bytes) => Decode(bytes, 0, bytes.Length);

        /// <inheritdoc cref="Decode(byte[])"/>
        internal string Decode(byte[] bytes, int offset, int count)
        {
            if (count == 0)
                return string.Empty;
            lock (_lock)
            {
                int n = _decoder.GetCharCount(bytes, offset, count, false);
                var chars = new char[n];
                _decoder.GetChars(bytes, offset, count, chars, 0, false);
                return new string(chars);
            }
        }
    }
}
