using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Routing;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpRouteTest : TestBase
    {
        [TestMethod]
        public void LoadIpRoutesWillNotFail()
        {
            var list = Connection.LoadAll<IpRoute>();
            Assert.IsNotNull(list);
        }

        /// <summary>
        /// A filter on the other version's name of a renamed field is refused, naming it, before the read is sent. Left to
        /// the router it came back as no rows on the API and REST and as EVERY row on the CLI transports: 7.24.4 reads
        /// <c>where routing-mark=main</c> as true for each row. RouterOS 6 over the API cannot describe the menu, so there
        /// the filter goes out unchecked and the API's own answer stands: no rows.
        /// </summary>
        [TestMethod]
        public void AFilterOnTheOtherVersionsNameOfAField_IsRefused()
        {
            string otherName = GetMikrotikVersion().Major < 7 ? "routing-table" : "routing-mark";
            var filter = Connection.CreateParameter(otherName, "main", TikCommandParameterFormat.Filter);

            var type = ResolveConnectionType();
            bool api = type == TikConnectionType.Api || type == TikConnectionType.ApiSsl;
            if (GetMikrotikVersion().Major < 7 && api)
            {
                Assert.AreEqual(0, Connection.LoadList<IpRoute>(filter).Count(), "the API matched rows on an unknown field");
                return;
            }
            var ex = Assert.ThrowsException<TikUnknownFieldException>(() => Connection.LoadList<IpRoute>(filter).ToList());
            CollectionAssert.AreEqual(new[] { otherName }, ex.Fields.ToArray());
        }

        /// <summary>
        /// The routing table is <c>routing-table</c> on RouterOS 7 and <c>routing-mark</c> on RouterOS 6, and each
        /// refuses the other name: read under either, and a route read with a mark is saved under that name again.
        /// </summary>
        /// <remarks>
        /// RouterOS 6 prints no mark for a main-table route, so there the mark is given with a command and the entity
        /// only changes it. A table other than <c>main</c> on both sides, so an unmapped field cannot pass as a default.
        /// </remarks>
        [TestMethod]
        public void TheRoutingTableIsReadAndSavedUnderTheNameTheRouterUses()
        {
            bool v6 = GetMikrotikVersion().Major < 7;
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            string first = "t4n-rt-a-" + tag, second = "t4n-rt-b-" + tag;
            if (!v6)
            {
                SaveTracked(new RoutingTable { Name = first, Fib = true, Comment = "t4n-rt-" + tag });
                SaveTracked(new RoutingTable { Name = second, Fib = true, Comment = "t4n-rt-" + tag });
            }

            var route = new IpRoute
            {
                DstAddress = "203.0.113.77/32",
                Gateway = "127.0.0.1",
                Disabled = true,
                Comment = "t4n-rt-" + tag,
            };
            if (!v6)
                route.RoutingTable = first;
            SaveTracked(route);
            if (v6)
                Connection.CreateCommandAndParameters("/ip/route/set", ".id", route.Id, "routing-mark", first).ExecuteNonQuery();

            var read = Connection.LoadById<IpRoute>(route.Id);
            if (v6 && NativeListOmitsRouteDetail())
                Assert.Inconclusive("WinBox native reads RouterOS 6 routes from the list, which sends no routing-mark "
                                    + "(Docs/findings-routeros-6.md, problem 1b)");
            Assert.AreEqual(first, read.RoutingTable.Value, "read");

            read.RoutingTable = second;
            Connection.Save(read);

            Assert.AreEqual(second, Connection.LoadById<IpRoute>(route.Id).RoutingTable.Value, "saved");
        }

        /// <summary>
        /// G3.4: <c>/ip/route</c> is the IPv4 routes, with the fields the API reports.
        /// </summary>
        /// <remarks>
        /// WinBox keeps one routes table ([44,21]) for both families and tells them apart by its own
        /// <c>rtype</c>; the native transport addressed that shared table directly, so a read answered with
        /// the IPv6 routes as well (six rows against the API's two on the lab CHR, the extras being
        /// <c>::1/128</c> and <c>fe80::/64</c>) and with only the columns the list view sketches —
        /// distance, scope, target-scope, vrf-interface and routing-table were absent, the gateway came
        /// back as an unresolved reference number, and <c>active</c> as the raw <c>4</c>.
        /// <para>Four API fields native still does not read, and this test does not pretend otherwise:
        /// <c>immediate-gw</c> (a hyperlink handle into [44,16]), <c>dynamic</c>, and the
        /// <c>dhcp</c>/<c>connect</c> source flags — for which native reports the same fact as
        /// <c>belongs-to</c>. See Docs/winbox-native-m2-protocol.md.</para>
        /// </remarks>
        [TestMethod]
        [TestLock(TestLockScope.Router)]
        public void IpRoutesAgreeWithTheApi()
        {
            var viaTransport = Connection.LoadAll<IpRoute>().ToList();

            string host = LabConfig.Get("host");
            string user = LabConfig.Get("user");
            string pass = LabConfig.Get("pass") ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadAll<IpRoute>().ToList();

                CollectionAssert.AreEquivalent(
                    viaApi.Select(r => r.DstAddress).ToList(),
                    viaTransport.Select(r => r.DstAddress).ToList(),
                    "the transport under test listed different /ip/route destinations than the binary API — "
                    + "an IPv6 destination here means the read is not filtered to the IPv4 family");

                // RouterOS 6's native route list sends no scope or target-scope (findings-routeros-6.md, problem 1b).
                bool scopesAbsent = GetMikrotikVersion().Major < 7 && NativeListOmitsRouteDetail();
                foreach (var api in viaApi)
                {
                    var mine = viaTransport.FirstOrDefault(r => r.Id == api.Id);
                    if (mine == null) continue;
                    Assert.AreEqual(api.Distance, mine.Distance, "distance on " + api.DstAddress);
                    if (!scopesAbsent)
                    {
                        Assert.AreEqual(api.Scope, mine.Scope, "scope on " + api.DstAddress);
                        Assert.AreEqual(api.TargetScope, mine.TargetScope, "target-scope on " + api.DstAddress);
                    }
                    Assert.AreEqual(api.Gateway, mine.Gateway, "gateway on " + api.DstAddress);
                    Assert.AreEqual(api.Active, mine.Active, "active on " + api.DstAddress);
                }
            }
        }

        /// <summary>
        /// WinBox native reads a route from the list (<c>getall</c>), and on RouterOS 6 that list leaves out
        /// <c>scope</c>, <c>target-scope</c>, <c>routing-mark</c> and <c>vrf-interface</c>; they are left absent by
        /// decision rather than fetched row by row.
        /// </summary>
        private bool NativeListOmitsRouteDetail()
        {
            var t = ResolveConnectionType();
            return t == TikConnectionType.WinboxNative || t == TikConnectionType.WinboxNativeMac;
        }
    }
}
