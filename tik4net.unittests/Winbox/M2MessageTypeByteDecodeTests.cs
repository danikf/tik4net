// M2MessageTypeByteDecodeTests.cs — every type byte the walker steps over, ParseAllFields reads (WinBox + MAC review,
// §5 X5).
//
// The decoder switched on exact type bytes while the walker (M2Message.Walk) steps over a field by its ftype and size
// flags. Where they disagreed the field was skipped in silence — the frame stayed in sync, so nothing looked wrong — and
// a normal-length raw read as the text "[300B]". One hand-built frame per row of the review's table, each with a
// trailing sentinel field that must still be read.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class M2MessageTypeByteDecodeTests
    {
        private const int Key = 0x5;
        private const int SentinelKey = 0xFF0006;

        // 'M2' + one field (key 5, the type byte, the value bytes) + a u8 sentinel 0xFF0006 = 42.
        private static M2Record Parse(byte type, params byte[] value)
        {
            var frame = new List<byte> { (byte)'M', (byte)'2', Key, 0, 0, type };
            frame.AddRange(value);
            frame.AddRange(new byte[] { 0x06, 0x00, 0xFF, 0x09, 42 });
            var fields = M2Message.ParseAllFields(frame.ToArray());
            Assert.AreEqual((byte)42, fields[SentinelKey].Value, "the field after must still be read");
            return fields;
        }

        private static byte[] Le16(int v) => new[] { (byte)v, (byte)(v >> 8) };
        private static byte[] Le32(int v) => BitConverter.GetBytes(v);
        private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
        private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

        private static void AssertField(M2Record fields, string type, string value)
        {
            Assert.IsTrue(fields.ContainsKey(Key), "the field was skipped");
            Assert.AreEqual(type, fields[Key].TraceName);
            Assert.AreEqual(value, fields[Key].Value?.ToString());
        }

        [TestMethod]
        public void Raw_NormalLength_IsItsHex_NotItsSize()
        {
            byte[] data = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
            AssertField(Parse(0x30, Cat(Le16(300), data)), "raw_l", BitConverter.ToString(data).Replace("-", ""));
        }

        [TestMethod]
        public void String_LongLength_IsRead()
            => AssertField(Parse(0x22, Cat(Le32(3), Ascii("abc"))), "str_l", "abc");

        [TestMethod]
        public void Raw_LongLength_IsRead()
            => AssertField(Parse(0x32, Cat(Le32(2), new byte[] { 0xAB, 0xCD })), "raw_l", "ABCD");

        [TestMethod]
        public void BoolArray_EverySizeClass_IsRead()
        {
            AssertField(Parse(0x80, Cat(Le16(2), new byte[] { 1, 0 })), "bool[]", "[true,false]");
            AssertField(Parse(0x81, Cat(new byte[] { 2 }, new byte[] { 0, 1 })), "bool[]", "[false,true]");
            AssertField(Parse(0x82, Cat(Le32(1), new byte[] { 1 })), "bool[]", "[true]");
        }

        [TestMethod]
        public void U32Array_ShortAndLong_AreRead()
        {
            AssertField(Parse(0x89, Cat(new byte[] { 2 }, Le32(7), Le32(8))), "u32[]", "[7,8]");
            AssertField(Parse(0x8A, Cat(Le32(1), Le32(9))), "u32[]", "[9]");
        }

        [TestMethod]
        public void StringArray_ShortAndLong_AreRead()
        {
            AssertField(Parse(0xA1, Cat(new byte[] { 2, 3 }, Ascii("one"), new byte[] { 3 }, Ascii("two"))), "str[]", "[one,two]");
            AssertField(Parse(0xA2, Cat(Le32(1), Le32(2), Ascii("ab"))), "str[]", "[ab]");
        }

        [TestMethod]
        public void Addr6Array_ShortAndLong_AreRead()
        {
            byte[] a = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            string hex = BitConverter.ToString(a).Replace("-", "");
            AssertField(Parse(0x99, Cat(new byte[] { 1 }, a)), "ip6[]", "[" + hex + "]");
            AssertField(Parse(0x9A, Cat(Le32(1), a)), "ip6[]", "[" + hex + "]");
        }
    }
}
