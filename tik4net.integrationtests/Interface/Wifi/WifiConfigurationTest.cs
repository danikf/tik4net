using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WifiConfigurationTest : TestBase
    {
        [TestMethod]
        public void ListWifiConfigurationsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/configuration");
            var list = Connection.LoadAll<WifiConfiguration>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiConfigurationWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/configuration");
            string marker = Guid.NewGuid().ToString();
            var config = new WifiConfiguration
            {
                Name = "test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(config);

            try
            {
                var loaded = Connection.LoadById<WifiConfiguration>(config.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(config);
            }
        }
    }
}
