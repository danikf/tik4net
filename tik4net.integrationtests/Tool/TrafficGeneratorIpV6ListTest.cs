// TrafficGeneratorIpV6ListTest.cs — a list of IPv6 address/prefix PAIRS.
//
// /tool/traffic-generator/packet-template's `ipv6-src` and `ipv6-dst` are the only two webfig
// `multinetwork6` fields in the 7.24 catalog. The type is `inherit(types.multinetwork)`, so it is the same
// list of pairs the IPv4 one is, with two differences that decide every character of the result: the
// addresses ride in an FT_ADDR6_ARRAY rather than a u32 array, and the sibling holds the PREFIX LENGTH
// itself rather than a netmask — there is no 128-bit mask to fit in a u32. The length is printed at every
// length, /128 included: webfig's tostr hides it there, but that is the GUI's rule and the API prints it.
//
// Before this the field had no case on either side: it read as nothing at all, and a write was refused.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Tool
{
    [TestClass]
    public class TrafficGeneratorIpV6ListTest : TestBase
    {
        private const string TemplateName = "tik4net-test-tmpl";

        // Documentation prefixes (RFC 3849), which nothing in this lab routes.
        private const string Prefix = "2001:db8:1::/64";
        private const string FullLength = "2001:db8:2::5/128";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        [TestCleanup]
        public void RemoveTestTemplate()
        {
            using (var api = OpenSideApi())
                foreach (var row in api.CreateCommand("/tool/traffic-generator/packet-template/print")
                             .ExecuteList()
                             .Where(r => r.GetResponseFieldOrDefault("name", "") == TemplateName).ToList())
                    api.CreateCommandAndParameters("/tool/traffic-generator/packet-template/remove",
                        TikSpecialProperties.Id, row.GetResponseField(TikSpecialProperties.Id)).ExecuteNonQuery();
        }

        // The template has to exist before the field can be set: a packet template is only valid once its
        // header stack says the packet HAS an IPv6 header, and the router rejects more than one value per
        // field on a stack this small ("only 16 random bytes in header supported").
        private static string CreateTemplate(ITikConnection conn)
        {
            conn.CreateCommandAndParameters("/tool/traffic-generator/packet-template/add",
                "name", TemplateName,
                "header-stack", "mac,ipv6,udp",
                "interface", TestConstants.Interface).ExecuteNonQuery();
            return ReadId(conn);
        }

        private static ITikReSentence Row(ITikConnection conn)
            => conn.CreateCommand("/tool/traffic-generator/packet-template/print").ExecuteList()
                   .Single(r => r.GetResponseFieldOrDefault("name", "") == TemplateName);

        private static string ReadId(ITikConnection conn)
            => Row(conn).GetResponseField(TikSpecialProperties.Id);

        private static void SetField(ITikConnection conn, string id, string name, string value)
            => conn.CreateCommandAndParameters("/tool/traffic-generator/packet-template/set",
                TikSpecialProperties.Id, id, name, value).ExecuteNonQuery();

        [TestMethod]
        public void APrefixIsWrittenAndReadBackAsTheApiSpellsIt()
        {
            using (var api = OpenSideApi())
            {
                string id = CreateTemplate(api);
                SetField(Connection, id, "ipv6-src", Prefix);

                Assert.AreEqual(Prefix, Row(api).GetResponseField("ipv6-src"),
                    "as the API reads it after the write over the transport under test");
            }
        }

        [TestMethod]
        public void ABareAddressIsTheFullLengthPrefixAndReadsBackSpelledOut()
        {
            using (var api = OpenSideApi())
            {
                string id = CreateTemplate(api);
                SetField(Connection, id, "ipv6-dst", "2001:db8:2::5");

                Assert.AreEqual(FullLength, Row(api).GetResponseField("ipv6-dst"),
                    "an address with no length means /128 on the way in (types.network6.fromstr), and "
                    + "RouterOS prints the /128 on the way out");
            }
        }

        [TestMethod]
        public void ThePrefixReadsBackAsAnAddressAndALengthRatherThanNotAtAll()
        {
            string id;
            using (var api = OpenSideApi())
            {
                id = CreateTemplate(api);
                SetField(api, id, "ipv6-src", Prefix);
                SetField(api, id, "ipv6-dst", FullLength);
            }

            var row = Row(Connection);
            Assert.AreEqual(Prefix, row.GetResponseField("ipv6-src"));
            Assert.AreEqual(FullLength, row.GetResponseField("ipv6-dst"),
                "the sibling array holds the prefix LENGTH, and it is rendered at 128 too");
        }
    }
}
