using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class IpCloudTest : TestBase
    {
        [TestMethod]
        public void LoadIpCloudWillNotFail()
        {
            EnsureCommandAvailable("/ip/cloud");
            var cloud = Connection.LoadSingle<IpCloud>();
            Assert.IsNotNull(cloud);
        }
    }
}
