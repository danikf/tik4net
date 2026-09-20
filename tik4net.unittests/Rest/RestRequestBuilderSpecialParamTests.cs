using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// The client-side markers (<c>.cli-flags</c>, <c>.cli-stats</c>, <c>.cli-json</c>, <c>detail</c>) are
    /// instructions to a transport, not data words: REST has to drop every one of them whatever request shape
    /// the command turns into. RouterOS refuses the ones it does not know — <c>Bad Request: unknown parameter
    /// .cli-flags</c> — so a marker that survives into a body fails the whole call.
    /// </summary>
    [TestClass]
    public class RestRequestBuilderSpecialParamTests
    {
        private static IList<ITikCommandParameter> WithMarkers(params (string Name, string Value)[] items)
            => items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(i.Name, i.Value, TikCommandParameterFormat.NameValue))
                .Concat(new[]
                {
                    (ITikCommandParameter)new TikCommandParameter(TikSpecialProperties.CliFlags, "disabled,running", TikCommandParameterFormat.NameValue),
                    new TikCommandParameter(TikSpecialProperties.CliStats, "yes", TikCommandParameterFormat.NameValue),
                    new TikCommandParameter(TikSpecialProperties.CliJson, "yes", TikCommandParameterFormat.NameValue),
                    new TikCommandParameter("detail", string.Empty, TikCommandParameterFormat.NameValue),
                })
                .ToList();

        private static void AssertNoMarkers(RestRequestBuilder.RestRequest req, string because)
        {
            string body = req.JsonBody ?? string.Empty;
            foreach (string marker in new[] { "cli-flags", "cli-stats", "cli-json", "detail" })
            {
                Assert.IsFalse(body.Contains(marker), because + " body: " + body);
                Assert.IsFalse(req.RelativePath.Contains(marker), because + " path: " + req.RelativePath);
            }
        }

        [TestMethod]
        public void APrintDropsThem()
        {
            AssertNoMarkers(RestRequestBuilder.Build("/interface/print", WithMarkers()), "print");
        }

        /// <summary>
        /// A monitor command is POSTed to its path with the parameters as the operation's INPUTS
        /// (<c>TikMonitorVerbs</c>), which is the shape that carried <c>.cli-flags</c> to the router and made
        /// <c>/tool/romon/discover</c>, <c>/tool/romon/ping</c> and <c>/interface/ethernet/monitor</c> fail on
        /// both REST transports.
        /// </summary>
        [TestMethod]
        public void AMonitorCommandDropsThem()
        {
            AssertNoMarkers(RestRequestBuilder.Build("/tool/romon/discover", WithMarkers(("duration", "3"))), "monitor");
        }

        [TestMethod]
        public void AnActionPostDropsThem()
        {
            AssertNoMarkers(RestRequestBuilder.Build("/log/error", WithMarkers(("message", "m")),
                RestRequestBuilder.RestCallKind.NonQuery), "action");
        }

        [TestMethod]
        public void AnAddDropsThem()
        {
            AssertNoMarkers(RestRequestBuilder.Build("/interface/vlan/add", WithMarkers(("name", "v1"))), "add");
        }

        [TestMethod]
        public void ASetDropsThem()
        {
            AssertNoMarkers(RestRequestBuilder.Build("/interface/vlan/set",
                WithMarkers((TikSpecialProperties.Id, "*1"), ("name", "v1"))), "set");
        }

        /// <summary>The data words themselves still have to arrive — dropping too much is the other failure.</summary>
        [TestMethod]
        public void TheRealParametersSurvive()
        {
            var req = RestRequestBuilder.Build("/tool/romon/discover", WithMarkers(("duration", "3")));

            Assert.AreEqual("{\"duration\":\"3\"}", req.JsonBody);
        }
    }
}
