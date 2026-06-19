using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.tests
{
    [TestClass]
    public class L2tpServerTest : TestBase
    {
        // Singleton — only a LoadSingle test (no Add/Delete).
        [TestMethod]
        public void LoadL2tpServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/l2tp-server/server");
            var server = Connection.LoadSingle<L2tpServer>();
            Assert.IsNotNull(server);
        }
    }
}
