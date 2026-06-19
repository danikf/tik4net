using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiProvisioningTest : TestBase
    {
        [TestMethod]
        public void ListWifiProvisioningsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/provisioning");
            var list = Connection.LoadAll<WifiProvisioning>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiProvisioningWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/provisioning");
            string marker = Guid.NewGuid().ToString();
            // action=none requires no master-configuration and succeeds unconditionally.
            var rule = new WifiProvisioning
            {
                Action = WifiProvisioning.WifiProvisioningAction.None,
                Comment = marker,
            };
            Connection.Save(rule);

            try
            {
                var loaded = Connection.LoadById<WifiProvisioning>(rule.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(WifiProvisioning.WifiProvisioningAction.None, loaded.Action);
            }
            finally
            {
                Connection.Delete(rule);
            }
        }
    }
}
