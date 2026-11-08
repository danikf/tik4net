using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestLock("/ip/dhcp-server/config")]
    public class DhcpServerConfigTest : TestBase
    {
        [TestMethod]
        public void LoadDhcpServerConfigWillNotFail()
        {
            EnsureCommandAvailable("/ip/dhcp-server/config");
            var config = Connection.LoadSingle<DhcpServerConfig>();
            Assert.IsNotNull(config);
            Assert.IsTrue(config.StoreLeasesDisk.Value.HasValue, "store-leases-disk is always printed");
        }

        [TestMethod]
        public void StoreLeasesDiskRoundTripsItsWordsAndAnInterval()
        {
            // The router takes the words immediately and never, otherwise a duration. A router-wide setting:
            // restored in the finally to what the router held before.
            EnsureCommandAvailable("/ip/dhcp-server/config");
            var original = Connection.LoadSingle<DhcpServerConfig>().StoreLeasesDisk.Value;
            try
            {
                foreach (var value in new[]
                {
                    TikDuration.FromSpecial(TikDurationSpecial.Immediately),
                    TikDuration.FromSpecial(TikDurationSpecial.Never),
                    (TikDuration)TimeSpan.FromMinutes(5),
                })
                {
                    var config = Connection.LoadSingle<DhcpServerConfig>();
                    config.StoreLeasesDisk = value;
                    Connection.Save(config);
                    Assert.AreEqual(value, Connection.LoadSingle<DhcpServerConfig>().StoreLeasesDisk.Value, value.ToString());
                }
            }
            finally
            {
                var restore = Connection.LoadSingle<DhcpServerConfig>();
                restore.StoreLeasesDisk = original;
                Connection.Save(restore);
            }
        }
    }
}
