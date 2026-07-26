using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers the REST spelling of <c>unset</c>. It used to be <c>PATCH /rest/&lt;path&gt;/&lt;id&gt;</c> with
    /// <c>{field: null}</c>, which only ever worked for free-text fields: RouterOS validates the null against
    /// the property's TYPE before treating it as "clear", so unsetting a typed one answered
    /// <c>400 value of range expects range of ip addresses</c>. Verified live that
    /// <c>POST /rest/&lt;path&gt;/unset</c> with the binary API's own <c>.id</c> + <c>value-name</c> body clears
    /// any field, typed or not.
    /// </summary>
    [TestClass]
    public class RestRequestBuilderUnsetTests
    {
        private static IList<ITikCommandParameter> Params(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, TikCommandParameterFormat.NameValue))
                .ToList();

        [TestMethod]
        public void Unset_PostsToTheUnsetEndpointRatherThanPatchingANull()
        {
            var req = RestRequestBuilder.Build("/ip/firewall/filter/unset",
                Params((TikSpecialProperties.Id, "*1"), (TikSpecialProperties.UnsetValueName, "src-address")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ip/firewall/filter/unset", req.RelativePath);
            Assert.AreEqual("{\".id\":\"*1\",\"value-name\":\"src-address\"}", req.JsonBody);
        }

        [TestMethod]
        public void Unset_DoesNotEmitAJsonNull()
        {
            var req = RestRequestBuilder.Build("/ip/firewall/filter/unset",
                Params((TikSpecialProperties.Id, "*1"), (TikSpecialProperties.UnsetValueName, "connection-mark")));

            // The old form was {"connection-mark":null} — the exact shape RouterOS rejects for a typed field.
            Assert.IsFalse(req.JsonBody.Contains("null"),
                "a JSON null is the form the router type-checks and rejects");
        }

        [TestMethod]
        public void Unset_WithoutValueName_FailsLoudly()
        {
            Assert.ThrowsException<System.InvalidOperationException>(
                () => RestRequestBuilder.Build("/ip/firewall/filter/unset", Params((TikSpecialProperties.Id, "*1"))));
        }

        [TestMethod]
        public void Comment_IsPostedAsItsOwnVerbEndpoint()
        {
            // 'comment' is a real RouterOS menu command, not a synonym for 'set comment=' — REST already
            // routed it to POST /<path>/comment; this pins that alongside the CLI/native fixes.
            var req = RestRequestBuilder.Build("/ip/firewall/filter/comment",
                Params((TikSpecialProperties.Id, "*1"), ("comment", "web")));

            Assert.AreEqual("POST", req.Method.Method);
            Assert.AreEqual("/ip/firewall/filter/comment", req.RelativePath);
        }
    }
}
