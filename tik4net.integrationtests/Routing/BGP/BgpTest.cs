using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Bgp;

namespace tik4net.integrationtests
{
    [TestClass]
    public class BgpTest: TestBase
    {
        [TestMethod]
        public void ListAllBgpAdvertisementsWillNotFail()
        {
            // /routing/bgp/advertisements is a read-only, per-peer dynamic query (RouterOS computes it
            // on print). WinBox does not expose it as a window/handler — see
            // WinboxHandlerMap.NoWinboxWindow for what it offers instead — so the native M2 transport has
            // no handler to derive. It works fine over API and CLI transports.
            SkipOnWinboxNativeUnmappedPath("/routing/bgp/advertisements");

            var list = Connection.LoadAll<BgpAdvertisements>();
            Assert.IsNotNull(list);
        }

        /// <summary>
        /// G3.7: over WinBox-native the path must FAIL, and say why it can never work.
        /// </summary>
        /// <remarks>
        /// WinBox reaches advertisements through the BGP session window's 'Dump Adv.' action
        /// (<c>type:'doit'</c>, cmd:9, with a 'Save To' string) — a command that writes a file, not a table
        /// anything can read. So there is no window, and the generic "add a PathAlias naming the window"
        /// advice would send a caller looking for one that does not exist. Two things are pinned here: that
        /// the transport raises rather than answering with an empty list, and that the message says it is
        /// not a mapping gap.
        /// </remarks>
        [TestMethod]
        public void BgpAdvertisementsOverWinboxNativeSaysThereIsNoWindow()
        {
            if (ResolveConnectionType() != TikConnectionType.WinboxNative
                && ResolveConnectionType() != TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive("this is about the native transport's path map");

            try
            {
                Connection.LoadAll<BgpAdvertisements>();
                Assert.Fail("an unreachable path must raise, not answer with an empty list — a short list "
                            + "reads exactly like 'the router has none'");
            }
            catch (TikPathNotMappedException ex)
            {
                StringAssert.Contains(ex.Message, "no WinBox window",
                    "the message must say the window does not exist");
                StringAssert.Contains(ex.Message, "not a mapping gap",
                    "and must not invite a PathAlias for a window that cannot be named");
            }
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
