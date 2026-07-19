using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    [TestClass]
    public class FirewallRawTest : TestBase
    {
        [TestMethod]
        public void ListFirewallRawsWillNotFail()
        {
            EnsureCommandAvailable("/ip/firewall/raw");
            var list = Connection.LoadAll<FirewallRaw>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddFirewallRawWillNotFail()
        {
            EnsureCommandAvailable("/ip/firewall/raw");
            string marker = Guid.NewGuid().ToString();
            var entity = new FirewallRaw
            {
                Chain = FirewallRaw.ChainType.Prerouting,
                Action = FirewallRaw.ActionType.Accept,
                Comment = marker,
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<FirewallRaw>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}
