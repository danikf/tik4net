using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Bgp;

namespace tik4net.tests
{
    [TestClass]
    public class BgpTest: TestBase
    {
        [TestMethod]
        public void ListAllBgpAdvertisementsWillNotFail()
        {
            var list = Connection.LoadAll<BgpAdvertisements>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllInstancesWillNotFail()
        {
            var list = Connection.LoadAll<BgpInstance>();
            Assert.IsNotNull(list);
        }

        /// <summary>RouterOS 7+: /routing/bgp/connection replaced /routing/bgp/peer.</summary>
        [TestMethod]
        public void ListAllConnectionsWillNotFail()
        {
            EnsureMinRouterOsVersion(7, "/routing/bgp/connection");
            var list = Connection.LoadAll<BgpConnection>();
            Assert.IsNotNull(list);
        }

        /// <summary>RouterOS 6 only — /routing/bgp/peer was removed in RouterOS 7.</summary>
        [TestMethod]
        [Obsolete]
        public void ListAllPeersWillNotFail()
        {
            EnsureMaxRouterOsVersion(7, "/routing/bgp/peer");
#pragma warning disable CS0618
            var list = Connection.LoadAll<BgpPeer>();
#pragma warning restore CS0618
            Assert.IsNotNull(list);
        }

        /// <summary>RouterOS 6 only — /routing/bgp/network was removed in RouterOS 7.</summary>
        [TestMethod]
        [Obsolete]
        public void ListAllBgpNetworksWillNotFail()
        {
            EnsureMaxRouterOsVersion(7, "/routing/bgp/network");
#pragma warning disable CS0618
            var list = Connection.LoadAll<BgpNetwork>();
#pragma warning restore CS0618
            Assert.IsNotNull(list);
        }
   }
}
