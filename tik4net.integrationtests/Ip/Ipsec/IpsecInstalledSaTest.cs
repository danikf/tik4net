using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpsecInstalledSaTest : TestBase
    {
        [TestMethod]
        public void ListIpsecInstalledSasWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/installed-sa");
            var list = Connection.LoadAll<IpsecInstalledSa>();
            Assert.IsNotNull(list);
        }
    }
}
