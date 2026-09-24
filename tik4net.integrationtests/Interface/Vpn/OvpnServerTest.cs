using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.integrationtests
{
    [TestClass]
    public class OvpnServerTest : TestBase
    {
        [TestMethod]
        public void LoadOvpnServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/ovpn-server/server");
            var list = Connection.LoadAll<OvpnServer>();
            Assert.IsNotNull(list);
        }

        // A list of named servers: add one, change it through the row's .id, delete it. Created disabled, on a
        // port nothing listens on. RouterOS 6 (and 7 before the menu became a list) has one unnamed server here
        // and no 'add'; that refusal is the router's, so it is Inconclusive rather than a failure.
        [TestMethod]
        public void AddUpdateDeleteOvpnServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/ovpn-server/server");
            string name = "t4n-ovpn-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var server = new OvpnServer { Name = name, Port = 11940, Disabled = true };
            try
            {
                SaveTracked(server);
            }
            catch (TikNoSuchCommandException ex)
            {
                Assert.Inconclusive("/interface/ovpn-server/server has no 'add' on this RouterOS (a single server): " + ex.Message);
            }

            var loaded = Connection.LoadAll<OvpnServer>().Single(s => s.Name == name);
            Assert.IsFalse(string.IsNullOrEmpty(loaded.Id));
            Assert.AreEqual(11940, loaded.Port);

            loaded.Port = 11941;
            Connection.Save(loaded);
            Assert.AreEqual(11941, Connection.LoadAll<OvpnServer>().Single(s => s.Name == name).Port);

            Connection.Delete(loaded);
            Assert.IsFalse(Connection.LoadAll<OvpnServer>().Any(s => s.Name == name));
        }
    }
}
