using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.tests
{
    [TestClass]
    public class IpSettingsTest : TestBase
    {
        [TestMethod]
        public void LoadIpSettingsWillNotFail()
        {
            EnsureCommandAvailable("/ip/settings");
            var settings = Connection.LoadSingle<IpSettings>();
            Assert.IsNotNull(settings);
            Assert.IsTrue(settings.IpForward, "ip-forward should be enabled on this router");
        }
    }
}
