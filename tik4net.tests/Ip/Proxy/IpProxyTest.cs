using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Proxy;

namespace tik4net.tests
{
    [TestClass]
    public class IpProxyTest : TestBase
    {
        #region IpProxy (singleton)

        [TestMethod]
        public void LoadIpProxyWillNotFail()
        {
            EnsureCommandAvailable("/ip/proxy");
            var proxy = Connection.LoadSingle<IpProxy>();
            Assert.IsNotNull(proxy);
        }

        #endregion

        #region IpProxyAccess

        [TestMethod]
        public void ListIpProxyAccessWillNotFail()
        {
            EnsureCommandAvailable("/ip/proxy/access");
            var list = Connection.LoadAll<IpProxyAccess>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpProxyAccessWillNotFail()
        {
            EnsureCommandAvailable("/ip/proxy/access");
            string marker = Guid.NewGuid().ToString();
            var rule = new IpProxyAccess
            {
                Action = ProxyAccessAction.Deny,
                DstHost = "blocked.example.com",
                Comment = marker,
            };
            Connection.Save(rule);

            var loaded = Connection.LoadById<IpProxyAccess>(rule.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(ProxyAccessAction.Deny, loaded.Action);

            Connection.Delete(loaded);
        }

        #endregion
    }
}
