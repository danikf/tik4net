using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    [SafeInParallelLegs]
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
