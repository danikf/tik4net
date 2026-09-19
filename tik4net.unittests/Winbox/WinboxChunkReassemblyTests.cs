using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Crypto;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The receive side of WinBox chunk framing, as RouterOS actually writes it: an encrypted frame whose size
    /// is an exact multiple of 255 ends on a full chunk, with no short final chunk behind it. Measured on
    /// 7.24 over both carriers — the only reachable size is 3570 bytes (14 chunks), and a terminal answer of
    /// ~3.4 KB lands on it.
    /// </summary>
    /// <remarks>
    /// Waiting for the short chunk there merges the frame with the next one: over TCP the merged blob fails to
    /// decrypt and the session dies; over the MAC layer both frames are dropped, the terminal's byte
    /// acknowledgement stops moving and the router stops sending — a whole-table read hung in its last window.
    /// The router-side framing is built here by hand rather than with <c>WinboxTcpTransport.Chunk</c>, which
    /// adds the terminator the router leaves out.
    /// </remarks>
    [TestClass]
    public class WinboxChunkReassemblyTests
    {
        private static readonly byte[] AesKey = RandomBytes(16), HmacKey = RandomBytes(20);

        private static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        /// <summary>An encrypted frame of exactly <paramref name="frameLength"/> bytes (18 + a multiple of 16).</summary>
        private static byte[] EncryptedFrame(int frameLength, byte fill)
        {
            // The ciphertext is msg + 20-byte HMAC + 1..16 pad bytes, so a message 21 short of the ciphertext
            // fills it exactly.
            int ciphertext = frameLength - WinboxTcpTransport.EncryptedFrameOverhead;
            Assert.AreEqual(0, ciphertext % 16, "an encrypted frame is 18 bytes plus whole AES blocks");
            byte[] frame = WinboxStreamCrypto.Encrypt(Enumerable.Repeat(fill, ciphertext - 21).ToArray(), AesKey, HmacKey);
            Assert.AreEqual(frameLength, frame.Length);
            return frame;
        }

        /// <summary>Chunks a frame the way RouterOS does: 255-byte chunks, and no empty chunk after a full one.</summary>
        private static byte[] RouterChunked(byte[] frame)
        {
            var wire = new List<byte>();
            for (int pos = 0; pos < frame.Length; pos += 0xFF)
            {
                int take = Math.Min(0xFF, frame.Length - pos);
                wire.Add((byte)take);
                wire.Add(pos == 0 ? WinboxTcpTransport.EncryptedFrameTag : WinboxTcpTransport.ContinuationTag);
                wire.AddRange(frame.Skip(pos).Take(take));
            }
            return wire.ToArray();
        }

        private static Func<int, byte[]> Reader(byte[] wire)
        {
            var stream = new MemoryStream(wire);
            return count =>
            {
                var buf = new byte[count];
                int got = stream.Read(buf, 0, count);
                if (got < count) throw new EndOfStreamException("the reader asked for bytes the router never sent");
                return buf;
            };
        }

        /// <summary>Every encrypted frame size up to 16 KB that ends on a full chunk.</summary>
        private static IEnumerable<int> FullChunkSizes()
            => Enumerable.Range(1, 64).Select(i => i * 0xFF).Where(n => (n - 18) % 16 == 0);

        [TestMethod]
        public void TheSizesThatEndOnAFullChunkStartAt3570()
            => Assert.AreEqual(3570, FullChunkSizes().First());

        [TestMethod]
        public void AFrameEndingOnAFullChunkIsReadFromTheStreamWithoutSwallowingTheNext()
        {
            foreach (int size in FullChunkSizes())
            {
                byte[] big = EncryptedFrame(size, 0x41), next = EncryptedFrame(162, 0x42);
                var read = Reader(RouterChunked(big).Concat(RouterChunked(next)).ToArray());

                CollectionAssert.AreEqual(big, WinboxTcpTransport.ReadChunkedFrame(read, 0x06, encrypted: true), "size " + size);
                CollectionAssert.AreEqual(next, WinboxTcpTransport.ReadChunkedFrame(read, 0x06, encrypted: true), "after size " + size);
            }
        }

        [TestMethod]
        public void AFrameEndingOnAFullChunkIsTakenFromTheDatagramBufferWithoutWaitingForMore()
        {
            foreach (int size in FullChunkSizes())
            {
                byte[] big = EncryptedFrame(size, 0x41), next = EncryptedFrame(162, 0x42);
                var buffer = new List<byte>();

                // Datagram by datagram, as the MAC layer delivers it: the frame is complete the moment its
                // last byte arrives, not when the next frame's first datagram does.
                byte[] wire = RouterChunked(big);
                for (int pos = 0; pos < wire.Length; pos += 1450)
                {
                    Assert.IsNull(WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: true), "size " + size + " taken early");
                    buffer.AddRange(wire.Skip(pos).Take(1450));
                }
                CollectionAssert.AreEqual(big, WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: true), "size " + size);
                Assert.AreEqual(0, buffer.Count);

                buffer.AddRange(RouterChunked(next));
                CollectionAssert.AreEqual(next, WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: true), "after size " + size);
            }
        }

        [TestMethod]
        public void AnEmptyTerminatorAfterAFullChunkIsSkipped()
        {
            // What we send ourselves (WinboxTcpTransport.Chunk), and what a router that did write it would send.
            byte[] big = EncryptedFrame(3570, 0x41), next = EncryptedFrame(162, 0x42);
            byte[] wire = WinboxTcpTransport.Chunk(big, 0x06).Concat(RouterChunked(next)).ToArray();

            var read = Reader(wire);
            CollectionAssert.AreEqual(big, WinboxTcpTransport.ReadChunkedFrame(read, 0x06, encrypted: true));
            CollectionAssert.AreEqual(next, WinboxTcpTransport.ReadChunkedFrame(read, 0x06, encrypted: true));

            var buffer = new List<byte>(wire);
            CollectionAssert.AreEqual(big, WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: true));
            CollectionAssert.AreEqual(next, WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: true));
            Assert.AreEqual(0, buffer.Count);
        }

        [TestMethod]
        public void AFrameThatDeclaresNoLengthStillEndsOnlyAtAShortChunk()
        {
            // A handshake frame is tagged 0x06 but not encrypted, so its first two bytes are data, not a
            // length. These two happen to "declare" 255 bytes — the frame must not stop there.
            byte[] payload = new byte[300];
            payload[0] = 0x00; payload[1] = 0xFF - 18;
            byte[] wire = WinboxTcpTransport.Chunk(payload, 0x06);

            var buffer = new List<byte>(wire.Take(257));
            Assert.IsNull(WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: false));
            buffer.AddRange(wire.Skip(257));
            CollectionAssert.AreEqual(payload, WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: false));

            CollectionAssert.AreEqual(payload, WinboxTcpTransport.ReadChunkedFrame(Reader(wire), 0x06, encrypted: false));
        }
    }
}
