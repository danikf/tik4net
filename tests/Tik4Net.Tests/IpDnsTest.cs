using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tik4Net.Objects.Ip;
using Tik4Net.Objects;

namespace Tik4Net.Tests
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
