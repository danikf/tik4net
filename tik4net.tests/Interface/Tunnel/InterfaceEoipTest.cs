using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Tunnel;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceEoipTest : TestBase
    {
        [TestMethod]
        public void ListEoipsWillNotFail()
        {
            EnsureCommandAvailable("/interface/eoip");
            var list = Connection.LoadAll<InterfaceEoip>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddEoipWillNotFail()
        {
            EnsureCommandAvailable("/interface/eoip");
            string marker = Guid.NewGuid().ToString();
            var eoip = new InterfaceEoip
            {
                Name = "test-eoip",
                RemoteAddress = "192.0.2.1",
                TunnelId = 42,
                Comment = marker,
            };
            Connection.Save(eoip);

            var loaded = Connection.LoadById<InterfaceEoip>(eoip.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(42, loaded.TunnelId);

            Connection.Delete(loaded);
        }
    }
}
