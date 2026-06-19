using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class IpSshTest : TestBase
    {
        [TestMethod]
        public void LoadIpSshWillNotFail()
        {
            EnsureCommandAvailable("/ip/ssh");
            var ssh = Connection.LoadSingle<IpSsh>();
            Assert.IsNotNull(ssh);
        }
    }
}
