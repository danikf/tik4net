// M2TypeMappingTests.cs — the two parses of an M2Type are total (WinBox + MAC review, D3 b).
//
// A record value is typed from its TLV type byte and a catalog field from its .jg prefix letter. Both parses are total
// when every type byte the walker steps over parses to the M2Type of its ftype, every one of id2int's fourteen letters
// names a distinct M2Type, no other letter names one, and every M2Type is reached both ways.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class M2TypeMappingTests
    {
        private const int Key = 0x5;
        private const string Id2IntLetters = "buqasmrBUQASMR";

        private static readonly M2Type[] AllTypes =
            System.Enum.GetValues(typeof(M2Type)).Cast<M2Type>().Where(t => t != M2Type.Unknown).ToArray();

        // 'M2' + one field (key 5, the type byte, zero value bytes): a zero count, a zero length or a zero number,
        // whatever the type reads.
        private static M2Record Parse(int typeByte)
        {
            var frame = new List<byte> { (byte)'M', (byte)'2', Key, 0, 0, (byte)typeByte };
            frame.AddRange(new byte[16]);
            return M2Message.ParseAllFields(frame.ToArray());
        }

        [TestMethod]
        public void EveryTypeByteTheWalkerKnows_ParsesToTheM2TypeOfItsFType()
        {
            var seen = new HashSet<M2Type>();
            for (int b = 0; b < 256; b++)
            {
                var fields = Parse(b);
                if (!fields.TryGetValue(Key, out var value)) continue;   // the walker stops at a type it cannot size
                M2Type? type = M2Types.FromTypeByte(b);
                Assert.IsNotNull(type, $"type byte 0x{b:X2} is walked but has no M2Type");
                Assert.AreEqual(type.Value, value.Type, $"0x{b:X2}");
                Assert.AreEqual(b >> 3, (int)value.Type, $"0x{b:X2}: the type is the ftype");
                Assert.AreEqual((b & M2Message.TypeByte.Short) != 0, value.ShortForm, $"0x{b:X2}");
                seen.Add(type.Value);
            }
            CollectionAssert.AreEquivalent(AllTypes, seen.ToArray(), "every M2Type has a type byte the walker knows");
        }

        [TestMethod]
        public void EveryId2IntLetter_NamesADistinctM2Type_AndNoOtherLetterNamesOne()
        {
            var seen = new HashSet<M2Type>();
            foreach (char c in Id2IntLetters)
            {
                Assert.IsTrue(M2Types.TryFromJgPrefix(c, out var type), $"'{c}'");
                Assert.AreEqual(char.IsUpper(c), type.IsArray(), $"'{c}': upper case is the array form");
                Assert.AreEqual(type.FType(), (int)type, $"'{c}': the ftype the unset list names it by");
                Assert.IsTrue(seen.Add(type), $"'{c}' names a type another letter names");
            }
            CollectionAssert.AreEquivalent(AllTypes, seen.ToArray(), "every M2Type has a .jg letter");
            for (char c = 'A'; c <= 'z'; c++)
                if (char.IsLetter(c) && Id2IntLetters.IndexOf(c) < 0)
                {
                    Assert.IsFalse(M2Types.TryFromJgPrefix(c, out var unknown), $"'{c}' is not one of id2int's letters");
                    Assert.AreEqual(M2Type.Unknown, unknown, $"'{c}'");
                }
        }

        [TestMethod]
        public void AnUnknownLetter_NamesItselfAsAU32Scalar()
        {
            Assert.AreEqual(1, M2Type.Unknown.FType());
            Assert.IsFalse(M2Type.Unknown.IsArray());
            Assert.AreEqual(WinboxM2Protocol.TypedKey.Scalar, WinboxM2Protocol.TypedKey.KindOf(M2Type.Unknown));
            Assert.AreEqual("?", M2Types.JgName(M2Type.Unknown));
        }

        [TestMethod]
        public void TheKindOfAType_IsArrayBoolOrScalar()
        {
            foreach (var type in AllTypes)
            {
                int expected = type.IsArray() ? WinboxM2Protocol.TypedKey.Array
                    : type == M2Type.Bool ? WinboxM2Protocol.TypedKey.Bool : WinboxM2Protocol.TypedKey.Scalar;
                Assert.AreEqual(expected, WinboxM2Protocol.TypedKey.KindOf(type), type.ToString());
            }
        }
    }
}
