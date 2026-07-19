using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceLteTest : TestBase
    {
        // LTE interfaces are hardware-backed — created automatically when a modem is detected.
        // No Add test: the router under test has no LTE hardware.
        [TestMethod]
        public void ListLtesWillNotFail()
        {
            EnsureCommandAvailable("/interface/lte");
            var list = Connection.LoadAll<InterfaceLte>();
            Assert.IsNotNull(list);
        }
    }
}
