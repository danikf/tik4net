using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolEmailTest : TestBase
    {
        // Singleton — LoadSingle must not throw and must return a non-null result.
        [TestMethod]
        public void LoadToolEmailWillNotFail()
        {
            EnsureCommandAvailable("/tool/e-mail");
            var email = Connection.LoadSingle<ToolEmail>();
            Assert.IsNotNull(email);
        }

        // RouterOS 6 prints the SMTP server as `address`, RouterOS 7 as `server`; one of them must arrive.
        [TestMethod]
        public void TheServerReadsUnderOneOfItsTwoNames()
        {
            EnsureCommandAvailable("/tool/e-mail");
            var email = Connection.LoadSingle<ToolEmail>();
            Assert.IsTrue(email.Server != null || email.Address != null, "neither server nor address was read");
        }
    }
}
