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
        [DataRow(int.MaxValue)]
        public void DecodeLengthReadsWhatEncodeLengthWrote_AndNoFurther(int length)
        {
            byte[] prefix = ApiWordCodec.EncodeLength(length);
            int read = 1;

            long decoded = ApiWordCodec.DecodeLength(prefix[0], () => prefix[read++]);

            Assert.AreEqual(length, decoded);
            Assert.AreEqual(prefix.Length, read, "bytes of the prefix consumed");
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
