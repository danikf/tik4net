using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecIdentityTest : TestBase
    {
        [TestMethod]
        public void ListIpsecIdentitiesWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/identity");
            var list = Connection.LoadAll<IpsecIdentity>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecIdentityWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/identity");
            EnsureCommandAvailable("/ip/ipsec/peer");

            // A short alphanumeric name — hyphens in GUIDs are accepted by RouterOS but we strip them.
            string peerName = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string marker   = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);

            var peer = new IpsecPeer
            {
                Name    = peerName,
                Address = "192.0.2.50",
                Comment = peerName,
            };
            Connection.Save(peer);

            IpsecIdentity identity = null;
            try
            {
                identity = new IpsecIdentity
                {
                    Peer       = peerName,
                    AuthMethod = IpsecIdentity.AuthMethodType.PreSharedKey,
                    Secret     = "test123",
                    Comment    = marker,
                };
                Connection.Save(identity);

                var loaded = Connection.LoadById<IpsecIdentity>(identity.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(peerName, loaded.Peer);
                Assert.AreEqual(IpsecIdentity.AuthMethodType.PreSharedKey, loaded.AuthMethod);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                if (identity != null && !string.IsNullOrEmpty(identity.Id))
                    Connection.Delete(identity);
                Connection.Delete(peer);
            }
        }
    }
}
