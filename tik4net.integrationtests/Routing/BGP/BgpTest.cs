using System;
using System.Collections.Generic;
using System.Linq;
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
            // no handler to derive. It works fine over API and CLI transports. The skip waits for that
            // refusal rather than naming the transport, so it lifts by itself if a handler ever appears;
            // BgpAdvertisementsOverWinboxNativeSaysThereIsNoWindow below pins the refusal itself.
            SkipIfWinboxNativeCannot("/routing/bgp/advertisements", () =>
            {
                var list = Connection.LoadAll<BgpAdvertisements>();
                Assert.IsNotNull(list);
            });
        }

        /// <summary>
        /// The rows a transport reads are the binary API's: same ids, same values. Needs an established BGP session
        /// that advertises something (the lab's is on the second router, CHR2, peering with the first — run with
        /// <c>-Router chr2</c>); with none the API has no rows and the test is Inconclusive.
        /// </summary>
        /// <remarks>
        /// On RouterOS 6 the CLI transports read this menu as its plain table (no <c>as-value</c>), ids from <c>find</c>
        /// in the same script; the table cuts a peer name to 8 characters, and a cut value is left out, never read as the
        /// name. So <c>peer</c> is either the API's or absent, and every other field must agree.
        /// </remarks>
        [TestMethod]
        public void AdvertisementsMatchTheBinaryApi()
        {
            SkipIfWinboxNativeCannot("/routing/bgp/advertisements", () =>
            {
                List<BgpAdvertisements> apiRows;
                using (var api = LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                    apiRows = api.LoadAll<BgpAdvertisements>().ToList();
                if (apiRows.Count == 0)
                    Assert.Inconclusive("no advertisements on this router: needs an established BGP session (lab: -Router chr2)");

                var rows = Connection.LoadAll<BgpAdvertisements>().ToDictionary(r => r.Id);
                Assert.AreEqual(apiRows.Count, rows.Count, "row count");
                foreach (var apiRow in apiRows)
                {
                    Assert.IsTrue(rows.TryGetValue(apiRow.Id, out var row), $"no row {apiRow.Id}");
                    Assert.AreEqual(apiRow.Prefix, row.Prefix, $"{apiRow.Id} prefix");
                    Assert.AreEqual(apiRow.Nexthop, row.Nexthop, $"{apiRow.Id} nexthop");
                    Assert.AreEqual(apiRow.Origin, row.Origin, $"{apiRow.Id} origin");
                    if (row.Peer.IsPresent)
                        Assert.AreEqual(apiRow.Peer, row.Peer, $"{apiRow.Id} peer");
                }
            });
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
            // /routing/bgp/instance is not on every RouterOS 7: 7.19.6 answers "bad command name instance",
            // 7.24.4 has the menu.
            EnsureCommandAvailable("/routing/bgp/instance");
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
