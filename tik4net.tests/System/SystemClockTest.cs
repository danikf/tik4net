using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class SystemClockTest : TestBase
    {
        // Singleton — LoadSingle must not throw and must return a non-null result.
        [TestMethod]
        public void LoadSystemClockWillNotFail()
        {
            EnsureCommandAvailable("/system/clock");
            var clock = Connection.LoadSingle<SystemClock>();
            Assert.IsNotNull(clock);
        }
    }
}
