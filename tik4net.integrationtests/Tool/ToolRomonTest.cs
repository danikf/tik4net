using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool.Romon;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolRomonTest : TestBase
    {
        [TestMethod]
        public void LoadRomonSettingsWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon");
            var settings = Connection.LoadSingle<ToolRomon>();
            Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void ListRomonPortsWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon/port");
            var list = Connection.LoadAll<ToolRomonPort>();
            Assert.IsNotNull(list);
        }

        // discover and ping are refused with "RoMON not running" while RoMON is off, and turning it on is a
        // change to the lab router's L2 behaviour that a test has no business making behind the operator's back.
        // The guard probes the settings menu: EnsureCommandAvailable reads '<path>/print', which an action path
        // does not have, and each transport refuses that differently ("no such command" on the API, "bad
        // parameter print" on the CLI, 500 on REST) — none of it about whether discover/ping exist.
        private void EnsureRomonRunning()
        {
            EnsureCommandAvailable("/tool/romon");
            if (Connection.LoadSingle<ToolRomon>().Enabled != true)
                Assert.Inconclusive("RoMON is not enabled on the test router (/tool/romon set enabled=yes).");
        }

        [TestMethod]
        public void RomonDiscoverWillNotFail()
        {
            EnsureRomonRunning();
            var neighbours = Connection.RomonDiscover(2).ToList();
            Assert.IsNotNull(neighbours);
            // Each neighbour once, whatever the transport's repeat-per-refresh habit.
            Assert.AreEqual(neighbours.Count, neighbours.Select(n => n.Address).Distinct().Count());
            foreach (var n in neighbours)
            {
                Assert.IsFalse(string.IsNullOrEmpty(n.Address), "a neighbour without its RoMON id");
                Assert.IsTrue(n.Hops >= 1, "hops=" + n.Hops);
            }
        }

        [TestMethod]
        public void RomonPingToUnknownIdTimesOut()
        {
            EnsureRomonRunning();
            var rows = Connection.RomonPing("AA:BB:CC:DD:EE:FF", 1).ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("timeout", rows[0].Status);
            Assert.IsNull(rows[0].Time);
        }

        [TestMethod]
        public void RomonPingToNeighbourAnswers()
        {
            EnsureRomonRunning();
            var neighbour = Connection.RomonDiscover(2).FirstOrDefault();
            if (neighbour == null)
                Assert.Inconclusive("The test router has no RoMON neighbour — the lab needs a second RoMON-enabled router on its segment.");

            var rows = Connection.RomonPing(neighbour.Address, 2).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.Any(r => r.Time != null), "no echo was answered: " + string.Join(" | ", rows));
            Assert.IsTrue(rows.All(r => string.Equals(r.Host, neighbour.Address, StringComparison.OrdinalIgnoreCase)),
                "hosts: " + string.Join(", ", rows.Select(r => r.Host)));
        }

        [TestMethod]
        public void AddRomonPortWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon/port");
            string marker = Guid.NewGuid().ToString().Substring(0, 8);
            // The default "all" entry already exists → use a specific interface.
            var entry = new ToolRomonPort
            {
                Interface = "ether1",
                Forbid = false,
                Comment = marker,
            };
            SaveTracked(entry);

            var loaded = Connection.LoadById<ToolRomonPort>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}
