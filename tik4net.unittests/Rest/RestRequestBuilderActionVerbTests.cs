using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers what the builder does with a trailing path segment it is told about. It cannot tell a COMMAND from a
    /// MENU NAME from the text alone — <c>/log/error</c> and <c>/ip/address</c> are the same shape — so the caller
    /// says (RestConnectionWireTests covers how it finds out). Measured live on 7.23.2 that the POST form works:
    /// <c>curl -X POST -d '{"message":"…"}' /rest/log/error</c> returns <c>200 []</c> and the line appears
    /// in <c>/log</c>, and that <c>GET /rest/log/error</c> answers <c>400 no such command</c>.
    /// </summary>
    [TestClass]
    public class RestRequestBuilderActionVerbTests
    {
        private static IList<ITikCommandParameter> Params(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, TikCommandParameterFormat.NameValue))
                .ToList();

        [TestMethod]
        public void NonQuery_UnknownTrailingSegment_IsPostedAsAnAction()
        {
            var req = RestRequestBuilder.Build("/log/error", Params(("message", "boom")),
                RestTrailingSegment.Command);

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/log/error", req.RelativePath);
            Assert.AreEqual("{\"message\":\"boom\"}", req.JsonBody);
        }

        [TestMethod]
        public void NonQuery_AllFourLogLevels_ReachTheirOwnEndpoint()
        {
            // debug/info/warning/error are four separate router commands, not one command with a level
            // parameter — each has to survive as its own last segment.
            foreach (string level in new[] { "debug", "info", "warning", "error" })
            {
                var req = RestRequestBuilder.Build("/log/" + level, Params(("message", "m")),
                    RestTrailingSegment.Command);

                Assert.AreEqual("/log/" + level, req.RelativePath, "level " + level);
                Assert.AreEqual("POST", req.Method.Method, "level " + level);
            }
        }

        [TestMethod]
        public void Read_UnknownTrailingSegment_IsStillAMenuName()
        {
            // Getting this wrong would turn every ordinary list into a POST.
            var req = RestRequestBuilder.Build("/ip/address", new List<ITikCommandParameter>());

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/ip/address", req.RelativePath);
        }

        [TestMethod]
        public void NonQuery_RecognisedVerbs_KeepTheirOwnMapping()
        {
            // add/set/remove have REST spellings of their own (PUT, PATCH, DELETE) that were established against
            // the router; "a command is a POST of the whole path" must not replace them.
            var add = RestRequestBuilder.Build("/ip/address/add", Params(("address", "10.0.0.1/24")),
                RestTrailingSegment.Command);
            Assert.AreEqual("PUT", add.Method.Method);
            Assert.AreEqual("/ip/address", add.RelativePath);

            var set = RestRequestBuilder.Build("/ip/address/set",
                Params((TikSpecialProperties.Id, "*1"), ("comment", "c")),
                RestTrailingSegment.Command);
            Assert.AreEqual("PATCH", set.Method.Method);
            Assert.AreEqual("/ip/address/*1", set.RelativePath);

            var remove = RestRequestBuilder.Build("/ip/address/remove", Params((TikSpecialProperties.Id, "*1")),
                RestTrailingSegment.Command);
            Assert.AreEqual("DELETE", remove.Method.Method);
            Assert.AreEqual("/ip/address/*1", remove.RelativePath);
        }

        [TestMethod]
        public void ACommandReadThroughARead_KeepsItsInputs()
        {
            // /tool/wol answers with a row, so it is invoked through a read method, whose rewrite makes its inputs
            // filters. Dropped, RouterOS answers "missing =mac=".
            var req = RestRequestBuilder.Build("/tool/wol",
                new List<ITikCommandParameter> { new TikCommandParameter("mac", "00:11:22:33:44:55", TikCommandParameterFormat.Filter) },
                RestTrailingSegment.Command);

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/tool/wol", req.RelativePath);
            Assert.AreEqual("{\"mac\":\"00:11:22:33:44:55\"}", req.JsonBody);
        }

        [TestMethod]
        public void Inspect_IsPostedToItsOwnPath()
        {
            // POST /rest/console/inspect/print → 400 "no such command"; POST /rest/console/inspect answers (7.24.4).
            var req = RestRequestBuilder.Build("/console/inspect", Params(("request", "child"), ("path", "ip,route,add")), RestTrailingSegment.Command);

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/console/inspect", req.RelativePath);
            StringAssert.Contains(req.JsonBody, "\"path\":\"ip,route,add\"");
        }

        [TestMethod]
        public void NonQuery_ActionWithNoParameters_PostsWithoutABody()
        {
            var req = RestRequestBuilder.Build("/ip/dhcp-client/renew",
                new List<ITikCommandParameter>(), RestTrailingSegment.Command);

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ip/dhcp-client/renew", req.RelativePath);
            Assert.IsNull(req.JsonBody, "an empty JSON object is not the same as no body");
        }
    }
}
