using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers the REST spelling of <c>get</c>.
    /// </summary>
    /// <remarks>
    /// REST has no <c>get</c> verb — it addresses a row by URL — so the verb has to be translated rather than
    /// forwarded. Until it was, the trailing segment fell through to the generic POST branch and
    /// <c>/interface/get</c> went out as <c>POST /rest/interface/get</c>, which RouterOS answers
    /// <c>400 no such command</c>: the one transport refusing a command the binary API and all five CLI
    /// transports honour.
    /// <para>
    /// Verified live on RouterOS 7.24: <c>GET /rest/interface/*2?.proplist=name</c> answers
    /// <c>{"name":"ether1"}</c>, <c>GET /rest/system/identity?.proplist=name</c> answers <c>{"name":"CHR"}</c>,
    /// and the id-only form returns the whole row object.
    /// </para>
    /// </remarks>
    [TestClass]
    public class RestRequestBuilderGetTests
    {
        private static IList<ITikCommandParameter> Params(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, TikCommandParameterFormat.NameValue))
                .ToList();

        [TestMethod]
        public void Get_ByIdAndValueName_IsAGetOfTheRowNarrowedByProplist()
        {
            var req = RestRequestBuilder.Build("/interface/get",
                Params((TikSpecialProperties.Id, "*2"), ("value-name", "name")));

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/interface/*2?.proplist=name", req.RelativePath);
            Assert.IsNull(req.JsonBody);
        }

        [TestMethod]
        public void Get_ByIdAlone_AddressesTheRowAndAsksForEverything()
        {
            var req = RestRequestBuilder.Build("/interface/get",
                Params((TikSpecialProperties.Id, "*2")));

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/interface/*2", req.RelativePath);
        }

        [TestMethod]
        public void Get_OnASingletonMenu_HasNoIdSegment()
        {
            var req = RestRequestBuilder.Build("/system/identity/get",
                Params(("value-name", "name")));

            Assert.AreEqual("GET", req.Method.Method);
            Assert.AreEqual("/system/identity?.proplist=name", req.RelativePath);
        }

        [TestMethod]
        public void Get_IsNotPostedToAGetEndpoint()
        {
            // The exact shape of the original defect: an unrecognised trailing segment became part of the
            // URL and was POSTed. That request is well-formed HTTP, so nothing but the router's 400 could
            // reveal it.
            var req = RestRequestBuilder.Build("/interface/get",
                Params((TikSpecialProperties.Id, "*2"), ("value-name", "name")));

            StringAssert.DoesNotMatch(req.RelativePath,
                new System.Text.RegularExpressions.Regex("/get"));
            Assert.AreNotEqual("POST", req.Method.Method);
        }

        [TestMethod]
        public void Get_DoesNotPercentEncodeTheIdStar()
        {
            // RouterOS ids are '*' plus hex and the router matches them literally; percent-encoding the star
            // yields a URL it does not resolve.
            var req = RestRequestBuilder.Build("/ip/firewall/filter/get",
                Params((TikSpecialProperties.Id, "*1A")));

            Assert.AreEqual("/ip/firewall/filter/*1A", req.RelativePath);
        }
    }
}
