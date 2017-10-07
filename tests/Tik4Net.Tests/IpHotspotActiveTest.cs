using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Hotspot;

namespace tik4net.tests
{
    [TestClass]
    public class IpHotspotActiveTest : TestBase
    {
        [TestMethod]
        public void LoadAllActiveWillNotFail()
        {
            Connection.LoadAll<HotspotActive>();
        }
    }
}
