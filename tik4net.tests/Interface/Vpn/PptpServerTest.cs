using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.tests
{
    [TestClass]
    public class PptpServerTest : TestBase
    {
        // Singleton — only a LoadSingle test (no Add/Delete).
        [TestMethod]
        public void LoadPptpServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/pptp-server/server");
            var server = Connection.LoadSingle<PptpServer>();
            Assert.IsNotNull(server);
        }
    }
}
