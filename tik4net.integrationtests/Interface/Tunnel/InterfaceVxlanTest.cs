using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Tunnel;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceVxlanTest : TestBase
    {
        [TestMethod]
        public void ListVxlansWillNotFail()
        {
            EnsureCommandAvailable("/interface/vxlan");
            var list = Connection.LoadAll<InterfaceVxlan>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddVxlanWillNotFail()
        {
            EnsureCommandAvailable("/interface/vxlan");
            string marker = Guid.NewGuid().ToString();
            var vxlan = new InterfaceVxlan
            {
                Name = "test-vxlan",
                Vni = 100,
                Comment = marker,
            };
            Connection.Save(vxlan);

            var loaded = Connection.LoadById<InterfaceVxlan>(vxlan.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(100, loaded.Vni);

            Connection.Delete(loaded);
        }
    }
}
