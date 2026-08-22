// DnsForwarderListFieldsTest.cs — a list-valued field must be writable, not only readable.
//
// /ip/dns/forwarders carries two of the list shapes on one row:
//   dns-servers  a list of ADDRESSES — on the wire a message array whose elements are `addr` compounds.
//   doh-servers  a list of STRINGS — on the wire a string array (2-byte count, 2-byte length per element).
//
// Both could be read over WinBox native and neither could be written: every array wire type was refused
// as "not yet encodable". The refusal was loud, so this is not the u64 kind of silent drop — but a field
// the router settles happily over the API had no way through the native transport at all.
//
// The row is created and read back over a SIDE API CONNECTION. A native write verified by a native read
// would pass on a router that stored nothing.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Ip.Dns
{
    [TestClass]
    public class DnsForwarderListFieldsTest : TestBase
    {
        private const string ForwarderName = "tik4net-test-forwarder";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateTestForwarder()
        {
            using (var api = OpenSideApi())
            {
                RemoveTestForwarder(api);
                api.CreateCommandAndParameters("/ip/dns/forwarders/add",
                    "name", ForwarderName,
                    "dns-servers", "8.8.8.8,1.1.1.1",
                    "doh-servers", "https://dns.example/dns-query").ExecuteNonQuery();
            }
        }

        [TestCleanup]
        public void DeleteTestForwarder()
        {
            using (var api = OpenSideApi())
                RemoveTestForwarder(api);
        }

        private static void RemoveTestForwarder(ITikConnection conn)
        {
            foreach (var row in conn.CreateCommand("/ip/dns/forwarders/print").ExecuteList()
                         .Where(r => r.GetResponseFieldOrDefault("name", "") == ForwarderName).ToList())
                conn.CreateCommandAndParameters("/ip/dns/forwarders/remove",
                    TikSpecialProperties.Id, row.GetResponseField(TikSpecialProperties.Id)).ExecuteNonQuery();
        }

        private static ITikReSentence TestForwarder(ITikConnection conn)
            => conn.CreateCommand("/ip/dns/forwarders/print").ExecuteList()
                   .Single(r => r.GetResponseFieldOrDefault("name", "") == ForwarderName);

        private void SetOnTransportUnderTest(string field, string value)
        {
            string id = TestForwarder(Connection).GetResponseField(TikSpecialProperties.Id);
            Connection.CreateCommandAndParameters("/ip/dns/forwarders/set",
                field, value, TikSpecialProperties.Id, id).ExecuteNonQuery();
        }

        [TestMethod]
        public void AListOfAddressesReadsAsTheApiSpellsIt()
        {
            Assert.AreEqual("8.8.8.8,1.1.1.1",
                TestForwarder(Connection).GetResponseField("dns-servers"));
        }

        [TestMethod]
        public void AListOfAddressesWrittenOverTheTransportUnderTestReachesTheRouter()
        {
            SetOnTransportUnderTest("dns-servers", "9.9.9.9,8.8.4.4");

            using (var api = OpenSideApi())
                Assert.AreEqual("9.9.9.9,8.8.4.4", TestForwarder(api).GetResponseField("dns-servers"),
                    "as the API reads it after the write");
        }

        [TestMethod]
        public void AListOfStringsWrittenOverTheTransportUnderTestReachesTheRouter()
        {
            SetOnTransportUnderTest("doh-servers", "https://one.example/dns-query,https://two.example/dns-query");

            using (var api = OpenSideApi())
                Assert.AreEqual("https://one.example/dns-query,https://two.example/dns-query",
                    TestForwarder(api).GetResponseField("doh-servers"),
                    "as the API reads it after the write");
        }

        [TestMethod]
        public void AListIsClearedByWritingItEmpty()
        {
            // The empty list is a value, not a missing field: a key the router is not told about keeps what
            // it already holds, so a clear that sends nothing reports success and changes nothing.
            SetOnTransportUnderTest("doh-servers", "");

            using (var api = OpenSideApi())
                Assert.AreEqual("", TestForwarder(api).GetResponseFieldOrDefault("doh-servers", ""),
                    "as the API reads it after the write");
        }
    }
}
