using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Upnp;

namespace tik4net.tests
{
    [TestClass]
    public class IpUpnpTest : TestBase
    {
        #region IpUpnp (singleton)

        [TestMethod]
        public void LoadIpUpnpWillNotFail()
        {
            EnsureCommandAvailable("/ip/upnp");
            var upnp = Connection.LoadSingle<IpUpnp>();
            Assert.IsNotNull(upnp);
        }

        #endregion

        #region IpUpnpInterface

        [TestMethod]
        public void ListIpUpnpInterfacesWillNotFail()
        {
            EnsureCommandAvailable("/ip/upnp/interfaces");
            var list = Connection.LoadAll<IpUpnpInterface>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpUpnpInterfaceWillNotFail()
        {
            EnsureCommandAvailable("/ip/upnp/interfaces");
            var entry = new IpUpnpInterface
            {
                Interface = "ether1",
                Type = UpnpInterfaceType.External,
            };
            Connection.Save(entry);

            var loaded = Connection.LoadById<IpUpnpInterface>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(UpnpInterfaceType.External, loaded.Type);

            Connection.Delete(loaded);
        }

        #endregion
    }
}
