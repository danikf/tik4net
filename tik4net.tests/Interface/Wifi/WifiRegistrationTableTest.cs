using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wifi;

namespace tik4net.tests
{
    [TestClass]
    public class WifiRegistrationTableTest : TestBase
    {
        [TestMethod]
        public void ListWifiRegistrationTableWillNotFail()
        {
            EnsureCommandAvailable("/interface/wifi/registration-table");
            var list = Connection.LoadAll<WifiRegistrationTable>();
            Assert.IsNotNull(list);
        }
    }
}
