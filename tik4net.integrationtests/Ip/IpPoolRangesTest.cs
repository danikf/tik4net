// IpPoolRangesTest.cs — a list of address PAIRS.
//
// /ip/pool's `ranges` is a webfig `multinetwork`, which is a list of (address, sibling) pairs. This one
// has no `maskid`, so the pairs are flattened into a SINGLE u32 array — [lo0,hi0,lo1,hi1,…] — and the
// element declares range:1, so the second half of each pair is the range END rather than a netmask.
//
// Neither half of that was handled: the field read as the raw array "[184264896,352037056,…]" and no write
// was possible at all. The API's own text is the reference — it prints a bare address for a one-address
// range and re-collapses an exactly-aligned range to its prefix form, which is why the third element goes
// in as a /24 and comes back as one.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Ip
{
    [TestClass]
    public class IpPoolRangesTest : TestBase
    {
        private const string PoolName = "tik4net-test-pool";

        // Subnets nothing in this lab routes through.
        private const string TestRanges = "192.168.251.10-192.168.251.20,192.168.252.5,192.168.253.0/24";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        [TestCleanup]
        public void RemoveTestPool()
        {
            using (var api = OpenSideApi())
                foreach (var row in api.CreateCommand("/ip/pool/print").ExecuteList()
                             .Where(r => r.GetResponseFieldOrDefault("name", "") == PoolName).ToList())
                    api.CreateCommandAndParameters("/ip/pool/remove",
                        TikSpecialProperties.Id, row.GetResponseField(TikSpecialProperties.Id)).ExecuteNonQuery();
        }

        private static string ReadRanges(ITikConnection conn)
            => conn.CreateCommand("/ip/pool/print").ExecuteList()
                   .Single(r => r.GetResponseFieldOrDefault("name", "") == PoolName)
                   .GetResponseField("ranges");

        [TestMethod]
        public void APoolIsCreatedWithItsRangesOverTheTransportUnderTest()
        {
            Connection.CreateCommandAndParameters("/ip/pool/add",
                "name", PoolName, "ranges", TestRanges).ExecuteNonQuery();

            using (var api = OpenSideApi())
                Assert.AreEqual(TestRanges, ReadRanges(api), "as the API reads it after the write");
        }

        [TestMethod]
        public void TheRangesReadBackAsTheApiSpellsThem()
        {
            using (var api = OpenSideApi())
                api.CreateCommandAndParameters("/ip/pool/add",
                    "name", PoolName, "ranges", TestRanges).ExecuteNonQuery();

            Assert.AreEqual(TestRanges, ReadRanges(Connection),
                "each pair as one range, not as two raw u32s");
        }

        [TestMethod]
        public void ASingleAddressIsTheRangeThatStartsAndEndsOnIt()
        {
            Connection.CreateCommandAndParameters("/ip/pool/add",
                "name", PoolName, "ranges", "192.168.251.7").ExecuteNonQuery();

            using (var api = OpenSideApi())
                Assert.AreEqual("192.168.251.7", ReadRanges(api), "as the API reads it after the write");
        }
    }
}
