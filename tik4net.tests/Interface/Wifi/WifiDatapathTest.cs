using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiDatapathTest : TestBase
    {
        [TestMethod]
        public void ListWifiDatapathsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/datapath");
            var list = Connection.LoadAll<WifiDatapath>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiDatapathWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/datapath");
            string marker = Guid.NewGuid().ToString();
            var datapath = new WifiDatapath
            {
                Name = "test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(datapath);

            try
            {
                var loaded = Connection.LoadById<WifiDatapath>(datapath.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(datapath);
            }
        }
    }
}
