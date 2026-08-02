using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceBondingTest : TestBase
    {
        // Throwaway slave for the bond. A bonding interface cannot be created without at least one slave
        // ("failure: need at least one slave interface"), and the obvious candidate — TestConstants.Interface
        // — is the router's management port. Enslaving it makes the DHCP client sitting on it log
        // "events on master port will be handled by slave ether1, update your config!!! (IPv4)" at ERROR
        // severity on every run of every transport (verified live on 7.23.2: topics dhcp,error), which is
        // alarming for anyone who opens /log afterwards, and briefly puts the connection the suite itself is
        // running over into a bond. A veth has no such passenger: bonding one logs nothing beyond the normal
        // "device added" line.
        private const string SlaveName = "test-bond-slave";

        [TestMethod]
        public void ListBondingsWillNotFail()
        {
            EnsureCommandAvailable("/interface/bonding");
            var list = Connection.LoadAll<InterfaceBonding>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddBondingWillNotFail()
        {
            EnsureCommandAvailable("/interface/bonding");
            string slave = CreateThrowawaySlave();
            try
            {
                string marker = Guid.NewGuid().ToString();
                var bonding = new InterfaceBonding
                {
                    Name = "test-bond",
                    Slaves = slave,
                    Comment = marker,
                };
                SaveTracked(bonding);

                var loaded = Connection.LoadById<InterfaceBonding>(bonding.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual("test-bond", loaded.Name);

                Connection.Delete(loaded);
            }
            finally
            {
                RemoveThrowawaySlave();
            }
        }

        /// <summary>
        /// Creates the disposable veth used as the bond's slave and returns its name, falling back to
        /// <see cref="TestConstants.Interface"/> on a router without the <c>/interface/veth</c> menu.
        /// </summary>
        /// <remarks>
        /// <c>/interface/veth</c> comes with the <c>container</c> package, which the test router has (see the
        /// <c>chr-test-router-init</c> skill). The fallback keeps the test running — not skipped — on a router
        /// that lacks it; there it is the old behaviour, log noise included.
        /// </remarks>
        private string CreateThrowawaySlave()
        {
            RemoveThrowawaySlave();   // residue from a killed run would fail the add on a duplicate name
            try
            {
                Connection.CreateCommandAndParameters("/interface/veth/add", "name", SlaveName).ExecuteNonQuery();
            }
            catch (Exception)
            {
                return TestConstants.Interface;
            }
            return SlaveName;
        }

        private void RemoveThrowawaySlave()
        {
            try
            {
                foreach (var row in Connection.CreateCommandAndParameters("/interface/veth/print", "name", SlaveName).ExecuteList())
                    Connection.CreateCommandAndParameters("/interface/veth/remove", TikSpecialProperties.Id, row.GetId()).ExecuteNonQuery();
            }
            catch (Exception)
            {
                // No veth menu, or nothing to remove — the bond itself is cleaned up by SaveTracked either way.
            }
        }
    }
}
