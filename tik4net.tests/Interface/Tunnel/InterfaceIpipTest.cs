using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Tunnel;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceIpipTest : TestBase
    {
        [TestMethod]
        public void ListIpipsWillNotFail()
        {
            EnsureCommandAvailable("/interface/ipip");
            var list = Connection.LoadAll<InterfaceIpip>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpipWillNotFail()
        {
            EnsureCommandAvailable("/interface/ipip");
            string marker = Guid.NewGuid().ToString();
            var ipip = new InterfaceIpip
            {
                Name = "test-ipip",
                RemoteAddress = "192.0.2.1",
                Comment = marker,
            };
            Connection.Save(ipip);

            var loaded = Connection.LoadById<InterfaceIpip>(ipip.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual("192.0.2.1", loaded.RemoteAddress);

            Connection.Delete(loaded);
        }
    }
}
