using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestLock("/ip/settings")]
    public class IpSettingsTest : TestBase
    {
        [TestMethod]
        public void LoadIpSettingsWillNotFail()
        {
            EnsureCommandAvailable("/ip/settings");
            var settings = Connection.LoadSingle<IpSettings>();
            Assert.IsNotNull(settings);
            Assert.IsTrue(settings.IpForward.GetValueOrDefault(), "ip-forward should be enabled on this router");
        }

        [TestMethod]
        public void TheIcmpRateMaskIsReadAndWrittenAsAHexNumber()
        {
            // Printed 0x1818 on every transport and version; written 0x181A — hex digits, where an upper-case 0X
            // prefix is refused on 6.49.13. Restored in the finally: it is a router-wide setting.
            EnsureCommandAvailable("/ip/settings");
            var settings = Connection.LoadSingle<IpSettings>();
            var original = settings.IcmpRateMask.Value.Value;
            try
            {
                settings.IcmpRateMask = new TikHexNumber(original.Value ^ 0x2);
                Connection.Save(settings);
                Assert.AreEqual(original.Value ^ 0x2, Connection.LoadSingle<IpSettings>().IcmpRateMask.Value.Value.Value);
            }
            finally
            {
                var restore = Connection.LoadSingle<IpSettings>();
                restore.IcmpRateMask = original;
                Connection.Save(restore);
            }
        }
    }
}
