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
            Connection.Save(community);

            var loaded = Connection.LoadById<SnmpCommunity>(community.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}
