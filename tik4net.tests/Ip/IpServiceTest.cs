using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
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
    }
}
