using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemLedsTest : TestBase
    {
        // Hardware-backed: always returns empty list on CHR / virtual routers.
        [TestMethod]
        public void ListSystemLedsWillNotFail()
        {
            EnsureCommandAvailable("/system/leds");
            var list = Connection.LoadAll<SystemLeds>();
            Assert.IsNotNull(list);
        }
    }
}
