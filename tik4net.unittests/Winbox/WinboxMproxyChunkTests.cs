#nullable enable
// WinboxMproxyChunkTests.cs — the catalog's mproxy read takes the file chunk from the reply by the one TLV walk
// (WinBox + MAC review, §11 F6).
//
// The reader walked the TLVs by hand: for a user-namespace string/raw field it read the length (advancing past it),
// and when the field was empty it then skipped the field FROM THAT ADVANCED POSITION — so the skip read the next
// field's first byte as a length and the walk lost step, missing the chunk behind it.

using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxMproxyChunkTests
    {
        private static byte[]? Extract(byte[] reply)
            => WinboxMproxyClient.ExtractChunk(reply);

        private static readonly byte[] Chunk = Enumerable.Range(0, 40).Select(i => (byte)(i + 1)).ToArray();

        [TestMethod]
        public void AnEmptyRawFieldBeforeTheChunk_IsSteppedOver()
        {
            byte[] reply = M2Message.BuildM2(M2Message.SysToArr(), M2Message.RawUser(0x3, new byte[0]),
                M2Message.RawUser(0x2, Chunk), M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7));
            CollectionAssert.AreEqual(Chunk, Extract(reply));
        }

        [TestMethod]
        public void AnEmptyStringFieldBeforeTheChunk_IsSteppedOver()
        {
            byte[] reply = M2Message.BuildM2(M2Message.StringUser(0x3, ""), M2Message.RawUser(0x2, Chunk));
            CollectionAssert.AreEqual(Chunk, Extract(reply));
        }

        [TestMethod]
        public void TheChunkAfterSystemFields_IsFound_AndAFullLengthOneToo()
        {
            byte[] big = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();   // a normal-length (2-byte) raw
            byte[] reply = M2Message.BuildM2(M2Message.SysToArr(), M2Message.SysFrom(), M2Message.RawUser(0x2, big));
            CollectionAssert.AreEqual(big, Extract(reply));
        }

        [TestMethod]
        public void AReplyWithNoChunk_IsNull()
        {
            Assert.IsNull(Extract(M2Message.BuildM2(M2Message.SysToArr(), M2Message.RawUser(0x2, new byte[0]))));
        }
    }
}
