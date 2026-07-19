using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.CapsMan;

namespace tik4net.integrationtests
{
    [TestClass]
    public class CapsManManagerTest : TestBase
    {
        [TestMethod]
        public void LoadCapsManManagerWillNotFail()
        {
            EnsureCommandAvailable("/caps-man/manager");
            // /caps-man/manager is a singleton — LoadSingle returns the one settings record.
            var manager = Connection.LoadSingle<CapsManManager>();
            Assert.IsNotNull(manager);
        }
    }
}
