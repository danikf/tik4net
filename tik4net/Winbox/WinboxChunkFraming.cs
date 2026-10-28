using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Winbox
{
    /// <summary>
    /// The WinBox chunk framing both M2 carriers share: <c>[len 1B][tag 1B][data len-bytes]</c>, where
    /// <c>len=0xFF</c> marks a continuation chunk (full 255 bytes) and a shorter length marks the final chunk —
    /// except that an encrypted frame also ends at the length it declares, see <see cref="ReadChunkedFrame"/>.
    /// </summary>
    /// <remarks>
    /// Over TCP (<see cref="WinboxTcpTransport"/>) the chunks are the byte stream; over the MAC layer
    /// (<see cref="WinboxMacM2Session"/>) they ride inside <c>PKT_DATA</c> payloads and are reassembled from a
    /// datagram buffer (<see cref="TryTakeChunkedFrame"/>). Pure functions over bytes; no I/O.
    /// </remarks>
    internal static class WinboxChunkFraming
    {
        /// <summary>First tag of an encrypted frame (and of an EC-SRP5 handshake frame).</summary>
        internal const byte EncryptedTag = 0x06;

        /// <summary>First tag of an unencrypted (legacy MD5) frame — see <see cref="BuildRawFrame"/>.</summary>
        internal const byte RawTag = 0x01;

        /// <summary>Tag of every chunk after a frame's first.</summary>
        internal const byte ContinuationTag = 0xFF;

        /// <summary>
        /// Bytes of an encrypted frame beyond the ciphertext length it declares: the 2-byte length itself and
        /// the 16-byte IV (<c>[enc_len 2B BE][IV 16B][ciphertext]</c>, see <c>WinboxStreamCrypto</c>).
        /// </summary>
        internal const int EncryptedFrameOverhead = 18;

        /// <summary>
        /// Reads one chunked frame through <paramref name="readExact"/> — the one reassembly rule both carriers
        /// share (the MAC layer applies it to its datagram buffer via <see cref="TryTakeChunkedFrame"/>).
        /// </summary>
        /// <remarks>
        /// A chunk shorter than <c>0xFF</c> ends a frame. So does reaching the length an encrypted frame declares
        /// in its first two bytes, and that second rule is not optional: RouterOS sends <b>no</b> zero-length
        /// final chunk after an encrypted frame whose size is an exact multiple of 255 (the one reachable size
        /// is 3570 bytes, 14 full chunks, and a terminal pull of ~3.4 KB lands on it). Waiting for a short chunk
        /// there swallows the next frame into this one, both fail to decrypt, and the session stalls or dies.
        /// The router does accept such a terminator from us, and <see cref="Chunk"/> still sends one; a
        /// zero-length continuation chunk where a frame should start is that terminator, and is skipped.
        /// <para>
        /// A handshake frame (tag 0x06 but not yet encrypted) declares no length, so
        /// <paramref name="encrypted"/> is the caller's to say. The raw (0x01) frame's inner length is not used
        /// either: that path serves only pre-6.43 routers and has never been observed at this boundary.
        /// </para>
        /// </remarks>
        internal static byte[] ReadChunkedFrame(Func<int, byte[]> readExact, byte expectedFirstTag, bool encrypted)
        {
            var assembled = new List<byte>();
            bool first = true;
            while (true)
            {
                byte[] hdr = readExact(2);
                int chunkLen = hdr[0];
                byte tag = hdr[1];
                if (first)
                {
                    if (chunkLen == 0 && tag == ContinuationTag) continue;   // the previous frame's terminator
                    if (tag != expectedFirstTag)
                        throw new InvalidOperationException(
                            $"Expected frame tag 0x{expectedFirstTag:x2}, got 0x{tag:x2}");
                    first = false;
                }
                assembled.AddRange(readExact(chunkLen));
                if (chunkLen < 0xFF || (encrypted && DeclaredLengthReached(assembled))) break;
            }
            return assembled.ToArray();
        }

        /// <summary>
        /// Takes one complete chunked frame off the front of <paramref name="buffer"/> by the same rule as
        /// <see cref="ReadChunkedFrame"/>, or returns <c>null</c> and leaves the buffer untouched when it does
        /// not hold a whole frame yet. The first tag is not checked: the MAC layer never has.
        /// </summary>
        internal static byte[]? TryTakeChunkedFrame(List<byte> buffer, bool encrypted)
        {
            int pos = 0;
            // A zero-length continuation chunk where a frame starts is the previous frame's terminator.
            while (buffer.Count - pos >= 2 && buffer[pos] == 0 && buffer[pos + 1] == ContinuationTag) pos += 2;
            int start = pos;

            var frame = new List<byte>();
            while (true)
            {
                if (buffer.Count - pos < 2) break;                  // need a chunk header
                int chunkLen = buffer[pos];
                if (buffer.Count - pos - 2 < chunkLen) break;       // incomplete chunk
                for (int i = 0; i < chunkLen; i++) frame.Add(buffer[pos + 2 + i]);
                pos += 2 + chunkLen;
                if (chunkLen < 0xFF || (encrypted && DeclaredLengthReached(frame)))
                {
                    buffer.RemoveRange(0, pos);
                    return frame.ToArray();
                }
            }
            if (start > 0) buffer.RemoveRange(0, start);            // the terminator is consumed either way
            return null;
        }

        private static bool DeclaredLengthReached(List<byte> assembled)
            => assembled.Count >= 2
               && assembled.Count == EncryptedFrameOverhead + ((assembled[0] << 8) | assembled[1]);

        /// <summary>
        /// Splits <paramref name="data"/> into WinBox chunks — <c>[len 1B][tag 1B][data]</c>, the first
        /// carrying <paramref name="firstTag"/> and every continuation <c>0xFF</c>.
        /// </summary>
        /// <remarks>
        /// The one place the chunk rule is written down: both the encrypted and the raw send path go through
        /// it, because a length of exactly <c>0xFF</c> means "255 bytes and more to come" to
        /// <see cref="WinboxTcpTransport.RecvChunked"/> and to the router. A payload of 255 bytes or more written unchunked is
        /// unparseable by either — which is what the raw path did until this was factored out. A body whose
        /// length is an exact multiple of 255 therefore ends with an explicit zero-length final chunk.
        /// </remarks>
        internal static byte[] Chunk(byte[] data, byte firstTag)
        {
            int chunks = data.Length / 0xFF + 1;
            byte[] frame = new byte[data.Length + 2 * chunks];

            byte tag = firstTag;
            int pos = 0, outPos = 0;
            while (true)
            {
                int rem = data.Length - pos;
                int take = rem >= 0xFF ? 0xFF : rem;
                frame[outPos++] = (byte)take;
                frame[outPos++] = tag;
                Buffer.BlockCopy(data, pos, frame, outPos, take);
                outPos += take;
                pos += take;
                if (take < 0xFF) break;
                tag = 0xFF;
            }
            return frame;
        }

        /// <summary>
        /// Builds one unencrypted frame: a 2-byte big-endian message length followed by the message,
        /// carried by the same chunk layer as the encrypted path with <c>0x01</c> as the first tag.
        /// </summary>
        /// <remarks>
        /// The inner length is redundant with the chunk lengths and the receive side ignores it
        /// (<c>WinboxM2Session</c> skips the two bytes), but the router sends it and expects it.
        /// </remarks>
        internal static byte[] BuildRawFrame(byte[] m2)
        {
            int n = m2.Length;
            if (n > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(m2),
                    $"A raw WinBox frame carries a 2-byte length, so it cannot hold {n} bytes.");

            byte[] body = new byte[2 + n];
            body[0] = (byte)(n >> 8);
            body[1] = (byte)n;
            Buffer.BlockCopy(m2, 0, body, 2, n);
            return Chunk(body, RawTag);
        }

        /// <summary>
        /// The message inside an assembled raw (<see cref="RawTag"/>) frame: what follows its 2-byte inner length, which the
        /// receive side does not use (see <see cref="BuildRawFrame"/>). A frame too short to carry one comes back as it is.
        /// </summary>
        internal static byte[] StripRawLength(byte[] assembled)
            => assembled.Length >= 2 ? assembled.Skip(2).ToArray() : assembled;
    }
}
