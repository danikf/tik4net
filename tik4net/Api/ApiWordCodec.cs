using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace tik4net.Api
{
    /// <summary>
    /// How the binary API frames a word on the wire: a length prefix of 1 to 4 bytes whose high bits give its size
    /// (0xF0 plus four bytes past that), then the word's bytes; a sentence is its words and a zero-length word.
    /// </summary>
    internal static class ApiWordCodec
    {
        /// <summary>The length prefix for a word of <paramref name="length"/> bytes.</summary>
        internal static byte[] EncodeLength(int length)
        {
            if (length < 0x80)
                return new[] { (byte)length };
            if (length < 0x4000)
                return new[] { (byte)((length >> 8) | 0x80), (byte)length };
            if (length < 0x200000)
                return new[] { (byte)((length >> 16) | 0xC0), (byte)(length >> 8), (byte)length };
            if (length < 0x10000000)
                return new[] { (byte)((length >> 24) | 0xE0), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
            return new[] { (byte)0xF0, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
        }

        /// <summary>
        /// The word length a prefix starting with <paramref name="first"/> announces; <paramref name="next"/>
        /// supplies the prefix's further bytes, and is called only as often as the first byte says.
        /// </summary>
        /// <exception cref="IOException">The first byte is one of the control bytes 0xF1–0xFF the protocol reserves.</exception>
        internal static long DecodeLength(byte first, Func<byte> next)
        {
            int length;

            if ((first & 0x80) == 0x00)
            {
                length = first;
            }
            else if ((first & 0xC0) == 0x80)
            {
                length = ((first & 0x3F) << 8) + next();
            }
            else if ((first & 0xE0) == 0xC0)
            {
                length = ((first & 0x1F) << 8) + next();
                length = (length << 8) + next();
            }
            else if ((first & 0xF0) == 0xE0)
            {
                length = ((first & 0x0F) << 8) + next();
                length = (length << 8) + next();
                length = (length << 8) + next();
            }
            else if (first == 0xF0)
            {
                // 5-byte encoding: 0xF0 + four bytes (network order)
                length =                  next();
                length = (length << 8) + next();
                length = (length << 8) + next();
                length = (length << 8) + next();
            }
            else
            {
                // Control bytes 0xF1–0xFF are reserved by the protocol
                throw new IOException($"Unexpected control byte 0x{first:X2} in word length.");
            }

            return length;
        }

        /// <summary>
        /// One sentence as it goes on the wire: each row's length prefix and bytes in <paramref name="encoding"/>,
        /// then the terminating zero-length word.
        /// </summary>
        internal static byte[] EncodeSentence(IEnumerable<string> rows, Encoding encoding)
        {
            using (var buffer = new MemoryStream())
            {
                foreach (string row in rows)
                {
                    byte[] bytes = encoding.GetBytes(row);
                    byte[] length = EncodeLength(bytes.Length);
                    buffer.Write(length, 0, length.Length);
                    buffer.Write(bytes, 0, bytes.Length);
                }

                buffer.WriteByte(0);
                return buffer.ToArray();
            }
        }
    }
}
