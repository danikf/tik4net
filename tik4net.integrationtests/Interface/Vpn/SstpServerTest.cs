using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SstpServerTest : TestBase
    {
        // Singleton — only a LoadSingle test (no Add/Delete).
        [TestMethod]
        public void LoadSstpServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/sstp-server/server");
            var server = Connection.LoadSingle<SstpServer>();
            Assert.IsNotNull(server);
        }
    }
}
