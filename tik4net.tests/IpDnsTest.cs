using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip;
using tik4net.Objects;
using tik4net.Objects.Ip.Dns;
using System.Linq;
using System.Diagnostics;

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

        [TestMethod]
        public void LoadStaticDnsWillNotFail()
        {
            var dnsConfiguration = Connection.LoadSingle<DnsStatic>();
            Assert.IsNotNull(dnsConfiguration);
        }

        [TestMethod]
        public void CreateStaticDnsWillNotFail()
        {
            //create item
            var dnsItem = new DnsStatic()
            {
                Address = "1.1.1.1",
                Name = Guid.NewGuid().ToString(),

            };
            Connection.Save(dnsItem);

            //Load all
            var items = Connection.LoadAll<DnsStatic>();

            //Asserts
            var item = items.SingleOrDefault(i => i.Name == dnsItem.Name);
            Assert.IsNotNull(item);
            Assert.AreEqual(dnsItem.Address, item.Address);            

            //cleanup
            Connection.Delete(item);
        }


        [TestMethod]       
        public void StaticDnsRecordWithRegexWillNotFail_Issue77()
        {
            //create item
            var dnsItem = new DnsStatic()
            {
                Address = "1.1.1.1",
                Regexp = "*.local",

            };
            Connection.Save(dnsItem);

            //Load all
            var items = Connection.LoadAll<DnsStatic>();

            //Asserts
            var item = items.SingleOrDefault(i => i.Address == dnsItem.Address);
            Assert.IsNotNull(item);
            Assert.AreEqual(dnsItem.Regexp, item.Regexp);
            Assert.IsTrue(string.IsNullOrWhiteSpace(item.Name));

            //cleanup
            Connection.Delete(item);
        }
    }
}
