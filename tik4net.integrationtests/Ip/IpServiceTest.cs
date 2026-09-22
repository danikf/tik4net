using System.Linq;
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

        // RouterOS 6 prints the access list as `address`, RouterOS 7 as `available-from`. A value that is not the
        // default has to survive the round trip on either: an empty list would read back as the default whatever
        // name the router used, and prove nothing. 0.0.0.0/0 allows everyone, so ftp stays reachable meanwhile.
        [TestMethod]
        public void TheAccessListRoundTripsUnderTheRoutersOwnName()
        {
            EnsureCommandAvailable("/ip/service");
            var ftp = Connection.LoadAll<IpService>().Single(s => s.Name == "ftp");
            string original = ftp.Address;
            try
            {
                ftp.Address = "0.0.0.0/0";
                Connection.Save(ftp);

                Assert.AreEqual("0.0.0.0/0", Connection.LoadAll<IpService>().Single(s => s.Name == "ftp").Address);
            }
            finally
            {
                var restore = Connection.LoadAll<IpService>().Single(s => s.Name == "ftp");
                restore.Address = original;
                Connection.Save(restore);
            }
        }
    }
}
