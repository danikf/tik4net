// WinboxNativeSafeModeErrorTests.cs — a router's refusal of the Safe Mode toggle (another session holding it, a user
// without the policy) reaches the caller as the TikCommandException ITikSafeModeConnection documents, as every other
// native entry point's M2 error does, never as the internal WinboxM2OperationException.
// Router-free: the real connection over a scripted channel.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using static tik4net.unittests.Winbox.NativeConnectionHarness;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    [DoNotParallelize]
    public class WinboxNativeSafeModeErrorTests
    {
        private const string Window =
            "[{name:'X',c:[{name:'Y',title:'Y',type:'map',path:[ 90,1 ],c:[{name:'Name',type:'string',id:'s10006'}]}]}]";

        private static byte[] Refusal(byte[] request)
            => ReplyTo(request, M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, 0xFE0006),
                M2Message.StringSys(WinboxM2Protocol.SysKey.ErrorString, "safe mode already taken"));

        private static void AssertTranslated(Exception ex)
        {
            Assert.IsInstanceOfType(ex, typeof(TikCommandException));
            Assert.IsNotInstanceOfType(ex, typeof(WinboxM2OperationException));
            StringAssert.Contains(ex.Message, "safe mode already taken");
        }

        [TestMethod]
        public void ARefusedTake_IsATikCommandException()
        {
            using (var h = new NativeConnectionHarness(Window))
            {
                h.Answer = Refusal;
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection.SafeModeTake());
                AssertTranslated(ex);
                Assert.IsFalse(h.Connection.SafeModeGet(), "a refused take does not hold safe mode");
            }
        }

        [TestMethod]
        public void ARefusedRelease_IsATikCommandException()
        {
            using (var h = new NativeConnectionHarness(Window))
            {
                h.Connection.SafeModeTake();
                h.Answer = Refusal;
                var ex = Assert.ThrowsException<TikCommandTrapException>(() => h.Connection.SafeModeRelease());
                AssertTranslated(ex);
                Assert.IsTrue(h.Connection.SafeModeGet(), "a refused release still holds safe mode");
            }
        }
    }
}
