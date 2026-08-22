// SnifferFilterListTest.cs — a list element that carries its own negation flag.
//
// /tool/sniffer's filters are the live shape of a `not`-WRAPPED message-array element: the element is a
// submessage holding a bool beside the value, and the API renders it as a '!' in front of that ONE entry
// (filter-ip-address="!192.168.251.0/24,10.0.0.1/32"). Both directions were wrong before:
//
//   read    the wrapper had no addressable parts in the catalog, so an element fell back to a generic dump
//           of its submessage and the whole field read as "true,false" — the negation FLAGS, in place of
//           the addresses.
//   write   nothing about the element could be encoded, so the field was refused outright.
//
// The window is also where a label collision costs a field: the sniffer has a streaming 'Port' and a
// filter 'Port', and first-wins left the second reachable under no name at all.
//
// Every assertion is made through a SIDE API CONNECTION as well as through the transport under test: a
// native write verified only by a native read would pass on a router that stored nothing.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Tool
{
    [TestClass]
    public class SnifferFilterListTest : TestBase
    {
        // Addresses and a MAC nothing in this lab routes to.
        private const string TestSubnet = "192.168.251.0/24";
        private const string TestMac = "AA:BB:CC:DD:EE:FF";

        private static readonly string[] FilterFields =
        {
            "filter-ip-address", "filter-mac-address", "filter-port", "filter-ipv6-address",
        };

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        [TestCleanup]
        public void ClearFilters()
        {
            using (var api = OpenSideApi())
                foreach (var field in FilterFields)
                    api.CreateCommandAndParameters("/tool/sniffer/set", field, "").ExecuteNonQuery();
        }

        private void SetOnTransportUnderTest(string field, string value)
            => Connection.CreateCommandAndParameters("/tool/sniffer/set", field, value).ExecuteNonQuery();

        private static string Read(ITikConnection conn, string field)
            => conn.CreateCommand("/tool/sniffer/print").ExecuteSingleRow().GetResponseFieldOrDefault(field, "");

        [TestMethod]
        public void ANegatedElementIsWrittenWithItsFlagAndNotItsValue()
        {
            SetOnTransportUnderTest("filter-ip-address", "!" + TestSubnet + ",10.0.0.1/32");

            using (var api = OpenSideApi())
                Assert.AreEqual("!" + TestSubnet + ",10.0.0.1/32", Read(api, "filter-ip-address"),
                    "as the API reads it after the write");
        }

        [TestMethod]
        public void ANegatedElementReadsWithItsPrefix()
        {
            using (var api = OpenSideApi())
                api.CreateCommandAndParameters("/tool/sniffer/set",
                    "filter-ip-address", "!" + TestSubnet + ",10.0.0.1/32").ExecuteNonQuery();

            Assert.AreEqual("!" + TestSubnet + ",10.0.0.1/32", Read(Connection, "filter-ip-address"),
                "the addresses, not the negation flags");
        }

        [TestMethod]
        public void AMacNetworkElementCarriesItsMask()
        {
            // The element is a `macnetwork`: six address bytes and six mask bytes. A bare MAC means the
            // all-ones mask, which RouterOS spells out on the way back even though WinBox hides it.
            SetOnTransportUnderTest("filter-mac-address", "!" + TestMac);

            using (var api = OpenSideApi())
                Assert.AreEqual("!" + TestMac + "/FF:FF:FF:FF:FF:FF", Read(api, "filter-mac-address"),
                    "as the API reads it after the write");
        }

        [TestMethod]
        public void TheFilterPortIsReachableDespiteSharingItsLabelWithTheStreamingPort()
        {
            // Both are labelled 'Port' in the window; the filter one lives under the Filter tab, which is
            // how RouterOS spells it too. The API prints a well-known port by NAME.
            SetOnTransportUnderTest("filter-port", "80,!443");

            using (var api = OpenSideApi())
                Assert.AreEqual("http,!https", Read(api, "filter-port"),
                    "as the API reads it after the write");
        }

        [TestMethod]
        public void AFilterIsClearedByWritingItEmpty()
        {
            using (var api = OpenSideApi())
                api.CreateCommandAndParameters("/tool/sniffer/set",
                    "filter-ip-address", TestSubnet).ExecuteNonQuery();

            SetOnTransportUnderTest("filter-ip-address", "");

            using (var api = OpenSideApi())
                Assert.AreEqual("", Read(api, "filter-ip-address"), "as the API reads it after the write");
        }
    }
}
