using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceVrrpTest : TestBase
    {
        [TestMethod]
        public void ListVrrpsWillNotFail()
        {
            EnsureCommandAvailable("/interface/vrrp");
            var list = Connection.LoadAll<InterfaceVrrp>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddVrrpWillNotFail()
        {
            EnsureCommandAvailable("/interface/vrrp");
            string marker = Guid.NewGuid().ToString();
            var vrrp = new InterfaceVrrp
            {
                Name = "test-vrrp",
                Interface = "ether1",
                Vrid = 99,
                Comment = marker,
            };
            Connection.Save(vrrp);

            var loaded = Connection.LoadById<InterfaceVrrp>(vrrp.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(99, loaded.Vrid);

            Connection.Delete(loaded);
        }
    }
}
