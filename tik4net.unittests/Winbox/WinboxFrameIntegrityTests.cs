using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Crypto;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Frame-level tests for the two WinBox failure modes that are silent by construction: a request framed
    /// so that no reader can parse it, and a frame that does not decrypt being reported as an orderly close.
    /// </summary>
    /// <remarks>
    /// Both are unreachable through the normal transports on a modern router — the raw path is the pre-6.43
    /// legacy handshake, and a wrong key means the session was never going to work — which is exactly why
    /// neither had a test: nothing that runs every day walks over them.
    /// </remarks>
    [TestClass]
    public class WinboxFrameIntegrityTests
    {
        /// <summary>
        /// Reads a chunked frame the way <c>WinboxTcpTransport.RecvChunked</c> and the router do: a chunk
        /// length of <c>0xFF</c> means 255 bytes of payload and another chunk behind it. Deliberately a
        /// second implementation rather than a call into the transport — a framing test that both writes and
        /// reads through the same code cannot fail.
        /// </summary>
        private static byte[] ReadChunked(byte[] frame, byte expectedFirstTag)
        {
            var body = new List<byte>();
            int pos = 0;
            bool first = true;
            while (true)
            {
                Assert.IsTrue(pos + 2 <= frame.Length, "The frame ended in the middle of a chunk header.");
                int len = frame[pos];
                byte tag = frame[pos + 1];
                pos += 2;

                if (first)
                {
                    Assert.AreEqual(expectedFirstTag, tag, "Wrong tag on the first chunk.");
                    first = false;
                }
                else
                {
                    Assert.AreEqual((byte)0xFF, tag, "A continuation chunk carries tag 0xFF.");
                }

                Assert.IsTrue(pos + len <= frame.Length, "A chunk claims more bytes than the frame holds.");
                body.AddRange(frame.Skip(pos).Take(len));
                pos += len;
                if (len < 0xFF) break;
            }

            Assert.AreEqual(frame.Length, pos, "The frame has bytes behind its final chunk.");
            return body.ToArray();
        }

        /// <summary>
        /// Every message length that matters, around both boundaries. 253 is where the body — the message
        /// plus its 2-byte inner length — reaches 255 and the frame needs a second chunk; the old builder
        /// switched form at 255 instead and wrote <c>n + 2</c> into a byte, so 253 announced itself as a
        /// continuation and 254 as an empty chunk. From 255 up it wrote the payload unchunked behind a
        /// single <c>0xFF</c>, which no reader on either end can parse (X-1).
        /// </summary>
        private static IEnumerable<int> InterestingLengths()
            => new[] { 0, 1, 250, 251, 252, 253, 254, 255, 256, 257, 507, 508, 509, 510, 511, 900 };

        [TestMethod]
        public void BuildRawFrame_ProducesAFrameItsOwnReaderCanParse_AtEveryLength()
        {
            foreach (int n in InterestingLengths())
            {
                byte[] m2 = Enumerable.Range(0, n).Select(i => (byte)(i % 251)).ToArray();

                byte[] body = ReadChunked(WinboxTcpTransport.BuildRawFrame(m2), 0x01);

                Assert.IsTrue(body.Length >= 2, $"A {n}-byte message must keep its 2-byte inner length.");
                Assert.AreEqual(n, (body[0] << 8) | body[1],
                    $"The inner length must state the {n}-byte message's real size.");
                CollectionAssert.AreEqual(m2, body.Skip(2).ToArray(),
                    $"A {n}-byte raw message must survive its own framing byte-for-byte.");
            }
        }

        /// <summary>
        /// The encrypted path's framing, for the same reason — it was always chunked, and the two paths now
        /// share the code that chunks them, so this is what pins the shared rule.
        /// </summary>
        [TestMethod]
        public void Chunk_RoundTrips_AtEveryLength()
        {
            foreach (int n in InterestingLengths())
            {
                byte[] data = Enumerable.Range(0, n).Select(i => (byte)(i % 251)).ToArray();

                CollectionAssert.AreEqual(data, ReadChunked(WinboxTcpTransport.Chunk(data, 0x06), 0x06),
                    $"A {n}-byte encrypted body must survive its own framing byte-for-byte.");
            }
        }

        /// <summary>
        /// A body whose length is an exact multiple of 255 ends with an explicit empty final chunk. A raw
        /// frame's reader waits for a short chunk, so without it that reader blocks on a chunk header the
        /// sender never intends to write. RouterOS itself omits the terminator after an encrypted frame and
        /// is read by its declared length instead (<c>WinboxChunkReassemblyTests</c>), but accepts one from us.
        /// </summary>
        [TestMethod]
        public void Chunk_EndsAnExactMultipleOf255_WithAnEmptyFinalChunk()
        {
            byte[] frame = WinboxTcpTransport.Chunk(new byte[255], 0x06);

            Assert.AreEqual(255 + 4, frame.Length, "255 bytes is one full chunk plus an empty terminator.");
            Assert.AreEqual((byte)0x00, frame[frame.Length - 2], "The final chunk must declare zero bytes.");
            Assert.AreEqual((byte)0xFF, frame[frame.Length - 1], "A continuation chunk carries tag 0xFF.");
        }

        /// <summary>
        /// A frame that does not decrypt must not answer <c>null</c>, because <c>null</c> is this channel's
        /// word for "the socket closed": the reader loop would end and fail every waiter with "the WinBox M2
        /// channel was closed", sending the reader of that report to look at the network (X-2).
        /// </summary>
        [TestMethod]
        public void DecryptOrThrow_ReportsAnUndecryptableFrame_AsAProtocolFailureNotAClose()
        {
            byte[] key = new byte[16], hmacKey = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
                rng.GetBytes(hmacKey);
            }

            // Truncated rather than encrypted under the wrong key: a wrong key leaves the ciphertext a whole
            // number of blocks, so whether Decrypt answers null depends on whether the garbage plaintext
            // happens to carry a plausible pad byte — about one frame in nine does, and a test that decides
            // the fix's fate on that is a coin toss. Losing a byte is one of the causes the fix names, and it
            // is refused every time.
            byte[] frame = WinboxStreamCrypto.Encrypt(M2Message.BuildM2(M2Message.SysFrom()), key, hmacKey);
            byte[] truncated = frame.Take(frame.Length - 1).ToArray();

            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => WinboxM2Session.DecryptOrThrow(truncated, key),
                "A frame the session cannot decrypt is a protocol failure and must be raised as one.");

            StringAssert.Contains(ex.Message, "could not be decrypted",
                "The message must name what actually happened — the whole defect was that it did not.");
        }

        /// <summary>A frame that decrypts is returned unchanged — the guard must not cost the happy path.</summary>
        [TestMethod]
        public void DecryptOrThrow_ReturnsThePlaintext_WhenTheKeyIsRight()
        {
            byte[] key = new byte[16], hmacKey = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
                rng.GetBytes(hmacKey);
            }

            byte[] plain = M2Message.BuildM2(M2Message.SysFrom(), M2Message.U32User(0x11, 42));

            CollectionAssert.AreEqual(plain,
                WinboxM2Session.DecryptOrThrow(WinboxStreamCrypto.Encrypt(plain, key, hmacKey), key));
        }
    }
}
