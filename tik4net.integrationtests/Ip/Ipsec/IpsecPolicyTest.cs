using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.integrationtests
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
                // Disabled: an enabled peer with no identity makes RouterOS log
                // "ipsec,error initiator can't find identity for peer: …" on every run. The policy only needs
                // the peer to exist, not to negotiate.
                Disabled = true,
            };
            SaveTracked(peer);

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
                // A list/array field (peer) is not yet encodable over native WinBox M2 writes; the resolver
                // says so explicitly. Reading the same table works — skip only the write, only where refused.
                SkipIfWinboxNativeCannot("/ip/ipsec/policy add", () => SaveTracked(policy));

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
