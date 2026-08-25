// TabPrefixedFieldNamesTest.cs — fields RouterOS names after the WinBox tab they sit on.
//
// The .jg puts three interface fields on a tab called 'Loop Protect' and labels them 'Send Interval',
// 'Disable Time' and 'Status'; RouterOS calls them loop-protect-send-interval, loop-protect-disable-time
// and loop-protect-status. The bridge does the same with an 'MLAG' tab: 'Peer Port', 'Priority' and
// 'Heartbeat' are mlag-peer-port, mlag-priority and mlag-heartbeat.
//
// Nothing in the catalog says which tabs do this — on the bridge window MLAG and STP are both a bare
// {name:'…',type:'tab'}, and STP's fields are NOT prefixed (protocol-mode, priority). So the list is
// shipped (WinboxFieldResolver.TabPrefixed) and read off the router with tab completion, and this test is
// what holds it to the router's answer.
//
// Read against a side API connection rather than against literals: the point is that the transports agree
// on the NAME, which is what the O/R mapper binds to.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class TabPrefixedFieldNamesTest : TestBase
    {
        private const string BridgeName = "tik4net-test-tabprefix-br";
        private const string VlanName = "tik4net-test-tabprefix-vlan";

        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        [TestInitialize]
        public void CreateFixtures()
        {
            using (var api = OpenSideApi())
            {
                RemoveFixtures(api);
                api.CreateCommandAndParameters("/interface/bridge/add",
                    TikCommandParameterFormat.NameValue, "name", BridgeName).ExecuteScalar();
                api.CreateCommandAndParameters("/interface/vlan/add",
                    TikCommandParameterFormat.NameValue,
                    "name", VlanName, "vlan-id", "993", "interface", "ether2").ExecuteScalar();
            }
        }

        [TestCleanup]
        public void DropFixtures()
        {
            using (var api = OpenSideApi()) RemoveFixtures(api);
        }

        /// <summary>The generic interface window's Loop Protect tab, read on a VLAN.</summary>
        [TestMethod]
        public void LoopProtectFieldsCarryTheTabPrefix()
        {
            AssertNamesAgree("/interface/vlan", "name", VlanName,
                "loop-protect-disable-time", "loop-protect-send-interval", "loop-protect-status");
        }

        /// <summary>
        /// The bridge's MLAG tab. <c>mlag-priority</c> is the interesting one: the window labels two
        /// different fields <c>Priority</c>, one under STP and one under MLAG, and without the prefix
        /// whichever was read first took the name.
        /// </summary>
        [TestMethod]
        public void MlagFieldsCarryTheTabPrefix()
        {
            AssertNamesAgree("/interface/bridge", "name", BridgeName,
                "mlag-heartbeat", "mlag-peer-port", "mlag-priority");
        }

        /// <summary>
        /// The collision the prefix settles, asserted as a PAIR on one row.
        /// </summary>
        /// <remarks>
        /// The bridge window labels two different fields <c>Priority</c> — one under STP, one under MLAG —
        /// and the router calls them <c>priority</c> (an STP bridge priority, <c>0x8000</c>) and
        /// <c>mlag-priority</c> (a small integer). Before the tab prefix the qualified name existed only
        /// because the two COLLIDED and the loser was tab-qualified, so which field held the plain name
        /// depended on read order. Asserting both on the same row is what would catch that coming back:
        /// either name alone can look right while the values are swapped.
        /// </remarks>
        [TestMethod]
        public void StpAndMlagPriorityAreDifferentFields()
        {
            AssertNamesAgree("/interface/bridge", "name", BridgeName, "priority", "mlag-priority");
        }

        /// <summary>
        /// The counter-example, and the reason the list is shipped rather than a rule: STP sits on a tab
        /// spelled exactly like MLAG in the catalog, and its fields are NOT prefixed.
        /// </summary>
        [TestMethod]
        public void StpFieldsDoNotCarryTheTabPrefix()
        {
            AssertNamesAgree("/interface/bridge", "name", BridgeName, "protocol-mode", "priority");
        }

        private void AssertNamesAgree(string path, string keyField, string keyValue,
                                      params string[] expectedNames)
        {
            using (var api = OpenSideApi())
            {
                var apiRow = Row(api, path, keyField, keyValue);
                Assert.IsNotNull(apiRow, "the API did not return " + path + " " + keyValue);

                var probeRow = Row(Connection, path, keyField, keyValue);
                Assert.IsNotNull(probeRow, ResolveConnectionType() + " did not return " + path + " " + keyValue);

                foreach (string name in expectedNames)
                {
                    string apiValue = apiRow.GetResponseFieldOrDefault(name, null);
                    Assert.IsNotNull(apiValue,
                        "the API does not report '" + name + "' on " + path
                        + " — the shipped list is describing a field this RouterOS does not have");

                    string probeValue = probeRow.GetResponseFieldOrDefault(name, null);
                    Assert.IsNotNull(probeValue,
                        ResolveConnectionType() + " does not report '" + name + "' on " + path
                        + ". It reports: " + string.Join(",", Names(probeRow).OrderBy(x => x)));
                    Assert.AreEqual(apiValue, probeValue, name + " on " + path);
                }
            }
        }

        private static IEnumerable<string> Names(ITikReSentence row)
            => row.Words.Select(w => w.Key);

        private static ITikReSentence Row(ITikConnection conn, string path, string keyField, string keyValue)
        {
            var cmd = conn.CreateCommandAndParameters(path + "/print",
                          TikCommandParameterFormat.Filter, keyField, keyValue);
            cmd.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            return cmd.ExecuteList().FirstOrDefault();
        }

        private static void RemoveFixtures(ITikConnection api)
        {
            Drop(api, "/interface/vlan", VlanName);
            Drop(api, "/interface/bridge", BridgeName);
        }

        private static void Drop(ITikConnection api, string path, string name)
        {
            foreach (var row in api.CreateCommandAndParameters(path + "/print",
                                    TikCommandParameterFormat.Filter, "name", name).ExecuteList().ToList())
            {
                try
                {
                    api.CreateCommandAndParameters(path + "/remove",
                        TikCommandParameterFormat.NameValue, ".id", row.GetId()).ExecuteNonQuery();
                }
                catch (TikCommandException) { }
            }
        }
    }
}
