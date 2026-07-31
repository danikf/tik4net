using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers how the builder tells an ACTION VERB from a MENU NAME in the trailing path segment.
    /// It cannot do that from the text alone — <c>/log/error</c> and <c>/ip/address</c> are the same shape —
    /// so the answer comes from which command method the caller used. Before P2.48 that information never
    /// reached the builder: anything not on the hard-coded write-verb allow-list became part of the path with
    /// an implicit <c>print</c>, so <c>connection.LogError(…)</c> went out as <c>GET /rest/log/error</c> and
    /// RouterOS answered <c>400 no such command</c>. Measured live on 7.23.2 that the POST form works:
    /// <c>curl -X POST -d '{"message":"…"}' /rest/log/error</c> returns <c>200 []</c> and the line appears
    /// in <c>/log</c>.
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
                RestRequestBuilder.RestCallKind.NonQuery);

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
                    RestRequestBuilder.RestCallKind.NonQuery);

                Assert.AreEqual("/log/" + level, req.RelativePath, "level " + level);
                Assert.AreEqual("POST", req.Method.Method, "level " + level);
            }
        }

        [TestMethod]
        public void Read_UnknownTrailingSegment_IsStillAMenuName()
        {
            // The read path must not change: /log is a table, and so is anything else whose last segment is
            // simply the menu. Getting this wrong would turn every ordinary list into a POST.
            var req = RestRequestBuilder.Build("/ip/address", new List<ITikCommandParameter>());

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/ip/address", req.RelativePath);
        }

        [TestMethod]
        public void NonQuery_RecognisedVerbs_KeepTheirOwnMapping()
        {
            // The new rule is a FALLBACK. add/set/remove/unset have REST spellings of their own (PUT, PATCH,
            // DELETE, POST …/unset) that were established against the router; a blanket "non-query means POST
            // the whole path" would have silently replaced all of them.
            var add = RestRequestBuilder.Build("/ip/address/add", Params(("address", "10.0.0.1/24")),
                RestRequestBuilder.RestCallKind.NonQuery);
            Assert.AreEqual("PUT", add.Method.Method);
            Assert.AreEqual("/ip/address", add.RelativePath);

            var set = RestRequestBuilder.Build("/ip/address/set",
                Params((TikSpecialProperties.Id, "*1"), ("comment", "c")),
                RestRequestBuilder.RestCallKind.NonQuery);
            Assert.AreEqual("PATCH", set.Method.Method);
            Assert.AreEqual("/ip/address/*1", set.RelativePath);

            var remove = RestRequestBuilder.Build("/ip/address/remove", Params((TikSpecialProperties.Id, "*1")),
                RestRequestBuilder.RestCallKind.NonQuery);
            Assert.AreEqual("DELETE", remove.Method.Method);
            Assert.AreEqual("/ip/address/*1", remove.RelativePath);
        }

        [TestMethod]
        public void Wol_IsTheSameUrlWhicheverWayItIsInvoked()
        {
            // /tool/wol is the trap this rule has to survive: the whole path IS the operation, so splitting a
            // "verb" off the end would be a guess. It is reachable both ways — through a read method, because
            // it answers with a row (which is why 'wol' is on the write-verb list), and through
            // ExecuteNonQuery. Both must produce POST /rest/tool/wol carrying the mac.
            var read = RestRequestBuilder.Build("/tool/wol", Params(("mac", "00:11:22:33:44:55")));
            var nonQuery = RestRequestBuilder.Build("/tool/wol", Params(("mac", "00:11:22:33:44:55")),
                RestRequestBuilder.RestCallKind.NonQuery);

            Assert.AreEqual("POST", read.Method.Method);
            Assert.AreEqual("/tool/wol", read.RelativePath);
            Assert.AreEqual(read.Method.Method, nonQuery.Method.Method);
            Assert.AreEqual(read.RelativePath, nonQuery.RelativePath);
            Assert.AreEqual(read.JsonBody, nonQuery.JsonBody);
        }

        [TestMethod]
        public void NonQuery_ActionWithNoParameters_PostsWithoutABody()
        {
            // 'renew' is deliberately NOT on the write-verb allow-list — this exercises the new fallback
            // rather than an entry someone already added.
            var req = RestRequestBuilder.Build("/ip/dhcp-client/renew",
                new List<ITikCommandParameter>(), RestRequestBuilder.RestCallKind.NonQuery);

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ip/dhcp-client/renew", req.RelativePath);
            Assert.IsNull(req.JsonBody, "an empty JSON object is not the same as no body");
        }
    }
}
