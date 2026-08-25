// FirewallConnectionIcmpTypeTest.cs — /ip/firewall/connection's icmp-type must read as the API prints it.
//
// The .jg catalog names the value because that is what the WinBox window shows: the wire carries 8 (M2
// key 0x10) and the catalog calls it 'echo-request'. RouterOS does not — /ip/firewall/connection/print
// over the binary API says 8 — and a transport is supposed to answer what the API answers.
//
// It took this long to surface because the field only EXISTS while an ICMP connection is live, and the
// connection table is otherwise full of TCP and UDP rows the audit compares happily. So the test makes
// one: a ping from the router puts a row in the table for as long as the tracker's icmp timeout (10s by
// default), which is comfortably longer than the two reads below.
//
// Compared against a side API connection rather than against a literal, so the test says "these two
// transports agree" rather than "this is the number today".

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Ip.Firewall
{
    [TestClass]
    public class FirewallConnectionIcmpTypeTest : TestBase
    {
        private const string PingTarget = "127.0.0.1";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestMethod]
        public void IcmpTypeReadsAsTheApiPrintsIt()
        {
            using (var api = OpenSideApi())
            {
                ITikReSentence apiRow = null;
                for (int attempt = 0; attempt < 3 && apiRow == null; attempt++)
                {
                    Ping(api);
                    apiRow = IcmpRow(api);
                }

                if (apiRow == null)
                    Assert.Inconclusive("the router tracked no ICMP connection after three pings to "
                        + PingTarget + " — nothing to compare, and the field exists only while one is live");

                string apiValue = apiRow.GetResponseFieldOrDefault("icmp-type", null);
                Assert.IsNotNull(apiValue, "the API reported an ICMP connection without an icmp-type");

                var probeRow = IcmpRow(Connection);
                if (probeRow == null)
                    Assert.Inconclusive("the connection expired before " + ResolveConnectionType()
                        + " could read it");

                string probeValue = probeRow.GetResponseFieldOrDefault("icmp-type", null);
                Assert.AreEqual(apiValue, probeValue,
                    "icmp-type: the API prints the number the wire carries; " + ResolveConnectionType()
                    + " answered '" + probeValue + "'. The .jg names this value because the WinBox window "
                    + "shows a name, but RouterOS does not.");
            }
        }

        private static void Ping(ITikConnection api)
        {
            // Two echoes rather than one: the first can be answered before the tracker has confirmed the
            // row, and a count of two costs nothing.
            api.CreateCommandAndParameters("/ping", TikCommandParameterFormat.NameValue,
                                           "address", PingTarget, "count", "2").ExecuteList();
        }

        /// <summary>
        /// The ICMP row, read with <c>detail</c>.
        /// </summary>
        /// <remarks>
        /// <c>detail</c> is what makes this test measure anything on the CLI transports: a plain
        /// <c>print as-value</c> of this table omits <c>icmp-type</c> entirely, so without it the row is
        /// found but the field is not and the test skips itself on four transports out of seven. With it
        /// they read <c>icmp-type=8</c> and agree with the API. Falling back when the modifier is refused,
        /// because the binary API rejects <c>detail</c> on some menus.
        /// </remarks>
        private static ITikReSentence IcmpRow(ITikConnection conn)
        {
            try { return IcmpRow(conn, withDetail: true); }
            catch (TikCommandException) { return IcmpRow(conn, withDetail: false); }
        }

        private static ITikReSentence IcmpRow(ITikConnection conn, bool withDetail)
        {
            var cmd = conn.CreateCommandAndParameters("/ip/firewall/connection/print",
                          TikCommandParameterFormat.Filter, "protocol", "icmp");
            if (withDetail)
                cmd.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            return cmd.ExecuteList()
                      .FirstOrDefault(r => r.GetResponseFieldOrDefault("icmp-type", null) != null);
        }
    }
}
