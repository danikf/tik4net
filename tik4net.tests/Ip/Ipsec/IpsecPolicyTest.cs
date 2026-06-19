using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecPolicyTest : TestBase
    {
        [TestMethod]
        public void ListIpsecPoliciesWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/policy");
            var list = Connection.LoadAll<IpsecPolicy>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecPolicyWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/policy");

            string marker = Guid.NewGuid().ToString();

            // A non-template action=encrypt policy requires an associated peer ("Peer not set!"),
            // so create a throwaway peer first and tear it down afterwards.
            string peerName = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var peer = new IpsecPeer
            {
                Name = peerName,
                Address = "192.0.2.1",
            };
            Connection.Save(peer);

            // Use RFC 5737 documentation subnets — safe test addresses that will not
            // overlap with real traffic on the test router.
            var policy = new IpsecPolicy
            {
                SrcAddress = "192.0.2.0/24",
                DstAddress = "198.51.100.0/24",
                Action = IpsecPolicy.ActionType.Encrypt,
                Level = IpsecPolicy.LevelType.Require,
                IpsecProtocols = IpsecPolicy.IpsecProtocolsType.Esp,
                Tunnel = true,
                Peer = peerName,
                Proposal = "default",
                Comment = marker,
            };

            try
            {
                Connection.Save(policy);

                var loaded = Connection.LoadById<IpsecPolicy>(policy.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual("192.0.2.0/24", loaded.SrcAddress);
                Assert.AreEqual("198.51.100.0/24", loaded.DstAddress);
            }
            finally
            {
                // Always clean up, even if an assertion fails.
                if (policy.Id != null)
                    try { Connection.Delete(policy); } catch { /* best effort */ }
                try { Connection.Delete(peer); } catch { /* best effort */ }
            }
        }
    }
}
