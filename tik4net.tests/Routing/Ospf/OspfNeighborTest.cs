using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Ospf;

namespace tik4net.tests
{
    [TestClass]
    public class OspfNeighborTest : TestBase
    {
        [TestMethod]
        public void ListOspfNeighborsWillNotFail()
        {
            EnsureCommandAvailable("/routing/ospf/neighbor");
            var list = Connection.LoadAll<OspfNeighbor>();
            Assert.IsNotNull(list);
        }
    }
}
