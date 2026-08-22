using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Snmp;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SnmpCommunityTest : TestBase
    {
        [TestMethod]
        public void ListSnmpCommunitiesWillNotFail()
        {
            EnsureCommandAvailable("/snmp/community");
            var list = Connection.LoadAll<SnmpCommunity>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddSnmpCommunityWillNotFail()
        {
            EnsureCommandAvailable("/snmp/community");
            string marker = Guid.NewGuid().ToString();

            var community = new SnmpCommunity
            {
                Name = marker,
            };
            SaveTracked(community);

            var loaded = Connection.LoadById<SnmpCommunity>(community.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }

        /// <summary>
        /// A community scoped to ONE IPv6 host. <c>addresses</c> is a webfig <c>network6</c>, whose sibling
        /// holds the prefix LENGTH rather than a netmask — and RouterOS prints that length at every length,
        /// <c>/128</c> included, where WinBox's own <c>tostr</c> hides it and shows the bare address. Reading
        /// the GUI's rule as the API's dropped the suffix on every such row.
        /// </summary>
        [TestMethod]
        public void AnIpV6HostAddressKeepsItsPrefixLength()
        {
            EnsureCommandAvailable("/snmp/community");
            string marker = Guid.NewGuid().ToString();

            var community = new SnmpCommunity { Name = marker, Addresses = "2001:db8:3::7/128" };
            SaveTracked(community);

            var loaded = Connection.LoadById<SnmpCommunity>(community.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("2001:db8:3::7/128", loaded.Addresses,
                "the API spells out /128; only the GUI hides it");

            Connection.Delete(loaded);
        }
    }
}
