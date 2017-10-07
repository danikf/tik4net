using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tik4Net.Objects;
using Tik4Net.Objects.Ip.Hotspot;

namespace Tik4Net.Tests
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
