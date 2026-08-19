using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip;

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
        public void IpRoutesAgreeWithTheApi()
        {
            var viaTransport = Connection.LoadAll<IpRoute>().ToList();

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadAll<IpRoute>().ToList();

                CollectionAssert.AreEquivalent(
                    viaApi.Select(r => r.DstAddress).ToList(),
                    viaTransport.Select(r => r.DstAddress).ToList(),
                    "the transport under test listed different /ip/route destinations than the binary API — "
                    + "an IPv6 destination here means the read is not filtered to the IPv4 family");

                foreach (var api in viaApi)
                {
                    var mine = viaTransport.FirstOrDefault(r => r.Id == api.Id);
                    if (mine == null) continue;
                    Assert.AreEqual(api.Distance, mine.Distance, "distance on " + api.DstAddress);
                    Assert.AreEqual(api.Scope, mine.Scope, "scope on " + api.DstAddress);
                    Assert.AreEqual(api.TargetScope, mine.TargetScope, "target-scope on " + api.DstAddress);
                    Assert.AreEqual(api.Gateway, mine.Gateway, "gateway on " + api.DstAddress);
                    Assert.AreEqual(api.Active, mine.Active, "active on " + api.DstAddress);
                }
            }
        }
    }
}
