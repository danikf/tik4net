using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpsecPeerTest : TestBase
    {
        [TestMethod]
        public void ListIpsecPeersWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/peer");
            var list = Connection.LoadAll<IpsecPeer>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecPeerWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/peer");
            // Use a short alphanumeric marker as MikroTik peer names must be valid identifiers.
            // A GUID with hyphens is accepted by RouterOS but we strip them to be safe.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var peer = new IpsecPeer
            {
                Name = marker,
                Address = "192.0.2.1",
                Comment = marker,
                // Disabled, because an enabled peer is one RouterOS immediately tries to negotiate with:
                // every run left "ipsec,error initiator can't find identity for peer: t4n…" in the router log
                // (there is no matching identity, and 192.0.2.1 is a TEST-NET address that answers nothing).
                // The test asserts the CRUD round-trip, which does not need the peer to be live.
                Disabled = true,
            };
            SaveTracked(peer);

            var loaded = Connection.LoadById<IpsecPeer>(peer.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
