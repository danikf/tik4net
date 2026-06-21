using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class SystemWatchdogTest : TestBase
    {
        [TestMethod]
        public void LoadSystemWatchdogWillNotFail()
        {
            EnsureCommandAvailable("/system/watchdog");
            var watchdog = Connection.LoadSingle<SystemWatchdog>();
            Assert.IsNotNull(watchdog);
        }
    }
}
