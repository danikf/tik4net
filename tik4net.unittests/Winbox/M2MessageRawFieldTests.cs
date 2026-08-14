using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Codec-level tests for <see cref="M2Message.ExtractRawField"/> (the verbatim cursor echo P2.9 needs)
    /// and for the field-skipping that every walker over an M2 message depends on.
    /// </summary>
    [TestClass]
    public class M2MessageRawFieldTests
    {
        private const int Key = 0xFE0015;

        /// <summary>Builds a message-array field (ftype 21) with the given size-flag form.</summary>
        private static byte[] MessageArray(int fullKey, byte typeByte, params byte[][] elements)
        {
            bool shortForm = (typeByte & 0x01) != 0;
            bool longForm = (typeByte & 0x02) != 0;
            Func<int, IEnumerable<byte>> len = n =>
                shortForm ? new[] { (byte)n }
                : longForm ? BitConverter.GetBytes((uint)n)
                : BitConverter.GetBytes((ushort)n);

            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF), (byte)((fullKey >> 16) & 0xFF), typeByte
            };
            b.AddRange(len(elements.Length));
            foreach (var e in elements)
            {
                b.AddRange(len(e.Length));
                b.AddRange(e);
            }
            return b.ToArray();
        }

        [TestMethod]
        public void ExtractRawField_ReturnsTheCompleteTlv_HeaderIncluded()
        {
            byte[] field = MessageArray(Key, 0xA8, M2Message.BuildM2(M2Message.U32User(0x11, 42)));
            byte[] m2 = M2Message.BuildM2(M2Message.SysFrom(), field, M2Message.U32User(0x12, 7));

            byte[] actual = M2Message.ExtractRawField(m2, Key);
            Assert.AreEqual(BitConverter.ToString(field), BitConverter.ToString(actual ?? new byte[0]),
                "The slice must be the field's own bytes — key, type and payload — so it can be re-sent as is.");
        }

        [TestMethod]
        public void ExtractRawField_ReturnsNull_WhenTheKeyIsAbsent()
        {
            byte[] m2 = M2Message.BuildM2(M2Message.SysFrom(), M2Message.U32User(0x12, 7));
            Assert.IsNull(M2Message.ExtractRawField(m2, Key));
        }

        /// <summary>
        /// A field whose declared length runs past the end of the frame yields nothing rather than a
        /// half-copied cursor: echoing a malformed token would push our own truncation onto the router.
        /// </summary>
        [TestMethod]
        public void ExtractRawField_ReturnsNull_WhenTheFieldIsTruncated()
        {
            byte[] field = MessageArray(Key, 0xA8, M2Message.BuildM2(M2Message.U32User(0x11, 42)));
            byte[] m2 = M2Message.BuildM2(M2Message.SysFrom(), field);

            Assert.IsNull(M2Message.ExtractRawField(m2.Take(m2.Length - 4).ToArray(), Key),
                "A field cut short by the frame boundary must not be echoed.");
        }

        /// <summary>
        /// The short-form (1-byte counts) message-array had no case in <c>SkipTypeBytes</c>, so a walker hit
        /// the <c>default: return 0</c> and then read the payload as the next key/type — the silent
        /// misalignment the codec's own comment warns about, and reachable now that ftype-21 fields are
        /// traversed on every paged reply. The assertion is on a field placed <b>after</b> it: what breaks is
        /// never the array itself, it is everything behind it.
        /// </summary>
        [DataTestMethod]
        [DataRow((byte)0xA9, DisplayName = "short form (1B counts)")]
        [DataRow((byte)0xA8, DisplayName = "normal form (2B counts)")]
        [DataRow((byte)0xAA, DisplayName = "long form (4B counts)")]
        public void SkipTypeBytes_WalksPastAMessageArray_InEverySizeForm(byte typeByte)
        {
            byte[] array = MessageArray(Key, typeByte,
                M2Message.BuildM2(M2Message.U32User(0x11, 42)),
                M2Message.BuildM2(M2Message.U32User(0x11, 43)));
            byte[] m2 = M2Message.BuildM2(M2Message.SysFrom(), array, M2Message.SessionIdField(265));

            Assert.IsTrue(M2Message.TryParseSessionId(m2, out int sessionId),
                "A field behind the message-array must still be found — otherwise the walk misaligned.");
            Assert.AreEqual(265, sessionId);
        }
    }
}
