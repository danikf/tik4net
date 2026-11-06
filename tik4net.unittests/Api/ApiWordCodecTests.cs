using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// H7 of the shared-helpers review: the binary API's word-length prefix, the one wire rule both the writer and
    /// the reader follow (<c>Docs/</c> and the MikroTik API specification: 1 to 4 bytes with the high bits as the
    /// size, 0xF0 plus four bytes past that).
    /// </summary>
    [TestClass]
    public class ApiWordCodecTests
    {
        [DataTestMethod]
        [DataRow(0, "00")]
        [DataRow(0x7F, "7F")]
        [DataRow(0x80, "80-80")]
        [DataRow(0x3FFF, "BF-FF")]
        [DataRow(0x4000, "C0-40-00")]
        [DataRow(0x1FFFFF, "DF-FF-FF")]
        [DataRow(0x200000, "E0-20-00-00")]
        [DataRow(0xFFFFFFF, "EF-FF-FF-FF")]
        [DataRow(0x10000000, "F0-10-00-00-00")]
        [DataRow(int.MaxValue, "F0-7F-FF-FF-FF")]
        public void EncodeLength(int length, string expected)
        {
            Assert.AreEqual(expected, BitConverter.ToString(ApiWordCodec.EncodeLength(length)));
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(0x7F)]
        [DataRow(0x80)]
        [DataRow(0x3FFF)]
        [DataRow(0x4000)]
        [DataRow(0x1FFFFF)]
        [DataRow(0x200000)]
        [DataRow(0xFFFFFFF)]
        [DataRow(0x10000000)]
        [DataRow(ApiWordCodec.MaxWordLength)]
        public void DecodeLengthReadsWhatEncodeLengthWrote_AndNoFurther(int length)
        {
            byte[] prefix = ApiWordCodec.EncodeLength(length);
            int read = 1;

            long decoded = ApiWordCodec.DecodeLength(prefix[0], () => prefix[read++]);

            Assert.AreEqual(length, decoded);
            Assert.AreEqual(prefix.Length, read, "bytes of the prefix consumed");
        }

        // X16: the five-byte prefix carries 32 bits. Past 2^31 an int read them as negative, and the reader died in
        // ArrayPool.Rent with an ArgumentOutOfRangeException; past what a byte[] can hold no word can be read at all.
        [DataTestMethod]
        [DataRow("F0-7F-FF-FF-C8", "2147483592")]
        [DataRow("F0-7F-FF-FF-FF", "2147483647")]
        [DataRow("F0-80-00-00-00", "2147483648")]
        [DataRow("F0-FF-FF-FF-FF", "4294967295")]
        public void DecodeLengthRefusesALengthNoByteArrayCanHold_NamingIt(string prefixHex, string expectedLength)
        {
            byte[] prefix = Array.ConvertAll(prefixHex.Split('-'), h => Convert.ToByte(h, 16));
            int read = 1;

            var ex = Assert.ThrowsException<IOException>(() => ApiWordCodec.DecodeLength(prefix[0], () => prefix[read++]));

            StringAssert.Contains(ex.Message, expectedLength);
            Assert.AreEqual(prefix.Length, read, "the whole prefix is consumed before the refusal");
        }

        [TestMethod]
        public void DecodeLengthRefusesAReservedControlByte()
        {
            for (int b = 0xF1; b <= 0xFF; b++)
                Assert.ThrowsException<IOException>(() => ApiWordCodec.DecodeLength((byte)b, () => 0));
        }

        [TestMethod]
        public void EncodeSentenceIsEveryWordWithItsPrefix_ThenTheEmptyWord()
        {
            string longWord = "=v=" + new string('x', 0x80 - 3);
            byte[] sentence = ApiWordCodec.EncodeSentence(new[] { "/ip/address/print", longWord, "=name=č" }, Encoding.UTF8);

            var expected = new List<byte>();
            foreach (string word in new[] { "/ip/address/print", longWord, "=name=č" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(word);
                expected.AddRange(ApiWordCodec.EncodeLength(bytes.Length));
                expected.AddRange(bytes);
            }
            expected.Add(0);

            CollectionAssert.AreEqual(expected, sentence);
            Assert.AreEqual(0x80, sentence[18], "the 128-byte word takes the two-byte prefix");
        }

        [TestMethod]
        public void EncodeSentenceOfNoRowsIsTheEmptyWord()
        {
            CollectionAssert.AreEqual(new byte[] { 0 }, ApiWordCodec.EncodeSentence(new string[0], Encoding.UTF8));
        }
    }
}
