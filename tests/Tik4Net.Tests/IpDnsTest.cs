using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip;
using tik4net.Objects;

namespace tik4net.tests
{
    [TestClass]
    public class IpDnsTest : TestBase
    {
        [TestMethod]
        public void LoadIpDnsSettingsWillNotFail()
        {
            var list = Connection.LoadAll<IpDns>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void LoadIpDnsSettings2WillNotFail()
        {
            var dnsConfiguration = Connection.LoadSingle<IpDns>();
            Assert.IsNotNull(dnsConfiguration);
        }
    }
}
