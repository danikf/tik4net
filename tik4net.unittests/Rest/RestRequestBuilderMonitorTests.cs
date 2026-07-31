using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers monitor commands whose path IS the operation — <c>/ping</c>, <c>/tool/traceroute</c>,
    /// <c>/interface/monitor-traffic</c> — reached through a READ method, which is how their consumers are
    /// written (they answer with rows).
    /// </summary>
    /// <remarks>
    /// P2.51: none of these names was on a verb list, so the implicit-<c>print</c> branch claimed them and the
    /// builder sent <c>POST /rest/ping/print</c> → <c>400 no such command</c>. Measured live on 7.23.2 that the
    /// path itself is the endpoint: <c>curl -X POST -d '{"address":"127.0.0.1","count":"2"}' /rest/ping</c>
    /// returns the two echo rows, and the same holds for <c>/rest/tool/traceroute</c> and
    /// <c>/rest/interface/monitor-traffic</c>. Sibling of the <c>/tool/wol</c> trap CLAUDE.md records.
    /// </remarks>
    [TestClass]
    public class RestRequestBuilderMonitorTests
    {
        private static IList<ITikCommandParameter> Params(TikCommandParameterFormat format,
            params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, format))
                .ToList();

        [TestMethod]
        public void PingIsPostedToItsOwnPathWithItsInputs()
        {
            var req = RestRequestBuilder.Build("/ping",
                Params(TikCommandParameterFormat.NameValue, ("address", "127.0.0.1"), ("count", "2")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ping", req.RelativePath);
            Assert.AreEqual("{\"address\":\"127.0.0.1\",\"count\":\"2\"}", req.JsonBody);
        }

        [TestMethod]
        public void TracerouteIsPostedToItsOwnPath()
        {
            var req = RestRequestBuilder.Build("/tool/traceroute",
                Params(TikCommandParameterFormat.NameValue, ("address", "127.0.0.1"), ("count", "1")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/tool/traceroute", req.RelativePath);
        }

        [TestMethod]
        public void MonitorTrafficIsPostedToItsOwnPath()
        {
            var req = RestRequestBuilder.Build("/interface/monitor-traffic",
                Params(TikCommandParameterFormat.NameValue, ("interface", "ether1"), ("once", "")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/interface/monitor-traffic", req.RelativePath);
            Assert.AreEqual("{\"interface\":\"ether1\",\"once\":\"\"}", req.JsonBody);
        }

        [TestMethod]
        public void TheReadPathsFilterRewriteStillReachesTheBody()
        {
            // TikGenericCommand.ResolveParamsForRead turns the caller's parameters into Filter format before
            // the transport sees them. For an operation there is nothing to filter, so they are its arguments —
            // the same rule BuildGenericPost already applies for /tool/wol.
            var req = RestRequestBuilder.Build("/ping",
                Params(TikCommandParameterFormat.Filter, ("address", "127.0.0.1"), ("count", "2")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ping", req.RelativePath);
            Assert.AreEqual("{\"address\":\"127.0.0.1\",\"count\":\"2\"}", req.JsonBody);
        }

        [TestMethod]
        public void EthernetMonitorKeepsTheUrlItAlreadyHad()
        {
            // 'monitor' was already a known verb, so the verb split and the new branch must agree on the URL.
            var req = RestRequestBuilder.Build("/interface/ethernet/monitor",
                Params(TikCommandParameterFormat.NameValue, ("numbers", "ether1"), ("once", "")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/interface/ethernet/monitor", req.RelativePath);
        }

        [TestMethod]
        public void AnOrdinaryTableIsStillAGet()
        {
            // The guard against over-reach: only the listed monitor names change shape.
            var req = RestRequestBuilder.Build("/ip/address", new List<ITikCommandParameter>());

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/ip/address", req.RelativePath);
        }
    }
}
