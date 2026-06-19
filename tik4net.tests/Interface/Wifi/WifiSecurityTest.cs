using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiSecurityTest : TestBase
    {
        [TestMethod]
        public void ListWifiSecuritiesWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/security");
            var list = Connection.LoadAll<WifiSecurity>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiSecurityWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/security");
            string marker = Guid.NewGuid().ToString();
            var security = new WifiSecurity
            {
                Name = "test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(security);

            try
            {
                var loaded = Connection.LoadById<WifiSecurity>(security.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(security);
            }
        }
    }
}
