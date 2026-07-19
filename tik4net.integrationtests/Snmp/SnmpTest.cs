using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Snmp;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SnmpTest : TestBase
    {
        [TestMethod]
        public void LoadSnmpWillNotFail()
        {
            EnsureCommandAvailable("/snmp");
            var snmp = Connection.LoadSingle<Snmp>();
            Assert.IsNotNull(snmp);
        }
    }
}
