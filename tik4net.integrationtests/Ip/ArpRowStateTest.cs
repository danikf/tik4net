// ArpRowStateTest.cs — the row-state flags the router computes and the catalog never names.
//
// `dynamic` is the field that says whether a row was learned by the router or configured by hand, and no
// .jg window declares it on any handler — so over WinBox native it reached no caller at all, on every table
// that has one. /ip/arp is where the difference is visible in a single table: an entry added by hand sits
// beside entries the router learned, and only that flag separates them.
//
// The test asserts the two transports AGREE, rather than asserting a fixed value: which rows a lab router
// has learned is not ours to decide, and a test that pinned it would measure the lab.

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests.Ip
{
    [TestClass]
    public class ArpRowStateTest : TestBase
    {
        private const string ProbeComment = "tik4net-test-arp-state";

        // An address and MAC nothing in this lab answers for, so the entry stays exactly as configured.
        private const string ProbeAddress = "192.168.251.77";
        private const string ProbeMac = "AA:BB:CC:DD:EE:FF";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateProbeEntry()
        {
            using (var api = OpenSideApi())
            {
                RemoveProbeEntry(api);
                api.CreateCommandAndParameters("/ip/arp/add",
                    "address", ProbeAddress, "mac-address", ProbeMac,
                    "interface", TestConstants.Interface, "comment", ProbeComment).ExecuteNonQuery();
            }
        }

        [TestCleanup]
        public void DeleteProbeEntry()
        {
            using (var api = OpenSideApi()) RemoveProbeEntry(api);
        }

        private static void RemoveProbeEntry(ITikConnection conn)
        {
            foreach (var row in conn.CreateCommand("/ip/arp/print").ExecuteList()
                         .Where(r => r.GetResponseFieldOrDefault("comment", "") == ProbeComment).ToList())
                conn.CreateCommandAndParameters("/ip/arp/remove",
                    TikSpecialProperties.Id, row.GetResponseField(TikSpecialProperties.Id)).ExecuteNonQuery();
        }

        private static ITikReSentence ProbeRow(ITikConnection conn)
            => conn.CreateCommand("/ip/arp/print").ExecuteList()
                   .Single(r => r.GetResponseFieldOrDefault("comment", "") == ProbeComment);

        [TestMethod]
        public void AConfiguredRowIsReportedAsNotDynamic()
        {
            Assert.AreEqual("false", ProbeRow(Connection).GetResponseField("dynamic"));
        }

        [TestMethod]
        public void EveryRowsDynamicFlagAgreesWithTheApi()
        {
            // Both transports, every row, paired by .id — the flag is only worth reporting if it says the
            // same thing the API says about the same row.
            using (var api = OpenSideApi())
            {
                var apiRows = api.CreateCommand("/ip/arp/print").ExecuteList()
                    .ToDictionary(r => r.GetResponseField(TikSpecialProperties.Id),
                                  r => r.GetResponseFieldOrDefault("dynamic", ""));

                foreach (var row in Connection.CreateCommand("/ip/arp/print").ExecuteList())
                {
                    string id = row.GetResponseField(TikSpecialProperties.Id);
                    if (!apiRows.TryGetValue(id, out string expected)) continue;   // learned/aged between reads
                    Assert.AreEqual(expected, row.GetResponseFieldOrDefault("dynamic", ""),
                        "dynamic on row " + id);
                }
            }
        }

        [TestMethod]
        public void ARowStateFlagCannotBeWritten()
        {
            // The router computes it, and the API refuses the write itself ("unknown parameter dynamic").
            // What matters is that EVERY transport refuses rather than sending something the router accepts
            // and ignores - so the assertion is on the common base: the API traps, WinBox native declines to
            // resolve the name at all, and both are a TikConnectionException.
            string id = ProbeRow(Connection).GetResponseField(TikSpecialProperties.Id);

            // Caught rather than Assert.ThrowsException: that helper matches the EXACT type, and the two
            // refusals are different subclasses of one base by design.
            try
            {
                Connection.CreateCommandAndParameters("/ip/arp/set",
                    "dynamic", "true", TikSpecialProperties.Id, id).ExecuteNonQuery();
                Assert.Fail("the write should have been refused");
            }
            catch (TikConnectionException)
            {
            }
            Assert.AreEqual("false", ProbeRow(Connection).GetResponseField("dynamic"),
                "and the row is unchanged");
        }
    }
}
