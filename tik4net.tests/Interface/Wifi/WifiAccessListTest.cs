using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiAccessListTest : TestBase
    {
        [TestMethod]
        public void ListWifiAccessListsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/access-list");
            var list = Connection.LoadAll<WifiAccessList>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiAccessListWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/access-list");
            string marker = Guid.NewGuid().ToString();
            // action=accept is the router default; no mandatory fields beyond that.
            var rule = new WifiAccessList
            {
                Action = WifiAccessList.WifiAccessListAction.Accept,
                Comment = marker,
            };
            Connection.Save(rule);

            try
            {
                var loaded = Connection.LoadById<WifiAccessList>(rule.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(WifiAccessList.WifiAccessListAction.Accept, loaded.Action);
            }
            finally
            {
                Connection.Delete(rule);
            }
        }
    }
}
