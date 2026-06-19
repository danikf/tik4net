using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Hotspot;

namespace tik4net.tests
{
    [TestClass]
    public class HotspotServerTest : TestBase
    {
        #region HotspotServer

        [TestMethod]
        public void ListHotspotServersWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot");
            var list = Connection.LoadAll<HotspotServer>();
            Assert.IsNotNull(list);
        }

        #endregion

        #region HotspotServerProfile

        [TestMethod]
        public void ListHotspotServerProfilesWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/profile");
            var list = Connection.LoadAll<HotspotServerProfile>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddHotspotServerProfileWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/profile");
            string marker = Guid.NewGuid().ToString("N").Substring(0, 12);
            var profile = new HotspotServerProfile
            {
                Name = "TEST_" + marker,
            };
            Connection.Save(profile);

            var loaded = Connection.LoadById<HotspotServerProfile>(profile.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(profile.Name, loaded.Name);

            Connection.Delete(loaded);
        }

        #endregion

        #region HotspotWalledGarden

        [TestMethod]
        public void ListHotspotWalledGardenWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/walled-garden");
            var list = Connection.LoadAll<HotspotWalledGarden>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddHotspotWalledGardenWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/walled-garden");
            string marker = Guid.NewGuid().ToString();
            var rule = new HotspotWalledGarden
            {
                DstHost = "*.example.com",
                Action = WalledGardenAction.Allow,
                Comment = marker,
            };
            Connection.Save(rule);

            var loaded = Connection.LoadById<HotspotWalledGarden>(rule.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }

        #endregion

        #region HotspotWalledGardenIp

        [TestMethod]
        public void ListHotspotWalledGardenIpWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/walled-garden/ip");
            var list = Connection.LoadAll<HotspotWalledGardenIp>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddHotspotWalledGardenIpWillNotFail()
        {
            EnsureCommandAvailable("/ip/hotspot/walled-garden/ip");
            string marker = Guid.NewGuid().ToString();
            var rule = new HotspotWalledGardenIp
            {
                DstAddress = "8.8.8.8",
                Protocol = "udp",
                DstPort = "53",
                Action = WalledGardenIpAction.Accept,
                Comment = marker,
            };
            Connection.Save(rule);

            var loaded = Connection.LoadById<HotspotWalledGardenIp>(rule.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }

        #endregion
    }
}
