using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <see cref="ApiReceiveBuffer"/> on its own, over streams that return as little as a stream may: every byte the
    /// stream holds comes out in order whatever the receive sizes, the fill callback sees each receive's count, and the
    /// end of the stream is reported, not waited on.
    /// </summary>
    [TestClass]
    public class ApiReceiveBufferTests
    {
        /// <summary>A stream that hands out at most <c>chunk</c> bytes per Read, and counts the Reads.</summary>
        private sealed class ChunkedStream : MemoryStream
        {
            private readonly int _chunk;
            internal int Reads;
            internal ChunkedStream(byte[] data, int chunk) : base(data) { _chunk = chunk; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                Reads++;
                return base.Read(buffer, offset, Math.Min(count, _chunk));
            }
        }

        private static byte[] Data(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 3)).ToArray();

        [DataTestMethod]
        [DataRow(1, 16)]
        [DataRow(3, 16)]
        [DataRow(16, 16)]
        [DataRow(1000, 16)]
        [DataRow(1, 1)]
        public void BytesComeOutInOrderAndEveryReceiveIsCounted(int chunk, int bufferSize)
        {
            byte[] data = Data(300);
            var stream = new ChunkedStream(data, chunk);
            var fills = new List<int>();
            var buffer = new ApiReceiveBuffer(stream, fills.Add, bufferSize);

            // Mixed the way the framing reads: a byte, then a body, of growing sizes (some larger than the buffer).
            var output = new List<byte>();
            int bodyLength = 0;
            while (output.Count < data.Length)
            {
                output.Add((byte)buffer.ReadByte());
                bodyLength = Math.Min(bodyLength + 5, data.Length - output.Count);
                var body = new byte[bodyLength];
                int got = 0;
                while (got < bodyLength)
                {
                    int n = buffer.Read(body, got, bodyLength - got);
                    Assert.IsTrue(n > 0, "the stream has not ended");
                    got += n;
                }
                output.AddRange(body);
            }

            CollectionAssert.AreEqual(data, output.ToArray());
            Assert.AreEqual(data.Length, fills.Sum(), "every byte received is counted once, when it is received");
            Assert.IsTrue(fills.All(n => n > 0 && n <= Math.Max(chunk, bufferSize)));
            Assert.AreEqual(-1, buffer.ReadByte(), "the end of the stream");
            Assert.AreEqual(0, buffer.Read(new byte[4], 0, 4), "the end of the stream");
        }

        [TestMethod]
        public void AReceiveFillsTheBufferRatherThanOneByte()
        {
            var stream = new ChunkedStream(Data(1000), 1000);
            var buffer = new ApiReceiveBuffer(stream, _ => { }, 4096);
            for (int i = 0; i < 1000; i++)
                buffer.ReadByte();
            Assert.AreEqual(1, stream.Reads, "1000 ReadByte calls from one receive");
        }

        [TestMethod]
        public void TheEndOfTheStreamInsideWhatWasBufferedIsReportedOnceTheBufferIsSpent()
        {
            var buffer = new ApiReceiveBuffer(new ChunkedStream(new byte[] { 1, 2 }, 2), _ => { }, 16);
            Assert.AreEqual(1, buffer.ReadByte());
            Assert.AreEqual(2, buffer.ReadByte());
            Assert.AreEqual(-1, buffer.ReadByte());
        }
    }
}
