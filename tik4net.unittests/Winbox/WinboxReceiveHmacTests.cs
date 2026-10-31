using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Crypto;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// A received frame's HMAC is checked with the session's receive key (review X11, D5).
    /// </summary>
    /// <remarks>
    /// The key was derived on every carrier and read nowhere: the HMAC was stripped unchecked, so a corrupted frame that
    /// happened to pad correctly decrypted to garbage instead of failing. Measured before the check went in (Session 6b,
    /// a throwaway probe over the WinBox CLI and native legs): RouterOS 7.25, 7.21.5 and 6.49.21 HMAC every frame, over
    /// TCP and over the MAC layer, exactly as the client does — tens of thousands of frames, no mismatch.
    /// </remarks>
    [TestClass]
    public class WinboxReceiveHmacTests
    {
        private static readonly byte[] Secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] Message = M2Message.BuildM2(M2Message.SysFrom(), M2Message.U32User(0x11, 42));

        private static byte[] OtherHmacKey()
        {
            var key = new byte[20];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            return key;
        }

        [TestMethod]
        public void AFrameTheRouterHmacedWithTheSessionKey_Decrypts()
        {
            var client = WinboxStreamKeys.Derive(Secret);
            var router = WinboxStreamKeys.Derive(Secret, server: true);
            CollectionAssert.AreEqual(Message, client.Decrypt(router.Encrypt(Message)));
        }

        [TestMethod]
        public void AFrameWhoseHmacDoesNotMatch_DoesNotDecrypt()
        {
            var client = WinboxStreamKeys.Derive(Secret);
            var router = WinboxStreamKeys.Derive(Secret, server: true);
            byte[] forged = WinboxStreamCrypto.Encrypt(Message, router.SendAes, OtherHmacKey());
            Assert.IsNull(client.Decrypt(forged), "the right AES key and pad, the wrong HMAC: not the router's frame");
        }

        [TestMethod]
        public void OverTcp_AFrameWhoseHmacDoesNotMatch_IsABrokenStream()
        {
            var client = WinboxStreamKeys.Derive(Secret);
            var router = WinboxStreamKeys.Derive(Secret, server: true);
            byte[] forged = WinboxStreamCrypto.Encrypt(Message, router.SendAes, OtherHmacKey());
            var ex = Assert.ThrowsException<System.InvalidOperationException>(() => WinboxM2Session.DecryptOrThrow(forged, client));
            StringAssert.Contains(ex.Message, "HMAC");
        }
    }
}
