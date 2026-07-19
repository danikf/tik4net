using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    [TestClass]
    public class FirewallLayer7ProtocolTest : TestBase
    {
        [TestMethod]
        public void ListFirewallLayer7ProtocolsWillNotFail()
        {
            EnsureCommandAvailable("/ip/firewall/layer7-protocol");
            var list = Connection.LoadAll<FirewallLayer7Protocol>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddFirewallLayer7ProtocolWillNotFail()
        {
            EnsureCommandAvailable("/ip/firewall/layer7-protocol");
            string marker = Guid.NewGuid().ToString();
            var entity = new FirewallLayer7Protocol
            {
                Name = marker,
                Regexp = ".*",
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<FirewallLayer7Protocol>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);
            Assert.AreEqual(".*", loaded.Regexp);

            Connection.Delete(loaded);
        }
    }
}
