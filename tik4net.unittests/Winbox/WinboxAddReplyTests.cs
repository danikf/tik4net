using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// The id an <c>add</c> reply carries is a u32. RouterOS 7 gives a route <c>*80000019</c>, above any <c>int</c>
    /// value; native holds ids in an <c>int</c> by their bits, as a read's <c>*HEX</c> parse does.
    /// </summary>
    [TestClass]
    public class WinboxAddReplyTests
    {
        [TestMethod]
        public void ARouteIdAboveTheIntRangeIsTheNewId_NotAnOverflow()
        {
            byte[] reply = M2Message.BuildM2(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, unchecked((int)0x80000019u)));

            int? id = WinboxNativeM2Operations.InterpretAdd(reply, new[] { 44, 21 });

            Assert.IsTrue(id.HasValue, "a reply with an id is a successful add");
            Assert.AreEqual(0x80000019u, unchecked((uint)id!.Value));
        }

        [TestMethod]
        public void AReplyWithoutAnIdHasNone()
            => Assert.IsNull(WinboxNativeM2Operations.InterpretAdd(M2Message.BuildM2(), new[] { 44, 21 }));
    }
}
