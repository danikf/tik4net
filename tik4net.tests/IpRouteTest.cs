using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class IpDnsTest : TestBase
    {
        [TestMethod]
        public void LoadIpRoutesWillNotFail()
        {
            var list = Connection.LoadAll<IpRoute>();
            Assert.IsNotNull(list);
        }
    }
}
