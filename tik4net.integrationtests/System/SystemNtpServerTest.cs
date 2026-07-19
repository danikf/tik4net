using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemNtpServerTest : TestBase
    {
        [TestMethod]
        public void LoadNtpServerWillNotFail()
        {
            EnsureCommandAvailable("/system/ntp/server");
            var server = Connection.LoadSingle<SystemNtpServer>();
            Assert.IsNotNull(server);
        }
    }
}
