using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.TrafficFlow;

namespace tik4net.tests
{
    [TestClass]
    public class IpTrafficFlowTest : TestBase
    {
        [TestMethod]
        public void LoadIpTrafficFlowWillNotFail()
        {
            EnsureCommandAvailable("/ip/traffic-flow");
            var trafficFlow = Connection.LoadSingle<IpTrafficFlow>();
            Assert.IsNotNull(trafficFlow);
        }
    }
}
