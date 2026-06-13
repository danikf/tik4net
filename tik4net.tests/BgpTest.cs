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
            // /routing/bgp/advertisements is a read-only, per-peer dynamic query (RouterOS computes it
            // on print). WinBox does not expose it as a window/handler — an exhaustive sweep of the 7.21.4
            // .jg catalog finds BGP Connection/Session/Template/VPN/Instance but no Advertisements node —
            // so the native M2 transport has no handler to derive. It works fine over API and CLI transports.
            SkipOnWinboxNativeUnmappedPath("/routing/bgp/advertisements");

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
