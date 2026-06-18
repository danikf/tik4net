using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.tests
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
    }
}
