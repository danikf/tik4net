using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class IpNeighborTest : TestBase
    {
        [TestMethod]
        public void ListIpNeighborsWillNotFail()
        {
            EnsureCommandAvailable("/ip/neighbor");
            var list = Connection.LoadAll<IpNeighbor>();
            Assert.IsNotNull(list);
        }
    }
}
