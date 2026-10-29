// M2TypeMappingTests.cs — M2Type against the two string vocabularies it replaces (WinBox + MAC review, D3 b).
//
// The record type names M2Message.ParseAllFields gives a value and the .jg wire-type names the catalog gives a field are
// mapped to M2Type both ways. The mapping is total when every type byte the walker steps over parses to a name that
// maps to that byte's M2Type, every .jg prefix letter's name maps to the letter's M2Type, and every M2Type has a type
// byte and a letter of its own.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class M2TypeMappingTests
    {
        private const int Key = 0x5;

        private static readonly M2Type[] AllTypes =
            System.Enum.GetValues(typeof(M2Type)).Cast<M2Type>().Where(t => t != M2Type.Unknown).ToArray();

        // 'M2' + one field (key 5, the type byte, zero value bytes): a zero count, a zero length or a zero number,
        // whatever the type reads.
        private static Dictionary<int, System.Tuple<string, object>> Parse(int typeByte)
        {
            var frame = new List<byte> { (byte)'M', (byte)'2', Key, 0, 0, (byte)typeByte };
            frame.AddRange(new byte[16]);
            return M2Message.ParseAllFields(frame.ToArray());
        }

        [TestMethod]
        public void EveryTypeByteTheWalkerKnows_ParsesToTheRecordNameOfItsM2Type()
        {
            var seen = new HashSet<M2Type>();
            for (int b = 0; b < 256; b++)
            {
                var fields = Parse(b);
                if (!fields.TryGetValue(Key, out var value)) continue;   // the walker stops at a type it cannot size
                M2Type? type = M2Types.FromTypeByte(b);
                Assert.IsNotNull(type, $"type byte 0x{b:X2} is walked but has no M2Type");
                string name = value.Item1;
                Assert.AreEqual(type.Value, M2Types.FromRecordName(name), $"0x{b:X2} '{name}'");
                Assert.AreEqual(name, M2Types.RecordName(type.Value, (b & M2Message.TypeByte.Short) != 0), $"0x{b:X2}");
                seen.Add(type.Value);
            }
            CollectionAssert.AreEquivalent(AllTypes, seen.ToArray(), "every M2Type has a type byte the walker knows");
        }

        [TestMethod]
        public void EveryJgPrefixLetter_NamesTheM2TypeOfItsWireTypeName()
        {
            var prefix = (Dictionary<char, string>)typeof(WinboxJgCatalog)
                .GetField("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var seen = new HashSet<M2Type>();
            foreach (var kv in prefix)
            {
                Assert.IsTrue(M2Types.TryFromJgPrefix(kv.Key, out var type), $"'{kv.Key}'");
                Assert.AreEqual(type, M2Types.FromJgName(kv.Value), $"'{kv.Key}' = {kv.Value}");
                Assert.AreEqual(kv.Value, M2Types.JgName(type), $"'{kv.Key}'");
                Assert.AreEqual(type.FType(), (int)type, $"'{kv.Key}': the ftype the unset list names it by");
                seen.Add(type);
            }
            CollectionAssert.AreEquivalent(AllTypes, seen.ToArray(), "every M2Type has a .jg letter");
            for (char c = 'A'; c <= 'z'; c++)
                if (char.IsLetter(c) && !prefix.ContainsKey(c))
                    Assert.IsFalse(M2Types.TryFromJgPrefix(c, out _), $"'{c}' is not one of id2int's letters");
        }

        [TestMethod]
        public void AnUnknownLetter_IsTheCatalogsQuestionMark_AndNamesItselfAsAU32()
        {
            Assert.AreEqual(M2Type.Unknown, M2Types.FromJgName("?"));
            Assert.AreEqual("?", M2Types.JgName(M2Type.Unknown));
            Assert.AreEqual(1, M2Type.Unknown.FType());
            Assert.IsFalse(M2Type.Unknown.IsArray());
            Assert.AreEqual(WinboxM2Protocol.TypedKey.KindOf("?"), WinboxM2Protocol.TypedKey.KindOf(M2Type.Unknown));
        }

        [TestMethod]
        public void TheKindOfAType_IsTheKindOfBothItsNames()
        {
            foreach (var type in AllTypes)
            {
                int kind = WinboxM2Protocol.TypedKey.KindOf(type);
                Assert.AreEqual(WinboxM2Protocol.TypedKey.KindOf(M2Types.JgName(type)), kind, type.ToString());
                Assert.AreEqual(WinboxM2Protocol.TypedKey.KindOf(M2Types.RecordName(type, false)), kind, type.ToString());
                Assert.AreEqual(WinboxM2Protocol.TypedKey.KindOf(M2Types.RecordName(type, true)), kind, type.ToString());
            }
        }
    }
}
