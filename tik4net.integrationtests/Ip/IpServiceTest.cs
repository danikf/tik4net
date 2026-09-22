using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpServiceTest : TestBase
    {
        [TestMethod]
        public void ListIpServicesWillNotFail()
        {
            EnsureCommandAvailable("/ip/service");
            var list = Connection.LoadAll<IpService>();
            Assert.IsNotNull(list);
        }

        // RouterOS 6 prints the access list as `address`, RouterOS 7 as `available-from`; every row carries it
        // (an empty list prints empty), so one of the two must arrive, whichever version the lab runs.
        [TestMethod]
        public void TheAccessListReadsUnderOneOfItsTwoNames()
        {
            EnsureCommandAvailable("/ip/service");
            foreach (var service in Connection.LoadAll<IpService>())
                Assert.IsTrue(service.Address != null || service.AvailableFrom != null,
                    $"{service.Name}: neither address nor available-from was read");
        }
    }
}
