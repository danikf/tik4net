using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceWifiTest : TestBase
    {
        // NOTE: Adding a /interface/wifi entry requires either a radio-mac (physical wifi radio)
        // or a master-interface (virtual BSSID bound to an existing wifi master). The test router
        // (192.168.4.236) is a virtual machine with no wifi hardware, so the add test cannot be
        // written reliably. Only the List test is included.

        [TestMethod]
        public void ListInterfaceWifisWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi");
            var list = Connection.LoadAll<InterfaceWifi>();
            Assert.IsNotNull(list);
        }
    }
}
