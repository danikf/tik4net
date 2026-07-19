using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemNtpClientTest : TestBase
    {
        // Singleton — only a LoadSingle test (no Add/Delete).
        [TestMethod]
        public void LoadSystemNtpClientWillNotFail()
        {
            EnsureCommandAvailable("/system/ntp/client");
            var client = Connection.LoadSingle<SystemNtpClient>();
            Assert.IsNotNull(client);
        }
    }
}
