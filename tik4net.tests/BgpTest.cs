using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Routing.Bgp;

namespace tik4net.tests
{
    [TestClass]
    public class BgpTest: TestBase
    {
        [TestMethod]
        public void ListAllBgpAdvertisementsWillNotFail()
        {
            var list = Connection.LoadAll<BgpAdvertisements>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllInstancesWillNotFail()
        {
            var list = Connection.LoadAll<BgpInstance>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllBgpNetworksWillNotFail()
        {
            var list = Connection.LoadAll<BgpNetwork>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllPeersWillNotFail()
        {
            var list = Connection.LoadAll<BgpPeer>();
            Assert.IsNotNull(list);
        }
   }
}
