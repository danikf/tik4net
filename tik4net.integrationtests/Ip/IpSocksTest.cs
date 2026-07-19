using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpSocksTest : TestBase
    {
        [TestMethod]
        public void LoadIpSocksWillNotFail()
        {
            EnsureCommandAvailable("/ip/socks");
            var socks = Connection.LoadSingle<IpSocks>();
            Assert.IsNotNull(socks);
        }
    }
}
