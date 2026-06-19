using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecActivePeersTest : TestBase
    {
        [TestMethod]
        public void ListIpsecActivePeersWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/active-peers");
            var list = Connection.LoadAll<IpsecActivePeers>();
            Assert.IsNotNull(list);
        }
    }
}
