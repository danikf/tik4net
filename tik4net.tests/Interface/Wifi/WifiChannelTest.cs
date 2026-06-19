using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiChannelTest : TestBase
    {
        [TestMethod]
        public void ListWifiChannelsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/channel");
            var list = Connection.LoadAll<WifiChannel>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddWifiChannelWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/channel");
            string marker = Guid.NewGuid().ToString();
            var channel = new WifiChannel
            {
                Name = "test-" + marker.Substring(0, 8),
                Comment = marker,
            };
            Connection.Save(channel);

            try
            {
                var loaded = Connection.LoadById<WifiChannel>(channel.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
            }
            finally
            {
                Connection.Delete(channel);
            }
        }
    }
}
