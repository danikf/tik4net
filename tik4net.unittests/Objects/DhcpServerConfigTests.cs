using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <c>/ip/dhcp-server/config</c> <c>store-leases-disk</c>: the router (7.25.1, CLI completion on
    /// <c>set store-leases-disk=</c>) offers the words <c>immediately</c> and <c>never</c>; any other value is a duration
    /// such as <c>5m</c>.
    /// </summary>
    [TestClass]
    public class DhcpServerConfigTests
    {
        [TestMethod]
        public void TheStoreLeasesDiskConstantsAreTheWordsTheRouterTakes()
        {
            Assert.AreEqual("immediately", DhcpServerConfig.StoreLeasesDiskType.Immediately);
            Assert.AreEqual("never", DhcpServerConfig.StoreLeasesDiskType.Never);
        }
    }
}
